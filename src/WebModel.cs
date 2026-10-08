using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Smart3DView;

// Tüm model aktarımının ara verisi (Revit'e bağlı değildir). Revit'in kendi aktarıcısından (CustomExporter) gelen hazır
// üçgenler buraya yazılır; aynı aile geometrisi bir kez saklanır (instancing), her eleman o geometrilere konumla bağlanır.
// Bağlı modeller ayrı "parça" olarak tutulur → sürüm kimliğiyle önbelleğe alınır (değişmeyen link yeniden okunmaz).
// Koordinat: Revit (ft, Z yukarı), parçanın kendi koordinatı. glTF'e (m, Y yukarı) yazarken çevrilir.
// Kullanıcı isteği 2026-10-08: "hem export yavaş hem web kasıyor" — Navisworks NWC'nin yaptığı gibi.

sealed class WebMesh
{
    public float R = 0.8f, G = 0.8f, B = 0.8f, A = 1f;   // Revit malzeme rengi (A < 1 → saydam)
    public float[] P = Array.Empty<float>();              // x,y,z …
    public float[] N = Array.Empty<float>();              // köşe normalleri
    public int[] I = Array.Empty<int>();                  // üçgen indisleri
}

sealed class WebGeom
{
    public int Mesh;           // parçanın Meshes listesinde
    public double[]? M;        // Revit dönüşümü: bx(3) by(3) bz(3) origin(3); null = birim
}

sealed class WebElement
{
    public string Name = "";
    public readonly Dictionary<string, object> Extras = new();
    public readonly List<WebGeom> Geoms = new();
}

sealed class WebPart
{
    public string Name = "";
    public readonly List<WebMesh> Meshes = new();
    public readonly List<WebElement> Elements = new();

    // ---- önbellek dosyası (ikili) --------------------------------------------------------------------------------

    const int Magic = 0x50443353;   // "S3DP"
    const int Version = 1;

    public void Save(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Magic); w.Write(Version); w.Write(Name);
        w.Write(Meshes.Count);
        foreach (var m in Meshes)
        {
            w.Write(m.R); w.Write(m.G); w.Write(m.B); w.Write(m.A);
            W(w, m.P); W(w, m.N);
            w.Write(m.I.Length); foreach (var i in m.I) w.Write(i);
        }
        w.Write(Elements.Count);
        foreach (var e in Elements)
        {
            w.Write(e.Name);
            w.Write(e.Extras.Count);
            foreach (var (k, v) in e.Extras)
            {
                w.Write(k);
                switch (v)
                {
                    case long l: w.Write((byte)1); w.Write(l); break;
                    case int i: w.Write((byte)2); w.Write(i); break;
                    case double d: w.Write((byte)3); w.Write(d); break;
                    default: w.Write((byte)0); w.Write(v?.ToString() ?? ""); break;
                }
            }
            w.Write(e.Geoms.Count);
            foreach (var g in e.Geoms)
            {
                w.Write(g.Mesh);
                w.Write(g.M != null);
                if (g.M != null) foreach (var x in g.M) w.Write(x);
            }
        }
    }

    public static WebPart? Load(string path)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version) return null;
            var p = new WebPart { Name = r.ReadString() };
            int nm = r.ReadInt32();
            for (int i = 0; i < nm; i++)
            {
                var m = new WebMesh { R = r.ReadSingle(), G = r.ReadSingle(), B = r.ReadSingle(), A = r.ReadSingle() };
                m.P = RF(r); m.N = RF(r);
                m.I = new int[r.ReadInt32()];
                for (int k = 0; k < m.I.Length; k++) m.I[k] = r.ReadInt32();
                p.Meshes.Add(m);
            }
            int ne = r.ReadInt32();
            for (int i = 0; i < ne; i++)
            {
                var e = new WebElement { Name = r.ReadString() };
                int nx = r.ReadInt32();
                for (int k = 0; k < nx; k++)
                {
                    string key = r.ReadString();
                    e.Extras[key] = r.ReadByte() switch
                    {
                        1 => r.ReadInt64(),
                        2 => r.ReadInt32(),
                        3 => r.ReadDouble(),
                        _ => r.ReadString(),
                    };
                }
                int ng = r.ReadInt32();
                for (int k = 0; k < ng; k++)
                {
                    var g = new WebGeom { Mesh = r.ReadInt32() };
                    if (r.ReadBoolean()) { g.M = new double[12]; for (int j = 0; j < 12; j++) g.M[j] = r.ReadDouble(); }
                    e.Geoms.Add(g);
                }
                p.Elements.Add(e);
            }
            return p;
        }
        catch { return null; }
    }

    static void W(BinaryWriter w, float[] a) { w.Write(a.Length); foreach (var x in a) w.Write(x); }
    static float[] RF(BinaryReader r) { var a = new float[r.ReadInt32()]; for (int i = 0; i < a.Length; i++) a[i] = r.ReadSingle(); return a; }
}

sealed class WebModel
{
    public string Name = "";
    public readonly List<WebPart> Parts = new();
    public readonly List<(int Part, double[]? M)> Placements = new();   // parça hangi konumda (bağlı model örneği)
    public readonly Dictionary<string, object> RootExtras = new();

    public int ElementCount
    {
        get { int n = 0; foreach (var (p, _) in Placements) n += Parts[p].Elements.Count; return n; }
    }
}

/// <summary>Bağlı model önbelleği: %LOCALAPPDATA%\Smart3DView\cache\&lt;model&gt;_&lt;sürüm&gt;.s3dp.</summary>
static class WebPartCache
{
    public static string Folder
    {
        get
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name, "cache");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    public static string PathFor(string key)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in key) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
        return Path.Combine(Folder, sb + ".s3dp");
    }
}

/// <summary>WebModel → glTF 2.0 ikili (.glb). Aynı geometriyi kullanan elemanlar aynı glTF mesh'ine bağlanır.</summary>
static class WebModelGlb
{
    const float Ft = 0.3048f;

    public static void Write(WebModel model, string path)
    {
        var bin = new MemoryStream();
        var bw = new BinaryWriter(bin);
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var meshes = new List<object>();
        var nodes = new List<object>();
        var materials = new List<object>();
        var matIndex = new Dictionary<(float, float, float, float), int>();

        int Mat(WebMesh m)
        {
            var key = (MathF.Round(m.R, 3), MathF.Round(m.G, 3), MathF.Round(m.B, 3), MathF.Round(m.A, 3));
            if (matIndex.TryGetValue(key, out int i)) return i;
            materials.Add(m.A < 0.999f
                ? new { pbrMetallicRoughness = new { baseColorFactor = new[] { m.R, m.G, m.B, m.A }, metallicFactor = 0f, roughnessFactor = 0.3f }, alphaMode = "BLEND", doubleSided = true }
                : (object)new { pbrMetallicRoughness = new { baseColorFactor = new[] { m.R, m.G, m.B, 1f }, metallicFactor = 0f, roughnessFactor = 0.85f }, doubleSided = true });
            return matIndex[key] = materials.Count - 1;
        }
        void Align() { while (bin.Length % 4 != 0) bw.Write((byte)0); }
        int View(long off, long len, int target) { bufferViews.Add(new { buffer = 0, byteOffset = off, byteLength = len, target }); return bufferViews.Count - 1; }

        // Parça mesh'leri → glTF mesh (parça başına indis eşlemesi).
        var meshMap = new List<int[]>();
        foreach (var part in model.Parts)
        {
            var map = new int[part.Meshes.Count];
            for (int mi = 0; mi < part.Meshes.Count; mi++)
            {
                var m = part.Meshes[mi];
                int nv = m.P.Length / 3;
                if (nv == 0 || m.I.Length == 0) { map[mi] = -1; continue; }
                float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
                Align();
                long pOff = bin.Length;
                for (int v = 0; v < nv; v++)
                {
                    float x = m.P[3 * v] * Ft, y = m.P[3 * v + 2] * Ft, z = -m.P[3 * v + 1] * Ft;
                    bw.Write(x); bw.Write(y); bw.Write(z);
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
                    x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
                }
                int pv = View(pOff, bin.Length - pOff, 34962);
                int nAcc = -1;
                if (m.N.Length == m.P.Length)
                {
                    long nOff = bin.Length;
                    for (int v = 0; v < nv; v++) { bw.Write(m.N[3 * v]); bw.Write(m.N[3 * v + 2]); bw.Write(-m.N[3 * v + 1]); }
                    int nvw = View(nOff, bin.Length - nOff, 34962);
                    accessors.Add(new { bufferView = nvw, componentType = 5126, count = nv, type = "VEC3" });
                    nAcc = accessors.Count - 1;
                }
                long iOff = bin.Length;
                foreach (var i in m.I) bw.Write((uint)i);
                int iv = View(iOff, bin.Length - iOff, 34963);
                accessors.Add(new { bufferView = pv, componentType = 5126, count = nv, type = "VEC3", min = new[] { x0, y0, z0 }, max = new[] { x1, y1, z1 } });
                int pAcc = accessors.Count - 1;
                accessors.Add(new { bufferView = iv, componentType = 5125, count = m.I.Length, type = "SCALAR" });
                int iAcc = accessors.Count - 1;
                var attrs = new Dictionary<string, int> { ["POSITION"] = pAcc };
                if (nAcc >= 0) attrs["NORMAL"] = nAcc;
                meshes.Add(new { primitives = new[] { new { attributes = attrs, indices = iAcc, material = Mat(m) } } });
                map[mi] = meshes.Count - 1;
            }
            meshMap.Add(map);
        }

        // Düğümler: kök → yerleşim (bağlı model örneği) → eleman → geometri.
        var placementNodes = new List<int>();
        foreach (var (pi, pm) in model.Placements)
        {
            var part = model.Parts[pi];
            var elemNodes = new List<int>();
            foreach (var e in part.Elements)
            {
                var geoms = e.Geoms.FindAll(g => g.Mesh >= 0 && g.Mesh < meshMap[pi].Length && meshMap[pi][g.Mesh] >= 0);
                if (geoms.Count == 0) continue;
                var extras = new Dictionary<string, object>(e.Extras) { ["model"] = part.Name };
                if (geoms.Count == 1 && geoms[0].M == null)
                {
                    nodes.Add(new { name = e.Name, mesh = meshMap[pi][geoms[0].Mesh], extras });
                }
                else
                {
                    var kids = new List<int>();
                    foreach (var g in geoms)
                    {
                        nodes.Add(g.M == null ? new { mesh = meshMap[pi][g.Mesh] } : (object)new { mesh = meshMap[pi][g.Mesh], matrix = Matrix(g.M) });
                        kids.Add(nodes.Count - 1);
                    }
                    nodes.Add(new { name = e.Name, children = kids, extras });
                }
                elemNodes.Add(nodes.Count - 1);
            }
            if (elemNodes.Count == 0) continue;
            nodes.Add(pm == null ? new { name = part.Name, children = elemNodes } : (object)new { name = part.Name, children = elemNodes, matrix = Matrix(pm) });
            placementNodes.Add(nodes.Count - 1);
        }
        var models = new List<string>();
        foreach (var p in model.Parts) if (!models.Contains(p.Name)) models.Add(p.Name);
        var rootExtras = new Dictionary<string, object>(model.RootExtras) { ["models"] = models };
        nodes.Add(new { name = model.Name, children = placementNodes, extras = rootExtras });
        int root = nodes.Count - 1;
        Align();

        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "Smart3DView " + AddinVersion.Version },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = new[] { root } } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new[] { new { byteLength = bin.Length } },
        };
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gltf));
        int jsonPad = (4 - json.Length % 4) % 4;
        using var f = File.Create(path);
        using var w = new BinaryWriter(f);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + json.Length + jsonPad + 8 + bin.Length));
        w.Write((uint)(json.Length + jsonPad)); w.Write(0x4E4F534Au);
        w.Write(json); for (int i = 0; i < jsonPad; i++) w.Write((byte)0x20);
        w.Write((uint)bin.Length); w.Write(0x004E4942u);
        w.Flush();
        bin.Position = 0; bin.CopyTo(f);
    }

    /// <summary>Revit dönüşümü (ft, Z yukarı) → glTF matrisi (m, Y yukarı), sütun öncelikli: M' = C·M·C⁻¹,
    /// C = 0,3048 × (x, z, −y).</summary>
    static float[] Matrix(double[] m)
    {
        // L: sütunları bx, by, bz (Revit). A: (x,y,z) → (x,z,−y); A⁻¹: (x,y,z) → (x,−z,y).
        static (double, double, double) A(double x, double y, double z) => (x, z, -y);
        static (double, double, double) Ainv(double x, double y, double z) => (x, -z, y);
        (double, double, double) L(double x, double y, double z) =>
            (m[0] * x + m[3] * y + m[6] * z, m[1] * x + m[4] * y + m[7] * z, m[2] * x + m[5] * y + m[8] * z);
        var r = new float[16];
        for (int c = 0; c < 3; c++)
        {
            var e = (c == 0 ? 1.0 : 0, c == 1 ? 1.0 : 0, c == 2 ? 1.0 : 0);
            var a = Ainv(e.Item1, e.Item2, e.Item3);
            var l = L(a.Item1, a.Item2, a.Item3);
            var g = A(l.Item1, l.Item2, l.Item3);
            r[4 * c] = (float)g.Item1; r[4 * c + 1] = (float)g.Item2; r[4 * c + 2] = (float)g.Item3;
        }
        var t = A(m[9], m[10], m[11]);
        r[12] = (float)t.Item1 * Ft; r[13] = (float)t.Item2 * Ft; r[14] = (float)t.Item3 * Ft; r[15] = 1;
        return r;
    }
}
