using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Smart3DView;

/// <summary>Kullanıcı istekleri (2026-10-07): kategori filtresi (aç/kapa), çakışmada iki renk (kırmızı/mavi),
/// sağ üstte görünüm küpü (tam sağdan/soldan/önden/üstten…), imleç altındaki elemanın anlık etiketi.</summary>
sealed unsafe partial class GlView
{
    // ---- model aç/kapa (sol üst) -------------------------------------------------------------------------------------
    // Kullanıcı isteği (2026-10-07): kategori filtresi yerine sol üstte ana dosya ve bağlı modellerin adları; her biri
    // tek tıkla komple gizlenir/gösterilir. Aynı model iki kez bağlıysa tek satır.

    ClashResult? _clashRes;
    readonly HashSet<string> _hiddenModels = new(StringComparer.CurrentCultureIgnoreCase);
    readonly List<(Rect r, string name)> _modelRects = new();
    string? _modelHover;

    /// <summary>Model listesinde bir satıra tıklandı (ad).</summary>
    public event Action<string>? ModelToggled;

    /// <summary>Sahnedeki modeller: ana model önce, sonra bağlantılar (ada göre tekil) ve eleman sayıları.</summary>
    public List<(string name, int count, bool link)> Models()
    {
        var res = new List<(string, int, bool)>();
        if (_scene == null) return res;
        var s = _scene;
        var cnt = new int[s.DocNames.Count];
        foreach (var d in s.ElemDoc) if (d < cnt.Length) cnt[d]++;
        var index = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        for (int i = 0; i < s.DocNames.Count; i++)
        {
            if (cnt[i] == 0) continue;
            string n = s.DocNames[i];
            bool link = i < s.DocKeys.Count && s.DocKeys[i] != -1;
            if (index.TryGetValue(n, out int k)) { var (nn, c, l) = res[k]; res[k] = (nn, c + cnt[i], l); }
            else { index[n] = res.Count; res.Add((n, cnt[i], link)); }
        }
        return res;
    }

    public void SetHiddenModels(IEnumerable<string> names)
    {
        _hiddenModels.Clear();
        foreach (var n in names) _hiddenModels.Add(n);
        if (_scene == null) return;
        ApplyHiddenBits();
        if (Selected > 0 && IsHidden(Selected)) Select(0);
        UploadState();
        Invalidate();
    }

    public bool IsHidden(uint id)
    {
        if (_scene == null || _hiddenModels.Count == 0 || id == 0 || id > _scene.ElemDoc.Count) return false;
        int d = _scene.ElemDoc[(int)id - 1];
        return d < _scene.DocNames.Count && _hiddenModels.Contains(_scene.DocNames[d]);
    }

    /// <summary>Çakışma denetimi için gizli eleman maskesi (indeks = id).</summary>
    public bool[]? HiddenMask()
    {
        if (_scene == null || _hiddenModels.Count == 0) return null;
        var m = new bool[_scene.Labels.Count + 1];
        for (uint id = 1; id < m.Length; id++) m[id] = IsHidden(id);
        return m;
    }

    string? ModelHit(Point p)
    {
        foreach (var (r, name) in _modelRects) if (r.Contains(p)) return name;
        return null;
    }

    /// <summary>Modelin "Renkli" tondaki renk sırası: adların (büyük/küçük harf duyarsız) ilk görünüş sırası, 16'da döner.</summary>
    int ModelColorIndex(string name)
    {
        var seen = new List<string>();
        foreach (var n in _scene?.DocNames ?? new List<string>())
        {
            if (seen.Exists(x => string.Equals(x, n, StringComparison.CurrentCultureIgnoreCase))) continue;
            if (string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase)) return seen.Count % ModelRgb.Length;
            seen.Add(n);
        }
        return 0;
    }

    /// <summary>Renkli tonda listede modelin rengi gösterilir (lejant).</summary>
    Color? ModelSwatch(string name)
    {
        if (!_pal.Colored || _pal.Detailed) return null;
        int c = ModelRgb[ModelColorIndex(name)];
        return Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }

    void DrawModelList()
    {
        _modelRects.Clear();
        if (_pText == 0) return;
        var models = Models();
        if (models.Count == 0) return;
        double x = 10 * Dpi, y = 10 * Dpi;
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        foreach (var (name, count, link) in models)
        {
            bool hidden = _hiddenModels.Contains(name);
            string t = (hidden ? "☐  " : "☑  ") + (link ? "🔗 " : "") + name + $"  ({count:N0})";
            var sw = ModelSwatch(name);
            var (_, w, h) = LabelTexture(t, _modelHover == name ? LabelStyle.Measure : LabelStyle.Tag, sw);
            DrawTextAt(new Point(x, y), t, _modelHover == name ? LabelStyle.Measure : LabelStyle.Tag, center: false, sw);
            _modelRects.Add((new Rect(x, y, w, h), name));
            y += h + 3 * Dpi;
        }
        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
    }

    void ApplyHiddenBits()
    {
        if (_scene == null) return;
        var s = _scene;
        var doc = new int[s.DocNames.Count];
        for (int d = 0; d < doc.Length; d++) doc[d] = ModelColorIndex(s.DocNames[d]) << 3;
        for (int i = 0; i < s.ElemColor.Count && 2 * (i + 1) < _state.Length; i++)
        {
            int dc = i < s.ElemDoc.Count && s.ElemDoc[i] < doc.Length ? doc[s.ElemDoc[i]] : 0;
            _state[2 * (i + 1)] = (byte)((s.ElemColor[i] & 7) | dc | (IsHidden((uint)(i + 1)) ? 0x80 : 0));
        }
    }

    /// <summary>Çakışmanın iki tarafı iki renk: her çakışan çiftte grubu küçük olan (boru &lt; kanal &lt; tava &lt;
    /// elektrik &lt; mekanik cihaz &lt; taşıyıcı) kırmızı, diğeri mavi. Çakışan bir eleman seçiliyse: kendisi kırmızı,
    /// çakıştıkları mavi, diğer çakışmalar soluk.</summary>
    void ApplyClashBits()
    {
        for (int i = 1; i < _state.Length / 2; i++) _state[2 * i + 1] &= 0xFC;   // detay rengi (üst 6 bit) kalsın
        var r = _clashRes;
        if (r == null || _scene == null) return;
        var grp = _scene.ElemGroup;
        int G(uint id) => id > 0 && id <= grp.Count ? grp[(int)id - 1] : 0;
        void Set(uint id, byte v) { if (2 * id + 1 < _state.Length) _state[2 * id + 1] = (byte)((_state[2 * id + 1] & 0xFC) | v); }
        if (Selected > 0 && r.Partners.TryGetValue(Selected, out var mine))
        {
            Set(Selected, 1);
            foreach (var p in mine) Set(p, 2);
            return;
        }
        foreach (var id in r.Elements)
        {
            byte c = 2;
            if (r.Partners.TryGetValue(id, out var ps))
                foreach (var p in ps) if (G(p) > G(id)) { c = 1; break; }
            Set(id, c);
        }
    }

    // ---- görünüm küpü ---------------------------------------------------------------------------------------------

    Point _lastMouse;
    Vector3D? _cubeHover;   // imlecin üstünde olduğu yön (yüz/kenar/köşe), küp koordinatında (-1/0/1)

    double Dpi { get { try { return VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { return 1; } } }
    double CubeSize => 88 * Dpi;
    Point CubeCenter => new(_w - 14 * Dpi - CubeSize / 2, 14 * Dpi + CubeSize / 2);
    double CubeK => CubeSize * 0.28;

    // küp ekseni → kutu-yerel: proje X, proje Y, Z
    Vector3D CubeAxis(int i) => i == 0 ? Axis(0) : i == 1 ? Axis(1) : new Vector3D(0, 0, 1);
    Vector3D CubeToLocal(Vector3D c) => CubeAxis(0) * c.X + CubeAxis(1) * c.Y + CubeAxis(2) * c.Z;

    Point CubeProject(Vector3D c, out double depth)
    {
        var (r, u) = Basis();
        var v = CubeToLocal(c);
        depth = Vector3D.DotProduct(v, _look);
        var cc = CubeCenter;
        return new Point(cc.X + Vector3D.DotProduct(v, r) * CubeK, cc.Y - Vector3D.DotProduct(v, u) * CubeK);
    }

    /// <summary>Küpte tıklanan/üstünde durulan bölge: yüz (tek eksen), kenar (iki), köşe (üç) — null: küp dışı.</summary>
    Vector3D? CubeHit(Point p)
    {
        var cc = CubeCenter;
        if (Math.Abs(p.X - cc.X) > CubeSize / 2 || Math.Abs(p.Y - cc.Y) > CubeSize / 2) return null;
        var (r, u) = Basis();
        var o = r * ((p.X - cc.X) / CubeK) + u * ((cc.Y - p.Y) / CubeK) - _look * 10;
        var d = _look;
        // küp koordinatına çevir
        var O = new double[3]; var D = new double[3];
        for (int i = 0; i < 3; i++) { O[i] = Vector3D.DotProduct(o, CubeAxis(i)); D[i] = Vector3D.DotProduct(d, CubeAxis(i)); }
        double t0 = -1e9, t1 = 1e9;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(D[i]) < 1e-12) { if (Math.Abs(O[i]) > 1) return null; continue; }
            double a = (-1 - O[i]) / D[i], b = (1 - O[i]) / D[i];
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
        }
        if (t0 > t1) return null;
        var h = new double[3];
        for (int i = 0; i < 3; i++) h[i] = O[i] + D[i] * t0;
        double S(double x) => Math.Abs(x) > 0.55 ? Math.Sign(x) : 0;
        var dir = new Vector3D(S(h[0]), S(h[1]), S(h[2]));
        return dir.LengthSquared < 0.5 ? null : dir;
    }

    // Küpün altında "⌂ 3B": izometrik görünüme dön (tam yüz görünümünde küpte tek yüz kalınca başka yöne geçmek için)
    Rect _homeRect = Rect.Empty;
    bool HomeHit(Point p) => !_homeRect.IsEmpty && _homeRect.Contains(p);

    void CubeClick(Vector3D dir)
    {
        var w = CubeToLocal(dir);
        w.Normalize();
        FitAll(-w);   // o yönden bakış (yukarı +Z korunur), bütün sahne sığdırılır
        Interact();
    }

    static readonly (Vector3D n, string tr, string en)[] CubeFaces =
    {
        (new Vector3D(0, 0, 1), "ÜST", "TOP"), (new Vector3D(0, 0, -1), "ALT", "BOTTOM"),
        (new Vector3D(0, -1, 0), "ÖN", "FRONT"), (new Vector3D(0, 1, 0), "ARKA", "BACK"),
        (new Vector3D(1, 0, 0), "SAĞ", "RIGHT"), (new Vector3D(-1, 0, 0), "SOL", "LEFT"),
    };

    void DrawViewCube()
    {
        if (_pText == 0) return;
        var tris = new List<float>();
        var lines = new List<float>();
        void V(List<float> l, Point p, float r, float g, float b, float a) { l.Add((float)p.X); l.Add((float)p.Y); l.Add(0); l.Add(r); l.Add(g); l.Add(b); l.Add(a); }
        bool dark = _pal.Dark || Luma(_pal.BgBottom) < 0.45;
        // yüzler: arkadan öne (ressam algoritması); yalnız kameraya bakanlar
        var faces = new List<(double depth, int fi)>();
        for (int fi = 0; fi < 6; fi++)
        {
            var nl = CubeToLocal(CubeFaces[fi].n);
            if (Vector3D.DotProduct(nl, _look) >= -1e-6) continue;
            faces.Add((Vector3D.DotProduct(nl, _look), fi));
        }
        faces.Sort((a, b) => b.depth.CompareTo(a.depth));
        foreach (var (_, fi) in faces)
        {
            var n = CubeFaces[fi].n;
            // yüzün 4 köşesi: n'ye dik iki eksen
            var a1 = Math.Abs(n.X) > 0.5 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
            var a2 = Vector3D.CrossProduct(n, a1);
            var c = new[] { n - a1 - a2, n + a1 - a2, n + a1 + a2, n - a1 + a2 };
            var sp = new Point[4];
            for (int k = 0; k < 4; k++) sp[k] = CubeProject(c[k], out _);
            double facing = -Vector3D.DotProduct(CubeToLocal(n), _look);
            bool hot = _cubeHover is { } hv && Vector3D.DotProduct(hv, n) > 0.5 && hv.LengthSquared < 1.5;
            float sh = (float)(0.80 + 0.17 * facing);
            float fr = hot ? 0.55f : sh * 0.93f, fg = hot ? 0.75f : sh * 0.94f, fb = hot ? 1f : sh * 0.96f;
            V(tris, sp[0], fr, fg, fb, 0.92f); V(tris, sp[1], fr, fg, fb, 0.92f); V(tris, sp[2], fr, fg, fb, 0.92f);
            V(tris, sp[0], fr, fg, fb, 0.92f); V(tris, sp[2], fr, fg, fb, 0.92f); V(tris, sp[3], fr, fg, fb, 0.92f);
            float lc = 0.25f;
            for (int k = 0; k < 4; k++) { V(lines, sp[k], lc, lc, lc, 0.9f); V(lines, sp[(k + 1) % 4], lc, lc, lc, 0.9f); }
        }
        // kenar/köşe vurgusu (yüz değilse küçük nokta)
        int ptStart = tris.Count / 7;
        if (_cubeHover is { } h && h.LengthSquared > 1.5) V(tris, CubeProject(h * 0.98, out _), 0.15f, 0.45f, 0.95f, 1);

        var mvp = new float[16];
        mvp[0] = 2f / _w; mvp[3] = -1; mvp[5] = -2f / _h; mvp[7] = 1; mvp[15] = 1;
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.UseProgram(_pOver);
        fixed (float* m = mvp) GL.UniformMatrix4fv(oMvp, 1, 1, m);
        GL.BindVertexArray(_ovao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _ovbo);
        var all = new float[tris.Count + lines.Count];
        tris.CopyTo(all); lines.CopyTo(all, tris.Count);
        fixed (float* p = all) GL.BufferData(GL.ARRAY_BUFFER, all.Length * 4, p, GL.DYNAMIC_DRAW);
        GL.Uniform1i(oPoint, 0);
        GL.DrawArrays(GL.TRIANGLES, 0, ptStart);
        GL.LineWidth(1f);
        GL.DrawArrays(GL.LINES, tris.Count / 7, lines.Count / 7);
        if (tris.Count / 7 > ptStart)
        {
            GL.Enable(GL.PROGRAM_POINT_SIZE); GL.Enable(GL.POINT_SPRITE);
            GL.Uniform1i(oPoint, 1); GL.Uniform1f(oSize, (float)(12 * Dpi));
            GL.DrawArrays(GL.POINTS, ptStart, 1);
            GL.Disable(GL.PROGRAM_POINT_SIZE); GL.Disable(GL.POINT_SPRITE);
        }
        GL.BindVertexArray(0);
        GL.BindBuffer(GL.ARRAY_BUFFER, 0);
        {
            var cc = CubeCenter;
            var hp = new Point(cc.X, cc.Y + CubeSize / 2 + 4 * Dpi);
            var txt = "⌂ " + L.T("3B", "3D");
            var (_, hw, hh) = LabelTexture(txt, LabelStyle.Tag);
            _homeRect = new Rect(hp.X - hw / 2.0, hp.Y, hw, hh);
            DrawTextAt(new Point(hp.X, hp.Y + hh / 2.0), txt, LabelStyle.Tag, center: true);
        }
        // yüz adları (yeterince karşıya bakan yüzlerde)
        foreach (var (_, fi) in faces)
        {
            var f = CubeFaces[fi];
            if (-Vector3D.DotProduct(CubeToLocal(f.n), _look) < 0.3) continue;
            var cp = CubeProject(f.n, out _);
            DrawTextAt(cp, L.T(f.tr, f.en), LabelStyle.Cube, center: true);
        }
        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
    }

    // ---- anlık etiket ---------------------------------------------------------------------------------------------

    bool _tagMode;
    uint _hoverId;
    Point _hoverPt;
    long _lastTagTicks;
    static readonly IntPtr TagTimer = (IntPtr)3;

    /// <summary>İmlecin altındaki elemanın kategori + boyutunu imlecin yanında göster.</summary>
    public bool TagMode
    {
        get => _tagMode;
        set { _tagMode = value; _hoverId = 0; Invalidate(); }
    }

    void TagHover(Point p, bool force = false)
    {
        _hoverPt = p;
        long now = Environment.TickCount64;
        if (!force && now - _lastTagTicks < 30) { Win32.SetTimer(_hwnd, TagTimer, 35, IntPtr.Zero); return; }
        _lastTagTicks = now;
        uint id = Pick(p).id;
        if (id != _hoverId) _hoverId = id;
        Invalidate();
    }

    string? HoverText(uint id)
    {
        if (_scene == null || id == 0 || id > _scene.Labels.Count) return null;
        int i = (int)id - 1;
        string cat = i < _scene.ElemCat.Count ? _scene.CatNames[_scene.ElemCat[i]] : "";
        string tag = i < _scene.ElemTag.Count ? _scene.ElemTag[i] : "";
        // etiketin "Kategori: Aile: Tip" kısmından tip adı
        var lab = _scene.Labels[i];
        int cut = lab.IndexOf("  ·  ", StringComparison.Ordinal);
        var head = cut > 0 ? lab[..cut] : lab;
        int c2 = head.LastIndexOf(": ", StringComparison.Ordinal);
        var type = c2 > 0 ? head[(c2 + 2)..] : "";
        var s = cat;
        if (type.Length > 0 && type != cat) s += " · " + type;
        if (tag.Length > 0) s += "  ·  " + tag;
        return s;
    }

    void DrawHoverTag()
    {
        if (!_tagMode || _hoverId == 0 || _measureMode || HoverText(_hoverId) is not { } t) return;
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        DrawTextAt(new Point(_hoverPt.X + 16 * Dpi, _hoverPt.Y + 20 * Dpi), t, LabelStyle.Tag, center: false);
        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
    }
}
