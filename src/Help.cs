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
