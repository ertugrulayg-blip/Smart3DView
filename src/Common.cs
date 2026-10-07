using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Smart3DView;

static class L
{
    public static readonly bool Tr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
    public static string T(string tr, string en) => Tr ? tr : en;
}

static class AddinVersion
{
    static readonly Assembly Asm = typeof(AddinVersion).Assembly;

    public static string Version { get; } = Asm.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    public static string BuildTime { get; } = Asm.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "BuildTime")?.Value ?? "?";

    public static string Text => $"v{Version} · {BuildTime}";
}

/// <summary>Pencerenin Revit'ten istediği işler (Revit türleri içermez; test düzeneği null verir).</summary>
interface IViewerHost
{
    /// <summary>Aynı çerçevede yeni kutu sınırlarıyla geometriyi yeniden okur.</summary>
    void Recollect(object context, double[] min, double[] max, System.Action<SceneData?, string?> done);

    /// <summary>PNG'yi Revit'e görüntü görünümü (Renderings) olarak ekler.</summary>
    void SaveImage(object context, string pngPath, System.Action<string> done);

    /// <summary>Bağlamın bu belgeden okunup okunmadığı (Revit'te model değişince "Yenile" düğmesini uyarmak için).</summary>
    bool IsFromDoc(object context, object doc);
}

/// <summary>Revit modelindeki değişiklik bildirimi (App, DocumentChanged'den çağırır). Pencere Revit türü bilmeden dinler.</summary>
static class ModelWatch
{
    public static event System.Action<object>? Changed;
    public static void Raise(object doc) => Changed?.Invoke(doc);
}

static class Product
{
    public const string Name = "Smart3DView";
}
