using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Smart3DView;

/// <summary>Ton paleti: arka plan, temel yüzey grisi, kenar ve kesit (poşe) rengi + ışık şiddetleri.
/// Colored: tesisat sistemleri renkli (soğutma mavi, yangın kırmızı, üfleme magenta, emiş/dönüş yeşil).</summary>
sealed record Palette(
    string Tr, string En,
    Color BgTop, Color BgBottom, Color Surface, Color Edge, Color Cut,
    double Ambient, double Key, double Fill, double Sky, double EdgePx,
    bool Dark, bool Tones, bool Colored = false, bool Detailed = false, bool Sketch = false)
{
    public string Name => L.T(Tr, En);

    static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static readonly Palette[] All =
    {
        new("Beyaz", "White",        C(0xF7F7F5), C(0xD9D9D7), C(0xF4F4F4), C(0x2E2E2E), C(0x3A3A3A), 0.50, 0.55, 0.18, 0.12, 1.2, false, true),
        new("Açık gri", "Light gray", C(0xE4E4E4), C(0xB6B6B6), C(0xD4D4D4), C(0x262626), C(0x303030), 0.45, 0.60, 0.20, 0.14, 1.2, false, true),
        new("Gri", "Gray",            C(0x969696), C(0x5E5E5E), C(0xC2C2C2), C(0x1A1A1A), C(0x222222), 0.40, 0.65, 0.20, 0.15, 1.2, false, true),
        new("Koyu", "Dark",           C(0x404040), C(0x1C1C1C), C(0x9E9E9E), C(0x101010), C(0x262626), 0.38, 0.65, 0.22, 0.12, 1.2, false, true),
        // Kağıt: kalem çizimi gibi (GlView.Sketch) — buz beyazı zemin; üst/yan yüzler ve kesitler beyaza çok yakın (yalnız eğri yüzler hafif tonlanır ki borular kaybolmasın), kenarlar kalın siyah kalem çizgisi (kullanıcı isteği 2026-10-08).
        // Eskiden kesit poşesi siyahtı, kenarlar kalın → "kapkara" görünüyordu (kullanıcı raporu 2026-10-08).
        new("Kağıt", "Paper",         C(0xF5F9FC), C(0xE2EAF1), C(0xFFFFFF), C(0x141414), C(0xFFFFFF), 1.10, 0.14, 0.00, 0.00, 2.2, false, false, Sketch: true),
        new("Renkli", "Colored",      C(0xF4F4F2), C(0xD2D2D0), C(0xE8E8E8), C(0x2A2A2A), C(0x3A3A3A), 0.48, 0.58, 0.20, 0.12, 1.2, false, true, Colored: true),
        new("Detaylı", "Detailed",    C(0xF2F4F6), C(0xCDD3D9), C(0xE0E0E0), C(0x2A2A2A), C(0x3A3A3A), 0.46, 0.60, 0.20, 0.12, 1.1, false, true, Colored: true, Detailed: true),
    };
}

/// <summary>Ayrı pencere: üstte OpenGL görünümü, altta ince çubuk (durum yazısı + araçlar + ton seçici).</summary>
sealed partial class ViewerWindow : Window
{
    static readonly System.Collections.Generic.List<ViewerWindow> Open = new();

    /// <summary>Her çağrı YENİ bir pencere açar (kullanıcı isteği, 2026-10-07: "istediğim kadar farklı 3B açabileyim").
    /// Yeni pencere bir öncekinin biraz sağ-altına kaydırılır ki üst üste binmesin.</summary>
    public static void Present(SceneData scene, IntPtr owner, string docTitle, IViewerHost? host)
    {
        var w = new ViewerWindow();
        if (owner != IntPtr.Zero) new WindowInteropHelper(w).Owner = owner;
        var prev = Open.Count > 0 ? Open[Open.Count - 1] : null;
        if (prev != null && prev.WindowState == WindowState.Normal)
        {
            var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            double l = prev.Left + 32, t = prev.Top + 32;
            if (l + 200 > vs.Right || t + 150 > vs.Bottom) { l = vs.Left + 40; t = vs.Top + 40; }
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.WindowState = WindowState.Normal;
            w.Width = prev.Width; w.Height = prev.Height;
            w.Left = l; w.Top = t;
        }
        Open.Add(w);
        w.Closed += (_, _) => Open.Remove(w);
        w._host = host;
        w.Load(scene, docTitle, keepView: false);
        w.Show();
        w.Activate();
    }

    readonly GlView _view = new();
    readonly Border _bar = new() { Height = 36, Padding = new Thickness(12, 0, 8, 0) };
    readonly TextBlock _status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _info = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
    readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _btnLic, _btnReload, _btnBox, _btnMove, _btnReset, _btnMeasure, _btnLockX, _btnLockY, _btnLockZ, _btnFree, _btnClear, _btnClash, _btnTol, _btnShot, _btnHelp;
    // Çakışma toleransı (mm): yalnız birbirinin içine bundan derin giren elemanlar çakışır; 0 = dokunma da sayılır.
    static readonly int[] TolSteps = { 5, 10, 25, 50, 0 };
    int _clashTolMm = 5;
    readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    IViewerHost? _host;
    SceneData? _scene;
    string _docTitle = "";
    Palette _pal = Palette.All[0];
    // Varsayılan ton "Detaylı" (kullanıcı geri bildirimi, 2026-10-07: "her şey çok gri, ekipmanlar ayırt edilsin").
    static readonly int DefaultPalette = Array.FindIndex(Palette.All, p => p.Detailed);
    int _palIndex = DefaultPalette;
    string? _error, _flash;
    string _timing = "";
    ClashResult? _clash;
    bool _clashOn, _busy, _stale;   // _stale: Revit modeli okunduktan sonra değişti → "Yenile" uyarır

    static string Hint => L.T(
        "Tık: seç  ·  Shift + orta tuş: döndür  ·  Orta tuş: kaydır  ·  Tekerlek: yakınlaştır  ·  Çift tık / F: sığdır  ·  Esc: seçimi bırak",
        "Click: select  ·  Shift + middle: orbit  ·  Middle: pan  ·  Wheel: zoom  ·  Double-click / F: fit  ·  Esc: clear selection");

    ViewerWindow()
    {
        Width = 1100; Height = 760;
        MinWidth = 560; MinHeight = 300;
        FontFamily = new FontFamily("Segoe UI");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        LoadSettings();

        _btnReload = MakeTool("⟳  " + L.T("Yenile", "Reload"),
            L.T("Revit'te yapılan değişiklikleri yansıt — kamera, kutu, ton, çakışma ve seçim korunur  (R / F5)",
                "Reflect the changes made in Revit — camera, box, tone, clash and selection are kept  (R / F5)"), Reload);
        _btnBox = MakeTool("▣  " + L.T("Kutu", "Box"),
            L.T("Kesit kutusunu düzenle: mavi tutamaçları sürükleyerek yüzleri içeri/dışarı al  (B)",
                "Edit the section box: drag the blue handles to move faces in/out  (B)"), ToggleBox);
        _btnMove = MakeTool("✥  " + L.T("Taşı", "Move"),
            L.T("Açıkken tutamaç sürüklemek bütün kutuyu o eksende taşır (Ctrl + sürükleme de taşır)  (M)",
                "When on, dragging a handle moves the whole box along that axis (Ctrl + drag also moves)  (M)"), ToggleMove);
        _btnReset = MakeTool("↺", L.T("Kutuyu başa döndür", "Reset the box"), () => { _view.ResetBox(); _view.FocusGl(); });
        _btnMeasure = MakeTool("📏  " + L.T("Ölç", "Measure"),
            L.T("İki nokta arası 3B mesafe: köşe, kenar ortası, boru ekseni, kenar ve yüzeye yapışır; X/Y/Z ile eksen kilidi  (D)",
                "3D distance between two points: snaps to corners, midpoints, pipe axes, edges and faces; X/Y/Z axis lock  (D)"), ToggleMeasure);
        _btnLockX = MakeTool("X", L.T("X eksenine kilitle  (X)", "Lock to the X axis  (X)"), () => { _view.LockAxis = 0; _view.FocusGl(); });
        _btnLockY = MakeTool("Y", L.T("Y eksenine kilitle  (Y)", "Lock to the Y axis  (Y)"), () => { _view.LockAxis = 1; _view.FocusGl(); });
        _btnLockZ = MakeTool("Z", L.T("Z eksenine (düşey) kilitle  (Z)", "Lock to the Z (vertical) axis  (Z)"), () => { _view.LockAxis = 2; _view.FocusGl(); });
        _btnFree = MakeTool(L.T("Serbest", "Free"), L.T("Eksen kilidini kaldır — serbest 3B ölçü", "Remove the axis lock — free 3D measure"), () => { _view.FreeAxis(); _view.FocusGl(); });
        _btnClear = MakeTool("🗑  " + L.T("Sil", "Delete"), L.T("Bütün ölçüleri sil  (Delete)", "Delete all measurements  (Delete)"), () => { _view.ClearMeasures(); _view.FocusGl(); });
        _btnClash = MakeTool("⚠  " + L.T("Çakışma", "Clash"),
            L.T("Farklı tesisatlar arasında çakışan elemanları kırmızı/mavi göster — toleranstan az giren, değen elemanlar sayılmaz; boru–dirsek gibi aynı hattın parçaları da sayılmaz  (C)",
                "Show clashing elements between different services in red/blue — overlaps below the tolerance and touching elements are ignored, as are parts of the same run  (C)"), () => _ = ToggleClash());
        _btnTol = MakeTool("", L.T("Çakışma toleransı: elemanlar birbirinin içine bundan fazla girerse çakışma sayılır — değen, yaslanan, üstüne oturan elemanlar sayılmaz. Tıkla: 5 → 10 → 25 → 50 mm → dokunma da sayılsın",
                                  "Clash tolerance: elements count as clashing only if they overlap by more than this — touching, resting or mounted elements are ignored. Click: 5 → 10 → 25 → 50 mm → touching counts too"), CycleTol);
        _btnShot = MakeTool("📷  " + L.T("Görüntü al", "Take picture"),
            L.T("Görüntüyü yüksek çözünürlükte Revit'e kaydet (Proje Tarayıcısı → Renderings)  (P)",
                "Save the view in high resolution to Revit (Project Browser → Renderings)  (P)"), TakePicture);
        _btnLic = MakeTool("", L.T("Lisans durumu / satın al", "License status / buy"), () => ShowLicense(null));
        _btnHelp = MakeTool("?", L.T("Hızlı başlangıç ve yardım  (F1)", "Quick start and help  (F1)"), Help.Open);
        foreach (var b in new[] { _btnLic, _btnReload, _btnBox, _btnMeasure, _btnClash, _btnShot, _btnHelp }) _tools.Children.Add(b);
        // Alt düğmeler çubuğa eklenmez: ana düğmenin ÜSTÜNDE ayrı bir şerit olarak açılır (kullanıcı isteği 2026-10-08).
        _flyouts.Add(MakeFlyout(_btnBox, () => _view.BoxMode, _btnMove, _btnReset));
        _flyouts.Add(MakeFlyout(_btnMeasure, () => _view.MeasureMode, _btnLockX, _btnLockY, _btnLockZ, _btnFree, _btnClear));
        _flyouts.Add(MakeFlyout(_btnClash, () => _clashOn, _btnTol));

        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_tools, 1);
        Grid.SetColumn(_info, 2);
        Grid.SetColumn(_swatches, 3);
        barGrid.Children.Add(_status);
        barGrid.Children.Add(_tools);
        barGrid.Children.Add(_info);
        barGrid.Children.Add(_swatches);
        _bar.Child = barGrid;

        var dock = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Bottom);
        dock.Children.Add(_bar);
        dock.Children.Add(_view);
        BuildSide();
        Content = dock;

        for (int i = 0; i < Palette.All.Length; i++) _swatches.Children.Add(MakeSwatch(i));

        _view.SelectionChanged += UpdateStatus;
        _view.SelectionChanged += RefreshSide;
        _view.MeasureChanged += () => { UpdateStatus(); RefreshTools(); };
        _view.PaletteKey += ApplyPalette;
        _view.ToolKey += k =>
        {
            if (k == 'C') _ = ToggleClash();
            else if (k == 'B') ToggleBox();
            else if (k == 'M' && _view.BoxMode) ToggleMove();
            else if (k == 'P') TakePicture();
            else if (k == 'R') Reload();
            else if (k == 'D') ToggleMeasure();
            else if (k == 'T') ToggleTag();
            else if (k == '?') Help.Open();
        };
        _view.Failed += msg => { _error = msg; UpdateStatus(); };
        _view.BoxEdited += () => { if (_clashOn) _ = RunClash(); };
        _view.GrowRequested += Grow;
        _view.EscapeOverride = CancelRead;
        _info.ToolTip = "GPU";
        _info.ToolTipOpening += (_, _) => _info.ToolTip = "GPU: " + (_view.Renderer.Length > 0 ? _view.Renderer : "?") + Environment.NewLine + _timing;
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _flash = null; UpdateStatus(); };
        Activated += (_, _) => { _view.FocusGl(); RefreshFlyouts(); };
        Deactivated += (_, _) => { foreach (var f in _flyouts) f.Popup.IsOpen = false; };   // popup en üstte kalır → başka uygulamaya geçince kapat
        LocationChanged += (_, _) => RepositionFlyouts();
        SizeChanged += (_, _) => RepositionFlyouts();
        StateChanged += (_, _) => RefreshFlyouts();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F1) { Help.Open(); e.Handled = true; } };
        Action licChanged = () => Dispatcher.BeginInvoke(RefreshTools);
        Action<object> modelChanged = doc => Dispatcher.BeginInvoke(() =>
        {
            if (_stale || _host == null || _scene?.Context == null || !_host.IsFromDoc(_scene.Context, doc)) return;
            _stale = true;
            RefreshTools();
        });
        License.Changed += licChanged;
        ModelWatch.Changed += modelChanged;
        Closed += (_, _) => { _closed = true; if (_read != null) _read.Cancel = true; foreach (var f in _flyouts) f.Popup.IsOpen = false; License.Changed -= licChanged; ModelWatch.Changed -= modelChanged; };   // statik olaylar kapanan pencereyi tutmasın
        Loaded += async (_, _) => { await License.Revalidate(); RefreshTools(); };
        Closing += (_, _) => SaveSettings();

        ApplyPalette(_palIndex, user: false);
    }

    void Load(SceneData scene, string docTitle, bool keepView)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _scene = scene;
        _docTitle = docTitle;
        Title = $"{Product.Name} — {docTitle} — {scene.Source} — v{AddinVersion.Version}";
        _info.Text = L.T(
            $"{scene.ElementCount:N0} eleman · {scene.TriangleCount:N0} üçgen · {scene.Seconds:0.0} sn",
            $"{scene.ElementCount:N0} elements · {scene.TriangleCount:N0} triangles · {scene.Seconds:0.0} s");
        _timing = scene.Timing;
        _clash = null;
        _view.SetScene(scene, keepView);
        _view.SetHiddenModels(_hidden);
        _loadSeconds = sw.Elapsed.TotalSeconds;
        if (_clashOn) _ = RunClash();
        UpdateStatus();
        RefreshSide();
    }

    bool _closed;
    double _loadSeconds;

    /// <summary>Okuma süresinin dökümü — nerede beklendiği görünsün (kullanıcı isteği 2026-10-07: "nerede zorlanıyor tespit et").</summary>
    string Breakdown(SceneData s)
    {
        double other = Math.Max(0, s.Seconds - s.GeoSeconds - s.TriSeconds - s.InfoSeconds);
        return L.T(
            $"{s.Seconds + _loadSeconds:0.0} sn: Revit geometri {s.GeoSeconds:0.0} · üçgenleme {s.TriSeconds:0.0} · eleman adı/sınıfı {s.InfoSeconds:0.0} · tarama/hazırlık {other:0.0} · pencereye yükleme {_loadSeconds:0.0}",
            $"{s.Seconds + _loadSeconds:0.0} s: Revit geometry {s.GeoSeconds:0.0} · triangulation {s.TriSeconds:0.0} · element name/class {s.InfoSeconds:0.0} · scan/setup {other:0.0} · upload to window {_loadSeconds:0.0}");
    }

    // ---- araçlar -------------------------------------------------------------------------------------------------

    void ToggleBox()
    {
        if (!_view.BoxMode && !RequireFull(L.T("Kutu düzenleme", "Box editing"))) return;
        _view.BoxMode = !_view.BoxMode;
        if (!_view.BoxMode) _view.MoveMode = false;
        RefreshTools();
        Flash(_view.BoxMode
            ? L.T("Mavi tutamaçları sürükle: yüz içeri/dışarı. Taşı açıkken (veya Ctrl ile) bütün kutu kayar. Okunan alanın dışına çıkınca o bölge Revit'ten okunur.",
                  "Drag the blue handles to move a face. With Move on (or Ctrl) the whole box slides. Going beyond the loaded area reads it from Revit.")
            : null);
        _view.FocusGl();
    }

    void ToggleMeasure()
    {
        if (!_view.MeasureMode && !RequireFull(L.T("Ölçü", "Measure"))) return;
        _view.MeasureMode = !_view.MeasureMode;
        RefreshTools();
        UpdateStatus();
        _view.FocusGl();
    }

    void ToggleMove()
    {
        _view.MoveMode = !_view.MoveMode;
        RefreshTools();
        _view.FocusGl();
    }

    void Grow(double[] lo, double[] hi)
    {
        if (_host == null || _scene?.Context == null) { Flash(L.T("Revit bağlantısı yok; yalnız okunan alan gösterilebilir.", "No Revit link; only the loaded area can be shown.")); return; }
        if (_busy) { _pendingGrow = true; return; }   // okuma sürerken yeniden büyütüldü → bitince son kutu okunur
        _busy = true;
        Flash(L.T("Genişleyen bölge Revit'ten okunuyor…  ·  Esc: iptal", "Reading the enlarged area from Revit…  ·  Esc: cancel"));
        string? selKey = _view.SelectedKey;
        var ctl = NewRead(L.T("Genişleyen bölge okunuyor", "Reading the enlarged area"), confirm: false);   // uyarı kapalı: her büyütmede sormak sıkıcı, Esc ve yüzde yeterli (kullanıcı isteği 2026-10-08)
        // Yüklü elemanlar korunur; Revit'ten yalnız yeni giren elemanların geometrisi istenir (dilim dilim).
        _host.Recollect(_scene.Context, lo, hi, (scene, err) => Dispatcher.BeginInvoke(() =>
        {
            _busy = false;
            _read = null;
            if (scene == null)
            {
                if (ctl.Cancel) { _pendingGrow = false; _view.ClampBoxToLoaded(); }   // iptal: kutu okunmuş alana döner
                Flash(err ?? "?");
                return;
            }
            Load(scene, _docTitle, keepView: true);
            _view.SelectByKey(selKey);
            Flash(L.T("Kutu genişletildi — ", "Box enlarged — ") + Breakdown(scene));
            if (_pendingGrow) { _pendingGrow = false; GrowIfNeeded(_view.BoxMin, _view.BoxMax); }
        }), append: _scene, ctl: ctl);
    }

    bool _pendingGrow;
    ReadControl? _read;   // sürmekte olan Revit okuması (Esc iptal eder)

    /// <summary>Dilimli okuma bağı (kullanıcı isteği 2026-10-08): büyük alanda önce sorar, ilerlemeyi durum
    /// çubuğunda gösterir, Esc ile iptal edilir.</summary>
    ReadControl NewRead(string what, bool confirm)
    {
        var ctl = new ReadControl();
        if (confirm)
            ctl.Confirm = (n, sec) =>
            {
                string t = sec < 60 ? L.T($"{sec:0} sn", $"{sec:0} s") : L.T($"{sec / 60:0.#} dk", $"{sec / 60:0.#} min");
                bool ok = MessageBox.Show(this,
                    L.T($"Bu alanda okunacak {n:N0} yeni eleman var; yaklaşık {t} sürebilir.\n\nOkuma parça parça yapılır: bu sürede Revit ve bu pencere kullanılabilir, Esc ile iptal edilebilir.\n\nDevam edilsin mi?",
                        $"There are {n:N0} new elements to read in this area; it may take about {t}.\n\nThe area is read in small parts: Revit and this window stay usable, and Esc cancels.\n\nContinue?"),
                    Product.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                if (!ok) ctl.Cancel = true;
                return ok;
            };
        ctl.Progress = (done, total) =>
        {
            if (ctl.Cancel) return;
            int pct = total == 0 ? 100 : (int)(100.0 * done / total);
            Flash(L.T($"{what}… %{pct}  ({done:N0} / {total:N0})  ·  Esc: iptal", $"{what}… {pct}%  ({done:N0} / {total:N0})  ·  Esc: cancel"));
        };
        _read = ctl;
        return ctl;
    }

    /// <summary>Esc: okuma sürüyorsa iptal eder (seçim kalır); yoksa false → Esc seçimi kaldırır.</summary>
    bool CancelRead()
    {
        if (_read == null) return false;
        _read.Cancel = true;
        Flash(L.T("İptal ediliyor…", "Cancelling…"));
        return true;
    }

    void GrowIfNeeded(double[] lo, double[] hi)
    {
        double[] cl = _view.LoadedMin, ch = _view.LoadedMax;
        for (int k = 0; k < 3; k++)
            if (lo[k] < cl[k] - 1e-6 || hi[k] > ch[k] + 1e-6) { Grow(lo, hi); return; }
    }

    /// <summary>Aynı alanı Revit'ten yeniden okur (model değişiklikleri). Pencerede yapılanlar korunur: kamera,
    /// düzenlenmiş kutu, ton, kutu/taşı modu, çakışma (yeni geometride yeniden hesaplanır) ve seçili eleman (ID ile).</summary>
    void Reload()
    {
        if (_busy) return;
        if (_host == null || _scene?.Context == null) { Flash(L.T("Revit bağlantısı yok; yenilenemez.", "No Revit link; cannot reload.")); return; }
        // Yalnız görünen kutu (hızlı); dışına çıkılınca kutu büyütme yeni elemanları ekler.
        double[] lo = _view.BoxMin, hi = _view.BoxMax;
        string? selKey = _view.SelectedKey;
        _busy = true;
        Flash(L.T("Revit'ten yeniden okunuyor…  ·  Esc: iptal", "Reloading from Revit…  ·  Esc: cancel"));
        var ctl = NewRead(L.T("Yeniden okunuyor", "Reloading"), confirm: false);
        _host.Recollect(_scene.Context, lo, hi, (scene, err) => Dispatcher.BeginInvoke(() =>
        {
            _busy = false;
            _read = null;
            if (scene == null) { Flash(err ?? "?"); return; }
            _stale = false;
            Load(scene, _docTitle, keepView: true);
            _view.SelectByKey(selKey);
            RefreshTools();
            Flash(L.T("Revit'teki değişiklikler yansıtıldı — ", "Changes from Revit applied — ") + Breakdown(scene));
        }), ctl: ctl);
    }

    void CycleTol()
    {
        int i = Array.IndexOf(TolSteps, _clashTolMm);
        _clashTolMm = TolSteps[(i + 1) % TolSteps.Length];
        RefreshTools();
        if (_clashOn) _ = RunClash();
        _view.FocusGl();
    }

    string TolText => _clashTolMm > 0
        ? "± " + (_scene?.FormatLength(_clashTolMm / 304.8) ?? $"{_clashTolMm} mm")
        : L.T("dokunma", "touch");

    async Task ToggleClash()
    {
        if (!_clashOn && !RequireFull(L.T("Çakışma", "Clash"))) return;
        _clashOn = !_clashOn;
        RefreshTools();
        if (!_clashOn)
        {
            _clash = null;
            _view.SetClash(null);
            UpdateStatus();
            return;
        }
        await RunClash();
    }

    async Task RunClash()
    {
        var scene = _scene;
        if (scene == null) return;
        var lo = _view.BoxMin;
        var hi = _view.BoxMax;
        Flash(L.T("Çakışmalar hesaplanıyor…", "Checking clashes…"));
        var hid = _view.HiddenMask();
        var r = await Task.Run(() => ClashDetector.Run(scene, lo, hi, hid, _clashTolMm / 304.8));
        if (scene != _scene || !_clashOn) return;
        _clash = r;
        _view.SetClash(r);
        Flash(r.PairCount == 0
            ? L.T($"Çakışma yok ({r.Seconds:0.0} sn).", $"No clashes ({r.Seconds:0.0} s).")
            : L.T($"{r.Elements.Count} çakışan eleman, {r.PairCount} çakışma ({r.Seconds:0.0} sn). Kırmızı ve mavi = çakışmanın iki tarafı. Bir elemana tıkla: kendisi kırmızı, çakıştıkları mavi olur.",
                  $"{r.Elements.Count} clashing elements, {r.PairCount} clashes ({r.Seconds:0.0} s). Red and blue = the two sides of a clash. Click an element: it turns red, the elements it clashes with turn blue."));
    }

    void TakePicture()
    {
        if (!RequireFull(L.T("Görüntü al", "Take picture"))) return;
        var px = _view.Capture(3840, out int w, out int h);
        if (px == null) { Flash(L.T("Görüntü alınamadı.", "Could not capture the image.")); return; }
        try
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name, "images");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"Smart3DView_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            using (var fs = File.Create(path))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(fs);
            }
            if (_host != null && _scene?.Context != null)
            {
                Flash(L.T("Görüntü Revit'e kaydediliyor…", "Saving the image to Revit…"));
                _host.SaveImage(_scene.Context, path, msg => Dispatcher.BeginInvoke(() => { Flash(msg); PictureSaved(msg, path); }));
            }
            else PictureSaved(null, path);
        }
        catch (Exception ex) { Flash(ex.Message); }
    }

    /// <summary>Görüntünün nereye kaydedildiğini açıkça gösterir (kullanıcı geri bildirimi, 2026-10-07: "fotoğraf
    /// çekiyor mu bilmiyorum, nereye atıyor göremiyorum"): Revit'teki yeri + PNG dosyası, aç / klasörü aç düğmeleri.</summary>
    void PictureSaved(string? revitMsg, string path)
    {
        var w = new Window
        {
            Title = L.T("Görüntü kaydedildi", "Picture saved"), Owner = this, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MaxWidth = 560 };
        try
        {
            var thumb = new BitmapImage();
            thumb.BeginInit(); thumb.UriSource = new Uri(path); thumb.DecodePixelWidth = 520; thumb.CacheOption = BitmapCacheOption.OnLoad; thumb.EndInit();
            stack.Children.Add(new Image { Source = thumb, Width = 520, Margin = new Thickness(0, 0, 0, 10) });
        }
        catch { }
        void Line(string t, bool bold = false) => stack.Children.Add(new TextBlock
        {
            Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        });
        if (revitMsg != null) Line(revitMsg, bold: true);
        Line(L.T("PNG dosyası: ", "PNG file: ") + path);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        Button B(string t, Action a)
        {
            var b = new Button { Content = t, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0), MinWidth = 80 };
            b.Click += (_, _) => { try { a(); } catch (Exception ex) { Flash(ex.Message); } };
            return b;
        }
        row.Children.Add(B(L.T("Görüntüyü aç", "Open picture"), () =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })));
        row.Children.Add(B(L.T("Klasörü aç", "Open folder"), () =>
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")));
        var ok = B(L.T("Tamam", "OK"), () => w.Close());
        ok.IsDefault = true; ok.IsCancel = true;
        row.Children.Add(ok);
        stack.Children.Add(row);
        w.Content = stack;
        w.Show();
    }

    void Flash(string? msg)
    {
        _flash = msg;
        _flashTimer.Stop();
        if (msg != null && !_busy) _flashTimer.Start();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (_error != null)
        {
            _status.Text = L.T("3B görüntü başlatılamadı: ", "Could not start the 3D view: ") + _error;
            _status.Opacity = 1;
            return;
        }
        var sel = _view.MeasureMode ? _view.MeasureStatus : _view.SelectedLabel;   // ölçüde durum çubuğu ölçüyü gösterir
        if (!_view.MeasureMode && sel != null && _clash != null && _clash.Partners.TryGetValue(_view.Selected, out var partners) && _scene != null)
        {
            var first = _scene.Labels[(int)partners[0] - 1];
            sel += L.T("   ⚠ çakışıyor: ", "   ⚠ clashes with: ") + first + (partners.Count > 1 ? $"  (+{partners.Count - 1})" : "");
        }
        // Kısa bilgi mesajı (görüntü kaydedildi, kutu genişletildi…) süresince seçili elemanın önüne geçer; ölçüde ölçü önde.
        _status.Text = (_view.MeasureMode ? sel ?? _flash : _flash ?? sel) ?? Hint;
        _status.ToolTip = _status.Text;
        _status.FontWeight = sel != null || _flash != null ? FontWeights.SemiBold : FontWeights.Normal;
        _status.Opacity = sel != null || _flash != null ? 0.95 : 0.6;
    }

    Border MakeTool(string text, string tip, Action click)
    {
        var b = new Border
        {
            Child = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(3, 0, 3, 0),
            CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip,
        };
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    // ---- alt araç şeridi: ana düğmenin üstünde, çubuğun dışında ----------------------------------------------------

    sealed class Flyout
    {
        public System.Windows.Controls.Primitives.Popup Popup = null!;
        public Border Frame = null!, Anchor = null!;
        public Func<bool> IsOn = null!;
        public double Lift;   // yan yana iki şerit çakışırsa biri bir sıra yukarı
    }
    readonly System.Collections.Generic.List<Flyout> _flyouts = new();

    /// <summary>3B görünüm bir HwndHost: WPF öğesi üstüne çizilemez → ayrı pencere (Popup). Ana düğmenin ortasına,
    /// hemen üstüne yerleşir; araç açıkken ve pencere etkinken görünür (başka uygulamaya geçince gizlenir).</summary>
    Flyout MakeFlyout(Border anchor, Func<bool> isOn, params Border[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in buttons) row.Children.Add(b);
        var frame = new Border { Child = row, Padding = new Thickness(3, 4, 3, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        var fly = new Flyout { Frame = frame, Anchor = anchor, IsOn = isOn };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = frame, PlacementTarget = anchor, StaysOpen = true, AllowsTransparency = true,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            CustomPopupPlacementCallback = (popupSize, targetSize, _) => new[]
            {
                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                    new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 7 - fly.Lift * popupSize.Height / Math.Max(1, frame.ActualHeight)),
                    System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
            },
        };
        fly.Popup = popup;
        return fly;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    void RefreshFlyouts()
    {
        bool visible = IsActive && WindowState != WindowState.Minimized && IsVisible && GetForegroundWindow() == new WindowInteropHelper(this).Handle;
        bool dark = Luma(_pal.BgBottom) < 0.45;
        foreach (var f in _flyouts)
        {
            f.Frame.Background = new SolidColorBrush(_pal.BgBottom);
            f.Frame.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x40, 0, 0, 0));
            bool open = visible && f.IsOn();
            if (f.Popup.IsOpen != open) f.Popup.IsOpen = open;
        }
        // Açık şeritler soldan sağa; öncekine değen bir sıra yukarı çıkar.
        var rows = new System.Collections.Generic.List<double>();
        foreach (var f in _flyouts.Where(f => f.Popup.IsOpen).OrderBy(f => f.Anchor.TranslatePoint(new Point(), this).X))
        {
            f.Frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = f.Frame.DesiredSize.Width, h = f.Frame.DesiredSize.Height;
            double left = f.Anchor.TranslatePoint(new Point(f.Anchor.ActualWidth / 2, 0), this).X - w / 2;
            int row = 0;
            while (row < rows.Count && left < rows[row] + 6) row++;
            if (row == rows.Count) rows.Add(0);
            rows[row] = left + w;
            f.Lift = row * (h + 6);
        }
        RepositionFlyouts();
    }

    /// <summary>Popup pencereyle birlikte kaymaz; ofseti dürterek yeniden yerleştirilir.</summary>
    void RepositionFlyouts()
    {
        foreach (var f in _flyouts)
            if (f.Popup.IsOpen) { f.Popup.HorizontalOffset += 0.01; f.Popup.HorizontalOffset -= 0.01; }
    }

    void RefreshTools()
    {
        RefreshSide();
        bool dark = Luma(_pal.BgBottom) < 0.45;
        var ink = dark ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20);
        var st = License.State;
        ((TextBlock)_btnLic.Child).Text = st == LicenseState.Trial
            ? L.T($"⏳ Deneme: {License.TrialDaysLeft} gün", $"⏳ Trial: {License.TrialDaysLeft} days")
            : L.T("🔒 Ücretsiz sürüm · Lisans al", "🔒 Free version · Get license");
        _btnLic.Visibility = st == LicenseState.Licensed ? Visibility.Collapsed : Visibility.Visible;
        _btnReload.Visibility = _host != null ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)_btnTol.Child).Text = TolText;
        ((TextBlock)_btnReload.Child).Text = "⟳  " + L.T("Yenile", "Reload") + (_stale ? " •" : "");
        foreach (var (b, on) in new[] { (_btnLic, false), (_btnReload, false), (_btnBox, _view.BoxMode), (_btnMove, _view.MoveMode), (_btnReset, false), (_btnMeasure, _view.MeasureMode), (_btnLockX, _view.LockAxis == 0), (_btnLockY, _view.LockAxis == 1), (_btnLockZ, _view.LockAxis == 2), (_btnFree, _view.LockAxis < 0), (_btnClear, false), (_btnClash, _clashOn), (_btnTol, false), (_btnShot, false), (_btnHelp, false) })
        {
            ((TextBlock)b.Child).Foreground = new SolidColorBrush(on ? Colors.White : ink);
            b.Background = on ? new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xD8)) : new SolidColorBrush(Color.FromArgb(dark ? (byte)0x22 : (byte)0x10, ink.R, ink.G, ink.B));
            b.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x2B, 0x6C, 0xD8) : Color.FromArgb(0x40, ink.R, ink.G, ink.B));
        }
        if (_stale)   // model Revit'te değişti: turuncu uyarı
        {
            var orange = Color.FromRgb(0xE0, 0x7A, 0x10);
            ((TextBlock)_btnReload.Child).Foreground = Brushes.White;
            _btnReload.Background = new SolidColorBrush(orange);
            _btnReload.BorderBrush = new SolidColorBrush(orange);
            _btnReload.ToolTip = L.T("Model Revit'te değişti — yansıtmak için tıkla  (R / F5)", "The model changed in Revit — click to apply  (R / F5)");
        }
        else _btnReload.ToolTip = L.T("Revit'te yapılan değişiklikleri yansıt — kamera, kutu, ton, çakışma ve seçim korunur  (R / F5)",
                                       "Reflect the changes made in Revit — camera, box, tone, clash and selection are kept  (R / F5)");
        RefreshFlyouts();
    }

    // ---- lisans ---------------------------------------------------------------------------------------------------

    bool RequireFull(string feature)
    {
        if (License.FullFeatures) return true;
        ShowLicense(L.T($"\"{feature}\" tam lisans gerektirir (deneme süresi bitti).", $"\"{feature}\" needs a full license (the trial has ended)."));
        return License.FullFeatures;
    }

    void ShowLicense(string? reason)
    {
        new LicenseWindow(this, reason).ShowDialog();
        RefreshTools();
        _view.FocusGl();
    }

    // ---- palet ---------------------------------------------------------------------------------------------------

    void ApplyPalette(int i) => ApplyPalette(i, user: true);

    void ApplyPalette(int i, bool user)
    {
        i = Math.Clamp(i, 0, Palette.All.Length - 1);
        if (Palette.All[i].Colored && !License.FullFeatures)
        {
            if (user) ShowLicense(L.T($"{Palette.All[i].Tr} ton", $"{Palette.All[i].En} mode"));
            if (user) return;
            i = 0;
        }
        _palIndex = i;
        _pal = Palette.All[_palIndex];
        _view.SetPalette(_pal);
        _bar.Background = new SolidColorBrush(_pal.BgBottom);
        bool dark = Luma(_pal.BgBottom) < 0.45;
        var ink = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20));
        _status.Foreground = ink;
        _info.Foreground = ink;
        _info.Opacity = 0.55;
        _bar.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0));
        _bar.BorderThickness = new Thickness(0, 1, 0, 0);
        for (int k = 0; k < _swatches.Children.Count; k++)
        {
            var ring = (Border)_swatches.Children[k];
            ring.BorderBrush = k == _palIndex ? new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x22, 0x22, 0x22)) : Brushes.Transparent;
        }
        RefreshTools();
    }

    Border MakeSwatch(int i)
    {
        var p = Palette.All[i];
        Brush fill = p.Detailed
            ? new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xB5, 0xB3, 0xAE), 0), new GradientStop(Color.FromRgb(0xB5, 0xB3, 0xAE), 0.2),
                    new GradientStop(Color.FromRgb(0xF6, 0xF4, 0xEE), 0.2), new GradientStop(Color.FromRgb(0xF6, 0xF4, 0xEE), 0.4),
                    new GradientStop(Color.FromRgb(0x9A, 0xA3, 0xAD), 0.4), new GradientStop(Color.FromRgb(0x9A, 0xA3, 0xAD), 0.6),
                    new GradientStop(Color.FromRgb(0xE3, 0xA3, 0x3A), 0.6), new GradientStop(Color.FromRgb(0xE3, 0xA3, 0x3A), 0.8),
                    new GradientStop(Color.FromRgb(0x8E, 0x7C, 0xC3), 0.8), new GradientStop(Color.FromRgb(0x8E, 0x7C, 0xC3), 1),
                },
            }
            : p.Colored
            ? new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x29, 0x6B, 0xE0), 0), new GradientStop(Color.FromRgb(0x29, 0x6B, 0xE0), 0.25),
                    new GradientStop(Color.FromRgb(0xE0, 0x21, 0x21), 0.25), new GradientStop(Color.FromRgb(0xE0, 0x21, 0x21), 0.5),
                    new GradientStop(Color.FromRgb(0xDB, 0x33, 0xC7), 0.5), new GradientStop(Color.FromRgb(0xDB, 0x33, 0xC7), 0.75),
                    new GradientStop(Color.FromRgb(0x2E, 0xAD, 0x4C), 0.75), new GradientStop(Color.FromRgb(0x2E, 0xAD, 0x4C), 1),
                },
            }
            : new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(p.BgTop, 0), new GradientStop(p.BgTop, 0.5),
                    new GradientStop(p.Surface, 0.5), new GradientStop(p.Surface, 1),
                },
            };
        var dot = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = fill,
            BorderBrush = new SolidColorBrush(p.Edge) { Opacity = 0.7 }, BorderThickness = new Thickness(1),
        };
        string tip = p.Detailed
            ? L.T("Detaylı: her eleman türü kendi renginde — duvar gri, kapı beyaz, tava metalik, cihazlar ve tesisat sistemleri renkli",
                  "Detailed: every element type in its own color — walls gray, doors white, trays metallic, equipment and MEP systems colored")
            : p.Colored
            ? L.T("Renkli: soğutma mavi · yangın kırmızı · üfleme magenta · emiş/dönüş yeşil", "Colored: cooling blue · fire red · supply magenta · return/exhaust green")
            : p.Name;
        var ring = new Border
        {
            Child = dot, Padding = new Thickness(2), Margin = new Thickness(2, 0, 2, 0),
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand,
            ToolTip = $"{tip}  ({i + 1})", Background = Brushes.Transparent,
        };
        ring.MouseLeftButtonUp += (_, e) => { ApplyPalette(i); _view.FocusGl(); e.Handled = true; };
        return ring;
    }

    static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    // ---- ayarlar -------------------------------------------------------------------------------------------------

    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name, "settings.txt");

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var kv = File.ReadAllLines(SettingsPath).Select(l => l.Split('=', 2)).Where(a => a.Length == 2)
                .ToDictionary(a => a[0].Trim(), a => a[1].Trim());
            double D(string k) => kv.TryGetValue(k, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
            // pv yoksa ayar "Detaylı" ton gelmeden önce yazılmış: bir kez yeni varsayılana geçilir, sonra kullanıcının seçimi korunur.
            // pv=2: "Siyah" (4. sıra) kaldırılmadan önceki sıra (2026-10-08) → Siyah seçiliyse Koyu, sonrakiler bir geri.
            if (kv.TryGetValue("pv", out var pv) && kv.TryGetValue("palette", out var ps) && int.TryParse(ps, out var pi))
                _palIndex = Math.Clamp(pv == "2" && pi >= 4 ? Math.Max(3, pi - 1) : pi, 0, Palette.All.Length - 1);
            if (kv.TryGetValue("clashtol", out var ct) && int.TryParse(ct, out var cti) && Array.IndexOf(TolSteps, cti) >= 0) _clashTolMm = cti;
            if (kv.TryGetValue("tag", out var tg)) _tagOn = tg == "1";
            double l = D("left"), t = D("top"), w = D("width"), h = D("height");
            if (!double.IsNaN(w) && !double.IsNaN(h) && w > 200 && h > 150) { Width = w; Height = h; }
            var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!double.IsNaN(l) && !double.IsNaN(t) && vs.Contains(new Point(l + 60, t + 20)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l; Top = t;
            }
            if (kv.TryGetValue("max", out var mx) && mx == "1") WindowState = WindowState.Maximized;
        }
        catch { /* ayar okunamazsa varsayılanlar */ }
    }

    void SaveSettings()
    {
        try
        {
            var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var ci = CultureInfo.InvariantCulture;
            File.WriteAllLines(SettingsPath, new[]
            {
                $"palette={_palIndex}", "pv=3",
                $"clashtol={_clashTolMm}",
                $"tag={(_tagOn ? 1 : 0)}",
                $"left={r.Left.ToString(ci)}", $"top={r.Top.ToString(ci)}",
                $"width={r.Width.ToString(ci)}", $"height={r.Height.ToString(ci)}",
                $"max={(WindowState == WindowState.Maximized ? 1 : 0)}",
            });
        }
        catch { /* yazılamazsa önemli değil */ }
    }
}
