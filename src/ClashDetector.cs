using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Smart3DView;

sealed class ClashResult
{
    public readonly HashSet<uint> Elements = new();
    public readonly Dictionary<uint, List<uint>> Partners = new();
    public int PairCount;
    public double Seconds;
}

/// <summary>Penceredeki üçgen ağları üzerinde çakışma denetimi (Revit API kullanmaz → bağlı modellerde de çalışır,
/// paralel ve hızlı). Yalnız FARKLI çakışma gruplarındaki elemanlar denetlenir; aynı izolasyon/taşıyıcı ve
/// connector ile birbirine bağlı elemanlar çakışma sayılmaz.</summary>
static class ClashDetector
{
    const double Tol = 0.003; // ft ≈ 1 mm: bu kadar yakın yüzeyler "dokunuyor" sayılır

    /// <param name="hidden">filtreyle gizlenen elemanlar (indeks = id) — denetime girmez</param>
    public static ClashResult Run(SceneData s, double[] boxMin, double[] boxMax, bool[]? hidden = null)
    {
        var sw = Stopwatch.StartNew();
        var res = new ClashResult();
        int n = s.Labels.Count;
        if (n == 0) return res;
        var verts = CollectionsMarshal.AsSpan(s.Vertices);
        var group = s.ElemGroup;

        // Eleman başına üçgen koordinatları (yalnız denetime giren gruplar).
        var count = new int[n + 1];
        foreach (var list in new[] { s.Opaque, s.Glass })
        {
            var idx = CollectionsMarshal.AsSpan(list);
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                uint id = verts[(int)idx[t]].Id;
                if (id > 0 && id <= n && group[(int)id - 1] != ClashGroup.None && (hidden == null || !hidden[id])) count[id]++;
            }
        }
        var start = new int[n + 2];
        for (int i = 1; i <= n; i++) start[i + 1] = start[i] + count[i];
        var tris = new double[start[n + 1] * 9];
        var fill = (int[])start.Clone();
        var min = new double[(n + 1) * 3];
        var max = new double[(n + 1) * 3];
        Array.Fill(min, double.MaxValue);
        Array.Fill(max, double.MinValue);
        foreach (var list in new[] { s.Opaque, s.Glass })
        {
            var idx = CollectionsMarshal.AsSpan(list);
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                uint id = verts[(int)idx[t]].Id;
                if (id == 0 || id > n || group[(int)id - 1] == ClashGroup.None || (hidden != null && hidden[id])) continue;
                int o = fill[id]++ * 9;
                for (int k = 0; k < 3; k++)
                {
                    var v = verts[(int)idx[t + k]];
                    tris[o + 3 * k] = v.X; tris[o + 3 * k + 1] = v.Y; tris[o + 3 * k + 2] = v.Z;
                    int b = (int)id * 3;
                    if (v.X < min[b]) min[b] = v.X; if (v.Y < min[b + 1]) min[b + 1] = v.Y; if (v.Z < min[b + 2]) min[b + 2] = v.Z;
                    if (v.X > max[b]) max[b] = v.X; if (v.Y > max[b + 1]) max[b + 1] = v.Y; if (v.Z > max[b + 2]) max[b + 2] = v.Z;
                }
            }
        }

        // Aday elemanlar: grubu olan ve geçerli kutuyla kesişenler; X'e göre sıralı süpürme.
        var cand = new List<uint>();
        for (uint id = 1; id <= n; id++)
        {
            if (count[id] == 0) continue;
            int b = (int)id * 3;
            if (max[b] < boxMin[0] || min[b] > boxMax[0] || max[b + 1] < boxMin[1] || min[b + 1] > boxMax[1]
                || max[b + 2] < boxMin[2] || min[b + 2] > boxMax[2]) continue;
            cand.Add(id);
        }
        cand.Sort((a, b) => min[a * 3].CompareTo(min[b * 3]));

        var pairs = new List<(uint, uint)>();
        for (int i = 0; i < cand.Count; i++)
        {
            uint a = cand[i];
            int ba = (int)a * 3;
            for (int j = i + 1; j < cand.Count; j++)
            {
                uint b = cand[j];
                int bb = (int)b * 3;
                if (min[bb] > max[ba] + Tol) break;
                if (min[bb + 1] > max[ba + 1] + Tol || max[bb + 1] < min[ba + 1] - Tol) continue;
                if (min[bb + 2] > max[ba + 2] + Tol || max[bb + 2] < min[ba + 2] - Tol) continue;
                if (group[(int)a - 1] == group[(int)b - 1] || Related(s, a, b)) continue;
                pairs.Add((a, b));
            }
        }

        var hits = new ConcurrentBag<(uint, uint)>();
        Parallel.ForEach(pairs, p =>
        {
            if (Clash(tris, start, min, max, p.Item1, p.Item2, boxMin, boxMax)) hits.Add(p);
        });

        foreach (var (a, b) in hits)
        {
            res.Elements.Add(a);
            res.Elements.Add(b);
            Partner(res, a, b);
            Partner(res, b, a);
        }
        res.PairCount = hits.Count;
        res.Seconds = sw.Elapsed.TotalSeconds;
        return res;
    }

    static void Partner(ClashResult r, uint a, uint b)
    {
        if (!r.Partners.TryGetValue(a, out var l)) r.Partners[a] = l = new List<uint>();
        l.Add(b);
    }

    static bool Related(SceneData s, uint a, uint b)
    {
        uint ca = s.ElemCanon[(int)a - 1], cb = s.ElemCanon[(int)b - 1];
        if (ca == cb) return true;
        var c = s.Connected;
        return c.Contains(SceneData.PairKey(a, b)) || c.Contains(SceneData.PairKey(ca, cb))
            || c.Contains(SceneData.PairKey(ca, b)) || c.Contains(SceneData.PairKey(a, cb));
    }

    static bool Clash(double[] tris, int[] start, double[] min, double[] max, uint a, uint b, double[] boxMin, double[] boxMax)
    {
        // Ortak bölge = iki eleman kutusunun kesişimi ∩ geçerli kesit kutusu (tolerans payıyla).
        Span<double> o0 = stackalloc double[3], o1 = stackalloc double[3];
        for (int k = 0; k < 3; k++)
        {
            o0[k] = Math.Max(Math.Max(min[a * 3 + k], min[b * 3 + k]) - Tol, boxMin[k]);
            o1[k] = Math.Min(Math.Min(max[a * 3 + k], max[b * 3 + k]) + Tol, boxMax[k]);
            if (o0[k] > o1[k]) return false;
        }
        var ta = TrisIn(tris, start[a], start[a + 1], o0, o1);
        var tb = TrisIn(tris, start[b], start[b + 1], o0, o1);
        foreach (int i in ta)
            foreach (int j in tb)
                if (TriTri(tris, i * 9, j * 9)) return true;

        // Dokunmadan tamamen içinde kalma (ör. kanalın içinden geçen boru).
        if (Inside(min, max, a, b) && PointInMesh(tris, start[a] * 9, tris, start[b], start[b + 1])) return true;
        if (Inside(min, max, b, a) && PointInMesh(tris, start[b] * 9, tris, start[a], start[a + 1])) return true;
        return false;
    }

    static List<int> TrisIn(double[] t, int from, int to, ReadOnlySpan<double> o0, ReadOnlySpan<double> o1)
    {
        var res = new List<int>();
        for (int i = from; i < to; i++)
        {
            int o = i * 9;
            bool hit = true;
            for (int k = 0; k < 3 && hit; k++)
            {
                double lo = Math.Min(t[o + k], Math.Min(t[o + 3 + k], t[o + 6 + k]));
                double hi = Math.Max(t[o + k], Math.Max(t[o + 3 + k], t[o + 6 + k]));
                hit = hi >= o0[k] && lo <= o1[k];
            }
            if (hit) res.Add(i);
        }
        return res;
    }

    static bool Inside(double[] min, double[] max, uint inner, uint outer)
    {
        for (int k = 0; k < 3; k++)
            if (min[inner * 3 + k] < min[outer * 3 + k] || max[inner * 3 + k] > max[outer * 3 + k]) return false;
        return true;
    }

    /// <summary>Işın tekliği: noktadan hafif eğik bir ışın, ağı tek sayıda keserse nokta içeridedir.</summary>
    static bool PointInMesh(double[] pt, int po, double[] t, int from, int to)
    {
        double px = pt[po], py = pt[po + 1], pz = pt[po + 2];
        double dx = 1, dy = 0.000173, dz = 0.000291;
        int hits = 0;
        for (int i = from; i < to; i++)
        {
            int o = i * 9;
            double e1x = t[o + 3] - t[o], e1y = t[o + 4] - t[o + 1], e1z = t[o + 5] - t[o + 2];
            double e2x = t[o + 6] - t[o], e2y = t[o + 7] - t[o + 1], e2z = t[o + 8] - t[o + 2];
            double hx = dy * e2z - dz * e2y, hy = dz * e2x - dx * e2z, hz = dx * e2y - dy * e2x;
            double det = e1x * hx + e1y * hy + e1z * hz;
            if (Math.Abs(det) < 1e-15) continue;
            double inv = 1 / det;
            double sx = px - t[o], sy = py - t[o + 1], sz = pz - t[o + 2];
            double u = (sx * hx + sy * hy + sz * hz) * inv;
            if (u < 0 || u > 1) continue;
            double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
            double v = (dx * qx + dy * qy + dz * qz) * inv;
            if (v < 0 || u + v > 1) continue;
            if ((e2x * qx + e2y * qy + e2z * qz) * inv > 1e-9) hits++;
        }
        return (hits & 1) == 1;
    }

    // ---- üçgen–üçgen kesişimi (Möller 1997, düzleme uzaklıkta Tol toleransı) ------------------------------------

    static bool TriTri(double[] t, int a, int b)
    {
        double a0x = t[a], a0y = t[a + 1], a0z = t[a + 2], a1x = t[a + 3], a1y = t[a + 4], a1z = t[a + 5], a2x = t[a + 6], a2y = t[a + 7], a2z = t[a + 8];
        double b0x = t[b], b0y = t[b + 1], b0z = t[b + 2], b1x = t[b + 3], b1y = t[b + 4], b1z = t[b + 5], b2x = t[b + 6], b2y = t[b + 7], b2z = t[b + 8];

        // A'nın düzlemi
        double e1x = a1x - a0x, e1y = a1y - a0y, e1z = a1z - a0z, e2x = a2x - a0x, e2y = a2y - a0y, e2z = a2z - a0z;
        double n1x = e1y * e2z - e1z * e2y, n1y = e1z * e2x - e1x * e2z, n1z = e1x * e2y - e1y * e2x;
        double l1 = Math.Sqrt(n1x * n1x + n1y * n1y + n1z * n1z);
        if (l1 < 1e-14) return false;
        double d1 = -(n1x * a0x + n1y * a0y + n1z * a0z);
        double du0 = Snap(n1x * b0x + n1y * b0y + n1z * b0z + d1, l1);
        double du1 = Snap(n1x * b1x + n1y * b1y + n1z * b1z + d1, l1);
        double du2 = Snap(n1x * b2x + n1y * b2y + n1z * b2z + d1, l1);
        double du0du1 = du0 * du1, du0du2 = du0 * du2;
        if (du0du1 > 0 && du0du2 > 0) return false;

        // B'nin düzlemi
        double f1x = b1x - b0x, f1y = b1y - b0y, f1z = b1z - b0z, f2x = b2x - b0x, f2y = b2y - b0y, f2z = b2z - b0z;
        double n2x = f1y * f2z - f1z * f2y, n2y = f1z * f2x - f1x * f2z, n2z = f1x * f2y - f1y * f2x;
        double l2 = Math.Sqrt(n2x * n2x + n2y * n2y + n2z * n2z);
        if (l2 < 1e-14) return false;
        double d2 = -(n2x * b0x + n2y * b0y + n2z * b0z);
        double dv0 = Snap(n2x * a0x + n2y * a0y + n2z * a0z + d2, l2);
        double dv1 = Snap(n2x * a1x + n2y * a1y + n2z * a1z + d2, l2);
        double dv2 = Snap(n2x * a2x + n2y * a2y + n2z * a2z + d2, l2);
        double dv0dv1 = dv0 * dv1, dv0dv2 = dv0 * dv2;
        if (dv0dv1 > 0 && dv0dv2 > 0) return false;

        // Kesişim doğrusu yönü; en büyük bileşene izdüşür
        double Dx = Math.Abs(n1y * n2z - n1z * n2y), Dy = Math.Abs(n1z * n2x - n1x * n2z), Dz = Math.Abs(n1x * n2y - n1y * n2x);
        int ax = Dx >= Dy && Dx >= Dz ? 0 : Dy >= Dz ? 1 : 2;
        double vp0 = ax == 0 ? a0x : ax == 1 ? a0y : a0z, vp1 = ax == 0 ? a1x : ax == 1 ? a1y : a1z, vp2 = ax == 0 ? a2x : ax == 1 ? a2y : a2z;
        double up0 = ax == 0 ? b0x : ax == 1 ? b0y : b0z, up1 = ax == 0 ? b1x : ax == 1 ? b1y : b1z, up2 = ax == 0 ? b2x : ax == 1 ? b2y : b2z;

        if (!Interval(vp0, vp1, vp2, dv0, dv1, dv2, dv0dv1, dv0dv2, out double i10, out double i11)
            || !Interval(up0, up1, up2, du0, du1, du2, du0du1, du0du2, out double i20, out double i21))
            return Coplanar(n1x, n1y, n1z, t, a, b);
        if (i10 > i11) (i10, i11) = (i11, i10);
        if (i20 > i21) (i20, i21) = (i21, i20);
        return !(i11 < i20 - Tol || i21 < i10 - Tol);
    }

    static double Snap(double d, double len) => Math.Abs(d) < Tol * len ? 0 : d;

    static bool Interval(double v0, double v1, double v2, double d0, double d1, double d2, double d0d1, double d0d2, out double i0, out double i1)
    {
        if (d0d1 > 0) { i0 = v2 + (v0 - v2) * d2 / (d2 - d0); i1 = v2 + (v1 - v2) * d2 / (d2 - d1); }
        else if (d0d2 > 0) { i0 = v1 + (v0 - v1) * d1 / (d1 - d0); i1 = v1 + (v2 - v1) * d1 / (d1 - d2); }
        else if (d1 * d2 > 0 || d0 != 0) { i0 = v0 + (v1 - v0) * d0 / (d0 - d1); i1 = v0 + (v2 - v0) * d0 / (d0 - d2); }
        else if (d1 != 0) { i0 = v1 + (v0 - v1) * d1 / (d1 - d0); i1 = v1 + (v2 - v1) * d1 / (d1 - d2); }
        else if (d2 != 0) { i0 = v2 + (v0 - v2) * d2 / (d2 - d0); i1 = v2 + (v1 - v2) * d2 / (d2 - d1); }
        else { i0 = i1 = 0; return false; } // eş düzlemli
        return true;
    }

    /// <summary>Eş düzlemli üçgenler: en büyük normal bileşenini atıp 2B'de kenar kesişimi + içerme testi.</summary>
    static bool Coplanar(double nx, double ny, double nz, double[] t, int a, int b)
    {
        nx = Math.Abs(nx); ny = Math.Abs(ny); nz = Math.Abs(nz);
        int i0, i1;
        if (nx >= ny && nx >= nz) { i0 = 1; i1 = 2; }
        else if (ny >= nz) { i0 = 0; i1 = 2; }
        else { i0 = 0; i1 = 1; }
        Span<double> A = stackalloc double[6], B = stackalloc double[6];
        for (int k = 0; k < 3; k++)
        {
            A[2 * k] = t[a + 3 * k + i0]; A[2 * k + 1] = t[a + 3 * k + i1];
            B[2 * k] = t[b + 3 * k + i0]; B[2 * k + 1] = t[b + 3 * k + i1];
        }
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (Seg(A[2 * i], A[2 * i + 1], A[(2 * i + 2) % 6], A[(2 * i + 3) % 6],
                        B[2 * j], B[2 * j + 1], B[(2 * j + 2) % 6], B[(2 * j + 3) % 6])) return true;
        return InTri(A[0], A[1], B) || InTri(B[0], B[1], A);
    }

    static double Orient(double ax, double ay, double bx, double by, double cx, double cy) =>
        (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    static bool Seg(double p1x, double p1y, double p2x, double p2y, double q1x, double q1y, double q2x, double q2y)
    {
        double o1 = Orient(p1x, p1y, p2x, p2y, q1x, q1y), o2 = Orient(p1x, p1y, p2x, p2y, q2x, q2y);
        double o3 = Orient(q1x, q1y, q2x, q2y, p1x, p1y), o4 = Orient(q1x, q1y, q2x, q2y, p2x, p2y);
        return o1 * o2 <= 0 && o3 * o4 <= 0
            && Math.Max(p1x, p2x) >= Math.Min(q1x, q2x) - Tol && Math.Max(q1x, q2x) >= Math.Min(p1x, p2x) - Tol
            && Math.Max(p1y, p2y) >= Math.Min(q1y, q2y) - Tol && Math.Max(q1y, q2y) >= Math.Min(p1y, p2y) - Tol;
    }

    static bool InTri(double px, double py, ReadOnlySpan<double> T)
    {
        double d1 = Orient(T[0], T[1], T[2], T[3], px, py);
        double d2 = Orient(T[2], T[3], T[4], T[5], px, py);
        double d3 = Orient(T[4], T[5], T[0], T[1], px, py);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}
