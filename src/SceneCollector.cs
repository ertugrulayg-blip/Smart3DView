using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using P3 = System.Windows.Media.Media3D.Point3D;
using V3 = System.Windows.Media.Media3D.Vector3D;

namespace Smart3DView;

/// <summary>Kutuyla kesişen elemanların geometrisini KIRPMADAN toplar (kesme işini GPU yapar → pencerede kutu anında
/// küçültülüp taşınabilir). Her elemana sistem rengi, çakışma grubu ve connector bağlantıları eklenir.</summary>
static class SceneCollector
{
    /// <param name="prev">Kutu büyütme: verilirse onun elemanları korunur, Revit'ten yalnız henüz okunmamış elemanlar
    /// istenir (eskiden her büyütmede tüm alan baştan okunuyordu — kullanıcı raporu 2026-10-07 "biraz fazla uzun sürüyor").</param>
    public static SceneData Collect(Document doc, View? activeView, ClipBox box, SceneData? prev = null)
    {
        var scene = prev?.CloneForAppend() ?? new SceneData();
        scene.Source = box.Source;
        var ctx = new Ctx(scene, box.Min, box.Max);
        var frameInv = box.Frame.Inverse;
        // 3B görünümde kullanıcının gizlediği elemanlar/kategoriler görünmesin; planda ise tavan vb. kesit üstü
        // elemanlar "görünür" sayılmadığı için tüm belge taranır.
        ElementId? viewId = activeView is View3D v3 && !v3.IsTemplate ? activeView.Id : null;

        // Okunacak belgeler: ana model + bağlı model örnekleri (aynı model iki kez bağlıysa iki geçiş).
        var passes = new List<(long key, Document d, Transform toLocal, RevitLinkInstance? li)> { (-1, doc, frameInv, null) };
        var links = viewId != null ? new FilteredElementCollector(doc, viewId) : new FilteredElementCollector(doc);
        foreach (RevitLinkInstance li in links.OfClass(typeof(RevitLinkInstance)))
            if (li.GetLinkDocument() is { } ld) passes.Add((li.Id.Value, ld, frameInv.Multiply(li.GetTotalTransform()), li));

        // Her geçişte: sahnedeki belge sırası, zaten yüklü elemanlar (eklemede atlanır) ve henüz okunmamış tavalar.
        var idx = new byte[passes.Count];
        var existing = new Dictionary<long, uint>?[passes.Count];
        var trays = new List<Element>[passes.Count];
        for (int i = 0; i < passes.Count; i++)
        {
            var (key, d, toLocal, _) = passes[i];
            int di = -1;
            for (int k = 0; k < scene.Docs.Count && k < scene.DocKeys.Count; k++)
                if (scene.DocKeys[k] == key && ReferenceEquals(scene.Docs[k], d)) { di = k; break; }
            if (di < 0)
            {
                if (scene.Docs.Count > 250) { idx[i] = 255; trays[i] = new(); continue; }
                di = scene.Docs.Count;
                scene.Docs.Add(d);
                scene.DocKeys.Add(key);
                scene.DocNames.Add(d.Title);
            }
            idx[i] = (byte)di;
            if (prev != null)
            {
                var map = new Dictionary<long, uint>();
                for (int j = 0; j < scene.ElemDoc.Count; j++)
                    if (scene.ElemDoc[j] == di) map[scene.ElemRevitId[j]] = (uint)(j + 1);
                existing[i] = map;
            }
            trays[i] = i == 0 ? TraysInBox(ctx, d, toLocal, existing[i]) : new();   // geçici Fine görünüm yalnız ana modelde (bağlılar: TrayOptions)
        }

        // Düz kablo tavaları (ladder/kafes) Revit'te yalnız FINE detaylı bir görünümden okunursa basamaklı gelir;
        // görünümsüz Options.DetailLevel=Fine onlarda işe yaramıyor (kullanıcı raporu, 2026-10-07: "fittingler fine,
        // tavalar coarse"). Geri alınan bir işlem içinde geçici Fine 3B görünüm açılır, tavalar onunla okunur, sonra
        // RollBack → modelde iz kalmaz. Açılamazsa (salt-okunur belge vb.) eski davranış.
        // Bağlı modellerin tavaları kendi Fine görünümlerinden biriyle okunur (DocPass.TrayOptions). Bağlantının tüm geometrisini Fine görünümden alıp tavalara
        // eşleştirmek büyük modelde Revit'i dakikalarca kilitledi (1.12.0 denemesi, 2026-10-07) — kaldırıldı.
        Transaction? tmp = null;
        Options? trayOpt = null;
        try
        {
            if (trays[0]?.Count > 0 && idx[0] != 255 && !doc.IsReadOnly && !doc.IsModifiable && !doc.IsFamilyDocument)
            {
                ViewFamilyType? vft = null;
                foreach (ViewFamilyType t in new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)))
                    if (t.ViewFamily == ViewFamily.ThreeDimensional) { vft = t; break; }
                if (vft != null)
                {
                    tmp = new Transaction(doc, Product.Name + " temp view");
                    if (tmp.Start() == TransactionStatus.Started)
                    {
                        var fv = View3D.CreateIsometric(doc, vft.Id);
                        if (fv.ViewTemplateId != ElementId.InvalidElementId) fv.ViewTemplateId = ElementId.InvalidElementId;
                        fv.DetailLevel = ViewDetailLevel.Fine;
                        doc.Regenerate();
                        trayOpt = new Options { View = fv, ComputeReferences = false, IncludeNonVisibleObjects = false };
                    }
                }
            }
        }
        catch { trayOpt = null; }

        try
        {
            for (int i = 0; i < passes.Count; i++)
            {
                if (idx[i] == 255) continue;
                var (_, d, toLocal, li) = passes[i];
                new DocPass(ctx, d, toLocal, li == null ? viewId : null, li == null ? null : d.Title, idx[i],
                    li == null && trays[i].Count > 0 ? trayOpt : null, existing[i]).Run();
            }
        }
        finally
        {
            try { if (tmp != null && tmp.HasStarted() && !tmp.HasEnded()) tmp.RollBack(); } catch { }
            tmp?.Dispose();
        }

        scene.FormatLength = LengthFormat.Make(doc);
        scene.FrameAngle = Math.Atan2(box.Frame.BasisX.Y, box.Frame.BasisX.X);
        scene.BoxMin = new[] { box.Min.X, box.Min.Y, box.Min.Z };
        scene.BoxMax = new[] { box.Max.X, box.Max.Y, box.Max.Z };
        scene.Seconds = ctx.Watch.Elapsed.TotalSeconds;
        scene.GeoSeconds = ctx.GeometrySeconds; scene.TriSeconds = ctx.TriSeconds + ctx.EdgeSeconds; scene.InfoSeconds = ctx.InfoSeconds;
        scene.Timing = L.T(
            $"Revit geometri {ctx.GeometrySeconds:0.00} sn · üçgenleme {ctx.TriSeconds:0.00} sn · kenar {ctx.EdgeSeconds:0.00} sn · sistem bilgisi {ctx.InfoSeconds:0.00} sn · toplam {scene.Seconds:0.00} sn",
            $"Revit geometry {ctx.GeometrySeconds:0.00} s · triangulation {ctx.TriSeconds:0.00} s · edges {ctx.EdgeSeconds:0.00} s · system info {ctx.InfoSeconds:0.00} s · total {scene.Seconds:0.00} s");
        return scene;
    }

    /// <summary>Kutunun (yerel) belge koordinatlarındaki eksen hizalı sınırı.</summary>
    internal static Outline DocOutline(Ctx c, Transform toDoc)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var p0 in BoxResolver.Corners(c.Min, c.Max))
        {
            var p = toDoc.OfPoint(p0);
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
        }
        return new Outline(new XYZ(x0, y0, z0), new XYZ(x1, y1, z1));
    }

    /// <summary>Kutuya giren ve henüz sahnede olmayan düz kablo tavaları.</summary>
    static List<Element> TraysInBox(Ctx c, Document d, Transform toLocal, Dictionary<long, uint>? existing)
    {
        var res = new List<Element>();
        try
        {
            foreach (var e in new FilteredElementCollector(d).OfCategory(BuiltInCategory.OST_CableTray).WhereElementIsNotElementType()
                         .WherePasses(new BoundingBoxIntersectsFilter(DocOutline(c, toLocal.Inverse))))
                if (existing == null || !existing.ContainsKey(e.Id.Value)) res.Add(e);
        }
        catch { }
        return res;
    }

    internal sealed class Ctx
    {
        public readonly SceneData Scene;
        public readonly XYZ Min, Max;
        public readonly Stopwatch Watch = Stopwatch.StartNew();
        public double GeometrySeconds, TriSeconds, EdgeSeconds, InfoSeconds;
        public static double Since(long t0) => (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
        public Ctx(SceneData scene, XYZ min, XYZ max) { Scene = scene; Min = min; Max = max; }
    }
}

/// <summary>Tek bir belge (ana model ya da bağlı model) için toplama geçişi.</summary>
sealed class DocPass
{
    const double SmoothEdgeCos = 0.985; // ≈ 10°: teğet yüzler arası kenar (boru dikiş çizgisi vb.) çizilmez

    static readonly HashSet<BuiltInCategory> Excluded = new()
    {
        BuiltInCategory.OST_RvtLinks, BuiltInCategory.OST_Cameras, BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_MEPSpaces, BuiltInCategory.OST_Areas, BuiltInCategory.OST_HVAC_Zones,
        BuiltInCategory.OST_Mass, BuiltInCategory.OST_PointClouds, BuiltInCategory.OST_SectionBox,
        BuiltInCategory.OST_VolumeOfInterest, BuiltInCategory.OST_Lines, BuiltInCategory.OST_Levels,
        BuiltInCategory.OST_Grids, BuiltInCategory.OST_LightingFixtureSource, BuiltInCategory.OST_RoomSeparationLines,
        BuiltInCategory.OST_MEPSpaceSeparationLines, BuiltInCategory.OST_ShaftOpening,
        BuiltInCategory.OST_IOSModelGroups, BuiltInCategory.OST_Assemblies,
    };

    static readonly HashSet<BuiltInCategory> Horizontal = new()
    {
        BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Stairs,
        BuiltInCategory.OST_StairsRuns, BuiltInCategory.OST_StairsLandings, BuiltInCategory.OST_StairsRailing,
        BuiltInCategory.OST_Ramps, BuiltInCategory.OST_Topography, BuiltInCategory.OST_Toposolid,
        BuiltInCategory.OST_RoofSoffit, BuiltInCategory.OST_Fascia, BuiltInCategory.OST_Gutter, BuiltInCategory.OST_EdgeSlab,
    };

    static readonly HashSet<BuiltInCategory> Structure = new()
    {
        BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralTruss, BuiltInCategory.OST_StructConnections,
    };

    static readonly HashSet<BuiltInCategory> PipeCats = new()
    {
        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_FlexPipeCurves, BuiltInCategory.OST_PipeInsulations, BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_FabricationPipework,
    };

    static readonly HashSet<BuiltInCategory> DuctCats = new()
    {
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctInsulations,
        BuiltInCategory.OST_DuctLinings, BuiltInCategory.OST_FabricationDuctwork,
    };

    static readonly HashSet<BuiltInCategory> ContainmentCats = new()
    {
        BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting, BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_ConduitFitting, BuiltInCategory.OST_FabricationContainment,
    };

    static readonly HashSet<BuiltInCategory> ElectricalCats = new()
    {
        BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_DataDevices,
        BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_NurseCallDevices,
        BuiltInCategory.OST_TelephoneDevices,
    };

    static readonly HashSet<BuiltInCategory> MechEquipCats = new()
    {
        BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PlumbingFixtures,
    };

    readonly SceneCollector.Ctx _c;
    readonly Document _doc;
    readonly Transform _toLocal, _toDoc;
    readonly ElementId? _viewId;
    readonly string? _linkName;
    readonly byte _docIdx;
    readonly Options? _trayOpt;   // kablo tavaları için geçici Fine görünümlü seçenekler (yalnız ana model)
    readonly Dictionary<long, uint>? _existing;            // kutu büyütme: zaten sahnede olanlar (Revit no → sahne no)
    readonly Options _opt = new() { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
    readonly Dictionary<long, bool> _glass = new();
    readonly Dictionary<long, bool> _skipStyle = new();
    readonly Dictionary<long, (SysColor, int)> _sysTypeInfo = new();
    // Bu belgedeki Revit eleman no → sahne no; geçiş sonunda izolasyon-taşıyıcı ve connector ilişkileri buna çevrilir.
    readonly Dictionary<long, uint> _sceneId = new();
    readonly List<(uint id, long host)> _pendingHost = new();
    readonly double _x0, _y0, _z0, _x1, _y1, _z1; // kutu (yerel)
    Tone _tone;
    uint _id;
    bool _emitted;

    public DocPass(SceneCollector.Ctx c, Document doc, Transform docToLocal, ElementId? viewId, string? linkName, byte docIdx, Options? trayOpt,
        Dictionary<long, uint>? existing = null)
    {
        _c = c; _doc = doc; _toLocal = docToLocal; _toDoc = docToLocal.Inverse; _viewId = viewId; _linkName = linkName;
        _docIdx = docIdx; _trayOpt = trayOpt; _existing = existing;
        if (existing != null) foreach (var kv in existing) _sceneId[kv.Key] = kv.Value;   // yeni elemanların bağlantıları eskilere de çözülsün
        _x0 = c.Min.X; _y0 = c.Min.Y; _z0 = c.Min.Z; _x1 = c.Max.X; _y1 = c.Max.Y; _z1 = c.Max.Z;
    }

    public void Run()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var c in BoxResolver.Corners(_c.Min, _c.Max))
        {
            var p = _toDoc.OfPoint(c);
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
        }
        var outline = new Outline(new XYZ(x0, y0, z0), new XYZ(x1, y1, z1));
        var col = _viewId != null ? new FilteredElementCollector(_doc, _viewId) : new FilteredElementCollector(_doc);
        col.WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(outline));
        var scene = _c.Scene;

        foreach (Element e in col)
        {
            if (e is RevitLinkInstance || e is ImportInstance || e.ViewSpecific) continue;
            var cat = e.Category;
            if (cat == null || cat.CategoryType != CategoryType.Model) continue;
            var bic = cat.BuiltInCategory;
            if (Excluded.Contains(bic)) continue;
            if (_existing != null && _existing.ContainsKey(e.Id.Value)) continue;   // zaten yüklü (kutu büyütme)
            GeometryElement? ge;
            long tg = Stopwatch.GetTimestamp();
            try { ge = e.get_Geometry(bic == BuiltInCategory.OST_CableTray && TrayOptions() is { } to ? to : _opt); } catch { ge = null; }
            if (ge == null || !ge.GetEnumerator().MoveNext())   // tava o görünümde gizliyse normal okuma
                try { ge = e.get_Geometry(_opt); } catch { ge = null; }
            _c.GeometrySeconds += SceneCollector.Ctx.Since(tg);
            if (ge == null) continue;
            _tone = Horizontal.Contains(bic) ? Tone.Horizontal
                : Structure.Contains(bic) ? Tone.Structure
                : PipeCats.Contains(bic) || DuctCats.Contains(bic) || ContainmentCats.Contains(bic)
                  || ElectricalCats.Contains(bic) || MechEquipCats.Contains(bic) ? Tone.Mep
                : Tone.General;
            _emitted = false;
            _id = (uint)scene.Labels.Count + 1;
            Walk(ge, 0);
            if (!_emitted) continue;

            long ti = Stopwatch.GetTimestamp();
            scene.Labels.Add(Label(e, cat));
            scene.ElemCat.Add((ushort)scene.CatIndex(cat.Name, Discipline(bic)));
            scene.ElemRevitId.Add(e.Id.Value);
            scene.ElemDoc.Add(_docIdx);
            var (color, group, host) = QuickClassify(e, bic);
            scene.ElemTag.Add("");
            scene.ElemColor.Add((byte)color);
            scene.ElemDetail.Add(DetailOf(bic, color, group));
            scene.ElemGroup.Add(group);
            scene.ElemCanon.Add(_id);
            _sceneId[e.Id.Value] = _id;
            if (host != null) _pendingHost.Add((_id, host.Value));
            _c.InfoSeconds += SceneCollector.Ctx.Since(ti);
        }

        foreach (var (id, host) in _pendingHost)
            if (_sceneId.TryGetValue(host, out var h)) scene.ElemCanon[(int)id - 1] = h;
    }

    Options? _linkTrayOpt;
    bool _linkTrayDone;

    /// <summary>Düz tavalar Revit'te yalnız Fine detaylı bir görünümle okunursa basamaklı/U gelir. Ana model: geçici
    /// Fine görünüm. Bağlı model salt-okunur (görünüm açılamaz) → bağlantının KENDİ görünümlerinden Fine olan, tavaları
    /// gizlemeyen biri kullanılır (3B öncelikli). Yoksa null → normal okuma.</summary>
    Options? TrayOptions()
    {
        if (_trayOpt != null || _linkName == null) return _trayOpt;
        if (_linkTrayDone) return _linkTrayOpt;
        _linkTrayDone = true;
        try
        {
            var trayCat = new ElementId(BuiltInCategory.OST_CableTray);
            View? best = null;
            foreach (View v in new FilteredElementCollector(_doc).OfClass(typeof(View)))
            {
                if (v.IsTemplate || v.DetailLevel != ViewDetailLevel.Fine) continue;
                if (v.ViewType is not (ViewType.ThreeD or ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.Section or ViewType.Elevation)) continue;
                try { if (v.CanCategoryBeHidden(trayCat) && v.GetCategoryHidden(trayCat)) continue; } catch { continue; }
                if (best == null || (v.ViewType == ViewType.ThreeD && best.ViewType != ViewType.ThreeD)) best = v;
                if (best.ViewType == ViewType.ThreeD) break;
            }
            if (best != null) _linkTrayOpt = new Options { View = best, ComputeReferences = false, IncludeNonVisibleObjects = false };
        }
        catch { _linkTrayOpt = null; }
        return _linkTrayOpt;
    }

    // ---- ikinci adım: eleman bilgisi --------------------------------------------------------------------------------

    /// <summary>Hızlı sınıflandırma: boru/kanal sistem tipine bakılmaz — parametre okuması kaldırıldı (kullanıcı isteği 2026-10-07, büyütmede Revit takılıyordu). Sprinkler yangın, gerisi kategori.</summary>
    (SysColor color, int group, long? host) QuickClassify(Element e, BuiltInCategory bic)
    {
        long? host = null;
        if (e is InsulationLiningBase ins && _doc.GetElement(ins.HostElementId) is { } h)
        {
            host = h.Id.Value;
            bic = h.Category?.BuiltInCategory ?? bic;
        }
        if (bic == BuiltInCategory.OST_Sprinklers) return (SysColor.Fire, ClashGroup.PipeFire, host);
        if (PipeCats.Contains(bic)) return (SysColor.None, ClashGroup.PipeOther, host);
        if (DuctCats.Contains(bic)) return (SysColor.None, ClashGroup.DuctOther, host);
        var (c, g, _) = Classify(e, bic);
        return (c, g, host);
    }

    // ---- "Detaylı" ton rengi ---------------------------------------------------------------------------------------

    /// <summary>Tanınan tesisat sistemi rengi önce gelir; yoksa tesisat grubu, sonra kategori.</summary>
    static byte DetailOf(BuiltInCategory bic, SysColor color, int group)
    {
        if (color != SysColor.None) return (byte)color;
        switch (group)
        {
            case ClashGroup.PipeHeating: return Detail.PipeHeating;
            case ClashGroup.PipeDomestic: return Detail.PipeDomestic;
            case ClashGroup.PipeDrainage: return Detail.PipeDrainage;
            case ClashGroup.PipeCooling: case ClashGroup.PipeFire: case ClashGroup.PipeOther: return Detail.PipeOther;
            case ClashGroup.DuctSupply: case ClashGroup.DuctReturn: case ClashGroup.DuctOther: return Detail.DuctOther;
            case ClashGroup.Containment: return Detail.Metal;
        }
        switch (bic)
        {
            case BuiltInCategory.OST_Walls: case BuiltInCategory.OST_StackedWalls: case BuiltInCategory.OST_Cornices:
            case BuiltInCategory.OST_Reveals: case BuiltInCategory.OST_Parts:
                return Detail.Wall;
            case BuiltInCategory.OST_Floors: case BuiltInCategory.OST_Roofs: case BuiltInCategory.OST_RoofSoffit:
            case BuiltInCategory.OST_Fascia: case BuiltInCategory.OST_Gutter: case BuiltInCategory.OST_EdgeSlab: case BuiltInCategory.OST_Ramps:
                return Detail.Floor;
            case BuiltInCategory.OST_Ceilings: return Detail.Ceiling;
            case BuiltInCategory.OST_Doors: return Detail.Door;
            case BuiltInCategory.OST_Windows: case BuiltInCategory.OST_CurtainWallMullions: case BuiltInCategory.OST_CurtainWallPanels:
            case BuiltInCategory.OST_CurtaSystem:
                return Detail.Frame;
            case BuiltInCategory.OST_StructuralColumns: case BuiltInCategory.OST_StructuralFraming: case BuiltInCategory.OST_StructuralFoundation:
            case BuiltInCategory.OST_Columns: case BuiltInCategory.OST_StructuralTruss: case BuiltInCategory.OST_StructConnections:
                return Detail.Concrete;
            case BuiltInCategory.OST_Stairs: case BuiltInCategory.OST_StairsRuns: case BuiltInCategory.OST_StairsLandings:
            case BuiltInCategory.OST_StairsRailing: case BuiltInCategory.OST_Railings: case BuiltInCategory.OST_RailingTopRail:
            case BuiltInCategory.OST_RailingHandRail:
                return Detail.Stair;
            case BuiltInCategory.OST_Furniture: case BuiltInCategory.OST_FurnitureSystems: case BuiltInCategory.OST_Casework:
            case BuiltInCategory.OST_SpecialityEquipment: case BuiltInCategory.OST_Entourage: case BuiltInCategory.OST_Planting:
                return Detail.Furniture;
            case BuiltInCategory.OST_MechanicalEquipment: return Detail.MechEquipment;
            case BuiltInCategory.OST_PlumbingFixtures: return Detail.Sanitary;
            case BuiltInCategory.OST_ElectricalEquipment: return Detail.ElecEquipment;
            case BuiltInCategory.OST_LightingFixtures: case BuiltInCategory.OST_LightingDevices: return Detail.Lighting;
            case BuiltInCategory.OST_Topography: case BuiltInCategory.OST_Toposolid: case BuiltInCategory.OST_Site:
                return Detail.Site;
        }
        return ElectricalCats.Contains(bic) ? Detail.Devices : Detail.General;
    }

    // ---- sistem sınıflandırması ----------------------------------------------------------------------------------

    (SysColor color, int group, long? host) Classify(Element e, BuiltInCategory bic)
    {
        long? host = null;
        var src = e;
        if (e is InsulationLiningBase ins && _doc.GetElement(ins.HostElementId) is { } h)
        {
            // İzolasyon/kaplama, taşıdığı boru/kanalın rengini ve grubunu alır; çakışmada onun parçası sayılır.
            host = h.Id.Value;
            src = h;
            bic = h.Category?.BuiltInCategory ?? bic;
        }

        if (PipeCats.Contains(bic))
        {
            if (bic == BuiltInCategory.OST_Sprinklers) return (SysColor.Fire, ClashGroup.PipeFire, host);
            var (c, g) = SystemTypeInfo(src, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, pipe: true);
            return (c, g, host);
        }
        if (DuctCats.Contains(bic))
        {
            var (c, g) = SystemTypeInfo(src, BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM, pipe: false);
            return (c, g, host);
        }
        if (ContainmentCats.Contains(bic)) return (SysColor.None, ClashGroup.Containment, host);
        if (ElectricalCats.Contains(bic)) return (SysColor.None, ClashGroup.Electrical, host);
        if (MechEquipCats.Contains(bic)) return (SysColor.None, ClashGroup.MechEquipment, host);
        if (bic is BuiltInCategory.OST_StructuralFraming or BuiltInCategory.OST_StructuralColumns)
            return (SysColor.None, ClashGroup.Structure, host);
        return (SysColor.None, ClashGroup.None, host);
    }

    (SysColor, int) SystemTypeInfo(Element e, BuiltInParameter bip, bool pipe)
    {
        ElementId typeId = ElementId.InvalidElementId;
        try { typeId = e.get_Parameter(bip)?.AsElementId() ?? ElementId.InvalidElementId; } catch { }
        if (typeId == ElementId.InvalidElementId) typeId = SystemTypeFromConnectors(e);
        string sysName = "";
        try { sysName = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? ""; } catch { }

        if (typeId != ElementId.InvalidElementId && sysName.Length == 0 && _sysTypeInfo.TryGetValue(typeId.Value, out var cached))
            return cached;

        var st = _doc.GetElement(typeId) as MEPSystemType;
        var cls = st?.SystemClassification;
        string name = ((st?.Name ?? "") + " " + sysName).ToLowerInvariant();
        var res = pipe ? PipeInfo(cls, name) : DuctInfo(cls, name);
        if (typeId != ElementId.InvalidElementId && sysName.Length == 0) _sysTypeInfo[typeId.Value] = res;
        return res;
    }

    static ConnectorSet? Connectors(Element e)
    {
        try
        {
            return e switch
            {
                MEPCurve mc => mc.ConnectorManager?.Connectors,
                FamilyInstance fi => fi.MEPModel?.ConnectorManager?.Connectors,
                _ => null,
            };
        }
        catch { return null; }
    }

    static ElementId SystemTypeFromConnectors(Element e)
    {
        var set = Connectors(e);
        if (set == null) return ElementId.InvalidElementId;
        try
        {
            foreach (Connector c in set)
            {
                var sys = c.MEPSystem;
                if (sys != null) return sys.GetTypeId();
            }
        }
        catch { }
        return ElementId.InvalidElementId;
    }

    static bool Has(string s, params string[] keys)
    {
        foreach (var k in keys) if (s.Contains(k)) return true;
        return false;
    }

    static (SysColor, int) PipeInfo(MEPSystemClassification? cls, string name)
    {
        bool fireName = Has(name, "fire", "sprink", "yangın", "yangin", "hydrant", "hidrant");
        if (fireName || cls is MEPSystemClassification.FireProtectWet or MEPSystemClassification.FireProtectDry
            or MEPSystemClassification.FireProtectPreaction or MEPSystemClassification.FireProtectOther)
            return (SysColor.Fire, ClashGroup.PipeFire);
        bool coolName = Has(name, "chill", "chw", "cool", "soğut", "sogut", "condens", "kondens", "ccw");
        bool heatName = Has(name, "heat", "lthw", "mthw", "hthw", "ısıt", "isit", "kalorifer", "boiler", "kazan");
        if (coolName) return (SysColor.Cooling, ClashGroup.PipeCooling);
        if (cls is MEPSystemClassification.SupplyHydronic or MEPSystemClassification.ReturnHydronic)
            return heatName ? (SysColor.None, ClashGroup.PipeHeating) : (SysColor.Cooling, ClashGroup.PipeCooling);
        if (heatName) return (SysColor.None, ClashGroup.PipeHeating);
        if (cls is MEPSystemClassification.DomesticColdWater or MEPSystemClassification.DomesticHotWater)
            return (SysColor.None, ClashGroup.PipeDomestic);
        if (cls is MEPSystemClassification.Sanitary or MEPSystemClassification.Vent or MEPSystemClassification.Storm)
            return (SysColor.None, ClashGroup.PipeDrainage);
        return (SysColor.None, ClashGroup.PipeOther);
    }

    static (SysColor, int) DuctInfo(MEPSystemClassification? cls, string name)
    {
        if (cls == MEPSystemClassification.SupplyAir) return (SysColor.Supply, ClashGroup.DuctSupply);
        if (cls is MEPSystemClassification.ReturnAir or MEPSystemClassification.ExhaustAir) return (SysColor.Return, ClashGroup.DuctReturn);
        if (Has(name, "supply", "fresh", "outside air", "üfleme", "ufleme", "taze", "besleme")) return (SysColor.Supply, ClashGroup.DuctSupply);
        if (Has(name, "return", "exhaust", "extract", "emiş", "emis", "dönüş", "donus", "egzoz", "atık", "atik"))
            return (SysColor.Return, ClashGroup.DuctReturn);
        return (SysColor.None, ClashGroup.DuctOther);
    }

    // ---- geometri ------------------------------------------------------------------------------------------------

    void Walk(GeometryElement ge, int depth)
    {
        foreach (GeometryObject o in ge)
        {
            if (SkipStyle(o.GraphicsStyleId)) continue;
            switch (o)
            {
                case Solid s when s.Faces.Size > 0: AddSolid(s); break;
                case GeometryInstance gi when depth < 10:
                    var g = gi.GetInstanceGeometry();
                    if (g != null) Walk(g, depth + 1);
                    break;
                case Mesh m when m.NumTriangles > 0: AddMesh(m); break;
            }
        }
    }

    bool SkipStyle(ElementId id)
    {
        if (id == ElementId.InvalidElementId) return false;
        if (_skipStyle.TryGetValue(id.Value, out var skip)) return skip;
        skip = _doc.GetElement(id) is GraphicsStyle gs
            && gs.GraphicsStyleCategory is { } gc
            && (gc.BuiltInCategory == BuiltInCategory.OST_LightingFixtureSource
                || gc.Parent?.BuiltInCategory == BuiltInCategory.OST_LightingFixtureSource);
        _skipStyle[id.Value] = skip;
        return skip;
    }

    bool IsGlass(ElementId id)
    {
        if (id == ElementId.InvalidElementId) return false;
        if (_glass.TryGetValue(id.Value, out var g)) return g;
        g = _doc.GetElement(id) is Material m && m.Transparency >= 30;
        _glass[id.Value] = g;
        return g;
    }

    void AddSolid(Solid s)
    {
        // Kutunun tamamen dışında kalan katıları (ör. büyük bir ailenin uzak parçası) atla; gerisi bütün olarak alınır.
        BoundingBoxXYZ bb;
        try { bb = s.GetBoundingBox(); } catch { return; }
        if (bb == null) return;
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var c in BoxResolver.Corners(bb.Min, bb.Max))
        {
            var p = _toLocal.OfPoint(bb.Transform.OfPoint(c));
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
        }
        if (x1 < _x0 || x0 > _x1 || y1 < _y0 || y0 > _y1 || z1 < _z0 || z0 > _z1) return;

        foreach (Face f in s.Faces)
        {
            var tone = IsGlass(f.MaterialElementId) ? Tone.Glass : _tone;
            long tt = Stopwatch.GetTimestamp();
            Mesh m;
            try { m = f.Triangulate(); } catch { continue; }
            if (m != null && m.NumTriangles > 0) EmitFace(f, m, tone);
            _c.TriSeconds += SceneCollector.Ctx.Since(tt);
        }
        long te = Stopwatch.GetTimestamp();
        var edges = _c.Scene.Edges;
        foreach (Edge e in s.Edges)
        {
            if (IsSmooth(e)) continue;
            IList<XYZ> pts;
            try { pts = e.Tessellate(); } catch { continue; }
            AddSnaps(e);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = _toLocal.OfPoint(pts[i]);
                var b = _toLocal.OfPoint(pts[i + 1]);
                edges.Add(new EdgeVertex { X = (float)a.X, Y = (float)a.Y, Z = (float)a.Z, Id = _id });
                edges.Add(new EdgeVertex { X = (float)b.X, Y = (float)b.Y, Z = (float)b.Z, Id = _id });
            }
        }
        _c.EdgeSeconds += SceneCollector.Ctx.Since(te);
        _emitted = true;
    }

    /// <summary>Ölçü yakalama noktaları: kenarın iki ucu (köşe), ortası, yaysa merkezi (boru/kanal ucu → eksen noktası).</summary>
    void AddSnaps(Edge e)
    {
        Curve c;
        try { c = e.AsCurve(); } catch { return; }
        if (c == null || !c.IsBound) return;
        var snaps = _c.Scene.Snaps;
        void S(XYZ p, byte kind)
        {
            var q = _toLocal.OfPoint(p);
            snaps.Add(new SnapPoint { X = (float)q.X, Y = (float)q.Y, Z = (float)q.Z, Id = _id, Kind = kind });
        }
        try
        {
            S(c.GetEndPoint(0), 0);
            S(c.GetEndPoint(1), 0);
            S(c.Evaluate(0.5, true), 1);
            if (c is Arc arc)
            {
                // Aynı merkez (borunun iki yarım yayı) art arda iki kez eklenmesin
                var q = _toLocal.OfPoint(arc.Center);
                int n = snaps.Count;
                bool dup = false;
                for (int i = Math.Max(0, n - 8); i < n; i++)
                {
                    var s = snaps[i];
                    if (s.Kind == 2 && s.Id == _id && Math.Abs(s.X - q.X) + Math.Abs(s.Y - q.Y) + Math.Abs(s.Z - q.Z) < 1e-4) { dup = true; break; }
                }
                if (!dup) S(arc.Center, 2);
            }
        }
        catch { /* yakalama noktası çıkmazsa ölçü yüzeye/kenara yine yapışır */ }
    }

    static bool IsSmooth(Edge e)
    {
        try
        {
            var f0 = e.GetFace(0);
            var f1 = e.GetFace(1);
            if (f0 == null || f1 == null) return false;
            // Hızlı yol: iki düzlemsel yüz → normalleri doğrudan karşılaştır (Revit'e yüzey sorgusu yok).
            if (f0 is PlanarFace p0 && f1 is PlanarFace p1) return p0.FaceNormal.DotProduct(p1.FaceNormal) > SmoothEdgeCos;
            var n0 = f0.ComputeNormal(e.EvaluateOnFace(0.5, f0));
            var n1 = f1.ComputeNormal(e.EvaluateOnFace(0.5, f1));
            return n0.DotProduct(n1) > SmoothEdgeCos;
        }
        catch { return false; }
    }

    /// <summary>Filtre disiplini (0 Mimari, 1 Statik, 2 Mekanik, 3 Elektrik). Önce bilinen kümeler, sonra enum adına göre
    /// (sürümler arası eklenen kategoriler de — ör. Plumbing Equipment — doğru başlığa düşsün); kalanlar Mimari.</summary>
    static byte Discipline(BuiltInCategory bic)
    {
        if (Structure.Contains(bic)) return 1;
        if (ContainmentCats.Contains(bic) || ElectricalCats.Contains(bic)) return 3;
        if (PipeCats.Contains(bic) || DuctCats.Contains(bic) || MechEquipCats.Contains(bic)) return 2;
        var n = bic.ToString();
        if (n.Contains("Struct") || n.Contains("Rebar") || n.Contains("Truss") || n.Contains("Stiffener")) return 1;
        if (n.Contains("Electric") || n.Contains("Lighting") || n.Contains("CableTray") || n.Contains("Conduit") || n.Contains("Wire")
            || n.Contains("FireAlarm") || n.Contains("Data") || n.Contains("Communication") || n.Contains("Security")
            || n.Contains("NurseCall") || n.Contains("Telephone") || n.Contains("Audio") || n.Contains("Switch")) return 3;
        if (n.Contains("Pipe") || n.Contains("Duct") || n.Contains("Plumbing") || n.Contains("Mechanical") || n.Contains("Sprinkler")
            || n.Contains("HVAC") || n.Contains("Fabrication") || n.Contains("MEPAncillar") || n.Contains("Hanger")) return 2;
        return 0;
    }

    /// <summary>Anlık etiket için kısa boyut: Revit'in hesapladığı boyut (boru/kanal/tava/conduit ve fittingleri),
    /// yoksa genişlik × yükseklik. Proje birimiyle biçimli (AsValueString). Bulunamazsa "".</summary>
    string Label(Element e, Category cat)
    {
        string type = _doc.GetElement(e.GetTypeId()) is ElementType et
            ? (string.IsNullOrEmpty(et.FamilyName) ? et.Name : $"{et.FamilyName}: {et.Name}")
            : e.Name;
        string s = $"{cat.Name}: {type}  ·  ID {e.Id.Value}";
        return _linkName != null ? $"{s}  ·  {L.T("bağlı", "link")}: {_linkName}" : s;
    }

    void EmitFace(Face f, Mesh m, Tone tone)
    {
        int n = m.Vertices.Count;
        var pts = new P3[n];
        for (int i = 0; i < n; i++) pts[i] = P(_toLocal.OfPoint(m.Vertices[i]));
        int tc = m.NumTriangles;
        var idx = new int[tc * 3];
        for (int t = 0; t < tc; t++)
        {
            var tri = m.get_Triangle(t);
            idx[3 * t] = (int)tri.get_Index(0);
            idx[3 * t + 1] = (int)tri.get_Index(1);
            idx[3 * t + 2] = (int)tri.get_Index(2);
        }

        // Yön: üçgen sarımı yüzün dış normaline uymuyorsa çevir. GPU kesit yüzlerini (poşe) arka yüzlerden ürettiği
        // için sarımın dışa bakması önemli.
        V3? refN = null;
        if (f is PlanarFace pf) refN = V(_toLocal.OfVector(pf.FaceNormal));
        else
        {
            try
            {
                var c = (m.Vertices[idx[0]] + m.Vertices[idx[1]] + m.Vertices[idx[2]]) / 3.0;
                var ir = f.Project(c);
                if (ir != null) refN = V(_toLocal.OfVector(f.ComputeNormal(ir.UVPoint)));
            }
            catch { /* yön bilinmiyor — sarımı olduğu gibi kullan */ }
        }
        if (refN is { } rn)
        {
            for (int t = 0; t < tc; t++)
            {
                var tn = TriNormal(pts[idx[3 * t]], pts[idx[3 * t + 1]], pts[idx[3 * t + 2]]);
                if (tn.LengthSquared < 1e-18) continue;
                if (V3.DotProduct(tn, rn) < 0)
                    for (int k = 0; k < tc; k++) (idx[3 * k + 1], idx[3 * k + 2]) = (idx[3 * k + 2], idx[3 * k + 1]);
                break;
            }
        }

        // Normaller: düzlemsel yüzde tek normal; eğrisel yüzde yüz içinde yumuşatılmış (borular yuvarlak görünür).
        var nrm = new V3[n];
        if (f is PlanarFace && refN is { } pn)
        {
            for (int i = 0; i < n; i++) nrm[i] = pn;
        }
        else
        {
            for (int t = 0; t < tc; t++)
            {
                int a = idx[3 * t], b = idx[3 * t + 1], c = idx[3 * t + 2];
                var tn = TriNormal(pts[a], pts[b], pts[c]);
                nrm[a] += tn; nrm[b] += tn; nrm[c] += tn;
            }
            for (int i = 0; i < n; i++)
            {
                if (nrm[i].LengthSquared > 1e-24) nrm[i].Normalize();
                else nrm[i] = refN ?? new V3(0, 0, 1);
            }
        }

        uint baseIdx = (uint)_c.Scene.Vertices.Count;
        for (int i = 0; i < n; i++) AddVertex(pts[i], nrm[i], tone);
        var list = tone == Tone.Glass ? _c.Scene.Glass : _c.Scene.Opaque;
        foreach (var i in idx) list.Add(baseIdx + (uint)i);
    }

    void AddMesh(Mesh m)
    {
        var tone = IsGlass(m.MaterialElementId) ? Tone.Glass : _tone;
        var list = tone == Tone.Glass ? _c.Scene.Glass : _c.Scene.Opaque;
        for (int t = 0; t < m.NumTriangles; t++)
        {
            var tri = m.get_Triangle(t);
            var a = P(_toLocal.OfPoint(tri.get_Vertex(0)));
            var b = P(_toLocal.OfPoint(tri.get_Vertex(1)));
            var c = P(_toLocal.OfPoint(tri.get_Vertex(2)));
            var n = TriNormal(a, b, c);
            if (n.LengthSquared < 1e-24) continue;
            n.Normalize();
            uint bi = (uint)_c.Scene.Vertices.Count;
            AddVertex(a, n, tone); AddVertex(b, n, tone); AddVertex(c, n, tone);
            list.Add(bi); list.Add(bi + 1); list.Add(bi + 2);
        }
        _emitted = true;
    }

    void AddVertex(P3 p, V3 n, Tone tone)
    {
        _c.Scene.Vertices.Add(new SurfVertex
        {
            X = (float)p.X, Y = (float)p.Y, Z = (float)p.Z,
            NX = Pack(n.X), NY = Pack(n.Y), NZ = Pack(n.Z),
            Tone = (short)tone, Id = _id,
        });
    }

    static short Pack(double v) => (short)Math.Round(Math.Clamp(v, -1, 1) * 32767);
    static V3 TriNormal(P3 a, P3 b, P3 c) => V3.CrossProduct(b - a, c - a);
    static P3 P(XYZ p) => new(p.X, p.Y, p.Z);
    static V3 V(XYZ v) => new(v.X, v.Y, v.Z);
}

/// <summary>Ölçü yazısı: projenin uzunluk birimi ve hassasiyeti (Proje Birimleri → Uzunluk). Biçimleyici Revit'e
/// dokunmaz (çarpan/sembol toplama anında alınır) → pencerede API bağlamı dışında güvenle çağrılır.</summary>
static class LengthFormat
{
    public static Func<double, string> Make(Document doc)
    {
        try
        {
            var fo = doc.GetUnits().GetFormatOptions(SpecTypeId.Length);
            var u = fo.GetUnitTypeId();
            double acc = fo.Accuracy > 0 ? fo.Accuracy : 1;
            if (u == UnitTypeId.FeetFractionalInches || u == UnitTypeId.FractionalInches)
            {
                // Hassasiyet görüntü biriminde: ft-in'de FEET (1/16" = 1/192 ft), kesirli inçte inç → inç adımına çevir
                bool feet = u == UnitTypeId.FeetFractionalInches;
                double step = feet ? acc * 12 : acc;
                if (step <= 0 || step > 12) step = 1.0 / 16;
                return ft => FeetInches(ft, step, feet);
            }
            double f = UnitUtils.ConvertFromInternalUnits(1.0, u);
            string sym =
                u == UnitTypeId.Millimeters ? "mm" : u == UnitTypeId.Centimeters ? "cm" : u == UnitTypeId.Decimeters ? "dm" :
                u == UnitTypeId.Meters || u == UnitTypeId.MetersCentimeters ? "m" :
                u == UnitTypeId.Feet ? "ft" : u == UnitTypeId.Inches ? "in" : "";
            int dec = Math.Clamp((int)Math.Ceiling(-Math.Log10(acc) - 1e-9), 0, 4);
            var ci = System.Globalization.CultureInfo.CurrentCulture;
            return ft => (ft * f).ToString("N" + dec, ci) + (sym.Length > 0 ? " " + sym : "");
        }
        catch
        {
            var ci = System.Globalization.CultureInfo.CurrentCulture;
            return ft => (ft * 304.8).ToString("N0", ci) + " mm";
        }
    }

    static string FeetInches(double ft, double stepIn, bool feet)
    {
        bool neg = ft < 0;
        double inches = Math.Round(Math.Abs(ft) * 12 / stepIn) * stepIn;
        int whole = (int)Math.Floor(inches + 1e-9);
        double frac = inches - whole;
        int den = (int)Math.Round(1 / stepIn), num = (int)Math.Round(frac * den);
        while (num > 0 && num % 2 == 0 && den % 2 == 0) { num /= 2; den /= 2; }
        string fr = num > 0 ? $" {num}/{den}" : "";
        string s = feet ? $"{whole / 12}' - {whole % 12}{fr}\"" : $"{whole}{fr}\"";
        return neg ? "-" + s : s;
    }
}
