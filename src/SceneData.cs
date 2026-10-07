using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Smart3DView;

/// <summary>Ton grupları — paletteki temel gri bu gruplara göre hafifçe açılıp koyulaşır.</summary>
enum Tone { General, Horizontal, Structure, Mep, Glass, Cut }

/// <summary>"Renkli" tonda kullanılan sistem rengi (gölgelendiricideki uClass dizisinin indeksi).</summary>
enum SysColor : byte { None = 0, Cooling = 1, Fire = 2, Supply = 3, Return = 4 }

/// <summary>Çakışma grubu: yalnız FARKLI gruplardaki elemanlar arasında çakışma aranır
/// (boru–dirsek, kaplin–dirsek gibi aynı tesisatın parçaları birbirini işaretlemez).</summary>
static class ClashGroup
{
    public const int None = 0,
        PipeCooling = 1, PipeHeating = 2, PipeFire = 3, PipeDomestic = 4, PipeDrainage = 5, PipeOther = 6,
        DuctSupply = 10, DuctReturn = 11, DuctOther = 12,
        Containment = 20, Electrical = 21, MechEquipment = 30, Structure = 40;
}

/// <summary>GPU köşe biçimi (24 bayt): konum, normal (short, normalize), ton, eleman no (1 tabanlı).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct SurfVertex
{
    public float X, Y, Z;
    public short NX, NY, NZ, Tone;
    public uint Id;
}

/// <summary>Kenar çizgisi köşesi (16 bayt).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct EdgeVertex
{
    public float X, Y, Z;
    public uint Id;
}

/// <summary>Ölçü yakalama noktası (kutu-yerel feet). Kind: 0 köşe/uç, 1 kenar ortası, 2 yay merkezi (boru ekseni).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct SnapPoint
{
    public float X, Y, Z;
    public uint Id;
    public byte Kind;
}

/// <summary>Kutu-yerel koordinatlarda (feet, merkez = 0) hazır sahne. Revit'e bağımlı değil; pencere bunu çizer.</summary>
sealed class SceneData
{
    public readonly List<SurfVertex> Vertices = new();
    public readonly List<uint> Opaque = new();     // üçgen indisleri (opak + kesit)
    public readonly List<uint> Glass = new();      // saydam üçgenler en son çizilir
    public readonly List<EdgeVertex> Edges = new(); // ardışık çiftler = doğru parçaları
    public readonly List<string> Labels = new();   // Labels[id-1] = "Kategori: Aile: Tip · id"

    // Eleman başına (indeks = id-1) sistem bilgisi — "Renkli" ton ve çakışma denetimi için.
    public readonly List<byte> ElemColor = new();  // SysColor
    public readonly List<int> ElemGroup = new();   // ClashGroup; 0 = çakışma denetimine girmez
    public readonly List<uint> ElemCanon = new();  // izolasyon → taşıdığı boru/kanal (yoksa kendisi)
    public readonly HashSet<ulong> Connected = new(); // connector ile birbirine bağlı eleman çiftleri (PairKey)
    public readonly List<SnapPoint> Snaps = new(); // ölçü yakalama: köşe, kenar ortası, yay merkezi

    // Filtre / özellikler / anlık etiket (indeks = id-1)
    public readonly List<string> CatNames = new();   // sahnedeki Revit kategori adları (filtre listesi)
    public readonly List<ushort> ElemCat = new();    // → CatNames indeksi
    public readonly List<long> ElemRevitId = new();  // Revit ElementId değeri (kendi belgesinde)
    public readonly List<byte> ElemDoc = new();      // → Docs indeksi (0 = ana model, sonrası bağlı modeller)
    public readonly List<object> Docs = new();       // Revit belgeleri (pencere türünü bilmez; Revit tarafı kullanır)
    public readonly List<string> ElemTag = new();    // anlık etiket: kısa boyut ("300x100", "Ø50") — yoksa ""

    public int CatIndex(string name)
    {
        int i = CatNames.IndexOf(name);
        if (i < 0) { CatNames.Add(name); i = CatNames.Count - 1; }
        return i;
    }

    /// <summary>Uzunluk biçimi (feet → projenin uzunluk birimi, Revit tarafı ayarlar). Varsayılan mm.</summary>
    public System.Func<double, string> FormatLength = ft => (ft * 304.8).ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " mm";
    /// <summary>Kutu çerçevesinin dünya Z ekseni etrafındaki dönüşü (radyan): ölçüde X/Y kilidi PROJE eksenlerine göre.</summary>
    public double FrameAngle;

    public static ulong PairKey(uint a, uint b) => a < b ? ((ulong)a << 32) | b : ((ulong)b << 32) | a;

    public double[] BoxMin = new double[3], BoxMax = new double[3]; // Revit'ten okunan kutu (yerel)
    public object? Context; // Revit tarafı bağlamı (belge, görünüm, çerçeve) — pencere içeriğine dokunmaz
    public double Seconds;
    public string Timing = ""; // okuma süresinin dökümü (teşhis için, bilgi yazısının ipucunda)
    public string Source = "";
    public int ElementCount => Labels.Count;
    public int TriangleCount => (Opaque.Count + Glass.Count) / 3;
}
