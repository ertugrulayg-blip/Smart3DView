using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Smart3DView;

enum LicenseState { Licensed, Trial, Free }

/// <summary>Lisans: 14 gün tam deneme → sonra gri 3B görüntüleme ücretsiz, gelişmiş özellikler (Renkli, Çakışma, Görüntü al,
/// Kutu düzenleme) lisans ister. Tam lisans schema-tools.net'te Polar üzerinden satılır; satın alımda Polar'ın "License Key"
/// avantajı anahtarı üretir. Eklenti anahtarı Polar'ın kimlik doğrulama gerektirmeyen customer-portal uç noktalarıyla
/// etkinleştirir/doğrular (sunucumuz yok). Bir anahtar en fazla 2 bilgisayarda (Polar ürün ayarı: activation limit = 2).</summary>
static class License
{
    // schema-tools.net Polar kuruluşu (Polar → Settings → General → Organization ID).
    public const string PolarOrganizationId = "23e8234a-0188-48c3-904c-f9725f72e6a0";
    public const string BuyUrl = "https://schema-tools.net/smart3dview/";
    public const string Price = "$4";
    public const int TrialDays = 14;
    const int RevalidateDays = 7, OfflineGraceDays = 30;
    const string Api = "https://api.polar.sh/v1/customer-portal/license-keys/";
    const string RegPath = @"Software\" + Product.Name;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name, "license.json");

    sealed class Data
    {
        public string? Key { get; set; }
        public string? ActivationId { get; set; }
        public DateTime? LastOk { get; set; }
        public DateTime? TrialStart { get; set; }
    }

    static readonly Data D = Load();

    public static event Action? Changed;

    public static LicenseState State
    {
        get
        {
            if (D.Key != null && D.ActivationId != null && D.LastOk is { } ok && (DateTime.UtcNow - ok).TotalDays <= OfflineGraceDays)
                return LicenseState.Licensed;
            return TrialDaysLeft > 0 ? LicenseState.Trial : LicenseState.Free;
        }
    }

    /// <summary>Gelişmiş özellikler açık mı (lisanslı ya da deneme süresinde).</summary>
    public static bool FullFeatures => State != LicenseState.Free;

    public static int TrialDaysLeft
    {
        get
        {
            var start = D.TrialStart ?? DateTime.UtcNow;
            return Math.Max(0, TrialDays - (int)Math.Floor((DateTime.UtcNow - start).TotalDays));
        }
    }

    public static string? MaskedKey => D.Key is { Length: > 8 } k ? k[..4] + "…" + k[^4..] : D.Key;

    // ---- kalıcılık -----------------------------------------------------------------------------------------------

    static Data Load()
    {
        Data d;
        try { d = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data() : new Data(); }
        catch { d = new Data(); }
        // Deneme başlangıcı hem dosyada hem kayıt defterinde; en eskisi geçerli (dosyayı silmek süreyi sıfırlamaz).
        DateTime? reg = null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            if (k?.GetValue("TrialStart") is string s && long.TryParse(s, out var ticks)) reg = new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { }
        var start = Min(d.TrialStart, reg) ?? DateTime.UtcNow;
        if (start > DateTime.UtcNow) start = DateTime.UtcNow; // saati ileri alıp geri getirme
        bool changed = d.TrialStart != start;
        d.TrialStart = start;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RegPath);
            k.SetValue("TrialStart", start.Ticks.ToString());
        }
        catch { }
        if (changed) Save(d);
        return d;
    }

    static DateTime? Min(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a < b ? a : b);

    static void Save(Data d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(d));
        }
        catch { }
    }

    // ---- Polar ---------------------------------------------------------------------------------------------------

    static async Task<(HttpStatusCode code, JsonNode? json)> Post(string action, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await Http.PostAsync(Api + action, content).ConfigureAwait(true);
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(true);
        JsonNode? json = null;
        try { if (text.Length > 0) json = JsonNode.Parse(text); } catch { }
        return (res.StatusCode, json);
    }

    /// <summary>Anahtarı bu bilgisayar için etkinleştirir. Dönen metin kullanıcıya gösterilir.</summary>
    public static async Task<(bool ok, string message)> Activate(string key)
    {
        key = key.Trim();
        if (key.Length < 8) return (false, L.T("Lisans anahtarını yapıştırın.", "Paste your license key."));
        if (PolarOrganizationId.Length == 0) return (false, L.T("Lisans sunucusu henüz yapılandırılmadı.", "The license server is not configured yet."));
        try
        {
            var (code, json) = await Post("activate", new
            {
                key,
                organization_id = PolarOrganizationId,
                // Kişisel veri göndermiyoruz (Autodesk yönergesi + gizlilik): yalnız bilgisayar adının tek yönlü özeti.
                label = DeviceCode(),
                meta = new { product = Product.Name, version = AddinVersion.Version },
            });
            if (code == HttpStatusCode.OK && json?["id"]?.GetValue<string>() is { } id)
            {
                D.Key = key;
                D.ActivationId = id;
                D.LastOk = DateTime.UtcNow;
                Save(D);
                Changed?.Invoke();
                return (true, L.T("✓ Lisans etkinleştirildi. Teşekkürler!", "✓ License activated. Thank you!"));
            }
            return (false, code switch
            {
                HttpStatusCode.NotFound => L.T("Bu anahtar bulunamadı. Satın alma e-postasındaki anahtarı eksiksiz yapıştırın.",
                                               "Key not found. Paste the full key from your purchase e-mail."),
                HttpStatusCode.Forbidden => L.T("Anahtar iptal edilmiş ya da 2 bilgisayar sınırına ulaşılmış. Eski bilgisayarda 'Bu bilgisayardaki lisansı kaldır' ile yer açabilirsiniz.",
                                                "The key is revoked or already used on 2 computers. Use 'Remove license from this computer' on the old one to free a seat."),
                _ => L.T($"Etkinleştirilemedi (HTTP {(int)code}).", $"Activation failed (HTTP {(int)code})."),
            });
        }
        catch (Exception ex)
        {
            return (false, L.T("Lisans sunucusuna bağlanılamadı: ", "Could not reach the license server: ") + ex.Message);
        }
    }

    /// <summary>Haftada bir sessiz doğrulama: anahtar iptal/iade edildiyse lisans kalkar. Ağ yoksa dokunmaz (30 gün tolerans).</summary>
    public static async Task Revalidate()
    {
        if (D.Key == null || D.ActivationId == null || PolarOrganizationId.Length == 0) return;
        if (D.LastOk is { } ok && (DateTime.UtcNow - ok).TotalDays < RevalidateDays) return;
        try
        {
            var (code, json) = await Post("validate", new { key = D.Key, organization_id = PolarOrganizationId, activation_id = D.ActivationId });
            if (code == HttpStatusCode.OK && json?["status"]?.GetValue<string>() == "granted")
            {
                D.LastOk = DateTime.UtcNow;
                Save(D);
            }
            else if (code is HttpStatusCode.NotFound or HttpStatusCode.Forbidden || code == HttpStatusCode.OK)
            {
                Clear();
            }
        }
        catch { /* çevrimdışı */ }
    }

    /// <summary>Bu bilgisayarın etkinleştirmesini Polar'da serbest bırakır (başka bilgisayara geçmek için).</summary>
    public static async Task<(bool ok, string message)> Deactivate()
    {
        if (D.Key == null || D.ActivationId == null) { Clear(); return (true, ""); }
        try
        {
            var (code, _) = await Post("deactivate", new { key = D.Key, organization_id = PolarOrganizationId, activation_id = D.ActivationId });
            if (code is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound)
            {
                Clear();
                return (true, L.T("Lisans bu bilgisayardan kaldırıldı; anahtarı başka bir bilgisayarda kullanabilirsiniz.",
                                  "License removed from this computer; you can use the key on another one."));
            }
            return (false, L.T($"Kaldırılamadı (HTTP {(int)code}).", $"Could not remove (HTTP {(int)code})."));
        }
        catch (Exception ex)
        {
            return (false, L.T("Lisans sunucusuna bağlanılamadı: ", "Could not reach the license server: ") + ex.Message);
        }
    }

    /// <summary>"PC-1A2B3C4D" — bilgisayarı ayırt eder ama adını açık etmez; müşteri portalında etkinleştirmeyi tanıması için yeterli.</summary>
    static string DeviceCode()
    {
        var h = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Product.Name + "|" + Environment.MachineName));
        return "PC-" + Convert.ToHexString(h, 0, 4);
    }

    static void Clear()
    {
        D.Key = null;
        D.ActivationId = null;
        D.LastOk = null;
        Save(D);
        Changed?.Invoke();
    }
}
