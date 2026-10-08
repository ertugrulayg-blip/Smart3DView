using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Smart3DView;

/// <summary>Web'e aktarma (Smart3DView Web, kullanıcı isteği 2026-10-08): dosya yeri sorma, .glb yazma ve "kaydedildi"
/// penceresi. Hem şerit düğmesi (tüm model) hem 3B penceredeki düğme (açık görünüm) kullanır. Revit'e bağlı değildir.</summary>
static class WebExport
{
    /// <summary>Varsayılan klasör: Belgeler\Smart3DView — görüntüleyicide "Modellerim klasörü" olarak bir kez seçilir.</summary>
    public static string DefaultFolder
    {
        get
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Product.Name);
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    public static string? AskPath(Window? owner, string title)
    {
        var bad = Path.GetInvalidFileNameChars();
        string name = new string(title.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        if (name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Web'e aktar — .glb dosyasını kaydet", "Export to web — save the .glb file"),
            Filter = L.T("3B model (*.glb)|*.glb", "3D model (*.glb)|*.glb"),
            DefaultExt = ".glb",
            FileName = (name.Length > 0 ? name : "model") + ".glb",
            InitialDirectory = DefaultFolder,
            OverwritePrompt = true,
        };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }

    /// <summary>Tüm model aktarımında hangi modellerin dahil edileceği: ana model + bağlı modeller, hepsi işaretli gelir
    /// (kullanıcı isteği 2026-10-08: "hangilerine gerek olmadığını seçebileyim"). Vazgeçilirse null.</summary>
    public static System.Collections.Generic.List<string>? AskModels(IntPtr ownerHwnd, string host, System.Collections.Generic.IList<string> links)
    {
        var w = new Window
        {
            Title = Product.Name + " — " + L.T("Web'e aktar: modeller", "Export to web: models"),
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = ownerHwnd != IntPtr.Zero ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (ownerHwnd != IntPtr.Zero) new WindowInteropHelper(w).Owner = ownerHwnd;
        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MinWidth = 380, MaxWidth = 620 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Aktarılacak modeller (gerekmeyenlerin işaretini kaldırın):", "Models to export (untick the ones you don't need):"),
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap,
        });
        var boxes = new System.Collections.Generic.List<(CheckBox cb, string name)>();
        var list = new StackPanel();
        void Add(string name, string label)
        {
            var cb = new CheckBox { Content = label, IsChecked = true, Margin = new Thickness(0, 3, 0, 3) };
            boxes.Add((cb, name));
            list.Children.Add(cb);
        }
        Add(host, L.T("Ana model: ", "Host model: ") + host);
        foreach (var l in links) Add(l, "🔗 " + l);
        stack.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (links.Count == 0)
            stack.Children.Add(new TextBlock { Text = L.T("Bu modelde yüklü bağlı model yok.", "This model has no loaded links."), Opacity = 0.6, Margin = new Thickness(0, 4, 0, 0) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        Button B(string t, RoutedEventHandler h)
        {
            var b = new Button { Content = t, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), MinWidth = 70 };
            b.Click += h;
            return b;
        }
        row.Children.Add(B(L.T("Tümü", "All"), (_, _) => boxes.ForEach(x => x.cb.IsChecked = true)));
        row.Children.Add(B(L.T("Hiçbiri", "None"), (_, _) => boxes.ForEach(x => x.cb.IsChecked = false)));
        row.Children.Add(new Border { Width = 30 });
        bool ok = false;
        var go = B(L.T("Devam", "Continue"), (_, _) => { ok = true; w.Close(); });
        go.IsDefault = true;
        var cancel = B(L.T("Vazgeç", "Cancel"), (_, _) => w.Close());
        cancel.IsCancel = true;
        row.Children.Add(go);
        row.Children.Add(cancel);
        stack.Children.Add(row);
        w.Content = stack;
        w.ShowDialog();
        if (!ok) return null;
        var sel = boxes.Where(x => x.cb.IsChecked == true).Select(x => x.name).ToList();
        return sel.Count > 0 ? sel : null;
    }

    /// <summary>Dosyayı arka planda yazar (büyük modelde birkaç saniye sürebilir; pencere donmaz).</summary>
    public static Task Write(SceneData s, string path, Func<int, bool>? include = null, double[]? boxMin = null, double[]? boxMax = null) =>
        Task.Run(() => GlbWriter.Write(s, path, include, boxMin, boxMax));

    /// <summary>Kaydedildi: dosya tarayıcıda kendiliğinden açılır (yerel sunucu); pencerede yol, boyut, süre, yeniden aç /
    /// klasörü aç.</summary>
    public static void ShowSaved(Window? owner, IntPtr ownerHwnd, string path, int elements, double seconds = -1)
    {
        LocalServer.OpenInBrowser(path);
        var w = new Window
        {
            Title = L.T("Web'e aktarıldı", "Exported to web"), SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = owner != null || ownerHwnd != IntPtr.Zero ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner != null) w.Owner = owner;
        else if (ownerHwnd != IntPtr.Zero) new WindowInteropHelper(w).Owner = ownerHwnd;
        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MaxWidth = 560 };
        void Line(string t, bool bold = false) => stack.Children.Add(new TextBlock
        {
            Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        });
        double mb = 0;
        try { mb = new FileInfo(path).Length / 1048576.0; } catch { }
        string took = seconds >= 0 ? L.T($" · {seconds:0} sn", $" · {seconds:0} s") : "";
        Line(L.T($"{elements:N0} eleman kaydedildi ({mb:0.0} MB){took}.", $"{elements:N0} elements saved ({mb:0.0} MB){took}."), bold: true);
        Line(path);
        Line(L.T("Tarayıcıda açıldı. Dosya bilgisayarınızda kalır; Revit açık olduğu sürece sayfayı yenileyince yeniden gelir. Daha sonra açmak için görüntüleyicide \"Modellerim klasörü\" ile bu klasörü bir kez seçin.",
                 "Opened in the browser. The file stays on your computer; while Revit is open, reloading the page brings it back. To open it later, pick this folder once with \"My models folder\" in the viewer."));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        Button B(string t, Action a)
        {
            var b = new Button { Content = t, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0), MinWidth = 80 };
            b.Click += (_, _) => { try { a(); } catch { } };
            return b;
        }
        var open = B(L.T("Tarayıcıda yeniden aç", "Open in browser again"), () => LocalServer.OpenInBrowser(path));
        row.Children.Add(open);
        row.Children.Add(B(L.T("Klasörü aç", "Open folder"), () => Process.Start("explorer.exe", $"/select,\"{path}\"")));
        var ok = B(L.T("Kapat", "Close"), () => w.Close());
        ok.IsCancel = true;
        ok.IsDefault = true;
        row.Children.Add(ok);
        stack.Children.Add(row);
        w.Content = stack;
        w.Show();
    }
}

/// <summary>Tüm model aktarımında ilerleme: Revit çalışmaya devam eder; İptal / Esc okumayı bir sonraki dilimde bırakır.</summary>
sealed class ExportProgressWindow : Window
{
    readonly TextBlock _text = new() { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _bar = new() { Height = 14, Minimum = 0, Maximum = 100, Width = 380 };
    readonly TextBlock _model = new() { Margin = new Thickness(0, 6, 0, 0), FontSize = 11, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
    readonly TextBlock _slow = new() { Margin = new Thickness(0, 2, 0, 0), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC2, 0x41, 0x0C)), Visibility = Visibility.Collapsed };
    readonly ReadControl _ctl;

    public ExportProgressWindow(IntPtr ownerHwnd, ReadControl ctl)
    {
        _ctl = ctl;
        Title = Product.Name + " — " + L.T("Web'e aktar", "Export to web");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = ownerHwnd != IntPtr.Zero ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (ownerHwnd != IntPtr.Zero) new WindowInteropHelper(this).Owner = ownerHwnd;
        var cancel = new Button { Content = L.T("İptal (Esc)", "Cancel (Esc)"), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => Cancel();
        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        stack.Children.Add(_text);
        stack.Children.Add(_bar);
        stack.Children.Add(_model);
        stack.Children.Add(_slow);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Revit'te çalışmaya devam edebilirsiniz.", "You can keep working in Revit."),
            Opacity = 0.6, FontSize = 11, Margin = new Thickness(0, 6, 0, 0),
        });
        stack.Children.Add(cancel);
        Content = stack;
        SetText(L.T("Model hazırlanıyor…", "Preparing the model…"));
        _bar.IsIndeterminate = true;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };
        Closed += (_, _) => _ctl.Cancel = _ctl.Cancel || !Finished;
    }

    /// <summary>Okuma bitti (pencere kapanınca iptal sayılmasın).</summary>
    public bool Finished;

    void Cancel()
    {
        _ctl.Cancel = true;
        SetText(L.T("İptal ediliyor…", "Cancelling…"));
    }

    public void SetText(string t) => _text.Text = t;

    /// <summary>O an okunan model ve yavaş eleman uyarısı — hangi bağlı modelin ağır olduğu görülsün, gerekirse iptal edip
    /// o modelin işaretini kaldırarak yeniden aktarılsın.</summary>
    public void SetDetail(string model, string? slow)
    {
        _model.Text = L.T("Model: ", "Model: ") + model;
        if (slow == null) return;
        _slow.Text = L.T("Yavaş eleman: ", "Slow element: ") + slow + L.T(" — bu model gerekmiyorsa İptal edip işaretini kaldırarak yeniden aktarın.", " — if you don't need this model, cancel and export again with it unticked.");
        _slow.Visibility = Visibility.Visible;
    }

    public void SetProgress(int done, int total)
    {
        if (_ctl.Cancel) return;
        _bar.IsIndeterminate = false;
        _bar.Value = total == 0 ? 100 : 100.0 * done / total;
        SetText(L.T($"Tüm model okunuyor… %{(int)_bar.Value}  ({done:N0} / {total:N0} eleman)",
                    $"Reading the whole model… {(int)_bar.Value}%  ({done:N0} / {total:N0} elements)"));
    }
}

/// <summary>İlerleme penceresi kendi iş parçacığında: tüm model aktarımı Revit'in aktarıcısıyla tek seferde çalışır ve
/// bu sürede Revit'in iş parçacığı meşguldür. Pencere ayrı iş parçacığında olduğu için yüzde güncellenir ve İptal
/// tepki verir (aktarıcı her elemanda iptale bakar). Sahibi Revit penceresi yapılmaz: farklı iş parçacığındaki sahiplik
/// giriş kuyruklarını birleştirip pencereyi de dondurur — yerine en üstte durur.</summary>
sealed class ProgressThread
{
    ExportProgressWindow? _win;
    System.Windows.Threading.DispatcherTimer? _timer;
    readonly System.Threading.ManualResetEventSlim _ready = new();

    public ProgressThread(ReadControl ctl, Func<(int done, int total, string model)> poll)
    {
        var th = new System.Threading.Thread(() =>
        {
            _win = new ExportProgressWindow(IntPtr.Zero, ctl) { Topmost = true };
            var timer = _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                var (d, t, m) = poll();
                _win.SetProgress(d, Math.Max(t, d));
                _win.SetDetail(m, null);
            };
            _win.Closed += (_, _) => { timer.Stop(); System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); };
            _win.Show();
            timer.Start();
            _ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        });
        th.SetApartmentState(System.Threading.ApartmentState.STA);
        th.IsBackground = true;
        th.Start();
        _ready.Wait(3000);
    }

    /// <summary>Okuma bitti: yüzde güncellemesi durur, metin gösterilir (ör. "dosya yazılıyor").</summary>
    public void SetText(string t) => _win?.Dispatcher.BeginInvoke(() => { _timer?.Stop(); _win.SetText(t); });

    public void Close() => _win?.Dispatcher.BeginInvoke(() => { _win.Finished = true; _win.Close(); });
}

/// <summary>Aktarılan dosyayı tarayıcıda kendiliğinden açmak için yalnız bu bilgisayara açık küçük sunucu (kullanıcı
/// isteği 2026-10-08: "sürükle bırak yaptırtma, kendin aç"). Web sayfası diskteki dosyayı kendisi okuyamaz; eklenti
/// dosyayı http://127.0.0.1:&lt;port&gt;/&lt;rastgele anahtar&gt;/ adresinden verir — dosya yine bilgisayardan çıkmaz.
/// Yalnız kayıtlı dosyalar verilir; Revit kapanınca sunucu da kapanır.</summary>
static class LocalServer
{
    static System.Net.HttpListener? _listener;
    static int _port;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Files = new();

    /// <summary>Dosyayı yayınlar, görüntüleyici adresini döner (sunucu başlatılamazsa null).</summary>
    public static string? ViewerUrlFor(string path)
    {
        if (!EnsureStarted()) return null;
        string token = Guid.NewGuid().ToString("N");
        Files[token] = path;
        string file = Uri.EscapeDataString(Path.GetFileName(path));
        string src = $"http://127.0.0.1:{_port}/{token}/{file}";
        return $"{Product.WebViewerUrl}?src={Uri.EscapeDataString(src)}&name={file}";
    }

    public static void OpenInBrowser(string path)
    {
        string url = ViewerUrlFor(path) ?? Product.WebViewerUrl;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    static bool EnsureStarted()
    {
        if (_listener != null) return true;
        for (int p = 47321; p < 47341; p++)
        {
            var l = new System.Net.HttpListener();
            l.Prefixes.Add($"http://127.0.0.1:{p}/");
            try { l.Start(); }
            catch { l.Close(); continue; }
            _listener = l; _port = p;
            var th = new System.Threading.Thread(Loop) { IsBackground = true, Name = "Smart3DView local server" };
            th.Start();
            return true;
        }
        return false;
    }

    static void Loop()
    {
        while (_listener is { IsListening: true } l)
        {
            System.Net.HttpListenerContext c;
            try { c = l.GetContext(); } catch { return; }
            System.Threading.ThreadPool.QueueUserWorkItem(_ => Serve(c));
        }
    }

    static void Serve(System.Net.HttpListenerContext c)
    {
        var res = c.Response;
        try
        {
            // HTTPS sayfasından yerel adrese istek: CORS + Chrome'un "özel ağ erişimi" ön isteği.
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "*");
            res.AddHeader("Access-Control-Allow-Private-Network", "true");
            if (c.Request.HttpMethod == "OPTIONS") { res.StatusCode = 204; return; }
            var parts = c.Request.Url!.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 1 || !Files.TryGetValue(parts[0], out var path) || !File.Exists(path)) { res.StatusCode = 404; return; }
            res.ContentType = "model/gltf-binary";
            res.AddHeader("Cache-Control", "no-store");
            using var f = File.OpenRead(path);
            res.ContentLength64 = f.Length;
            f.CopyTo(res.OutputStream);
        }
        catch { try { res.StatusCode = 500; } catch { } }
        finally { try { res.Close(); } catch { } }
    }
}
