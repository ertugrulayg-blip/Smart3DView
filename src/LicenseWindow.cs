using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Smart3DView;

/// <summary>Lisans durumu, satın alma bağlantısı ve anahtar etkinleştirme.</summary>
sealed class LicenseWindow : Window
{
    readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 14) };
    readonly TextBox _key = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(6, 5, 6, 5) };
    readonly TextBlock _msg = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
    readonly StackPanel _buyPanel = new();
    readonly StackPanel _ownedPanel = new();
    readonly Button _activate, _remove;

    /// <param name="reason">Kilitli bir özelliğe tıklandıysa hangisi olduğu (üstte gösterilir).</param>
    public LicenseWindow(Window? owner, string? reason)
    {
        Title = Product.Name + L.T(" lisansı", " license");
        Width = 470; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner != null) Owner = owner;
        FontFamily = new FontFamily("Segoe UI");
        Background = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };
        root.Children.Add(new TextBlock { Text = Product.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        if (reason != null)
            root.Children.Add(new TextBlock
            {
                Text = reason, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 10),
                Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x5A, 0x00)),
            });
        root.Children.Add(_state);

        // Satın alma + etkinleştirme
        var buy = PrimaryButton(L.T($"Tam lisans satın al — {License.Price}", $"Buy full license — {License.Price}"));
        buy.Click += (_, _) => Open(License.BuyUrl);
        _buyPanel.Children.Add(buy);
        _buyPanel.Children.Add(new TextBlock
        {
            Text = L.T("Tek seferlik ödeme · süresiz · 2 bilgisayar. Anahtar satın alma e-postanızla gelir.",
                       "One-time payment · perpetual · 2 computers. The key arrives in your purchase e-mail."),
            FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 16), TextWrapping = TextWrapping.Wrap,
        });
        _buyPanel.Children.Add(new TextBlock { Text = L.T("Lisans anahtarı", "License key"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var row = new DockPanel();
        _activate = new Button { Content = L.T("Etkinleştir", "Activate"), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0) };
        _activate.Click += async (_, _) => await DoActivate();
        _key.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await DoActivate(); };
        DockPanel.SetDock(_activate, Dock.Right);
        row.Children.Add(_activate);
        row.Children.Add(_key);
        _buyPanel.Children.Add(row);

        // Lisanslı
        _remove = new Button { Content = L.T("Bu bilgisayardaki lisansı kaldır", "Remove license from this computer"), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        _remove.Click += async (_, _) =>
        {
            _remove.IsEnabled = false;
            var (ok, m) = await License.Deactivate();
            _remove.IsEnabled = true;
            Show(m, ok);
            Refresh();
        };
        _ownedPanel.Children.Add(_remove);

        root.Children.Add(_buyPanel);
        root.Children.Add(_ownedPanel);
        root.Children.Add(_msg);
        Content = root;
        Refresh();
    }

    void Refresh()
    {
        var s = License.State;
        _state.Text = s switch
        {
            LicenseState.Licensed => L.T($"✓ Lisanslı — anahtar {License.MaskedKey}. Tüm özellikler açık.",
                                         $"✓ Licensed — key {License.MaskedKey}. All features unlocked."),
            LicenseState.Trial => L.T($"Deneme sürümü: {License.TrialDaysLeft} gün kaldı. Süre bitince gri 3B görüntüleme ücretsiz kalır; Renkli ton, Çakışma, Görüntü al ve Kutu düzenleme tam lisans ister.",
                                      $"Trial: {License.TrialDaysLeft} days left. Afterwards grayscale 3D viewing stays free; Colored, Clash, Take picture and Box editing need a full license."),
            _ => L.T("Ücretsiz sürüm: gri 3B görüntüleme açık. Renkli ton, Çakışma, Görüntü al ve Kutu düzenleme için tam lisans gerekir.",
                     "Free version: grayscale 3D viewing. Colored, Clash, Take picture and Box editing need a full license."),
        };
        bool owned = s == LicenseState.Licensed;
        _buyPanel.Visibility = owned ? Visibility.Collapsed : Visibility.Visible;
        _ownedPanel.Visibility = owned ? Visibility.Visible : Visibility.Collapsed;
    }

    async System.Threading.Tasks.Task DoActivate()
    {
        _activate.IsEnabled = false;
        Show(L.T("Etkinleştiriliyor…", "Activating…"), true);
        var (ok, m) = await License.Activate(_key.Text);
        _activate.IsEnabled = true;
        Show(m, ok);
        Refresh();
    }

    void Show(string m, bool ok)
    {
        _msg.Text = m;
        _msg.Foreground = new SolidColorBrush(ok ? Color.FromRgb(0x1E, 0x7A, 0x34) : Color.FromRgb(0xC0, 0x20, 0x20));
    }

    static Button PrimaryButton(string text) => new()
    {
        Content = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
        Padding = new Thickness(16, 8, 16, 8),
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xD8)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xD8)),
        Cursor = Cursors.Hand,
    };

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* tarayıcı açılamadı */ }
    }
}
