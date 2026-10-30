using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Smart3DView;

/// <summary>İmlecin yanında küçük, koyu ipucu şeridi (kullanıcı isteği 2026-10-30: "boş tıklayınca alan seç yazan bir uyarı
/// çıksın ama rahatsız etmesin"). Odak almaz, tıklamaları alttaki Revit'e geçirir, görev çubuğunda görünmez; imleci izler.
/// Planda PickBox süresince açık kalır; seçim/kutu yokken birkaç saniye görünüp kendiliğinden söner (TaskDialog yerine).</summary>
sealed class CursorHint : Window
{
    readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(15) };
    IntPtr _hwnd;

    CursorHint(string text)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x26, 0x2B)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 5, 9, 5),
            MaxWidth = 420,
            Child = new TextBlock
            {
                Text = text, Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
            },
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Odak almaz (Revit'in seçimi bozulmaz), fare olayları alttaki pencereye geçer, Alt+Tab'da görünmez.
            SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
            Follow();
        };
        _follow.Tick += (_, _) => Follow();
    }

    void Follow()
    {
        if (_hwnd == IntPtr.Zero || !GetCursorPos(out var p)) return;
        // Fiziksel piksellerle konumlanır (DPI ölçeğinden bağımsız); imlecin sağ altında, ekran kenarında içeri döner.
        GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top, x = p.X + 18, y = p.Y + 22;
        var mon = MonitorFromPoint(p, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref mi))
        {
            if (x + w > mi.rcWork.Right) x = p.X - w - 8;
            if (y + h > mi.rcWork.Bottom) y = p.Y - h - 8;
        }
        SetWindowPos(_hwnd, (IntPtr)(-1), x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Kapatılana (Dispose) kadar imlecin yanında kalır — PickBox gibi Revit'in beklediği işlemler için.</summary>
    public static IDisposable While(string text)
    {
        CursorHint? h = null;
        try { h = new CursorHint(text); h.Show(); h._follow.Start(); } catch { }
        return new Closer(h);
    }

    /// <summary>Birkaç saniye görünür, sonra yavaşça söner. Hiçbir şeyi engellemez.</summary>
    public static void Flash(string text, double seconds = 4)
    {
        try
        {
            var h = new CursorHint(text);
            h.Show();
            h._follow.Start();
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            stop.Tick += (_, _) =>
            {
                stop.Stop();
                h._follow.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                fade.Completed += (_, _) => h.Close();
                h.BeginAnimation(OpacityProperty, fade);
            };
            stop.Start();
        }
        catch { }
    }

    sealed class Closer(CursorHint? h) : IDisposable
    {
        public void Dispose()
        {
            if (h == null) return;
            try { h._follow.Stop(); h.Close(); } catch { }
        }
    }

    const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;
    const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
}
