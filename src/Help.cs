using System;
using System.Diagnostics;
using System.IO;

namespace Smart3DView;

/// <summary>Hızlı başlangıç / yardım sayfası: eklentiyle birlikte gelen yerel kopya (help/index.html, çevrimdışı da açılır)
/// ve aynı sayfanın çevrimiçi hâli (Autodesk mağazası ikisini de istiyor).</summary>
static class Help
{
    public const string OnlineUrl = "https://schema-tools.net/smart3dview/help/";

    public static string OnlineWithLang => OnlineUrl + "?lang=" + (L.Tr ? "tr" : "en");

    static string? LocalPath
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(Help).Assembly.Location) ?? "";
            var p = Path.Combine(dir, "help", "index.html");
            return File.Exists(p) ? p : null;
        }
    }

    /// <summary>Penceremizde F1 bizim yardımımızı açar. Karşılanmayan F1, Windows'ta WM_HELP olarak sahip pencereye (Revit)
    /// gider ve Revit kendi yardımını açar (2026-10-30: "GLB'ye aktar" penceresinde görüldü).</summary>
    public static void Attach(System.Windows.Window w)
    {
        w.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F1) { Open(); e.Handled = true; } };
        w.SourceInitialized += (_, _) =>
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(w).Handle)?.AddHook(
                (IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == 0x0053) handled = true;   // WM_HELP: Revit'e iletilmesin (yardımı PreviewKeyDown açtı)
                    return IntPtr.Zero;
                });
    }

    /// <summary>Yerel kopya varsa onu (internetsiz de çalışır), yoksa çevrimiçi sayfayı varsayılan tarayıcıda açar.</summary>
    public static void Open()
    {
        try { Process.Start(new ProcessStartInfo(LocalPath ?? OnlineWithLang) { UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo(OnlineWithLang) { UseShellExecute = true }); } catch { }
        }
    }
}
