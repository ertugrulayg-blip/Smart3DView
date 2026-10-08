using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Media3D;

namespace Smart3DView;

/// <summary>OpenGL ile çizen çocuk pencere (HwndHost). Geometri bir kez GPU'ya yüklenir; kesit kutusu, renk/çakışma
/// durumu ve kamera yalnız uniform/doku olarak değişir → her ekran kartında akıcı.
/// Kesit: kutu dışı parçalar gölgelendiricide atılır, kesilen katıların içi (arka yüzler) kutu yüzeyine oturtulmuş
/// düz poşe olarak çizilir. Seçim: eleman numaraları ayrı tampona çizilip tıklanan piksel okunur.</summary>
sealed unsafe partial class GlView : HwndHost
{
    const string ClassName = "Smart3DViewGL";
    const double Fov = 32; // yatay görüş açısı (derece)
    const int StateTexWidth = 4096;
    static bool _classRegistered;
    public static readonly Vector3D IsoLook = Norm(new Vector3D(-1, 1, -0.85)); // güneydoğu-üstten (Revit varsayılanı gibi)
    static readonly Vector3D Z = new(0, 0, 1);

    // "Renkli" ton: soğutma mavi, yangın kırmızı, üfleme magenta, emiş/dönüş yeşil (SysColor sırası).
    static readonly float[] ClassColors =
    {
        0, 0, 0,
        0.16f, 0.42f, 0.88f,
        0.88f, 0.13f, 0.13f,
        0.86f, 0.20f, 0.78f,
        0.18f, 0.68f, 0.30f,
    };

    // "Detaylı" ton: her eleman türü kendi renginde (Detail sırası). Tesisat sistem renkleri "Renkli" ile aynı.
    static readonly float[] DetailColors = Rgb(
        0xCFCCC6,                                   // genel
        0x2E6FE0, 0xE02323, 0xDB33C7, 0x2EAD4C,     // soğutma, yangın, üfleme, dönüş/egzoz
        0xE2702E, 0x23A6C9, 0x8B6B4E, 0x4FB0A0,     // ısıtma, kullanım suyu, pis su, diğer boru
        0xA9BBCB, 0x9AA3AD,                         // kanal (galvaniz), tava/conduit (metal)
        0xC6C4BE, 0xD3CFC6, 0xEAE8E3, 0xF6F4EE,     // duvar, döşeme/çatı, asma tavan, kapı
        0x7F8A95, 0xB8B0A3, 0xC8B59A, 0xC29B70,     // pencere/doğrama, betonarme, merdiven/korkuluk, mobilya
        0x8E7CC3, 0xE3A33A, 0xF0D96A, 0xD98C5F,     // mekanik cihaz, elektrik pano/cihaz, aydınlatma, zayıf akım/yangın algılama
        0xF5F7F9, 0xA6B98A);                        // vitrifiye, arazi

    // "Renkli" ton: ana model ve her bağlı model ayrı pastel renk (kullanıcı isteği 2026-10-08). Sıra: DocNames'teki ilk görünüş.
    internal static readonly int[] ModelRgb =
    {
        0x9EC3EE, 0xF3B0AE, 0xAEDBA6, 0xF5D58A, 0xC8B4E8, 0xF4BE94, 0x9FD9D3, 0xEAB0D6,
        0xD2E297, 0xB2C1D6, 0xEFC6A4, 0xBCD7F3, 0xE1CDA3, 0xC4E6C4, 0xD9B6B6, 0xBDBDEA,
    };
    static readonly float[] ModelColors = Rgb(ModelRgb);

    static float[] Rgb(params int[] c)
    {
        var a = new float[c.Length * 3];
        for (int i = 0; i < c.Length; i++) { a[3 * i] = ((c[i] >> 16) & 255) / 255f; a[3 * i + 1] = ((c[i] >> 8) & 255) / 255f; a[3 * i + 2] = (c[i] & 255) / 255f; }
        return a;
    }

    IntPtr _hwnd, _hdc, _ctx;
    int _w = 1, _h = 1;
    public string? Error { get; private set; }
    public string Renderer { get; private set; } = "";
    public event Action<string>? Failed;
    public event Action? SelectionChanged;
    public event Action<int>? PaletteKey;
    /// <summary>Araç kısayolu: C çakışma, B kutu, M taşı, P görüntü al.</summary>
    public event Action<char>? ToolKey;
    /// <summary>Kutu sürüklemesi bitti (yeni sınırlar BoxMin/BoxMax'ta).</summary>
    public event Action? BoxEdited;
    /// <summary>Kutu, Revit'ten okunan bölgenin dışına çıktı → bu sınırlarla yeniden okunmalı.</summary>
    public event Action<double[], double[]>? GrowRequested;
    /// <summary>Esc önce buna sorulur (Revit okuması sürüyorsa iptal eder ve true döner); false → seçimi kaldır.</summary>
    public Func<bool>? EscapeOverride;

    // QUICKBOX_DIAG=<dosya> ortam değişkeniyle GPU adı ve kare süreleri yazılır (performans teşhisi için).
    static readonly string? DiagPath = Environment.GetEnvironmentVariable("QUICKBOX_DIAG");
    static void Diag(string s) { if (DiagPath != null) System.IO.File.AppendAllText(DiagPath, s + Environment.NewLine); }

    // sahne
    SceneData? _scene;
    bool _uploaded;
    float[] _elMin = Array.Empty<float>(), _elMax = Array.Empty<float>();
    double[] _geoMin = { -1, -1, -1 }, _geoMax = { 1, 1, 1 };
    int _opaqueCount, _glassCount, _edgeCount;
    uint _vao, _vbo, _iboO, _iboG, _evao, _evbo, _bgVao, _ovao, _ovbo, _stateTex;
    byte[] _state = Array.Empty<byte>();
    int _stateH = 1;
    bool _clashMode;

    // kesit kutusu: geçerli (kullanıcının düzenlediği) ve Revit'ten okunan
    readonly double[] _bMin = { -1, -1, -1 }, _bMax = { 1, 1, 1 };
    readonly double[] _cMin = { -1, -1, -1 }, _cMax = { 1, 1, 1 };
    readonly double[] _homeMin = { -1, -1, -1 }, _homeMax = { 1, 1, 1 };   // ilk açılan kutu (↺ buna döner; okunan alan ön yüklemeyle daha büyük)
    public double[] BoxMin => (double[])_bMin.Clone();
    public double[] BoxMax => (double[])_bMax.Clone();
    /// <summary>Revit'ten okunmuş alan (kutu küçültülse de bu kadarı yüklü).</summary>
    public double[] LoadedMin => (double[])_cMin.Clone();
    public double[] LoadedMax => (double[])_cMax.Clone();
    public bool BoxMode { get => _boxMode; set { _boxMode = value; _hover = -1; Invalidate(); } }
    public bool MoveMode { get; set; }
    bool _boxMode;
    int _hover = -1, _dragFace = -1;
    readonly double[] _dragMin = new double[3], _dragMax = new double[3];

    // shader programları ve uniform konumları
    uint _pSurf, _pEdge, _pBg, _pPick, _pOver;
    int sPass, sMvp, sTone, sCam, sKey, sFill, sSky, sLight, sSel, sSelColor, sGlassA, sBoxMin, sBoxMax, sState, sColored, sClash, sClass, sClashColor, sClashColor2, sFade, sDetail, sModel;
    int eMvp, eColor, eSel, eSelColor, eBoxMin, eBoxMax, eState, eClash, eFade, bTop, bBot, pkMvp, pkBoxMin, pkBoxMax, pkState, oMvp, oPoint, oSize;

    // çerçeve tamponları: MSAA (kenar yumuşatma) + seçim (R32UI) + düşük çözünürlük
    uint _msFbo, _msColor, _msDepth, _pkFbo, _pkColor, _pkDepth;
    int _samples, _fboW, _fboH;
    bool _msOk, _pkOk;

    Palette _pal = Palette.All[0];
    public uint Selected { get; private set; }
    public string? SelectedLabel => _scene != null && Selected > 0 && Selected <= _scene.Labels.Count ? _scene.Labels[(int)Selected - 1] : null;

    // kamera: konum + birim bakış yönü + odak mesafesi; yukarı daima +Z
    Point3D _pos;
    Vector3D _look = IsoLook;
    double _focus = 10;
    Point3D _center;
    double _radius = 10, _fitRadius = 1;

    // fare
    bool _leftDown, _nav, _fitPending;
    Point _down, _last;
    Point3D _pivot;
    double _panDepth;
    // Sürükleme/tekerlek sırasında hafif kalite (MSAA yok, 1 px çizgi); hareket durunca tam kaliteli tek kare.
    bool _interactive;
    static readonly IntPtr IdleTimer = (IntPtr)1;
    // Dinamik çözünürlük: etkileşim karelerinin GPU süresi ölçülür (zamanlayıcı sorgusu), hedefi aşarsa iç çözünürlük
    // düşürülür, büyük ölçekte kalan kare süresine göre geri yükseltilir. Ekran kartından bağımsız akıcılık sağlar.
    double _scale = 1;
    uint _lrFbo, _lrColor, _lrDepth;
    int _lrW, _lrH;
    readonly uint[] _q = new uint[2];
    readonly bool[] _qPending = new bool[2];
    int _qi;
    const double TargetMs = 12, MinScale = 0.35;
    // Çözünürlük düşürmek kareyi hızlandırmıyorsa darboğaz piksel değil (üçgen sayısı) → tam çözünürlükte kal.
    bool _pixelsNotBottleneck;
    double _prevMs = double.NaN, _prevScale = 1;

    public GlView() { Focusable = true; }

    // ---- dış API -------------------------------------------------------------------------------------------------

    /// <summary>Yeni sahne. keepView: kamerayı ve düzenlenmiş kutuyu koru (kutu büyütülünce yeniden okuma).</summary>
    public void SetScene(SceneData scene, bool keepView = false)
    {
        _scene = scene;
        _uploaded = false;
        Selected = 0;
        _clashMode = false;
        Array.Copy(scene.BoxMin, _cMin, 3);
        Array.Copy(scene.BoxMax, _cMax, 3);
        if (!keepView)
        {
            Array.Copy(scene.ViewMin ?? scene.BoxMin, _bMin, 3); Array.Copy(scene.ViewMax ?? scene.BoxMax, _bMax, 3);
            Array.Copy(_bMin, _homeMin, 3); Array.Copy(_bMax, _homeMax, 3);
        }
        ComputeBounds(scene);
        BuildState();
        _fitPending = !keepView && _w <= 1;
        ResetScale();
        if (_ctx != IntPtr.Zero && Error == null) Upload();
        if (!keepView) FitAll(IsoLook);
        MeasureSceneChanged(keepView);
        SelectionChanged?.Invoke();
    }

    /// <summary>Seçili elemanın sahneden bağımsız anahtarı (etiketteki "ID …" kısmı, bağlı model adı dahil) —
    /// yeniden okumada eleman numaraları değişir, seçim bu anahtarla geri bulunur.</summary>
    public string? SelectedKey => KeyOf(SelectedLabel);

    static string? KeyOf(string? label)
    {
        if (label == null) return null;
        int i = label.LastIndexOf("·  ID ", StringComparison.Ordinal);
        return i < 0 ? label : label.Substring(i);
    }

    public void SelectByKey(string? key)
    {
        if (key == null || _scene == null) return;
        for (int i = 0; i < _scene.Labels.Count; i++)
            if (KeyOf(_scene.Labels[i]) == key) { Select((uint)(i + 1)); return; }
    }

    public void SetPalette(Palette p) { _pal = p; Invalidate(); }

    public void FocusGl() { if (_hwnd != IntPtr.Zero) Win32.SetFocus(_hwnd); }

    /// <summary>Çakışma sonucunu göster (null = kapat).</summary>
    public void SetClash(ClashResult? r)
    {
        _clashMode = r != null;
        _clashRes = r;
        ApplyClashBits();
        UploadState();
        Invalidate();
    }

    /// <summary>Kutuyu okunmuş alana geri çeker (büyütme okuması iptal edilince).</summary>
    public void ClampBoxToLoaded()
    {
        for (int k = 0; k < 3; k++) { _bMin[k] = Math.Max(_bMin[k], _cMin[k]); _bMax[k] = Math.Min(_bMax[k], _cMax[k]); }
        UpdateCenter();
        Invalidate();
        BoxEdited?.Invoke();
    }

    public void ResetBox()
    {
        Array.Copy(_homeMin, _bMin, 3);
        Array.Copy(_homeMax, _bMax, 3);
        UpdateCenter();
        Invalidate();
        BoxEdited?.Invoke();
    }

    // ---- pencere -------------------------------------------------------------------------------------------------

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var inst = Win32.GetModuleHandle(null);
        if (!_classRegistered)
        {
            var wc = new Win32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
                style = Win32.CS_OWNDC | Win32.CS_DBLCLKS | Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                // Mesajları HwndHost.WndProc alt sınıflamasıyla işliyoruz; sınıfın kendi yordamı varsayılan olsun.
                lpfnWndProc = Win32.GetProcAddress(Win32.GetModuleHandle("user32.dll"), "DefWindowProcW"),
                hInstance = inst,
                hCursor = Win32.LoadCursor(IntPtr.Zero, (IntPtr)32512),
                lpszClassName = ClassName,
            };
            Win32.RegisterClassEx(ref wc); // zaten kayıtlıysa hata döner, sorun değil
            _classRegistered = true;
        }
        _hwnd = Win32.CreateWindowEx(0, ClassName, "", Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_CLIPSIBLINGS,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, inst, IntPtr.Zero);
        InitGl();
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_ctx != IntPtr.Zero)
        {
            MakeCurrent();
            FreeScene();
            FreeFbos();
            FreeMeasureGl();
            GL.Del(GL.DeleteVertexArrays, ref _bgVao);
            GL.Del(GL.DeleteVertexArrays, ref _ovao);
            GL.Del(GL.DeleteBuffers, ref _ovbo);
            FreeSketchGl();
            foreach (var p in new[] { _pSurf, _pEdge, _pBg, _pPick, _pOver }) if (p != 0) GL.DeleteProgram(p);
            GL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            GL.wglDeleteContext(_ctx);
            _ctx = IntPtr.Zero;
        }
        if (_hdc != IntPtr.Zero) Win32.ReleaseDC(_hwnd, _hdc);
        Win32.DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    void Fail(string msg)
    {
        Error = msg;
        Failed?.Invoke(msg);
    }

    void MakeCurrent() => GL.wglMakeCurrent(_hdc, _ctx);

    void Invalidate() { if (_hwnd != IntPtr.Zero) Win32.InvalidateRect(_hwnd, IntPtr.Zero, false); }

    void Interact()
    {
        _interactive = true;
        Win32.SetTimer(_hwnd, IdleTimer, 180, IntPtr.Zero); // aynı kimlikle yeniden kurmak sayacı sıfırlar
        Invalidate();
    }

    void InitGl()
    {
        _hdc = Win32.GetDC(_hwnd);
        var pfd = new Win32.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)sizeof(Win32.PIXELFORMATDESCRIPTOR), nVersion = 1,
            dwFlags = Win32.PFD_DRAW_TO_WINDOW | Win32.PFD_SUPPORT_OPENGL | Win32.PFD_DOUBLEBUFFER,
            cColorBits = 32, cAlphaBits = 8, cDepthBits = 24, cStencilBits = 8,
        };
        int pf = Win32.ChoosePixelFormat(_hdc, ref pfd);
        if (pf == 0 || !Win32.SetPixelFormat(_hdc, pf, ref pfd)) { Fail("OpenGL piksel biçimi ayarlanamadı."); return; }
        _ctx = GL.wglCreateContext(_hdc);
        if (_ctx == IntPtr.Zero || !GL.wglMakeCurrent(_hdc, _ctx)) { Fail("OpenGL bağlamı oluşturulamadı."); return; }
        if (GL.Load() is { } err) { Fail(err); return; }
        int maj = 0, min = 0;
        GL.GetIntegerv(GL.MAJOR_VERSION, &maj);
        GL.GetIntegerv(GL.MINOR_VERSION, &min);
        if (maj * 10 + min < 33) { Fail("OpenGL 3.3 gerekli, bulunan: " + GL.GetString(GL.VERSION)); return; }
        try { BuildPrograms(); }
        catch (Exception ex) { Fail(ex.Message); return; }
        int ms = 0;
        GL.GetIntegerv(GL.MAX_SAMPLES, &ms);
        _samples = Math.Min(8, ms);
        Renderer = GL.GetString(GL.RENDERER);
        Diag("renderer=" + Renderer + " version=" + GL.GetString(GL.VERSION) + " samples=" + _samples);
        _bgVao = GL.Gen(GL.GenVertexArrays);
        _q[0] = GL.Gen(GL.GenQueries);
        _q[1] = GL.Gen(GL.GenQueries);
        _ovao = GL.Gen(GL.GenVertexArrays);
        _ovbo = GL.Gen(GL.GenBuffers);
        GL.BindVertexArray(_ovao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _ovbo);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, GL.FLOAT, 0, 28, (IntPtr)0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 4, GL.FLOAT, 0, 28, (IntPtr)12);
        GL.BindVertexArray(0);
        GL.BindBuffer(GL.ARRAY_BUFFER, 0);
        InitMeasureGl();
        if (_scene != null) Upload();
    }

    // ---- shaderlar -----------------------------------------------------------------------------------------------

    const string SurfVs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNrm;
layout(location=2) in int aTone;
layout(location=3) in uint aId;
uniform mat4 uMvp;
out vec3 vPos; out vec3 vNrm; flat out int vTone; flat out uint vId;
void main(){ vPos=aPos; vNrm=aNrm; vTone=aTone; vId=aId; gl_Position=uMvp*vec4(aPos,1.0); }";

    // Işık: yarım küre ortam + kameraya bağlı ana/dolgu ışığı + dünyaya bağlı gök ışığı.
    // Kesit: kutu dışı atılır; arka yüz görünüyorsa (katı kutuyla kesilmiş) ışının kutuya girdiği noktaya düz poşe.
    const string SurfFs = @"#version 330 core
in vec3 vPos; in vec3 vNrm; flat in int vTone; flat in uint vId;
uniform vec3 uTone[6]; uniform vec3 uCam; uniform vec3 uKey; uniform vec3 uFill; uniform vec3 uSky;
uniform vec4 uLight; uniform uint uSel; uniform vec3 uSelColor; uniform float uGlassA;
uniform vec3 uBoxMin; uniform vec3 uBoxMax; uniform mat4 uMvp;
uniform usampler2D uState; uniform int uColored; uniform int uClash; uniform vec3 uClass[5]; uniform vec3 uDetail[25]; uniform vec3 uModel[16];
uniform vec3 uClashColor; uniform vec3 uClashColor2; uniform vec3 uFade; uniform int uPass;
out vec4 o;
void main(){
  if (any(lessThan(vPos, uBoxMin)) || any(greaterThan(vPos, uBoxMax))) discard;
  uvec2 st = texelFetch(uState, ivec2(int(vId % 4096u), int(vId / 4096u)), 0).rg;
  if ((st.r & 128u) != 0u) discard;   // filtreyle gizlenen kategori
  uint cls = st.r & 7u, mdl = (st.r >> 3) & 15u;   // r: alt 3 bit sistem rengi, 4 bit model rengi, üst bit gizli
  uint det = min(st.g >> 2, 24u), ck = st.g & 3u;   // g: alt 2 bit çakışma tarafı, üst 6 bit detay rengi
  bool clash = uClash != 0 && ck != 0u;
  vec3 clashC = ck == 2u ? uClashColor2 : uClashColor;   // çakışmanın iki tarafı: kırmızı / mavi
  vec3 base = uTone[vTone];
  if (uColored == 1 && vTone != 4) base = uModel[mdl];
  if (uColored == 2 && vTone != 4) base = uDetail[det];
  if (clash) base = clashC;
  vec3 c;
  // Katının içi mi görünüyor? Ekrandaki gerçek yüzey yönü (kameraya çevrilmiş) ile Revit'in dışa bakan normali
  // zıtsa evet (gl_FrontFacing'e güvenmiyoruz: sürücüye ve üçgen sarımına bağlı). 0. geçiş yalnız dış yüzleri,
  // 1. geçiş (poşe) yalnız iç yüzleri çizer; kesilmemiş katının iç yüzü kendi dış yüzünün arkasında kalıp görünmez.
  vec3 g = cross(dFdx(vPos), dFdy(vPos));
  if (dot(g, vPos - uCam) > 0.0) g = -g;
  bool inside = vTone != 4 && dot(g, vNrm) < 0.0;
  if (inside != (uPass == 1)) discard;
  if (inside) {
    vec3 cut = uTone[5];
    if (uColored == 1) cut = uModel[mdl] * 0.6;
    if (uColored == 2) cut = uDetail[det] * 0.6;
    if (clash) cut = clashC * 0.75;
    c = cut;
  } else {
    vec3 n = normalize(vNrm);
    if (dot(n, vPos - uCam) > 0.0) n = -n;
    float d = uLight.x * (0.80 + 0.20 * n.z)
            + uLight.y * max(dot(n, -uKey), 0.0)
            + uLight.z * max(dot(n, -uFill), 0.0)
            + uLight.w * max(dot(n, -uSky), 0.0);
    c = base * (clash ? max(d, 0.55) : d);
    if (uColored == 2 && !clash && (det == 9u || det == 10u || det == 15u)) {   // metal: tava, kanal, doğrama — parlama
      vec3 h = normalize(normalize(uCam - vPos) - uKey);
      c += vec3(0.38) * pow(max(dot(n, h), 0.0), 28.0);
    }
  }
  if (uClash != 0 && !clash) c = mix(c, uFade, 0.72);
  if (uSel != 0u && vId == uSel && !clash) c = mix(c, uSelColor, 0.5);   // çakışan seçili: kırmızı kalsın (mavi ortaklarıyla karışmasın)
  o = vec4(min(c, vec3(1.0)), vTone == 4 ? uGlassA : 1.0);
}";

    const string EdgeVs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in uint aId;
uniform mat4 uMvp;
out vec3 vPos; flat out uint vId;
void main(){ vPos=aPos; vId=aId; gl_Position=uMvp*vec4(aPos,1.0); }";

    const string EdgeFs = @"#version 330 core
in vec3 vPos; flat in uint vId;
uniform vec3 uColor; uniform uint uSel; uniform vec3 uSelColor; uniform vec3 uBoxMin; uniform vec3 uBoxMax;
uniform usampler2D uState; uniform int uClash; uniform vec3 uFade;
out vec4 o;
void main(){
  if (any(lessThan(vPos, uBoxMin)) || any(greaterThan(vPos, uBoxMax))) discard;
  vec3 c = uColor;
  uvec2 st = texelFetch(uState, ivec2(int(vId % 4096u), int(vId / 4096u)), 0).rg;
  if ((st.r & 128u) != 0u) discard;
  uint ck = st.g & 3u;
  if (uClash != 0) c = ck == 1u ? vec3(0.45, 0.0, 0.0) : ck == 2u ? vec3(0.0, 0.12, 0.5) : mix(uColor, uFade, 0.75);
  if (uSel != 0u && vId == uSel) c = uSelColor;
  o = vec4(c, 1.0);
}";

    const string BgVs = @"#version 330 core
out float vY;
void main(){
  vec2 p = vec2(gl_VertexID == 1 ? 3.0 : -1.0, gl_VertexID == 2 ? 3.0 : -1.0);
  vY = p.y * 0.5 + 0.5;
  gl_Position = vec4(p, 0.0, 1.0);
}";

    const string BgFs = @"#version 330 core
in float vY; uniform vec3 uTop; uniform vec3 uBot; out vec4 o;
void main(){ o = vec4(mix(uBot, uTop, clamp(vY, 0.0, 1.0)), 1.0); }";

    const string PickVs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=3) in uint aId;
uniform mat4 uMvp;
out vec3 vPos; flat out uint vId;
void main(){ vPos=aPos; vId=aId; gl_Position=uMvp*vec4(aPos,1.0); }";

    const string PickFs = @"#version 330 core
in vec3 vPos; flat in uint vId; uniform vec3 uBoxMin; uniform vec3 uBoxMax; uniform usampler2D uState; out uint o;
void main(){
  if (any(lessThan(vPos, uBoxMin)) || any(greaterThan(vPos, uBoxMax))) discard;
  if ((texelFetch(uState, ivec2(int(vId % 4096u), int(vId / 4096u)), 0).r & 128u) != 0u) discard;   // gizli kategori seçilmez
  o = vId;
}";

    // Kutu çizgileri ve tutamaçlar (köşe başına renk; noktalar beyaz halkalı yuvarlak).
    const string OverVs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec4 aCol;
uniform mat4 uMvp; uniform float uSize;
out vec4 vCol;
void main(){ vCol=aCol; gl_Position=uMvp*vec4(aPos,1.0); gl_PointSize=uSize; }";

    const string OverFs = @"#version 330 core
in vec4 vCol; uniform int uPoint; out vec4 o;
// Ölçü yakalama işaretleri (AutoCAD OSNAP gibi): 2 köşe = X, 3 orta = halka, 4 eksen merkezi = noktalı halka,
// 5 kenar = // , 6 yüzey = eşkenar dörtgen. d: şekle uzaklık (nokta-sprite birimi); koyu kenarlık + renkli çekirdek.
float shapeDist(vec2 c){
  vec2 q = c - vec2(0.5);
  if (uPoint == 2) return min(abs(q.x - q.y), abs(q.x + q.y)) * 0.7071 + max(max(abs(q.x), abs(q.y)) - 0.40, 0.0);
  if (uPoint == 3) return abs(length(q) - 0.36);
  if (uPoint == 4) return min(abs(length(q) - 0.38), max(length(q) - 0.07, 0.0));
  if (uPoint == 5) {
    float a = abs(q.x + q.y - 0.21) * 0.7071, b = abs(q.x + q.y + 0.21) * 0.7071;
    return min(a, b) + max(abs(q.x - q.y) * 0.7071 - 0.36, 0.0);
  }
  return abs(abs(q.x) + abs(q.y) - 0.36) * 0.7071;
}
void main(){
  if (uPoint >= 2) {
    float d = shapeDist(gl_PointCoord);
    if (d > 0.115) discard;
    o = d > 0.055 ? vec4(0.05, 0.05, 0.05, 0.9) : vec4(vCol.rgb, 1.0);
    return;
  }
  if (uPoint != 0) {
    vec2 d = gl_PointCoord - vec2(0.5); float r = dot(d, d);
    if (r > 0.25) discard;
    if (r > 0.19) { o = vec4(0.08, 0.08, 0.08, 1.0); return; }  // koyu dış halka: açık zeminde görünür
    if (r > 0.12) { o = vec4(1.0); return; }                    // beyaz iç halka: koyu zeminde görünür
  }
  o = vCol;
}";

    void BuildPrograms()
    {
        _pSurf = GL.Program(SurfVs, SurfFs);
        sMvp = GL.Uniform(_pSurf, "uMvp"); sTone = GL.Uniform(_pSurf, "uTone"); sCam = GL.Uniform(_pSurf, "uCam");
        sKey = GL.Uniform(_pSurf, "uKey"); sFill = GL.Uniform(_pSurf, "uFill"); sSky = GL.Uniform(_pSurf, "uSky");
        sLight = GL.Uniform(_pSurf, "uLight"); sSel = GL.Uniform(_pSurf, "uSel"); sSelColor = GL.Uniform(_pSurf, "uSelColor");
        sGlassA = GL.Uniform(_pSurf, "uGlassA"); sBoxMin = GL.Uniform(_pSurf, "uBoxMin"); sBoxMax = GL.Uniform(_pSurf, "uBoxMax");
        sState = GL.Uniform(_pSurf, "uState"); sColored = GL.Uniform(_pSurf, "uColored"); sClash = GL.Uniform(_pSurf, "uClash");
        sClass = GL.Uniform(_pSurf, "uClass"); sDetail = GL.Uniform(_pSurf, "uDetail"); sModel = GL.Uniform(_pSurf, "uModel"); sClashColor = GL.Uniform(_pSurf, "uClashColor"); sClashColor2 = GL.Uniform(_pSurf, "uClashColor2"); sFade = GL.Uniform(_pSurf, "uFade"); sPass = GL.Uniform(_pSurf, "uPass");
        _pEdge = GL.Program(EdgeVs, EdgeFs);
        eMvp = GL.Uniform(_pEdge, "uMvp"); eColor = GL.Uniform(_pEdge, "uColor"); eSel = GL.Uniform(_pEdge, "uSel");
        eSelColor = GL.Uniform(_pEdge, "uSelColor"); eBoxMin = GL.Uniform(_pEdge, "uBoxMin"); eBoxMax = GL.Uniform(_pEdge, "uBoxMax");
        eState = GL.Uniform(_pEdge, "uState"); eClash = GL.Uniform(_pEdge, "uClash"); eFade = GL.Uniform(_pEdge, "uFade");
        _pBg = GL.Program(BgVs, BgFs);
        bTop = GL.Uniform(_pBg, "uTop"); bBot = GL.Uniform(_pBg, "uBot");
        _pPick = GL.Program(PickVs, PickFs);
        pkMvp = GL.Uniform(_pPick, "uMvp"); pkBoxMin = GL.Uniform(_pPick, "uBoxMin"); pkBoxMax = GL.Uniform(_pPick, "uBoxMax"); pkState = GL.Uniform(_pPick, "uState");
        _pOver = GL.Program(OverVs, OverFs);
        oMvp = GL.Uniform(_pOver, "uMvp"); oPoint = GL.Uniform(_pOver, "uPoint"); oSize = GL.Uniform(_pOver, "uSize");
        InitSketchGl();
    }

    // ---- veri yükleme --------------------------------------------------------------------------------------------

    void ComputeBounds(SceneData s)
    {
        int n = s.Labels.Count;
        _elMin = new float[n * 3];
        _elMax = new float[n * 3];
        Array.Fill(_elMin, float.MaxValue);
        Array.Fill(_elMax, float.MinValue);
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var v in CollectionsMarshal.AsSpan(s.Vertices))
        {
            if (v.X < x0) x0 = v.X; if (v.Y < y0) y0 = v.Y; if (v.Z < z0) z0 = v.Z;
            if (v.X > x1) x1 = v.X; if (v.Y > y1) y1 = v.Y; if (v.Z > z1) z1 = v.Z;
            int k = ((int)v.Id - 1) * 3;
            if (k < 0 || k >= _elMin.Length) continue;
            if (v.X < _elMin[k]) _elMin[k] = v.X; if (v.Y < _elMin[k + 1]) _elMin[k + 1] = v.Y; if (v.Z < _elMin[k + 2]) _elMin[k + 2] = v.Z;
            if (v.X > _elMax[k]) _elMax[k] = v.X; if (v.Y > _elMax[k + 1]) _elMax[k + 1] = v.Y; if (v.Z > _elMax[k + 2]) _elMax[k + 2] = v.Z;
        }
        if (x0 > x1) { x0 = y0 = z0 = -1; x1 = y1 = z1 = 1; }
        _geoMin = new[] { x0, y0, z0 };
        _geoMax = new[] { x1, y1, z1 };
        UpdateCenter();
    }

    /// <summary>Görünür bölge = geometri ∩ kutu → sığdırma buna göre; yakın/uzak düzlemler her şeyi kapsar.</summary>
    void UpdateCenter()
    {
        var lo = new double[3];
        var hi = new double[3];
        for (int k = 0; k < 3; k++)
        {
            lo[k] = Math.Max(_geoMin[k], _bMin[k]);
            hi[k] = Math.Min(_geoMax[k], _bMax[k]);
            if (lo[k] > hi[k]) { lo[k] = _bMin[k]; hi[k] = _bMax[k]; }
        }
        _center = new Point3D((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
        _fitRadius = Math.Max(0.5, new Vector3D(hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]).Length / 2);
        double r = 0;
        for (int i = 0; i < 8; i++)
        {
            var p = new Point3D((i & 1) == 0 ? Math.Min(_geoMin[0], _bMin[0]) : Math.Max(_geoMax[0], _bMax[0]),
                                (i & 2) == 0 ? Math.Min(_geoMin[1], _bMin[1]) : Math.Max(_geoMax[1], _bMax[1]),
                                (i & 4) == 0 ? Math.Min(_geoMin[2], _bMin[2]) : Math.Max(_geoMax[2], _bMax[2]));
            r = Math.Max(r, (p - _center).Length);
        }
        _radius = Math.Max(0.5, r);
    }

    void BuildState()
    {
        var s = _scene!;
        int n = s.Labels.Count + 1;
        _stateH = (n + StateTexWidth - 1) / StateTexWidth;
        _state = new byte[StateTexWidth * _stateH * 2];
        for (int i = 0; i < s.ElemColor.Count; i++) _state[2 * (i + 1)] = s.ElemColor[i];
        for (int i = 0; i < s.ElemDetail.Count; i++) _state[2 * (i + 1) + 1] = (byte)(Math.Min((int)s.ElemDetail[i], Detail.Count - 1) << 2);
        _clashRes = null;
        ApplyHiddenBits();
    }

    void UploadState()
    {
        if (_ctx == IntPtr.Zero || Error != null) return;
        MakeCurrent();
        if (_stateTex == 0) { uint t; GL.GenTextures(1, &t); _stateTex = t; }
        GL.BindTexture(GL.TEXTURE_2D, _stateTex);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.NEAREST);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.NEAREST);
        GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);
        fixed (byte* p = _state) GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RG8UI, StateTexWidth, _stateH, 0, GL.RG_INTEGER, GL.UNSIGNED_BYTE, p);
        GL.BindTexture(GL.TEXTURE_2D, 0);
    }

    void FreeScene()
    {
        GL.Del(GL.DeleteVertexArrays, ref _vao);
        GL.Del(GL.DeleteVertexArrays, ref _evao);
        GL.Del(GL.DeleteBuffers, ref _vbo);
        GL.Del(GL.DeleteBuffers, ref _iboO);
        GL.Del(GL.DeleteBuffers, ref _iboG);
        GL.Del(GL.DeleteBuffers, ref _evbo);
        if (_stateTex != 0) { uint t = _stateTex; GL.DeleteTextures(1, &t); _stateTex = 0; }
    }

    void Upload()
    {
        if (_scene == null) return;
        MakeCurrent();
        FreeScene();
        var s = _scene;

        _vao = GL.Gen(GL.GenVertexArrays);
        GL.BindVertexArray(_vao);
        _vbo = GL.Gen(GL.GenBuffers);
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        var verts = CollectionsMarshal.AsSpan(s.Vertices);
        fixed (SurfVertex* p = verts) GL.BufferData(GL.ARRAY_BUFFER, (nint)verts.Length * sizeof(SurfVertex), p, GL.STATIC_DRAW);
        int stride = sizeof(SurfVertex);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, GL.FLOAT, 0, stride, (IntPtr)0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 3, GL.SHORT, 1, stride, (IntPtr)12);
        GL.EnableVertexAttribArray(2); GL.VertexAttribIPointer(2, 1, GL.SHORT, stride, (IntPtr)18);
        GL.EnableVertexAttribArray(3); GL.VertexAttribIPointer(3, 1, GL.UNSIGNED_INT, stride, (IntPtr)20);
        _iboO = IndexBuffer(s.Opaque, out _opaqueCount);
        _iboG = IndexBuffer(s.Glass, out _glassCount);
        GL.BindVertexArray(0);

        _evao = GL.Gen(GL.GenVertexArrays);
        GL.BindVertexArray(_evao);
        _evbo = GL.Gen(GL.GenBuffers);
        GL.BindBuffer(GL.ARRAY_BUFFER, _evbo);
        var edges = CollectionsMarshal.AsSpan(s.Edges);
        fixed (EdgeVertex* p = edges) GL.BufferData(GL.ARRAY_BUFFER, (nint)edges.Length * sizeof(EdgeVertex), p, GL.STATIC_DRAW);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, GL.FLOAT, 0, sizeof(EdgeVertex), (IntPtr)0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribIPointer(1, 1, GL.UNSIGNED_INT, sizeof(EdgeVertex), (IntPtr)12);
        _edgeCount = edges.Length;
        GL.BindVertexArray(0);
        GL.BindBuffer(GL.ARRAY_BUFFER, 0);
        UploadState();
        _uploaded = true;
        Invalidate();
    }

    static uint IndexBuffer(System.Collections.Generic.List<uint> list, out int count)
    {
        uint id = GL.Gen(GL.GenBuffers);
        GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, id);
        var span = CollectionsMarshal.AsSpan(list);
        fixed (uint* p = span) GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, (nint)span.Length * 4, p, GL.STATIC_DRAW);
        count = span.Length;
        return id;
    }

    // ---- çerçeve tamponları --------------------------------------------------------------------------------------

    void FreeFbos()
    {
        GL.Del(GL.DeleteFramebuffers, ref _msFbo);
        GL.Del(GL.DeleteRenderbuffers, ref _msColor);
        GL.Del(GL.DeleteRenderbuffers, ref _msDepth);
        GL.Del(GL.DeleteFramebuffers, ref _pkFbo);
        GL.Del(GL.DeleteRenderbuffers, ref _pkColor);
        GL.Del(GL.DeleteRenderbuffers, ref _pkDepth);
        GL.Del(GL.DeleteFramebuffers, ref _lrFbo);
        GL.Del(GL.DeleteRenderbuffers, ref _lrColor);
        GL.Del(GL.DeleteRenderbuffers, ref _lrDepth);
        _lrW = _lrH = 0;
        _msOk = _pkOk = false;
    }

    void EnsureFbos()
    {
        if (_fboW == _w && _fboH == _h && (_msFbo != 0 || _samples < 2)) return;
        FreeFbos();
        _fboW = _w; _fboH = _h;
        if (_samples >= 2)
        {
            _msFbo = GL.Gen(GL.GenFramebuffers);
            GL.BindFramebuffer(GL.FRAMEBUFFER, _msFbo);
            _msColor = Rb(GL.RGBA8, _samples, GL.COLOR_ATTACHMENT0, _w, _h);
            _msDepth = Rb(GL.DEPTH_COMPONENT24, _samples, GL.DEPTH_ATTACHMENT, _w, _h);
            _msOk = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
        }
        _pkFbo = GL.Gen(GL.GenFramebuffers);
        GL.BindFramebuffer(GL.FRAMEBUFFER, _pkFbo);
        _pkColor = Rb(GL.R32UI, 0, GL.COLOR_ATTACHMENT0, _w, _h);
        _pkDepth = Rb(GL.DEPTH_COMPONENT24, 0, GL.DEPTH_ATTACHMENT, _w, _h);
        _pkOk = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
    }

    bool EnsureLowRes(int w, int h)
    {
        if (_lrFbo != 0 && _lrW == w && _lrH == h) return true;
        GL.Del(GL.DeleteFramebuffers, ref _lrFbo);
        GL.Del(GL.DeleteRenderbuffers, ref _lrColor);
        GL.Del(GL.DeleteRenderbuffers, ref _lrDepth);
        _lrW = w; _lrH = h;
        _lrFbo = GL.Gen(GL.GenFramebuffers);
        GL.BindFramebuffer(GL.FRAMEBUFFER, _lrFbo);
        _lrColor = Rb(GL.RGBA8, 0, GL.COLOR_ATTACHMENT0, w, h);
        _lrDepth = Rb(GL.DEPTH_COMPONENT24, 0, GL.DEPTH_ATTACHMENT, w, h);
        bool ok = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        return ok;
    }

    static uint Rb(uint format, int samples, uint attachment, int w, int h)
    {
        uint rb = GL.Gen(GL.GenRenderbuffers);
        GL.BindRenderbuffer(GL.RENDERBUFFER, rb);
        if (samples > 0) GL.RenderbufferStorageMultisample(GL.RENDERBUFFER, samples, format, w, h);
        else GL.RenderbufferStorage(GL.RENDERBUFFER, format, w, h);
        GL.FramebufferRenderbuffer(GL.FRAMEBUFFER, attachment, GL.RENDERBUFFER, rb);
        return rb;
    }

    // ---- çizim ---------------------------------------------------------------------------------------------------

    void Render()
    {
        if (_ctx == IntPtr.Zero || Error != null) return;
        var diagSw = DiagPath != null ? System.Diagnostics.Stopwatch.StartNew() : null;
        MakeCurrent();
        EnsureFbos();
        bool hq = _msOk && !_interactive;
        int rw = _w, rh = _h;
        bool lowRes = false;
        if (_interactive && _scale < 0.999)
        {
            rw = Math.Max(1, (int)(_w * _scale));
            rh = Math.Max(1, (int)(_h * _scale));
            lowRes = EnsureLowRes(rw, rh);
            if (!lowRes) { rw = _w; rh = _h; }
        }
        // İki kare önceki ölçümü oku (bu yuvayı yeniden kullanmadan önce), sonra bu kareyi ölç.
        int qi = _qi;
        _qi ^= 1;
        if (_qPending[qi])
        {
            ulong ns;
            GL.GetQueryObjectui64v(_q[qi], GL.QUERY_RESULT, &ns);
            _qPending[qi] = false;
            AdjustScale(ns / 1e6);
        }
        bool measure = _interactive && _q[qi] != 0;
        if (measure) GL.BeginQuery(GL.TIME_ELAPSED, _q[qi]);

        DrawScene(hq ? _msFbo : lowRes ? _lrFbo : 0, rw, rh, hq ? (float)_pal.EdgePx : 1f, overlay: true);

        if (hq)
        {
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, _msFbo);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, 0);
            GL.BlitFramebuffer(0, 0, _w, _h, 0, 0, _w, _h, GL.COLOR_BUFFER_BIT, GL.NEAREST);
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        }
        else if (lowRes)
        {
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, _lrFbo);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, 0);
            GL.BlitFramebuffer(0, 0, rw, rh, 0, 0, _w, _h, GL.COLOR_BUFFER_BIT, GL.LINEAR);
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        }
        if (measure) { GL.EndQuery(GL.TIME_ELAPSED); _qPending[qi] = true; }
        Win32.SwapBuffers(_hdc);
        if (diagSw != null) { GL.Finish(); Diag("frame " + diagSw.Elapsed.TotalMilliseconds.ToString("0.0")); }
    }

    void DrawScene(uint fbo, int vw, int vh, float lineWidth, bool overlay)
    {
        GL.BindFramebuffer(GL.FRAMEBUFFER, fbo);
        GL.Viewport(0, 0, vw, vh);
        GL.ClearColor(0, 0, 0, 1);
        GL.DepthMask(1);
        GL.Clear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);

        // arka plan gradyanı
        GL.Disable(GL.DEPTH_TEST);
        GL.DepthMask(0);
        GL.UseProgram(_pBg);
        U3(bTop, _pal.BgTop); U3(bBot, _pal.BgBottom);
        GL.BindVertexArray(_bgVao);
        GL.DrawArrays(GL.TRIANGLES, 0, 3);
        GL.DepthMask(1);

        if (!_uploaded || _vao == 0) return;
        var mvp = Mvp();
        var (right, up) = Basis();
        var key = Norm(_look * 0.55 - up * 0.8 + right * 0.65);
        var fill = Norm(_look * 0.6 + up * 0.25 - right * 0.9);
        var sky = Norm(new Vector3D(0.25, 0.4, -1));
        var fade = Mix(_pal.BgTop, _pal.BgBottom, 0.5);

        GL.Enable(GL.DEPTH_TEST);
        GL.DepthFunc(GL.LEQUAL);
        GL.Enable(GL.POLYGON_OFFSET_FILL);
        GL.PolygonOffset(1f, 1f); // yüzeyleri biraz geri it → üzerindeki kenar çizgileri temiz görünsün
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _stateTex);

        GL.UseProgram(_pSurf);
        fixed (float* m = mvp) GL.UniformMatrix4fv(sMvp, 1, 1, m);
        var tones = ToneColors();
        fixed (float* t = tones) GL.Uniform3fv(sTone, 6, t);
        fixed (float* t = ClassColors) GL.Uniform3fv(sClass, 5, t);
        fixed (float* t = DetailColors) GL.Uniform3fv(sDetail, Detail.Count, t);
        fixed (float* t = ModelColors) GL.Uniform3fv(sModel, 16, t);
        GL.Uniform3f(sCam, (float)_pos.X, (float)_pos.Y, (float)_pos.Z);
        UV(sKey, key); UV(sFill, fill); UV(sSky, sky);
        GL.Uniform4f(sLight, (float)_pal.Ambient, (float)_pal.Key, (float)_pal.Fill, (float)_pal.Sky);
        GL.Uniform1ui(sSel, Selected);
        GL.Uniform3f(sSelColor, 0.20f, 0.52f, 0.96f);
        GL.Uniform1f(sGlassA, 0.32f);
        UBox(sBoxMin, sBoxMax);
        GL.Uniform1i(sState, 0);
        GL.Uniform1i(sColored, _pal.Detailed ? 2 : _pal.Colored ? 1 : 0);
        GL.Uniform1i(sClash, _clashMode ? 1 : 0);
        GL.Uniform3f(sClashColor, 1.0f, 0.08f, 0.08f);
        GL.Uniform3f(sClashColor2, 0.10f, 0.40f, 1.0f);
        GL.Uniform3f(sFade, fade.r, fade.g, fade.b);
        GL.Uniform1i(sPass, 0);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _iboO);
        GL.DrawElements(GL.TRIANGLES, _opaqueCount, GL.UNSIGNED_INT, IntPtr.Zero);

        if (_edgeCount > 0 && Sketch) DrawSketchEdges(mvp, fade, lineWidth, vw, vh);
        else if (_edgeCount > 0)
        {
            GL.UseProgram(_pEdge);
            fixed (float* m = mvp) GL.UniformMatrix4fv(eMvp, 1, 1, m);
            U3(eColor, _pal.Edge);
            GL.Uniform1ui(eSel, Selected);
            GL.Uniform3f(eSelColor, 0.08f, 0.36f, 0.85f);
            UBox(eBoxMin, eBoxMax);
            GL.Uniform1i(eState, 0);
            GL.Uniform1i(eClash, _clashMode ? 1 : 0);
            GL.Uniform3f(eFade, fade.r, fade.g, fade.b);
            GL.LineWidth(lineWidth);
            GL.BindVertexArray(_evao);
            GL.DrawArrays(GL.LINES, 0, _edgeCount);
        }

        // Kesit poşesi: kutunun kestiği katıların iç yüzleri. Biraz öne çekilir → katının uzak kenar çizgilerini örter.
        // Kutu hiçbir geometriyi kesmiyorsa atlanır.
        if (BoxCutsGeometry())
        {
            GL.UseProgram(_pSurf);
            GL.Uniform1i(sPass, 1);
            GL.PolygonOffset(-1f, -2f);
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _iboO);
            GL.DrawElements(GL.TRIANGLES, _opaqueCount, GL.UNSIGNED_INT, IntPtr.Zero);
            GL.Uniform1i(sPass, 0);
            GL.PolygonOffset(1f, 1f);
        }

        if (_glassCount > 0)
        {
            GL.UseProgram(_pSurf);
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
            GL.DepthMask(0);
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _iboG);
            GL.DrawElements(GL.TRIANGLES, _glassCount, GL.UNSIGNED_INT, IntPtr.Zero);
            GL.DepthMask(1);
            GL.Disable(GL.BLEND);
        }
        GL.Disable(GL.POLYGON_OFFSET_FILL);
        GL.BindTexture(GL.TEXTURE_2D, 0);
        GL.BindVertexArray(0);
        if (Sketch) DrawInk(fbo, vw, vh, mvp, lineWidth);   // dış hat + kesit çizgileri (kalem)

        if (overlay && _boxMode) DrawBoxOverlay(mvp);
        DrawMeasureOverlay(mvp, overlay);
        if (overlay) { DrawHoverTag(); DrawViewCube(); DrawModelList(); }   // görüntü alırken (overlay=false) küp ve etiket çizilmez
    }

    bool BoxCutsGeometry()
    {
        for (int k = 0; k < 3; k++)
            if (_geoMin[k] < _bMin[k] || _geoMax[k] > _bMax[k]) return true;
        return false;
    }

    void UBox(int lo, int hi)
    {
        GL.Uniform3f(lo, (float)_bMin[0], (float)_bMin[1], (float)_bMin[2]);
        GL.Uniform3f(hi, (float)_bMax[0], (float)_bMax[1], (float)_bMax[2]);
    }

    /// <summary>Kutu kenarları + 6 yüz tutamacı. Derinlik testi yok: kutu modelin arkasında da görünür.</summary>
    void DrawBoxOverlay(float[] mvp)
    {
        var data = new float[(24 + 6) * 7];
        int o = 0;
        void V(double x, double y, double z, float r, float g, float b, float a)
        {
            data[o++] = (float)x; data[o++] = (float)y; data[o++] = (float)z;
            data[o++] = r; data[o++] = g; data[o++] = b; data[o++] = a;
        }
        bool dark = _pal.Dark || Luma(_pal.BgBottom) < 0.45;
        float lc = dark ? 0.85f : 0.15f;
        for (int i = 0; i < 8; i++)
            for (int ax = 0; ax < 3; ax++)
            {
                if ((i & (1 << ax)) != 0) continue;
                int j = i | (1 << ax);
                V(C(i, 0), C(i, 1), C(i, 2), lc, lc, lc, 0.75f);
                V(C(j, 0), C(j, 1), C(j, 2), lc, lc, lc, 0.75f);
            }
        for (int f = 0; f < 6; f++)
        {
            var c = FaceCenter(f);
            bool hot = f == _hover || f == _dragFace;
            if (hot) V(c.X, c.Y, c.Z, 1.0f, 0.55f, 0.10f, 1);
            else V(c.X, c.Y, c.Z, 0.15f, 0.45f, 0.95f, 1);
        }
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.Enable(GL.PROGRAM_POINT_SIZE);
        GL.Enable(GL.POINT_SPRITE); // uyumluluk profilinde gl_PointCoord için gerekli
        GL.UseProgram(_pOver);
        fixed (float* m = mvp) GL.UniformMatrix4fv(oMvp, 1, 1, m);
        GL.BindVertexArray(_ovao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _ovbo);
        fixed (float* p = data) GL.BufferData(GL.ARRAY_BUFFER, data.Length * 4, p, GL.DYNAMIC_DRAW);
        GL.LineWidth(1f);
        GL.Uniform1i(oPoint, 0);
        GL.DrawArrays(GL.LINES, 0, 24);
        GL.Uniform1i(oPoint, 1);
        GL.Uniform1f(oSize, 17f);
        GL.DrawArrays(GL.POINTS, 24, 6);
        GL.BindVertexArray(0);
        GL.BindBuffer(GL.ARRAY_BUFFER, 0);
        GL.Disable(GL.PROGRAM_POINT_SIZE);
        GL.Disable(GL.POINT_SPRITE);
        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
    }

    double C(int corner, int ax) => (corner & (1 << ax)) == 0 ? _bMin[ax] : _bMax[ax];

    Point3D FaceCenter(int f)
    {
        int ax = f / 2;
        var c = new double[3];
        for (int k = 0; k < 3; k++) c[k] = (_bMin[k] + _bMax[k]) / 2;
        c[ax] = f % 2 == 0 ? _bMin[ax] : _bMax[ax];
        return new Point3D(c[0], c[1], c[2]);
    }

    /// <summary>Görüntüyü yaklaşık <paramref name="targetWidth"/> piksel genişlikte (pencerenin tam katı), kenar
    /// yumuşatmalı ve tutamaçsız çizer. BGRA, üst satır önce.</summary>
    public byte[]? Capture(int targetWidth, out int width, out int height)
    {
        width = height = 0;
        if (_ctx == IntPtr.Zero || Error != null || !_uploaded) return null;
        int scale = Math.Clamp((int)Math.Round((double)targetWidth / Math.Max(1, _w)), 1, 4);
        int samples = Math.Min(_samples, 4);
        MakeCurrent();
        int maxRb = 0;
        GL.GetIntegerv(GL.MAX_RENDERBUFFER_SIZE, &maxRb);
        int limit = Math.Min(maxRb > 0 ? maxRb : 4096, 8192);
        while (scale > 1 && (_w * scale > limit || _h * scale > limit)) scale--;
        int W = _w * scale, H = _h * scale;
        uint msFbo = GL.Gen(GL.GenFramebuffers), rsFbo = GL.Gen(GL.GenFramebuffers);
        GL.BindFramebuffer(GL.FRAMEBUFFER, msFbo);
        uint msC = Rb(GL.RGBA8, samples >= 2 ? samples : 0, GL.COLOR_ATTACHMENT0, W, H);
        uint msD = Rb(GL.DEPTH_COMPONENT24, samples >= 2 ? samples : 0, GL.DEPTH_ATTACHMENT, W, H);
        GL.BindFramebuffer(GL.FRAMEBUFFER, rsFbo);
        uint rsC = Rb(GL.RGBA8, 0, GL.COLOR_ATTACHMENT0, W, H);
        byte[]? pixels = null;
        int ow = _w, oh = _h;
        bool oi = _interactive;
        try
        {
            _w = W; _h = H; _interactive = false; // en-boy oranı aynı → kamera görüntüsü değişmez
            DrawScene(msFbo, W, H, (float)_pal.EdgePx * scale, overlay: false);
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, msFbo);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, rsFbo);
            GL.BlitFramebuffer(0, 0, W, H, 0, 0, W, H, GL.COLOR_BUFFER_BIT, GL.NEAREST);
            GL.BindFramebuffer(GL.FRAMEBUFFER, rsFbo);
            var raw = new byte[W * H * 4];
            GL.PixelStorei(GL.PACK_ALIGNMENT, 4);
            fixed (byte* p = raw) GL.ReadPixels(0, 0, W, H, GL.BGRA, GL.UNSIGNED_BYTE, p);
            pixels = new byte[raw.Length];
            int row = W * 4;
            for (int y = 0; y < H; y++) Buffer.BlockCopy(raw, (H - 1 - y) * row, pixels, y * row, row);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            width = W; height = H;
        }
        finally
        {
            _w = ow; _h = oh; _interactive = oi;
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
            GL.Del(GL.DeleteFramebuffers, ref msFbo);
            GL.Del(GL.DeleteFramebuffers, ref rsFbo);
            GL.Del(GL.DeleteRenderbuffers, ref msC);
            GL.Del(GL.DeleteRenderbuffers, ref msD);
            GL.Del(GL.DeleteRenderbuffers, ref rsC);
            Invalidate();
        }
        return pixels;
    }

    void ResetScale() { _scale = 1; _pixelsNotBottleneck = false; _prevMs = double.NaN; _prevScale = 1; }

    void AdjustScale(double gpuMs)
    {
        if (_pixelsNotBottleneck) { _scale = 1; return; }
        if (!double.IsNaN(_prevMs) && _scale < _prevScale - 0.01 && gpuMs > _prevMs * 0.85)
        {
            _pixelsNotBottleneck = true;
            _scale = 1;
            Diag("piksel darboğaz değil → tam çözünürlük");
            return;
        }
        _prevMs = gpuMs;
        _prevScale = _scale;
        if (gpuMs > TargetMs * 1.4) _scale = Math.Max(MinScale, _scale * Math.Sqrt(TargetMs / gpuMs));
        else if (gpuMs < TargetMs * 0.6) _scale = Math.Min(1, _scale * 1.1);
        Diag($"gpu {gpuMs:0.0} ms scale {_scale:0.00}");
    }

    /// <summary>Tıklanan noktadaki eleman (0 = boş) ve 3B nokta. Küçük bir pencere içinde merkeze en yakın eleman
    /// alınır → ince borular da kolay seçilir.</summary>
    (uint id, Point3D? point) Pick(Point p)
    {
        const int R = 4;
        var reg = PickRegion(p, R);
        if (reg == null) return (0, null);
        var (x0, y0, rw, rh, ids, depth) = reg.Value;
        int cx = (int)p.X, cy = _h - 1 - (int)p.Y;
        uint best = 0; int bestD = int.MaxValue; float bestZ = 1; int bx = cx, by = cy;
        for (int j = 0; j < rh; j++)
            for (int i = 0; i < rw; i++)
            {
                uint id = ids[j * rw + i];
                if (id == 0) continue;
                int dx = x0 + i - cx, dy = y0 + j - cy, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = id; bestZ = depth[j * rw + i]; bx = x0 + i; by = y0 + j; }
            }
        if (best == 0 || bestZ >= 1) return (0, null);
        return (best, Unproject(bx + 0.5, _h - 1 - by + 0.5, bestZ));
    }

    /// <summary>İmlecin çevresindeki (2R+1)² piksellik pencerede eleman numaraları ve derinlik (alt satır önce,
    /// GL koordinatı: x0/y0 sol-alt). Ölçü yakalaması da bunu kullanır.</summary>
    (int x0, int y0, int rw, int rh, uint[] ids, float[] depth)? PickRegion(Point p, int R)
    {
        if (_ctx == IntPtr.Zero || Error != null || !_uploaded || _vao == 0) return null;
        MakeCurrent();
        EnsureFbos();
        if (!_pkOk) return null;
        GL.BindFramebuffer(GL.FRAMEBUFFER, _pkFbo);
        GL.Viewport(0, 0, _w, _h);
        int cx = (int)p.X, cy = _h - 1 - (int)p.Y;
        int x0 = Math.Clamp(cx - R, 0, _w - 1), y0 = Math.Clamp(cy - R, 0, _h - 1);
        int x1 = Math.Clamp(cx + R, 0, _w - 1), y1 = Math.Clamp(cy + R, 0, _h - 1);
        int rw = x1 - x0 + 1, rh = y1 - y0 + 1;
        // Yalnız imlecin çevresi taranır → piksel maliyeti yok denecek kadar az.
        GL.Enable(GL.SCISSOR_TEST);
        GL.Scissor(x0, y0, rw, rh);
        uint* zero = stackalloc uint[4];
        GL.ClearBufferuiv(GL.COLOR, 0, zero);
        GL.DepthMask(1);
        GL.Clear(GL.DEPTH_BUFFER_BIT);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthFunc(GL.LEQUAL);
        GL.UseProgram(_pPick);
        var mvp = Mvp();
        fixed (float* m = mvp) GL.UniformMatrix4fv(pkMvp, 1, 1, m);
        UBox(pkBoxMin, pkBoxMax);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _stateTex);
        GL.Uniform1i(pkState, 0);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _iboO);
        GL.DrawElements(GL.TRIANGLES, _opaqueCount, GL.UNSIGNED_INT, IntPtr.Zero);
        if (_glassCount > 0)
        {
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _iboG);
            GL.DrawElements(GL.TRIANGLES, _glassCount, GL.UNSIGNED_INT, IntPtr.Zero);
        }
        GL.BindVertexArray(0);

        var ids = new uint[rw * rh];
        var depth = new float[rw * rh];
        GL.PixelStorei(GL.PACK_ALIGNMENT, 4);
        fixed (uint* pi = ids) GL.ReadPixels(x0, y0, rw, rh, GL.RED_INTEGER, GL.UNSIGNED_INT, pi);
        fixed (float* pd = depth) GL.ReadPixels(x0, y0, rw, rh, GL.DEPTH_COMPONENT, GL.FLOAT, pd);
        GL.Disable(GL.SCISSOR_TEST);
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        return (x0, y0, rw, rh, ids, depth);
    }

    // ---- kamera --------------------------------------------------------------------------------------------------

    double TanH => Math.Tan(Fov * Math.PI / 360);
    double TanV => TanH * _h / Math.Max(1, _w);

    (Vector3D right, Vector3D up) Basis()
    {
        var right = Vector3D.CrossProduct(_look, Z);
        if (right.LengthSquared < 1e-12) right = new Vector3D(1, 0, 0);
        right.Normalize();
        var up = Vector3D.CrossProduct(right, _look);
        up.Normalize();
        return (right, up);
    }

    (double near, double far) Planes()
    {
        double dc = (_center - _pos).Length;
        double near = Math.Max(dc - _radius * 1.05, _radius * 0.002);
        double far = dc + _radius * 1.05;
        return (near, Math.Max(far, near * 2));
    }

    /// <summary>Satır-öncelikli MVP (uniform'a transpose=1 ile verilir).</summary>
    float[] Mvp()
    {
        var (r, u) = Basis();
        var f = _look;
        var e = (Vector3D)_pos;
        double[] v0 = { r.X, r.Y, r.Z, -Vector3D.DotProduct(r, e) };
        double[] v1 = { u.X, u.Y, u.Z, -Vector3D.DotProduct(u, e) };
        double[] v2 = { -f.X, -f.Y, -f.Z, Vector3D.DotProduct(f, e) };
        var (n, fa) = Planes();
        double a = 1 / TanH, b = 1 / TanV, c = -(fa + n) / (fa - n), d = -2 * fa * n / (fa - n);
        var m = new float[16];
        for (int i = 0; i < 4; i++)
        {
            m[i] = (float)(a * v0[i]);
            m[4 + i] = (float)(b * v1[i]);
            m[8 + i] = (float)(c * v2[i] + (i == 3 ? d : 0));
            m[12 + i] = (float)(-v2[i]);
        }
        return m;
    }

    /// <summary>Dünya noktasının pencere pikseli (kameranın önünde değilse null).</summary>
    Point? Project(Point3D p)
    {
        var m = Mvp();
        double x = m[0] * p.X + m[1] * p.Y + m[2] * p.Z + m[3];
        double y = m[4] * p.X + m[5] * p.Y + m[6] * p.Z + m[7];
        double w = m[12] * p.X + m[13] * p.Y + m[14] * p.Z + m[15];
        if (w <= 1e-9) return null;
        return new Point((x / w + 1) / 2 * _w, (1 - y / w) / 2 * _h);
    }

    Vector3D RayDir(double x, double y)
    {
        var (r, u) = Basis();
        double nx = 2 * x / Math.Max(1, _w) - 1, ny = 1 - 2 * y / Math.Max(1, _h);
        return _look + r * (nx * TanH) + u * (ny * TanV); // dir·look = 1
    }

    Point3D Unproject(double x, double y, float depth)
    {
        var (n, f) = Planes();
        double zn = 2 * depth - 1;
        double eye = 2 * f * n / ((f + n) - zn * (f - n));
        return _pos + RayDir(x, y) * eye;
    }

    void FitAll(Vector3D look) => FitSphere(_center, _fitRadius, look);

    void FitSphere(Point3D c, double radius, Vector3D look)
    {
        _look = Norm(look);
        double t = Math.Min(TanH, _w > 1 ? TanV : TanH);
        double dist = Math.Max(radius, 0.3) / Math.Sin(Math.Atan(t)) * 1.03;
        _pos = c - _look * dist;
        _focus = dist;
        Invalidate();
    }

    bool ElementBox(uint id, out Point3D c, out double r)
    {
        c = default; r = 0;
        int k = ((int)id - 1) * 3;
        if (id == 0 || k + 2 >= _elMin.Length || _elMin[k] > _elMax[k]) return false;
        // Elemanın kutu içinde kalan kısmı
        var lo = new double[3];
        var hi = new double[3];
        for (int a = 0; a < 3; a++)
        {
            lo[a] = Math.Max(_elMin[k + a], _bMin[a]);
            hi[a] = Math.Min(_elMax[k + a], _bMax[a]);
            if (lo[a] > hi[a]) { lo[a] = _elMin[k + a]; hi[a] = _elMax[k + a]; }
        }
        c = new Point3D((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
        r = new Vector3D(hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]).Length / 2;
        return true;
    }

    void Select(uint id)
    {
        if (Selected == id) return;
        Selected = id;
        if (_clashRes != null) { ApplyClashBits(); UploadState(); }   // seçili çakışan kırmızı, ortakları mavi
        Invalidate();
        SelectionChanged?.Invoke();
    }

    void BeginNav(Point p)
    {
        _nav = true;
        _last = p;
        var (_, hit) = Pick(p);
        // Seçili eleman varsa döndürme onun merkezi etrafında; yoksa imlecin altındaki nokta etrafında.
        _pivot = ElementBox(Selected, out var c, out _) ? c : hit ?? (_pos + _look * _focus);
        _panDepth = hit is { } h ? Math.Max(Vector3D.DotProduct(h - _pos, _look), Planes().near) : _focus;
        Win32.SetCapture(_hwnd);
    }

    void Orbit(double dx, double dy)
    {
        const double k = 0.008;
        var m = Matrix3D.Identity;
        m.RotateAt(new Quaternion(Z, -dx * k * 180 / Math.PI), _pivot);
        _pos = m.Transform(_pos);
        _look = m.Transform(_look);

        double cur = Math.Asin(Math.Clamp(_look.Z, -1, 1));
        double next = Math.Clamp(cur - dy * k, -1.55, 1.55);
        var (right, _) = Basis();
        m = Matrix3D.Identity;
        m.RotateAt(new Quaternion(right, (next - cur) * 180 / Math.PI), _pivot);
        _pos = m.Transform(_pos);
        _look = Norm(m.Transform(_look));
    }

    void Pan(double dx, double dy)
    {
        var (right, up) = Basis();
        double wpp = 2 * TanH * _panDepth / Math.Max(1, _w);
        _pos += (-right * dx + up * dy) * wpp;
    }

    void Zoom(Point p, int delta)
    {
        double f = Math.Pow(0.82, delta / 120.0);
        var (_, hit) = Pick(p);
        double depth = hit is { } h ? Vector3D.DotProduct(h - _pos, _look) : _focus;
        var target = _pos + RayDir(p.X, p.Y) * depth;
        _pos = target + (_pos - target) * f;
        _focus = Math.Max(depth * f, _radius * 0.002);
        Invalidate();
    }

    void FitSelectionOrAll()
    {
        if (ElementBox(Selected, out var c, out var r)) FitSphere(c, r, _look);
        else FitAll(_look);
    }

    // ---- kutu tutamaçları ----------------------------------------------------------------------------------------

    int HandleAt(Point p)
    {
        if (!_boxMode) return -1;
        int best = -1;
        double bestD = 14 * 14;
        for (int f = 0; f < 6; f++)
        {
            if (Project(FaceCenter(f)) is not { } s) continue;
            double d = (s - p).LengthSquared;
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    /// <summary>Tutamacın ekseni ekrana izdüşürülür; fare bu yöndeki hareketi kadar yüz (veya taşımada bütün kutu) kayar.</summary>
    void DragFace(Point p, bool move)
    {
        int ax = _dragFace / 2;
        var c = new Point3D((_dragMin[0] + _dragMax[0]) / 2, (_dragMin[1] + _dragMax[1]) / 2, (_dragMin[2] + _dragMax[2]) / 2);
        var axis = new Vector3D(ax == 0 ? 1 : 0, ax == 1 ? 1 : 0, ax == 2 ? 1 : 0);
        double L = Math.Max(_fitRadius * 0.2, 0.5);
        if (Project(c) is not { } s0 || Project(c + axis * L) is not { } s1) return;
        var sv = s1 - s0;
        double len2 = sv.LengthSquared;
        if (len2 < 4) return; // eksen ekrana dik → bu yönde sürükleme anlamsız
        double delta = Vector.Multiply(p - _down, sv) / len2 * L;
        const double minSize = 0.3;
        if (move)
        {
            _bMin[ax] = _dragMin[ax] + delta;
            _bMax[ax] = _dragMax[ax] + delta;
        }
        else if (_dragFace % 2 == 1) _bMax[ax] = Math.Max(_dragMax[ax] + delta, _bMin[ax] + minSize);
        else _bMin[ax] = Math.Min(_dragMin[ax] + delta, _bMax[ax] - minSize);
        Interact();
    }

    void EndDragFace()
    {
        _dragFace = -1;
        Win32.ReleaseCapture();
        UpdateCenter();
        Invalidate();
        BoxEdited?.Invoke();
        const double eps = 1e-6;
        bool grows = false;
        for (int k = 0; k < 3; k++) grows |= _bMin[k] < _cMin[k] - eps || _bMax[k] > _cMax[k] + eps;
        if (grows) GrowRequested?.Invoke(BoxMin, BoxMax);
    }

    // ---- mesajlar ------------------------------------------------------------------------------------------------

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        Point Pt() => new(Win32.LoWord(lParam), Win32.HiWord(lParam));
        switch (msg)
        {
            case Win32.WM_ERASEBKGND:
                handled = true;
                return (IntPtr)1;
            case Win32.WM_PAINT:
                var ps = new Win32.PAINTSTRUCT();
                Win32.BeginPaint(hwnd, out ps);
                Render();
                Win32.EndPaint(hwnd, ref ps);
                handled = true;
                return IntPtr.Zero;
            case Win32.WM_SIZE:
                _w = Math.Max(1, Win32.LoWord(lParam));
                _h = Math.Max(1, Win32.HiWord(lParam));
                if (_fitPending) { _fitPending = false; FitAll(IsoLook); }
                ResetScale();
                Invalidate();
                break;
            case Win32.WM_GETDLGCODE:
                handled = true;
                return (IntPtr)0x0004; // DLGC_WANTALLKEYS
            case Win32.WM_SETCURSOR:
                if (_hover >= 0 || _dragFace >= 0)
                {
                    Win32.SetCursor(Win32.LoadCursor(IntPtr.Zero, (IntPtr)32646)); // dört yönlü ok
                    handled = true;
                    return (IntPtr)1;
                }
                if ((_cubeHover != null || HomeHit(_lastMouse) || _modelHover != null) && Win32.LoWord(lParam) == 1)   // küp, model listesi: el imleci
                {
                    Win32.SetCursor(Win32.LoadCursor(IntPtr.Zero, (IntPtr)32649));
                    handled = true;
                    return (IntPtr)1;
                }
                if (_measureMode && (Win32.LoWord(lParam) == 1))   // HTCLIENT: ölçüde artı imleç
                {
                    Win32.SetCursor(Win32.LoadCursor(IntPtr.Zero, (IntPtr)32515));
                    handled = true;
                    return (IntPtr)1;
                }
                break;
            case Win32.WM_TIMER:
                if (wParam == TagTimer)
                {
                    Win32.KillTimer(hwnd, TagTimer);
                    if (_tagMode && !_nav && !_measureMode) TagHover(_hoverPt, force: true);
                    handled = true;
                    return IntPtr.Zero;
                }
                if (wParam == SnapTimer)
                {
                    Win32.KillTimer(hwnd, SnapTimer);
                    if (_measureMode && !_nav) MeasureHover(_lastHover, force: true);
                    handled = true;
                    return IntPtr.Zero;
                }
                if (wParam == IdleTimer)
                {
                    Win32.KillTimer(hwnd, IdleTimer);
                    _interactive = false;
                    Invalidate();
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case Win32.WM_LBUTTONDOWN:
            {
                Win32.SetFocus(hwnd);
                _down = Pt();
                if (ModelHit(_down) is { } mn) { ModelToggled?.Invoke(mn); handled = true; return IntPtr.Zero; }
                if (CubeHit(_down) is { } cdir) { CubeClick(cdir); handled = true; return IntPtr.Zero; }
                if (HomeHit(_down)) { FitAll(IsoLook); Interact(); handled = true; return IntPtr.Zero; }
                int f = HandleAt(_down);
                if (f >= 0)
                {
                    _dragFace = f;
                    Array.Copy(_bMin, _dragMin, 3);
                    Array.Copy(_bMax, _dragMax, 3);
                    Win32.SetCapture(hwnd);
                }
                else _leftDown = true;
                handled = true;
                return IntPtr.Zero;
            }
            case Win32.WM_LBUTTONUP:
                if (_dragFace >= 0) EndDragFace();
                else if (_leftDown)
                {
                    _leftDown = false;
                    var p = Pt();
                    if ((p - _down).Length < 5) { if (_measureMode) MeasureClick(p); else Select(Pick(p).id); }
                }
                handled = true;
                return IntPtr.Zero;
            case Win32.WM_LBUTTONDBLCLK:
            {
                if (CubeHit(Pt()) is { } cdir2) { CubeClick(cdir2); handled = true; return IntPtr.Zero; }
                if (ModelHit(Pt()) != null) { handled = true; return IntPtr.Zero; }
                if (HomeHit(Pt())) { handled = true; return IntPtr.Zero; }
                if (HandleAt(Pt()) >= 0) { handled = true; return IntPtr.Zero; }
                if (_measureMode) { _down = Pt(); _leftDown = true; handled = true; return IntPtr.Zero; }   // ölçüde çift tık = iki ayrı tık
                var (id, _) = Pick(Pt());
                Select(id);
                FitSelectionOrAll();
                handled = true;
                return IntPtr.Zero;
            }
            case Win32.WM_MBUTTONDOWN:
            case Win32.WM_MBUTTONDBLCLK:
            case Win32.WM_RBUTTONDOWN:
                Win32.SetFocus(hwnd);
                BeginNav(Pt());
                handled = true;
                return IntPtr.Zero;
            case Win32.WM_MBUTTONUP:
            case Win32.WM_RBUTTONUP:
                if (_nav) { _nav = false; Win32.ReleaseCapture(); }
                handled = true;
                return IntPtr.Zero;
            case Win32.WM_CAPTURECHANGED:
                _nav = false;
                if (_dragFace >= 0) EndDragFace();
                break;
            case Win32.WM_MOUSEMOVE:
            {
                var p = Pt();
                if (_dragFace >= 0)
                {
                    DragFace(p, MoveMode || (wParam.ToInt64() & Win32.MK_CONTROL) != 0);
                }
                else if (_nav)
                {
                    double dx = p.X - _last.X, dy = p.Y - _last.Y;
                    _last = p;
                    if (dx != 0 || dy != 0)
                    {
                        // Revit gibi: Shift basılıyken döndür, değilse kaydır (sürükleme sırasında değiştirilebilir).
                        if ((wParam.ToInt64() & Win32.MK_SHIFT) != 0) Orbit(dx, dy);
                        else Pan(dx, dy);
                        Interact();
                    }
                }
                else
                {
                    _lastMouse = p;
                    var mh = ModelHit(p);
                    if (mh != _modelHover) { _modelHover = mh; Invalidate(); }
                    var ch = CubeHit(p);
                    if (ch != _cubeHover) { _cubeHover = ch; Invalidate(); }
                    int h = _boxMode && ch == null ? HandleAt(p) : -1;
                    if (h != _hover) { _hover = h; Invalidate(); }
                    if (ch == null && h < 0 && mh == null)
                    {
                        if (_measureMode) MeasureHover(p);
                        else if (_tagMode) TagHover(p);
                    }
                    else if (_hoverId != 0) { _hoverId = 0; Invalidate(); }
                }
                handled = true;
                return IntPtr.Zero;
            }
            case Win32.WM_MOUSEWHEEL:
            {
                var sp = new Win32.POINT { X = Win32.LoWord(lParam), Y = Win32.HiWord(lParam) };
                Win32.ScreenToClient(hwnd, ref sp);
                Zoom(new Point(sp.X, sp.Y), Win32.HiWord(wParam));
                Interact();
                handled = true;
                return IntPtr.Zero;
            }
            case Win32.WM_KEYDOWN:
            {
                int vk = (int)wParam.ToInt64();
                int n = Palette.All.Length;
                if (MeasureKey(vk)) { }
                else if (vk == 'D') ToolKey?.Invoke('D');   // ölç
                else if (vk == 'T') ToolKey?.Invoke('T');   // anlık etiket
                else if (vk >= 0x31 && vk < 0x31 + n) PaletteKey?.Invoke(vk - 0x31);
                else if (vk >= 0x61 && vk < 0x61 + n) PaletteKey?.Invoke(vk - 0x61);
                else if (vk == 'F') FitSelectionOrAll();
                else if (vk == Win32.VK_HOME) FitAll(IsoLook);
                else if (vk == Win32.VK_ESCAPE) { if (EscapeOverride?.Invoke() != true) Select(0); }
                else if (vk is 'C' or 'B' or 'M' or 'P' or 'R') ToolKey?.Invoke((char)vk);
                else if (vk == 0x74) ToolKey?.Invoke('R'); // F5: Revit'ten yenile
                else if (vk == 0x70) ToolKey?.Invoke('?'); // F1: yardım
                else break;
                handled = true;
                return IntPtr.Zero;
            }
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    // ---- yardımcılar ---------------------------------------------------------------------------------------------

    float[] ToneColors()
    {
        // Ton grubu çarpanları: açık paletlerde koyulaştırır, koyu paletlerde açar.
        double[] f = { 1.00, 0.90, 0.82, 0.66 };
        var res = new float[18];
        for (int t = 0; t < 4; t++) Put(res, t, ToneColor(_pal.Surface, f[t]));
        var glass = Lerp(_pal.Surface, _pal.Dark ? System.Windows.Media.Colors.White : System.Windows.Media.Color.FromRgb(0x9C, 0xA6, 0xAD), 0.35);
        Put(res, 4, glass);
        Put(res, 5, _pal.Cut);
        return res;
    }

    System.Windows.Media.Color ToneColor(System.Windows.Media.Color c, double f)
    {
        if (!_pal.Tones || f >= 1) return c;
        return _pal.Dark ? Lerp(c, System.Windows.Media.Colors.White, (1 - f) * 0.9)
            : System.Windows.Media.Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    }

    static System.Windows.Media.Color Lerp(System.Windows.Media.Color a, System.Windows.Media.Color b, double t) =>
        System.Windows.Media.Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static (float r, float g, float b) Mix(System.Windows.Media.Color a, System.Windows.Media.Color b, double t) =>
        ((float)((a.R + (b.R - a.R) * t) / 255), (float)((a.G + (b.G - a.G) * t) / 255), (float)((a.B + (b.B - a.B) * t) / 255));

    static double Luma(System.Windows.Media.Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    static void Put(float[] a, int i, System.Windows.Media.Color c) { a[3 * i] = c.R / 255f; a[3 * i + 1] = c.G / 255f; a[3 * i + 2] = c.B / 255f; }
    static void U3(int loc, System.Windows.Media.Color c) => GL.Uniform3f(loc, c.R / 255f, c.G / 255f, c.B / 255f);
    static void UV(int loc, Vector3D v) => GL.Uniform3f(loc, (float)v.X, (float)v.Y, (float)v.Z);
    static Vector3D Norm(Vector3D v) { v.Normalize(); return v; }
}
