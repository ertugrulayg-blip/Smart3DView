using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace Smart3DView;

/// <summary>Tüm model → WebModel, Revit'in kendi aktarıcısıyla (CustomExporter; Navisworks NWC de böyle yapar —
/// kullanıcı isteği 2026-10-08). Revit ekrana çizdiği hazır üçgenleri (OnPolymesh) akıtır: yüz yüz üçgenleme ve kenar
/// hesabı yok. Aynı aile geometrisi (SymbolGeometryId) bir kez alınır, sonraki örnekler atlanıp yalnız konumla bağlanır.
/// Bağlı modeller ayrı parça; sürüm kimliğiyle önbellekte varsa hiç okunmaz.</summary>
sealed class WebExportContext : IExportContext
{
    public const int LevelOfDetail = 6;   // Revit'in olağanı 8; 6 belirgin biçimde hızlı ve hafif, borular yine yuvarlak

    readonly Document _host;
    readonly bool _includeHost;
    readonly Func<Document, bool> _includeLink;
    readonly ReadControl _ctl;
    public readonly WebModel Model = new();

    // Arka plandaki ilerleme penceresi okur (yalnız sayı ve metin).
    public volatile int Done;
    public volatile string CurrentModel = "";

    sealed class PartState
    {
        public WebPart? Part;          // null: bu belge aktarılmıyor (ana model seçilmedi)
        public Document Doc = null!;
        public string? CacheFile;
        public readonly Dictionary<string, List<int>> Symbols = new();   // aynı aile geometrisi → mesh'ler
    }

    readonly Stack<PartState> _parts = new();
    readonly Stack<Transform> _xf = new();      // parçanın kendi koordinatına göre geçerli dönüşüm
    readonly Stack<int> _inst = new();          // örnek düğümü: 0 olağan, 1 geometrisi yakalanıyor, 2 önbellekten (atlandı)
    readonly Stack<bool> _links = new();        // bağlı model düğümü: true = yeni parça açıldı
    readonly Dictionary<string, int> _partByDoc = new();   // bu aktarımda bitmiş parçalar (aynı link iki kez yerleşmişse)

    WebElement? _el;
    Dictionary<(float, float, float, float), Builder>? _elGeo;
    int _depth;                                  // eleman içindeki örnek iç içeliği
    string? _capKey;
    Transform? _capInv;
    double[]? _capM;
    Dictionary<(float, float, float, float), Builder>? _capGeo;
    (float r, float g, float b, float a) _color = (0.8f, 0.8f, 0.8f, 1f);

    sealed class Builder
    {
        public readonly List<float> P = new(), N = new();
        public readonly List<int> I = new();
        public WebMesh ToMesh((float r, float g, float b, float a) c) =>
            new() { R = c.r, G = c.g, B = c.b, A = c.a, P = P.ToArray(), N = N.ToArray(), I = I.ToArray() };
    }

    public WebExportContext(Document host, bool includeHost, Func<Document, bool> includeLink, ReadControl ctl)
    {
        _host = host; _includeHost = includeHost; _includeLink = includeLink; _ctl = ctl;
        Model.Name = host.Title;
    }

    public bool Start()
    {
        var ps = new PartState { Doc = _host };
        if (_includeHost)
        {
            ps.Part = new WebPart { Name = _host.Title };
            Model.Parts.Add(ps.Part);
            Model.Placements.Add((Model.Parts.Count - 1, null));
        }
        _parts.Push(ps);
        _xf.Push(Transform.Identity);
        CurrentModel = _host.Title;
        return true;
    }

    public void Finish() { }

    public bool IsCanceled() => _ctl.Cancel;

    public RenderNodeAction OnViewBegin(ViewNode node)
    {
        try { node.LevelOfDetail = LevelOfDetail; } catch { }
        return RenderNodeAction.Proceed;
    }

    public void OnViewEnd(ElementId elementId) { }

    // ---- bağlı modeller ------------------------------------------------------------------------------------------

    static string? CacheFileFor(Document d)
    {
        try
        {
            var v = Document.GetDocumentVersion(d);
            if (v == null) return null;
            return WebPartCache.PathFor($"{d.Title}_{v.VersionGUID:N}_{v.NumberOfSaves}_lod{LevelOfDetail}_v1");
        }
        catch { return null; }
    }

    public RenderNodeAction OnLinkBegin(LinkNode node)
    {
        var d = node.GetDocument();
        var t = _xf.Peek().Multiply(node.GetTransform());
        if (d == null || !_includeLink(d)) { _links.Push(false); return RenderNodeAction.Skip; }

        // Bu aktarımda zaten okunduysa ya da önbellekte varsa: yalnız yerleşim eklenir, link okunmaz.
        if (!_partByDoc.TryGetValue(d.Title, out int pi))
        {
            var file = CacheFileFor(d);
            if (file != null && File.Exists(file) && WebPart.Load(file) is { } cached)
            {
                Model.Parts.Add(cached);
                pi = Model.Parts.Count - 1;
                _partByDoc[d.Title] = pi;
            }
            else pi = -1;
        }
        if (pi >= 0)
        {
            Model.Placements.Add((pi, ToArr(t)));
            _links.Push(false);
            return RenderNodeAction.Skip;
        }

        var ps = new PartState { Doc = d, Part = new WebPart { Name = d.Title }, CacheFile = CacheFileFor(d) };
        Model.Parts.Add(ps.Part);
        Model.Placements.Add((Model.Parts.Count - 1, ToArr(t)));
        _parts.Push(ps);
        _xf.Push(Transform.Identity);   // geometri bağlı modelin kendi koordinatında; yerleşim ayrı tutulur
        _links.Push(true);
        CurrentModel = d.Title;
        return RenderNodeAction.Proceed;
    }

    public void OnLinkEnd(LinkNode node)
    {
        if (!_links.Pop()) return;
        _xf.Pop();
        var ps = _parts.Pop();
        _partByDoc[ps.Doc.Title] = Model.Parts.IndexOf(ps.Part!);
        if (ps.CacheFile != null && !_ctl.Cancel)
        {
            try { ps.Part!.Save(ps.CacheFile); } catch { try { File.Delete(ps.CacheFile); } catch { } }
        }
        CurrentModel = _parts.Peek().Doc.Title;
    }

    // ---- elemanlar -----------------------------------------------------------------------------------------------

    public RenderNodeAction OnElementBegin(ElementId elementId)
    {
        _el = null;
        if (_ctl.Cancel) return RenderNodeAction.Skip;
        var ps = _parts.Peek();
        Element? e;
        try { e = ps.Doc.GetElement(elementId); } catch { return RenderNodeAction.Skip; }
        // Bağlı model örneği de bir eleman: içine girilsin (OnLinkBegin), kendisi eleman olarak yazılmaz.
        if (e is RevitLinkInstance) return RenderNodeAction.Proceed;
        if (ps.Part == null) return RenderNodeAction.Skip;   // ana model seçilmedi
        if (e?.Category is not { } cat || cat.CategoryType != CategoryType.Model) return RenderNodeAction.Skip;
        var bic = cat.BuiltInCategory;
        if (DocPass.IsExcluded(bic) || e is ImportInstance) return RenderNodeAction.Skip;

        string type = ps.Doc.GetElement(e.GetTypeId()) is ElementType et
            ? (string.IsNullOrEmpty(et.FamilyName) ? et.Name : $"{et.FamilyName}: {et.Name}") : e.Name;
        var el = new WebElement { Name = $"{cat.Name}: {type}" };
        el.Extras["category"] = cat.Name;
        el.Extras["revitId"] = elementId.Value;
        try { el.Extras["uniqueId"] = e.UniqueId; } catch { }
        var (group, detail, host) = DocPass.WebClass(ps.Doc, e, bic);
        el.Extras["group"] = group;
        el.Extras["detail"] = (int)detail;
        if (host != null) el.Extras["hostId"] = host.Value;
        _el = el;
        _elGeo = new();
        _depth = 0;
        Done++;
        return RenderNodeAction.Proceed;
    }

    public void OnElementEnd(ElementId elementId)
    {
        if (_el == null) return;
        var part = _parts.Peek().Part!;
        foreach (var (c, b) in _elGeo!)
        {
            if (b.I.Count == 0) continue;
            part.Meshes.Add(b.ToMesh(c));
            _el.Geoms.Add(new WebGeom { Mesh = part.Meshes.Count - 1 });
        }
        if (_el.Geoms.Count > 0) part.Elements.Add(_el);
        _el = null; _elGeo = null;
    }

    // ---- aile örnekleri: aynı geometri bir kez ---------------------------------------------------------------------

    public RenderNodeAction OnInstanceBegin(InstanceNode node)
    {
        var t = _xf.Peek().Multiply(node.GetTransform());
        if (_el != null && _depth == 0 && _capKey == null)
        {
            string? key = null;
            try { key = node.GetSymbolGeometryId().AsUniqueIdentifier(); } catch { }
            var ps = _parts.Peek();
            if (key != null && ps.Symbols.TryGetValue(key, out var meshIdx))
            {
                var m = ToArr(t);
                foreach (var mi in meshIdx) _el.Geoms.Add(new WebGeom { Mesh = mi, M = m });
                _xf.Push(t);
                _inst.Push(2);
                return RenderNodeAction.Skip;   // geometri zaten alındı; OnInstanceEnd yine çağrılır
            }
            if (key != null)
            {
                _capKey = key; _capInv = t.Inverse; _capM = ToArr(t); _capGeo = new();
                _xf.Push(t); _inst.Push(1); _depth++;
                return RenderNodeAction.Proceed;
            }
        }
        _xf.Push(t); _inst.Push(0); _depth++;
        return RenderNodeAction.Proceed;
    }

    public void OnInstanceEnd(InstanceNode node)
    {
        int a = _inst.Pop();
        _xf.Pop();
        if (a == 2) return;
        _depth--;
        if (a != 1) return;
        // Yakalanan aile geometrisi: parçaya eklenir, bu eleman ve sonraki aynı örnekler ona bağlanır.
        var ps = _parts.Peek();
        var list = new List<int>();
        foreach (var (c, b) in _capGeo!)
        {
            if (b.I.Count == 0) continue;
            ps.Part!.Meshes.Add(b.ToMesh(c));
            list.Add(ps.Part.Meshes.Count - 1);
        }
        ps.Symbols[_capKey!] = list;
        foreach (var mi in list) _el?.Geoms.Add(new WebGeom { Mesh = mi, M = _capM });
        _capKey = null; _capInv = null; _capM = null; _capGeo = null;
    }

    // ---- geometri ve malzeme -------------------------------------------------------------------------------------

    public void OnMaterial(MaterialNode node)
    {
        try
        {
            var c = node.Color;
            _color = c != null && c.IsValid
                ? (c.Red / 255f, c.Green / 255f, c.Blue / 255f, (float)Math.Clamp(1 - node.Transparency, 0.05, 1))
                : (0.8f, 0.8f, 0.8f, 1f);
        }
        catch { _color = (0.8f, 0.8f, 0.8f, 1f); }
    }

    public void OnPolymesh(PolymeshTopology mesh)
    {
        if (_el == null) return;
        // Yakalamada aile koordinatı (örneğin kendi dönüşümüne göre), değilse parça koordinatı.
        var t = _capKey != null ? _capInv!.Multiply(_xf.Peek()) : _xf.Peek();
        var geo = _capKey != null ? _capGeo! : _elGeo!;
        var key = (MathF.Round(_color.r, 3), MathF.Round(_color.g, 3), MathF.Round(_color.b, 3), MathF.Round(_color.a, 3));
        if (!geo.TryGetValue(key, out var b)) geo[key] = b = new Builder();

        var pts = mesh.GetPoints();
        var normals = mesh.GetNormals();
        var dist = mesh.DistributionOfNormals;
        int baseIdx = b.P.Count / 3;
        var nsum = new double[pts.Count * 3];
        for (int i = 0; i < pts.Count; i++)
        {
            var p = t.OfPoint(pts[i]);
            b.P.Add((float)p.X); b.P.Add((float)p.Y); b.P.Add((float)p.Z);
        }
        var facets = mesh.GetFacets();
        for (int f = 0; f < facets.Count; f++)
        {
            var fc = facets[f];
            b.I.Add(baseIdx + fc.V1); b.I.Add(baseIdx + fc.V2); b.I.Add(baseIdx + fc.V3);
            if (dist == DistributionOfNormals.OnEachFacet && f < normals.Count)
            {
                var n = normals[f];
                foreach (var v in new[] { fc.V1, fc.V2, fc.V3 }) { nsum[3 * v] += n.X; nsum[3 * v + 1] += n.Y; nsum[3 * v + 2] += n.Z; }
            }
        }
        for (int i = 0; i < pts.Count; i++)
        {
            XYZ n = dist switch
            {
                DistributionOfNormals.AtEachPoint when i < normals.Count => normals[i],
                DistributionOfNormals.OnePerFace when normals.Count > 0 => normals[0],
                _ => new XYZ(nsum[3 * i], nsum[3 * i + 1], nsum[3 * i + 2]),
            };
            var w = t.OfVector(n);
            double len = w.GetLength();
            if (len < 1e-12) { b.N.Add(0); b.N.Add(0); b.N.Add(1); continue; }
            b.N.Add((float)(w.X / len)); b.N.Add((float)(w.Y / len)); b.N.Add((float)(w.Z / len));
        }
    }

    public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;
    public void OnFaceEnd(FaceNode node) { }
    public void OnLight(LightNode node) { }
    public void OnRPC(RPCNode node) { }

    static double[] ToArr(Transform t) => new[]
    {
        t.BasisX.X, t.BasisX.Y, t.BasisX.Z, t.BasisY.X, t.BasisY.Y, t.BasisY.Z,
        t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z, t.Origin.X, t.Origin.Y, t.Origin.Z,
    };
}
