using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Smart3DView;

/// <summary>Sahneyi glTF 2.0 ikili (.glb) dosyasına yazar — Smart3DView Web görüntüleyicisi için (kullanıcı isteği
/// 2026-10-08: model kullanıcının bilgisayarında kalır, tarayıcı yerelde açar). Revit'e bağlı değildir.
/// Her eleman bir düğüm: adı etiket, "extras" içinde kategori, Revit ID, model ve renk sınıfı. Malzeme = "Detaylı"
/// ton rengi (görüntüleyicide "Orijinal renkler"). Koordinat: Revit (ft, Z yukarı) → glTF (m, Y yukarı).</summary>
static class GlbWriter
{
    const float Ft = 0.3048f;

    /// <param name="include">Verilirse yalnız true dönen elemanlar yazılır (ör. pencerede gizlenen modeller atlanır).</param>
    /// <param name="boxMin">Pencerenin kesit kutusu (sahne koordinatı, ft); verilirse kök düğümün extras.sectionBox'ına
    /// glTF koordinatında yazılır → web görüntüleyici aynı yerden keser (kullanıcı isteği 2026-10-08).</param>
    public static void Write(SceneData s, string path, Func<int, bool>? include = null, double[]? boxMin = null, double[]? boxMax = null)
    {
        int n = s.Labels.Count;
        // Eleman başına üçgen listeleri (opak / cam ayrı ilkel).
        var opaque = new List<uint>[n];
        var glass = new List<uint>[n];
        void Bucket(List<uint> src, List<uint>[] dst)
        {
            for (int t = 0; t + 2 < src.Count; t += 3)
            {
                uint id = s.Vertices[(int)src[t]].Id;
                if (id == 0 || id > n) continue;
                (dst[id - 1] ??= new List<uint>()).Add(src[t]);
                dst[id - 1].Add(src[t + 1]);
                dst[id - 1].Add(src[t + 2]);
            }
        }
        Bucket(s.Opaque, opaque);
        Bucket(s.Glass, glass);

        var bin = new MemoryStream();
        var bw = new BinaryWriter(bin);
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var meshes = new List<object>();
        var nodes = new List<object>();
        var children = new List<int>();

        // Malzemeler: kullanılan her renk sınıfı bir kez + cam.
        var materials = new List<object>();
        var detailMat = new Dictionary<int, int>();
        int glassMat = -1;
        int MatFor(int det)
        {
            if (detailMat.TryGetValue(det, out int m)) return m;
            var c = GlView.DetailRgb(det);
            materials.Add(new
            {
                name = "detail-" + det,
                pbrMetallicRoughness = new { baseColorFactor = new[] { c.r, c.g, c.b, 1f }, metallicFactor = 0f, roughnessFactor = 0.85f },
                doubleSided = true,
            });
            return detailMat[det] = materials.Count - 1;
        }
        int GlassMat()
        {
            if (glassMat >= 0) return glassMat;
            materials.Add(new
            {
                name = "glass",
                pbrMetallicRoughness = new { baseColorFactor = new[] { 0.62f, 0.72f, 0.80f, 0.35f }, metallicFactor = 0f, roughnessFactor = 0.2f },
                alphaMode = "BLEND",
                doubleSided = true,
            });
            return glassMat = materials.Count - 1;
        }

        int View(long offset, long length, int? target)
        {
            bufferViews.Add(target is { } tg ? new { buffer = 0, byteOffset = offset, byteLength = length, target = tg }
                                             : (object)new { buffer = 0, byteOffset = offset, byteLength = length });
            return bufferViews.Count - 1;
        }
        void Align() { while (bin.Length % 4 != 0) bw.Write((byte)0); }

        /// <summary>Bir ilkelin üçgenleri: köşeler yeniden numaralanır, konum + normal + indis yazılır.</summary>
        object Primitive(List<uint> tris, int material)
        {
            var remap = new Dictionary<uint, uint>();
            var order = new List<uint>();
            var idx = new uint[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                if (!remap.TryGetValue(tris[i], out uint k)) { k = (uint)order.Count; remap[tris[i]] = k; order.Add(tris[i]); }
                idx[i] = k;
            }
            float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
            Align();
            long pOff = bin.Length;
            foreach (var vi in order)
            {
                var v = s.Vertices[(int)vi];
                float x = v.X * Ft, y = v.Z * Ft, z = -v.Y * Ft;
                bw.Write(x); bw.Write(y); bw.Write(z);
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
                x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
            }
            int pView = View(pOff, bin.Length - pOff, 34962);
            long nOff = bin.Length;
            foreach (var vi in order)
            {
                var v = s.Vertices[(int)vi];
                float nx = v.NX / 32767f, ny = v.NZ / 32767f, nz = -v.NY / 32767f;
                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-6f) { nx = 0; ny = 1; nz = 0; len = 1; }
                bw.Write(nx / len); bw.Write(ny / len); bw.Write(nz / len);
            }
            int nView = View(nOff, bin.Length - nOff, 34962);
            long iOff = bin.Length;
            foreach (var i in idx) bw.Write(i);
            int iView = View(iOff, bin.Length - iOff, 34963);

            accessors.Add(new { bufferView = pView, componentType = 5126, count = order.Count, type = "VEC3", min = new[] { x0, y0, z0 }, max = new[] { x1, y1, z1 } });
            int pAcc = accessors.Count - 1;
            accessors.Add(new { bufferView = nView, componentType = 5126, count = order.Count, type = "VEC3" });
            int nAcc = accessors.Count - 1;
            accessors.Add(new { bufferView = iView, componentType = 5125, count = idx.Length, type = "SCALAR" });
            int iAcc = accessors.Count - 1;
            return new { attributes = new Dictionary<string, int> { ["POSITION"] = pAcc, ["NORMAL"] = nAcc }, indices = iAcc, material };
        }

        for (int e = 0; e < n; e++)
        {
            if (include != null && !include(e)) continue;
            var prims = new List<object>();
            int det = e < s.ElemDetail.Count ? s.ElemDetail[e] : 0;
            if (opaque[e] is { Count: > 0 } o) prims.Add(Primitive(o, MatFor(det)));
            if (glass[e] is { Count: > 0 } g) prims.Add(Primitive(g, GlassMat()));
            if (prims.Count == 0) continue;
            meshes.Add(new { primitives = prims });
            var extras = new Dictionary<string, object>
            {
                ["category"] = e < s.ElemCat.Count && s.ElemCat[e] < s.CatNames.Count ? s.CatNames[s.ElemCat[e]] : "",
                ["model"] = e < s.ElemDoc.Count && s.ElemDoc[e] < s.DocNames.Count ? s.DocNames[s.ElemDoc[e]] : "",
                ["detail"] = det,
            };
            if (e < s.ElemRevitId.Count) extras["revitId"] = s.ElemRevitId[e];
            if (e < s.ElemUid.Count) extras["uniqueId"] = s.ElemUid[e];
            nodes.Add(new { name = s.Labels[e], mesh = meshes.Count - 1, extras });
            children.Add(nodes.Count - 1);
        }
        var rootExtras = new Dictionary<string, object> { ["models"] = s.DocNames };
        if (boxMin != null && boxMax != null)
            rootExtras["sectionBox"] = new
            {
                min = new[] { (float)boxMin[0] * Ft, (float)boxMin[2] * Ft, -(float)boxMax[1] * Ft },
                max = new[] { (float)boxMax[0] * Ft, (float)boxMax[2] * Ft, -(float)boxMin[1] * Ft },
            };
        nodes.Add(new { name = s.Source, children, extras = rootExtras });
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
        long total = 12 + 8 + json.Length + jsonPad + 8 + bin.Length;
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
        w.Write((uint)(json.Length + jsonPad)); w.Write(0x4E4F534Au);
        w.Write(json); for (int i = 0; i < jsonPad; i++) w.Write((byte)0x20);
        w.Write((uint)bin.Length); w.Write(0x004E4942u);
        w.Flush();
        bin.Position = 0; bin.CopyTo(f);
    }
}
