using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Smart3DView;

/// <summary>Bir sahnenin Revit tarafı: hangi belge/görünüm/çerçeveden okunduğu (yeniden okuma ve görüntü kaydı için).</summary>
sealed class RevitContext
{
    public readonly Document Doc;
    public readonly ElementId ViewId;
    public readonly ClipBox Box;
    public RevitContext(Document doc, ElementId viewId, ClipBox box) { Doc = doc; ViewId = viewId; Box = box; }
}

/// <summary>Modeless pencere Revit API'sini doğrudan çağıramaz; istekler kuyruğa alınır ve Revit uygun olduğunda
/// ExternalEvent ile ana iş parçacığında çalıştırılır. Olay App.OnStartup'ta bir kez oluşturulur (yerel değişkende
/// tutulan ExternalEvent toplanıp hiç çalışmayabiliyor).</summary>
sealed class RevitBridge : IExternalEventHandler
{
    static readonly Queue<Action<UIApplication>> Pending = new();
    public static ExternalEvent? Event;

    public static void Post(Action<UIApplication> action)
    {
        Pending.Enqueue(action);
        Event?.Raise();
    }

    public void Execute(UIApplication app)
    {
        while (Pending.Count > 0)
        {
            var a = Pending.Dequeue();
            try { a(app); }
            catch (Exception ex) { TaskDialog.Show(Product.Name, ex.Message); }
        }
    }

    public string GetName() => Product.Name;
}

sealed class RevitHost : IViewerHost
{
    public static readonly RevitHost Instance = new();

    public void Recollect(object context, double[] min, double[] max, Action<SceneData?, string?> done)
    {
        var ctx = (RevitContext)context;
        RevitBridge.Post(_ =>
        {
            if (!ctx.Doc.IsValidObject) { done(null, L.T("Model kapatılmış.", "The model has been closed.")); return; }
            var box = new ClipBox
            {
                Frame = ctx.Box.Frame,
                Min = new XYZ(min[0], min[1], min[2]),
                Max = new XYZ(max[0], max[1], max[2]),
                Source = ctx.Box.Source,
            };
            var view = ctx.Doc.GetElement(ctx.ViewId) as View;
            var scene = SceneCollector.Collect(ctx.Doc, view, box);
            scene.Context = new RevitContext(ctx.Doc, ctx.ViewId, box);
            done(scene, null);
        });
    }


    // Özellik panelinde üstte gösterilen parametreler (sırasıyla); değer proje birimiyle (AsValueString).
    static readonly BuiltInParameter[] KeyParams =
    {
        BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
        BuiltInParameter.RBS_CALCULATED_SIZE,
        BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM,
        BuiltInParameter.RBS_CURVE_WIDTH_PARAM, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,
        BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, BuiltInParameter.RBS_PIPE_OUTER_DIAMETER, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM,
        BuiltInParameter.CURVE_ELEM_LENGTH,
        BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM,
        BuiltInParameter.RBS_OFFSET_PARAM, BuiltInParameter.RBS_CTC_BOTTOM_ELEVATION, BuiltInParameter.RBS_CTC_TOP_ELEVATION,
        BuiltInParameter.INSTANCE_ELEVATION_PARAM, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM,
        BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM,
        BuiltInParameter.RBS_SYSTEM_NAME_PARAM, BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS, BuiltInParameter.RBS_REFERENCE_LINING_THICKNESS,
        BuiltInParameter.ALL_MODEL_MARK, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
    };

    static string? Val(Parameter p)
    {
        if (p == null || !p.HasValue) return null;
        string? s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
        if (string.IsNullOrWhiteSpace(s) && p.StorageType == StorageType.ElementId)
        {
            var id = p.AsElementId();
            if (id != ElementId.InvalidElementId && p.Element?.Document.GetElement(id) is Element r) s = r.Name;
        }
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    public void GetInfo(object context, SceneData scene, uint sceneId, Action<List<(string name, string value, bool key)>?, string?> done)
    {
        RevitBridge.Post(_ =>
        {
            int i = (int)sceneId - 1;
            if (i < 0 || i >= scene.ElemRevitId.Count) { done(null, "?"); return; }
            if (scene.Docs[scene.ElemDoc[i]] is not Document d || !d.IsValidObject) { done(null, L.T("Model kapatılmış.", "The model has been closed.")); return; }
            var e = d.GetElement(new ElementId(scene.ElemRevitId[i]));
            if (e == null) { done(null, L.T("Eleman artık yok (silinmiş olabilir) — Yenile.", "The element no longer exists (maybe deleted) — Reload.")); return; }
            var rows = new List<(string, string, bool)>();
            var seen = new HashSet<string>();
            rows.Add((L.T("Kategori", "Category"), e.Category?.Name ?? "", true));
            rows.Add(("ID", e.Id.Value.ToString() + (scene.ElemDoc[i] > 0 ? "  (" + d.Title + ")" : ""), true));
            foreach (var bip in KeyParams)
            {
                Parameter? p = null;
                try { p = e.get_Parameter(bip); } catch { }
                if (p == null || Val(p) is not { } v) continue;
                var n = p.Definition?.Name ?? bip.ToString();
                if (seen.Add(n)) rows.Add((n, v, true));
            }
            var rest = new List<(string, string, bool)>();
            foreach (Parameter p in e.Parameters)
            {
                var n = p.Definition?.Name;
                if (string.IsNullOrEmpty(n) || seen.Contains(n) || Val(p) is not { } v) continue;
                seen.Add(n);
                rest.Add((n, v, false));
            }
            rest.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.CurrentCultureIgnoreCase));
            rows.AddRange(rest);
            done(rows, null);
        });
    }

    public void ShowInRevit(object context, SceneData scene, uint sceneId, Action<string?> done)
    {
        RevitBridge.Post(app =>
        {
            int i = (int)sceneId - 1;
            if (i < 0 || i >= scene.ElemRevitId.Count) { done("?"); return; }
            if (scene.ElemDoc[i] != 0) { done(L.T("Bağlı modeldeki eleman Revit'te seçilemez.", "Elements of linked models cannot be selected in Revit.")); return; }
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null || scene.Docs[0] is not Document d || !uidoc.Document.Equals(d)) { done(L.T("Bu modeli Revit'te etkin pencere yapın.", "Make this model the active window in Revit.")); return; }
            var id = new ElementId(scene.ElemRevitId[i]);
            if (d.GetElement(id) == null) { done(L.T("Eleman artık yok — Yenile.", "The element no longer exists — Reload.")); return; }
            var ids = new List<ElementId> { id };
            uidoc.Selection.SetElementIds(ids);
            try { uidoc.ShowElements(ids); } catch { }
            done(null);
        });
    }

    public bool IsFromDoc(object context, object doc) => context is RevitContext c && doc is Document d && c.Doc.IsValidObject && c.Doc.Equals(d);
    public void SaveImage(object context, string pngPath, Action<string> done)
    {
        var ctx = (RevitContext)context;
        RevitBridge.Post(_ =>
        {
            if (!ctx.Doc.IsValidObject) { done(L.T("Model kapatılmış.", "The model has been closed.")); return; }
            string name = $"{Product.Name} - {DateTime.Now:yyyy-MM-dd HH.mm.ss}";
            using var t = new Transaction(ctx.Doc, Product.Name + L.T(" görüntü", " image"));
            t.Start();
            // Render penceresindeki "Save to Project" ile aynı tür: Proje Tarayıcısı'nda Renderings altında görünür.
            var view = ImageView.Create(ctx.Doc, new ImageTypeOptions(pngPath, false, ImageTypeSource.Import));
            try { view.Name = name; } catch { /* ad çakışırsa Revit'in verdiği ad kalsın */ }
            t.Commit();
            done(L.T($"Görüntü kaydedildi: Proje Tarayıcısı → Renderings → \"{view.Name}\"",
                     $"Image saved: Project Browser → Renderings → \"{view.Name}\""));
        });
    }
}
