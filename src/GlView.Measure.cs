using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Smart3DView;

/// <summary>Ölçü aracı (kullanıcı isteği, 2026-10-07): iki nokta arası 3B mesafe. İmleç elemanların köşelerine, kenar
/// ortalarına, yay merkezlerine (boru/kanal ekseni), kenarlara ve yüzeylerine yapışır. X/Y/Z ile ikinci nokta
/// proje ekseni boyunca kilitlenir (yalnız o eksendeki mesafe ölçülür). Ölçüler ekranda kalır (Görüntü al'a da girer);
/// Esc: süren ölçüyü iptal / hepsini sil / araçtan çık, Delete: hepsini sil, Backspace: sonuncuyu sil.</summary>
sealed unsafe partial class GlView
{
    enum SnapKind { End, Mid, Center, Edge, Face }

    readonly record struct Snap(Point3D P, SnapKind Kind);

    bool _measureMode;
    int _lockAx = -1;                    // -1 serbest, 0/1/2 = proje X/Y/Z
    Snap? _snap;                         // imlecin yapıştığı nokta
    Point3D? _mStart, _mEnd;             // süren ölçü (bitiş = kilit uygulanmış nokta)
    readonly List<(Point3D a, Point3D b)> _measures = new();
    Point _lastHover;
    long _lastSnapTicks;
    static readonly IntPtr SnapTimer = (IntPtr)2;

    // eleman → Snaps / Edges dizilerindeki ardışık aralıklar (toplama eleman eleman yapıldığı için birkaç aralık)
    Dictionary<uint, List<(int s, int e)>>? _snapIdx, _edgeIdx;

    // yazı: dokuya çizilmiş etiketler (önbellek), ekran-uzayı dörtgen
    uint _pText, _tvao, _tvbo;
    int tView, tTex;
    readonly Dictionary<string, (uint tex, int w, int h)> _labelTex = new();

    /// <summary>Durum çubuğu için güncel ölçü / ipucu metni.</summary>
    public string? MeasureStatus { get; private set; }
    public event Action? MeasureChanged;

    public bool MeasureMode
    {
        get => _measureMode;
        set
        {
            _measureMode = value;
            _snap = null; _mStart = null; _mEnd = null;
            if (!value) _lockAx = -1;
            UpdateMeasureStatus();
            Invalidate();
        }
    }

    /// <summary>-1 serbest, 0/1/2 X/Y/Z. Aynı ekseni tekrar vermek kilidi kaldırır.</summary>
    public int LockAxis
    {
        get => _lockAx;
        set
        {
            _lockAx = value == _lockAx ? -1 : value;
            RecomputeEnd(_lastHover);
            UpdateMeasureStatus();
            Invalidate();
        }
    }

    /// <summary>Kilidi kaldır (Serbest).</summary>
    public void FreeAxis() { if (_lockAx >= 0) LockAxis = _lockAx; }

    public bool HasMeasures => _measures.Count > 0;

    public void ClearMeasures()
    {
        _measures.Clear();
        _mStart = null; _mEnd = null;
        UpdateMeasureStatus();
        Invalidate();
    }

    string Fmt(double ft) => (_scene?.FormatLength ?? (x => (x * 304.8).ToString("N0", CultureInfo.CurrentCulture) + " mm"))(ft);

    // ---- sahne ---------------------------------------------------------------------------------------------------

    void MeasureSceneChanged(bool keepView)
    {
        _snapIdx = null; _edgeIdx = null;
        _snap = null; _mEnd = null;
        if (!keepView) { _measures.Clear(); _mStart = null; }   // Yenile/büyütme aynı çerçevede → ölçüler geçerli kalır
        UpdateMeasureStatus();
    }

    static Dictionary<uint, List<(int, int)>> BuildIndex(int count, Func<int, uint> idAt, int step)
    {
        var d = new Dictionary<uint, List<(int, int)>>();
        int i = 0;
        while (i < count)
        {
            uint id = idAt(i);
            int j = i + step;
            while (j < count && idAt(j) == id) j += step;
            if (!d.TryGetValue(id, out var l)) d[id] = l = new List<(int, int)>();
            l.Add((i, Math.Min(j, count)));
            i = j;
        }
        return d;
    }

    void EnsureIndex()
    {
        if (_scene == null) return;
        var s = _scene;
        _snapIdx ??= BuildIndex(s.Snaps.Count, i => s.Snaps[i].Id, 1);
        _edgeIdx ??= BuildIndex(s.Edges.Count & ~1, i => s.Edges[i].Id, 2);
    }

    // ---- eksenler ------------------------------------------------------------------------------------------------

    /// <summary>Proje ekseninin kutu-yerel karşılığı (çerçeve Z etrafında dönmüş olabilir).</summary>
    Vector3D Axis(int ax)
    {
        if (ax == 2) return new Vector3D(0, 0, 1);
        double a = _scene?.FrameAngle ?? 0, c = Math.Cos(a), s = Math.Sin(a);
        return ax == 0 ? new Vector3D(c, -s, 0) : new Vector3D(s, c, 0);
    }

    (double dx, double dy, double dz) Deltas(Point3D a, Point3D b)
    {
        var v = b - a;
        return (Vector3D.DotProduct(v, Axis(0)), Vector3D.DotProduct(v, Axis(1)), v.Z);
    }

    // ---- yakalama ------------------------------------------------------------------------------------------------

    const double SnapPx = 12, EdgePx = 8;

    bool InBox(double x, double y, double z)
    {
        const double e = 1e-4;
        return x >= _bMin[0] - e && x <= _bMax[0] + e && y >= _bMin[1] - e && y <= _bMax[1] + e && z >= _bMin[2] - e && z <= _bMax[2] + e;
    }

    double EyeDepth(Point3D p) => Vector3D.DotProduct(p - _pos, _look);

    double LinearDepth(float d)
    {
        var (n, f) = Planes();
        double zn = 2 * d - 1;
        return 2 * f * n / ((f + n) - zn * (f - n));
    }

    Snap? FindSnap(Point p)
    {
        if (_scene == null) return null;
        const int R = 14;
        var reg = PickRegion(p, R);
        if (reg == null) return null;
        var (x0, y0, rw, rh, ids, depth) = reg.Value;
        EnsureIndex();
        var s = _scene;

        // pencerede görünen elemanlar (merkeze yakından uzağa — en fazla 24)
        var near = new List<(uint id, int d)>();
        var seen = new HashSet<uint>();
        int cx = (int)p.X, cy = _h - 1 - (int)p.Y;
        for (int j = 0; j < rh; j++)
            for (int i = 0; i < rw; i++)
            {
                uint id = ids[j * rw + i];
                if (id == 0 || !seen.Add(id)) continue;
                int dx = x0 + i - cx, dy = y0 + j - cy;
                near.Add((id, dx * dx + dy * dy));
            }
        near.Sort((a, b) => a.d.CompareTo(b.d));
        if (near.Count > 24) near.RemoveRange(24, near.Count - 24);

        // Aday görünür mü: ekrandaki pikselinin derinliğinden fazla geride değilse (kendi yüzeyine biraz tolerans).
        bool Visible(Point3D q, Point sp)
        {
            int ix = (int)sp.X - x0, iy = _h - 1 - (int)sp.Y - y0;
            if (ix < 0 || iy < 0 || ix >= rw || iy >= rh) return true;
            float dz = depth[iy * rw + ix];
            if (dz >= 1) return true;
            double surf = LinearDepth(dz), cand = EyeDepth(q);
            return cand <= surf + Math.Max(0.03, surf * 0.012);
        }

        Snap? best = null;
        double bestScore = double.MaxValue;
        void Consider(Point3D q, SnapKind kind, double maxPx, double bias)
        {
            if (!InBox(q.X, q.Y, q.Z) || Project(q) is not { } sp) return;
            double d = (sp - p).Length;
            if (d > maxPx) return;
            double score = d + bias;
            if (score >= bestScore) return;
            if (!Visible(q, sp)) return;
            bestScore = score; best = new Snap(q, kind);
        }

        // 1) köşe / yay merkezi / kenar ortası — öncelikli (bias küçük)
        if (_snapIdx != null)
            foreach (var (id, _) in near)
            {
                if (!_snapIdx.TryGetValue(id, out var runs)) continue;
                foreach (var (a, b) in runs)
                    for (int k = a; k < b; k++)
                    {
                        var sn = s.Snaps[k];
                        var kind = sn.Kind == 2 ? SnapKind.Center : sn.Kind == 1 ? SnapKind.Mid : SnapKind.End;
                        // Eksen merkezi öncelikli (boru ucunda çember noktaları da çok yakın — MEP'te istenen eksen)
                        Consider(new Point3D(sn.X, sn.Y, sn.Z), kind, kind == SnapKind.Mid ? SnapPx - 2 : kind == SnapKind.Center ? SnapPx * 2 : SnapPx,
                                 kind == SnapKind.Mid ? 3 : kind == SnapKind.Center ? -10 : 0);
                    }
            }
        if (best != null) return best;

        // 2) kenar üzerindeki en yakın nokta (imleç ışınına en yakın)
        var ro = _pos;
        var rd = RayDir(p.X, p.Y); rd.Normalize();
        if (_edgeIdx != null)
            foreach (var (id, _) in near)
            {
                if (!_edgeIdx.TryGetValue(id, out var runs)) continue;
                foreach (var (a, b) in runs)
                    for (int k = a; k + 1 < b; k += 2)
                    {
                        var e0 = s.Edges[k]; var e1 = s.Edges[k + 1];
                        var A = new Point3D(e0.X, e0.Y, e0.Z);
                        var q = ClosestOnSegment(ro, rd, A, new Point3D(e1.X, e1.Y, e1.Z) - A);
                        Consider(q, SnapKind.Edge, EdgePx, 0);
                    }
            }
        if (best != null) return best;

        // 3) yüzey (imlecin altındaki piksel)
        {
            int ix = cx - x0, iy = cy - y0;
            if (ix >= 0 && iy >= 0 && ix < rw && iy < rh && ids[iy * rw + ix] != 0 && depth[iy * rw + ix] < 1)
                return new Snap(Unproject(p.X, p.Y, depth[iy * rw + ix]), SnapKind.Face);
        }
        return null;
    }

    /// <summary>Işın (o + t·d) ile parça (A + s·u, s∈[0,1]) arasındaki en yakın noktanın parça üzerindeki karşılığı.</summary>
    static Point3D ClosestOnSegment(Point3D o, Vector3D d, Point3D A, Vector3D u)
    {
        var w = A - o;
        double a = Vector3D.DotProduct(u, u), b = Vector3D.DotProduct(u, d), c = Vector3D.DotProduct(d, d);
        double dd = Vector3D.DotProduct(u, w), e = Vector3D.DotProduct(d, w);
        double den = a * c - b * b;
        double s = den > 1e-12 ? (b * e - c * dd) / den : 0;
        s = Math.Clamp(s, 0, 1);
        return A + u * s;
    }

    /// <summary>Kilitli eksen doğrusunun (S + t·ax) imleç ışınına en yakın noktası (yakalama yoksa).</summary>
    Point3D ClosestOnAxis(Point p, Point3D S, Vector3D ax)
    {
        var o = _pos; var d = RayDir(p.X, p.Y); d.Normalize();
        var w = S - o;
        double b = Vector3D.DotProduct(ax, d), dd = Vector3D.DotProduct(ax, w), e = Vector3D.DotProduct(d, w);
        double den = 1 - b * b;
        double t = den > 1e-9 ? (b * e - dd) / den : 0;
        return S + ax * t;
    }

    void RecomputeEnd(Point p)
    {
        if (_mStart is not { } S) { _mEnd = null; return; }
        if (_lockAx >= 0)
        {
            var ax = Axis(_lockAx);
            _mEnd = _snap is { } sn ? S + ax * Vector3D.DotProduct(sn.P - S, ax) : ClosestOnAxis(p, S, ax);
        }
        else _mEnd = _snap?.P;
    }

    // ---- fare / klavye ---------------------------------------------------------------------------------------------

    void MeasureHover(Point p, bool force = false)
    {
        _lastHover = p;
        long now = Environment.TickCount64;
        // Yakalama bir seçim tamponu çizimi gerektirir → en fazla ~40 kare/sn; arada kalan son konum zamanlayıcıyla.
        if (!force && now - _lastSnapTicks < 25) { Win32.SetTimer(_hwnd, SnapTimer, 30, IntPtr.Zero); return; }
        _lastSnapTicks = now;
        _snap = FindSnap(p);
        RecomputeEnd(p);
        UpdateMeasureStatus();
        Invalidate();
    }

    void MeasureClick(Point p)
    {
        MeasureHover(p, force: true);
        if (_mStart == null)
        {
            if (_snap is { } sn) { _mStart = sn.P; RecomputeEnd(p); }
        }
        else if (_mEnd is { } e && (e - _mStart.Value).Length > 1e-6)
        {
            _measures.Add((_mStart.Value, e));
            _mStart = null; _mEnd = null;
        }
        UpdateMeasureStatus();
        Invalidate();
    }

    /// <summary>true: tuş ölçü aracınca kullanıldı.</summary>
    bool MeasureKey(int vk)
    {
        if (!_measureMode) return false;
        switch (vk)
        {
            case 'X': LockAxis = 0; return true;
            case 'Y': LockAxis = 1; return true;
            case 'Z': LockAxis = 2; return true;
            case 0x2E: ClearMeasures(); return true;                       // Delete
            case 0x08:                                                       // Backspace
                if (_mStart != null) { _mStart = null; _mEnd = null; }
                else if (_measures.Count > 0) _measures.RemoveAt(_measures.Count - 1);
                UpdateMeasureStatus(); Invalidate();
                return true;
            case Win32.VK_ESCAPE:
                if (_mStart != null) { _mStart = null; _mEnd = null; UpdateMeasureStatus(); Invalidate(); }
                else if (_lockAx >= 0) LockAxis = _lockAx;
                else ToolKey?.Invoke('D');                                   // araçtan çık (ölçüler ekranda kalır)
                return true;
        }
        return false;
    }

    void UpdateMeasureStatus()
    {
        string? s = null;
        string lk = _lockAx >= 0 ? L.T("  ·  Kilit: ", "  ·  Lock: ") + "XYZ"[_lockAx] : "";
        if (_mStart is { } a && _mEnd is { } b)
        {
            var (dx, dy, dz) = Deltas(a, b);
            s = L.T("Mesafe: ", "Distance: ") + Fmt((b - a).Length)
                + $"   ΔX {Fmt(Math.Abs(dx))}  ·  ΔY {Fmt(Math.Abs(dy))}  ·  ΔZ {Fmt(Math.Abs(dz))}" + lk
                + L.T("   —  ikinci noktayı tıkla", "   —  click the second point");
        }
        else if (_measureMode)
        {
            s = (_mStart == null
                    ? L.T("Ölç: ilk noktayı tıkla (köşe, kenar ortası, boru ekseni, kenar ya da yüzey)", "Measure: click the first point (corner, midpoint, pipe axis, edge or face)")
                    : L.T("Ölç: ikinci noktayı tıkla", "Measure: click the second point"))
                + L.T("  ·  X/Y/Z eksen kilidi  ·  Backspace son ölçüyü, Delete hepsini siler  ·  Esc çık", "  ·  X/Y/Z axis lock  ·  Backspace removes the last, Delete all  ·  Esc exit")
                + lk;
            if (_mStart == null && _measures.Count > 0)
            {
                var (ma, mb) = _measures[^1];
                var (dx, dy, dz) = Deltas(ma, mb);
                s = L.T("Son ölçü: ", "Last: ") + Fmt((mb - ma).Length) + $"  (ΔX {Fmt(Math.Abs(dx))} · ΔY {Fmt(Math.Abs(dy))} · ΔZ {Fmt(Math.Abs(dz))})   —   " + s;
            }
        }
        if (_measureMode && _snap is { } sk)
        {
            string kn = sk.Kind switch
            {
                SnapKind.End => L.T("Köşe", "Endpoint"), SnapKind.Mid => L.T("Orta nokta", "Midpoint"),
                SnapKind.Center => L.T("Boru/kanal ekseni", "Pipe/duct axis"), SnapKind.Edge => L.T("Kenar", "Edge"), _ => L.T("Yüzey", "Face"),
            };
            s = "[" + kn + "]  " + s;
        }
        MeasureStatus = s;
        MeasureChanged?.Invoke();
    }

    // ---- çizim ---------------------------------------------------------------------------------------------------

    const string TextVs = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUv;
uniform vec3 uView;
out vec2 vUv;
void main(){ vUv=aUv; gl_Position=vec4(aPos.x/uView.x*2.0-1.0, 1.0-aPos.y/uView.y*2.0, 0.0, 1.0); }";

    const string TextFs = @"#version 330 core
in vec2 vUv; uniform sampler2D uTex; out vec4 o;
void main(){ o = texture(uTex, vUv); }";

    void InitMeasureGl()
    {
        _pText = GL.Program(TextVs, TextFs);
        tView = GL.Uniform(_pText, "uView"); tTex = GL.Uniform(_pText, "uTex");
        _tvao = GL.Gen(GL.GenVertexArrays);
        _tvbo = GL.Gen(GL.GenBuffers);
        GL.BindVertexArray(_tvao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _tvbo);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, GL.FLOAT, 0, 16, (IntPtr)0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, GL.FLOAT, 0, 16, (IntPtr)8);
        GL.BindVertexArray(0);
        GL.BindBuffer(GL.ARRAY_BUFFER, 0);
    }

    void FreeMeasureGl()
    {
        FreeLabels();
        GL.Del(GL.DeleteVertexArrays, ref _tvao);
        GL.Del(GL.DeleteBuffers, ref _tvbo);
        if (_pText != 0) { GL.DeleteProgram(_pText); _pText = 0; }
    }

    void FreeLabels()
    {
        foreach (var (tex, _, _) in _labelTex.Values) { uint t = tex; GL.DeleteTextures(1, &t); }
        _labelTex.Clear();
    }

    static (float r, float g, float b) KindColor(SnapKind k) => k switch
    {
        SnapKind.End => (0.10f, 0.75f, 0.25f),     // yeşil: köşe/uç
        SnapKind.Center => (0.85f, 0.20f, 0.85f),  // mor: yay merkezi (boru ekseni)
        SnapKind.Mid => (0.10f, 0.70f, 0.90f),     // camgöbeği: orta nokta
        SnapKind.Edge => (0.95f, 0.75f, 0.10f),    // sarı: kenar üzeri
        _ => (0.55f, 0.55f, 0.55f),                // gri: yüzey
    };

    /// <summary>Ölçüler (her zaman, görüntü alırken de) + canlı yakalama/kilit kılavuzu (yalnız pencerede).</summary>
    void DrawMeasureOverlay(float[] mvp, bool live)
    {
        live &= _measureMode;
        if (_measures.Count == 0 && !(live && (_snap != null || _mStart != null))) return;
        var data = new List<float>();
        void V(Point3D p, float r, float g, float b, float a)
        {
            data.Add((float)p.X); data.Add((float)p.Y); data.Add((float)p.Z);
            data.Add(r); data.Add(g); data.Add(b); data.Add(a);
        }
        const float mr = 1.0f, mg = 0.42f, mb = 0.05f;   // turuncu ölçü çizgisi
        // çizgiler
        foreach (var (a, b) in _measures) { V(a, mr, mg, mb, 1); V(b, mr, mg, mb, 1); }
        if (live && _mStart is { } S)
        {
            if (_lockAx >= 0)
            {
                var ax = Axis(_lockAx) * Math.Max(_fitRadius * 4, 10);
                float cr = _lockAx == 0 ? 0.9f : 0.1f, cg = _lockAx == 1 ? 0.75f : 0.1f, cb = _lockAx == 2 ? 0.95f : 0.1f;
                V(S - ax, cr, cg, cb, 0.55f); V(S + ax, cr, cg, cb, 0.55f);
            }
            if (_mEnd is { } E)
            {
                V(S, mr, mg, mb, 1); V(E, mr, mg, mb, 1);
                if (_lockAx < 0)   // eksen bileşenleri (X kırmızı, Y yeşil, Z mavi) — ince, yarı saydam
                {
                    var (dx, dy, _) = Deltas(S, E);
                    var p1 = S + Axis(0) * dx; var p2 = p1 + Axis(1) * dy;
                    V(S, 0.9f, 0.15f, 0.15f, 0.6f); V(p1, 0.9f, 0.15f, 0.15f, 0.6f);
                    V(p1, 0.1f, 0.7f, 0.2f, 0.6f); V(p2, 0.1f, 0.7f, 0.2f, 0.6f);
                    V(p2, 0.15f, 0.35f, 0.95f, 0.6f); V(E, 0.15f, 0.35f, 0.95f, 0.6f);
                }
            }
        }
        int lineVerts = data.Count / 7;
        // uç noktaları
        foreach (var (a, b) in _measures) { V(a, mr, mg, mb, 1); V(b, mr, mg, mb, 1); }
        if (live && _mStart is { } S2) V(S2, mr, mg, mb, 1);
        if (live && _mEnd is { } E2) V(E2, mr, mg, mb, 1);
        int ptVerts = data.Count / 7 - lineVerts;
        // yakalama işareti (büyük, türe göre renk)
        int snapVerts = 0;
        if (live && _snap is { } sn) { var (r, g, b) = KindColor(sn.Kind); V(sn.P, r, g, b, 1); snapVerts = 1; }

        var arr = data.ToArray();
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.Enable(GL.PROGRAM_POINT_SIZE);
        GL.Enable(GL.POINT_SPRITE);
        GL.UseProgram(_pOver);
        fixed (float* m = mvp) GL.UniformMatrix4fv(oMvp, 1, 1, m);
        GL.BindVertexArray(_ovao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _ovbo);
        fixed (float* p = arr) GL.BufferData(GL.ARRAY_BUFFER, arr.Length * 4, p, GL.DYNAMIC_DRAW);
        GL.Uniform1i(oPoint, 0);
        GL.LineWidth(2f);
        if (lineVerts > 0) GL.DrawArrays(GL.LINES, 0, lineVerts);
        GL.LineWidth(1f);
        GL.Uniform1i(oPoint, 1);
        GL.Uniform1f(oSize, 9f);
        if (ptVerts > 0) GL.DrawArrays(GL.POINTS, lineVerts, ptVerts);
        if (snapVerts > 0 && _snap is { } sk)
        {
            GL.Uniform1i(oPoint, sk.Kind switch { SnapKind.End => 2, SnapKind.Mid => 3, SnapKind.Center => 4, SnapKind.Edge => 5, _ => 6 });
            GL.Uniform1f(oSize, 22f);
            GL.DrawArrays(GL.POINTS, lineVerts + ptVerts, snapVerts);
        }
        GL.BindVertexArray(0);
        GL.BindBuffer(GL.ARRAY_BUFFER, 0);
        GL.Disable(GL.PROGRAM_POINT_SIZE);
        GL.Disable(GL.POINT_SPRITE);

        // etiketler: ölçü ortasının biraz üstünde
        foreach (var (a, b) in _measures) DrawLabel(a, b, Fmt((b - a).Length));
        if (live && _mStart is { } S3 && _mEnd is { } E3) DrawLabel(S3, E3, Fmt((E3 - S3).Length));

        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
    }

    void DrawLabel(Point3D a, Point3D b, string text)
    {
        var mid = new Point3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
        if (Project(mid) is not { } sp) return;
        if (_pText == 0) return;
        var (tex, w, h) = LabelTexture(text);
        if (tex == 0) return;
        float x0 = (float)Math.Round(sp.X - w / 2.0), y0 = (float)Math.Round(sp.Y - h - 6), x1 = x0 + w, y1 = y0 + h;
        var q = new[] { x0, y0, 0f, 0f, x1, y0, 1f, 0f, x0, y1, 0f, 1f, x1, y1, 1f, 1f };
        GL.UseProgram(_pText);
        GL.Uniform3f(tView, _w, _h, 0);
        GL.Uniform1i(tTex, 0);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, tex);
        GL.BlendFunc(GL.ONE, GL.ONE_MINUS_SRC_ALPHA);   // WPF bitmapi önceden çarpılmış alfa
        GL.BindVertexArray(_tvao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _tvbo);
        fixed (float* p = q) GL.BufferData(GL.ARRAY_BUFFER, q.Length * 4, p, GL.DYNAMIC_DRAW);
        GL.DrawArrays(GL.TRIANGLE_STRIP, 0, 4);
        GL.BindVertexArray(0);
        GL.BindBuffer(GL.ARRAY_BUFFER, 0);
        GL.BindTexture(GL.TEXTURE_2D, 0);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
    }

    /// <summary>Etiketi WPF ile (ClearType'sız, net) bir bitmape çizip dokuya yükler; aynı metin önbellekten.</summary>
    (uint tex, int w, int h) LabelTexture(string text)
    {
        if (_labelTex.TryGetValue(text, out var hit)) return hit;
        if (_labelTex.Count > 96) FreeLabels();
        double dpi = 1;
        try { dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            12.5 * dpi, Brushes.White, dpi);
        int padX = (int)(7 * dpi), padY = (int)(3 * dpi);
        int w = (int)Math.Ceiling(ft.Width) + 2 * padX, h = (int)Math.Ceiling(ft.Height) + 2 * padY;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0xD9, 0x5F, 0x0A)), null, new Rect(0, 0, w, h), 4 * dpi, 4 * dpi);
            dc.DrawText(ft, new Point(padX, padY));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        uint tex;
        GL.GenTextures(1, &tex);
        GL.BindTexture(GL.TEXTURE_2D, tex);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
        GL.PixelStorei(GL.UNPACK_ALIGNMENT, 4);
        fixed (byte* p = px) GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, w, h, 0, GL.BGRA, GL.UNSIGNED_BYTE, p);
        GL.BindTexture(GL.TEXTURE_2D, 0);
        var r = (tex, w, h);
        _labelTex[text] = r;
        return r;
    }
}
