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
    public static Task Write(SceneData s, string path, Func<int, bool>? include = null) =>
        Task.Run(() => GlbWriter.Write(s, path, include));

    /// <summary>Kaydedildi: dosya yolu, boyut; görüntüleyiciyi tarayıcıda aç / klasörü aç.</summary>
    public static void ShowSaved(Window? owner, IntPtr ownerHwnd, string path, int elements)
    {
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
        Line(L.T($"{elements:N0} eleman kaydedildi ({mb:0.0} MB).", $"{elements:N0} elements saved ({mb:0.0} MB)."), bold: true);
        Line(path);
        Line(L.T("Dosya bilgisayarınızda kalır. Görüntüleyicide dosyayı sayfaya sürükleyin ya da \"Modellerim klasörü\" ile bu klasörü bir kez seçin.",
                 "The file stays on your computer. In the viewer, drag the file onto the page, or pick this folder once with \"My models folder\"."));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        Button B(string t, Action a)
        {
            var b = new Button { Content = t, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0), MinWidth = 80 };
            b.Click += (_, _) => { try { a(); } catch { } };
            return b;
        }
        var open = B(L.T("Görüntüleyicide aç", "Open the viewer"), () =>
        {
            Process.Start(new ProcessStartInfo(Product.WebViewerUrl) { UseShellExecute = true });
            Process.Start("explorer.exe", $"/select,\"{path}\"");   // dosya sürüklenmeye hazır
            w.Close();
        });
        open.IsDefault = true;
        row.Children.Add(open);
        row.Children.Add(B(L.T("Klasörü aç", "Open folder"), () => Process.Start("explorer.exe", $"/select,\"{path}\"")));
        var ok = B(L.T("Kapat", "Close"), () => w.Close());
        ok.IsCancel = true;
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

    public void SetProgress(int done, int total)
    {
        if (_ctl.Cancel) return;
        _bar.IsIndeterminate = false;
        _bar.Value = total == 0 ? 100 : 100.0 * done / total;
        SetText(L.T($"Tüm model okunuyor… %{(int)_bar.Value}  ({done:N0} / {total:N0} eleman)",
                    $"Reading the whole model… {(int)_bar.Value}%  ({done:N0} / {total:N0} elements)"));
    }
}
