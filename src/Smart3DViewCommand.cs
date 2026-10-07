using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Smart3DView;

[Transaction(TransactionMode.Manual)]   // geçici Fine görünüm (kablo tavaları) için işlem açılıp geri alınır
public class Smart3DViewCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var uidoc = data.Application.ActiveUIDocument;
        if (uidoc == null) return Result.Cancelled;

        ClipBox? box;
        try { box = BoxResolver.Resolve(uidoc); }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

        if (box == null)
        {
            TaskDialog.Show(Product.Name, L.T(
                "Önce eleman seçin, 3B görünümde kesit kutusunu açın ya da plan görünümünde düğmeye basıp bir alan çizin.",
                "Select elements first, enable the section box in a 3D view, or press the button in a plan view and draw an area."));
            return Result.Cancelled;
        }

        var scene = SceneCollector.Collect(uidoc.Document, uidoc.ActiveView, box);
        scene.Context = new RevitContext(uidoc.Document, uidoc.ActiveView.Id, box);
        if (scene.TriangleCount == 0)
        {
            TaskDialog.Show(Product.Name, L.T("Kutu içinde 3B geometri bulunamadı.", "No 3D geometry found inside the box."));
            return Result.Cancelled;
        }

        ViewerWindow.Present(scene, data.Application.MainWindowHandle, uidoc.Document.Title, RevitHost.Instance);
        return Result.Succeeded;
    }
}

/// <summary>Kırpma kutusu: <see cref="Frame"/> içinde Min–Max. Çerçevenin başlangıcı ilk kutunun merkezi (GPU'da küçük
/// koordinatlar), eksenleri kutuyla hizalı, yalnız Z etrafında döner. Pencerede kutu büyütülünce aynı çerçeve korunur.</summary>
sealed class ClipBox
{
    public Transform Frame = Transform.Identity;
    public XYZ Min = XYZ.Zero, Max = XYZ.Zero;
    public string Source = "";
}

static class BoxResolver
{
    const double Margin = 1.0; // ft ≈ 30 cm — seçimin etrafında bırakılan pay

    /// <summary>Öncelik: seçim → 3B görünümün kesit kutusu → planda çizilen alan. ESC'de OperationCanceledException fırlar.</summary>
    public static ClipBox? Resolve(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;

        var picked = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null).ToList();
        if (picked.Count > 0)
        {
            // Bağlı model örneği tüm binayı kapsar; başka seçim varsa onu kutuya katma.
            var core = picked.Where(e => e is not RevitLinkInstance).ToList();
            if (core.Count == 0) core = picked;
            var min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue);
            var max = new XYZ(double.MinValue, double.MinValue, double.MinValue);
            bool any = false;
            foreach (var e in core)
            {
                var bb = e.get_BoundingBox(null);
                if (bb == null) continue;
                foreach (var c in Corners(bb.Min, bb.Max))
                {
                    var p = bb.Transform.OfPoint(c);
                    min = new XYZ(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                    max = new XYZ(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                }
                any = true;
            }
            if (any)
            {
                var m = new XYZ(Margin, Margin, Margin);
                return FromLocal(Transform.Identity, min - m, max + m,
                    L.T($"{core.Count} seçili eleman", $"{core.Count} selected element(s)"));
            }
        }

        if (view is View3D v3 && v3.IsSectionBoxActive)
        {
            var sb = v3.GetSectionBox();
            return FromLocal(sb.Transform, sb.Min, sb.Max, L.T("3B kesit kutusu", "3D section box"));
        }

        if (view is ViewPlan plan)
        {
            var pb = uidoc.Selection.PickBox(PickBoxStyle.Crossing,
                Product.Name + L.T(": açılacak alanı çizin", ": drag the area to open"));
            // Görünüm döndürülmüş olabilir: dikdörtgen ekrana hizalı, o yüzden görünümün sağ/yukarı eksenlerinde çalış.
            var right = new XYZ(view.RightDirection.X, view.RightDirection.Y, 0).Normalize();
            var up = XYZ.BasisZ.CrossProduct(right);
            var frame = Transform.Identity;
            frame.BasisX = right; frame.BasisY = up; frame.BasisZ = XYZ.BasisZ;
            var inv = frame.Inverse;
            var a = inv.OfPoint(pb.Min);
            var b = inv.OfPoint(pb.Max);
            var (z0, z1) = PlanZRange(doc, plan);
            return FromLocal(frame,
                new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), z0),
                new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), z1),
                L.T("plan alanı", "plan area"));
        }

        return null;
    }

    static ClipBox FromLocal(Transform frame, XYZ min, XYZ max, string source)
    {
        var center = (min + max) * 0.5;
        var half = (max - min) * 0.5;
        half = new XYZ(Math.Max(half.X, 0.1), Math.Max(half.Y, 0.1), Math.Max(half.Z, 0.1));
        return new ClipBox
        {
            Frame = frame.Multiply(Transform.CreateTranslation(center)),
            Min = -half,
            Max = half,
            Source = source,
        };
    }

    /// <summary>Plan görünümünün görünüm aralığından (görünüm derinliği → üst) Z aralığı.</summary>
    static (double, double) PlanZRange(Document doc, ViewPlan plan)
    {
        double baseZ = plan.GenLevel?.ProjectElevation ?? 0;
        var zs = new List<double>();
        try
        {
            var vr = plan.GetViewRange();
            foreach (var p in new[] { PlanViewPlane.ViewDepthPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane })
            {
                if (doc.GetElement(vr.GetLevelId(p)) is Level lv) zs.Add(lv.ProjectElevation + vr.GetOffset(p));
            }
        }
        catch { /* bazı plan türlerinde görünüm aralığı yok */ }
        double z0 = zs.Count > 0 ? zs.Min() : baseZ - 1;
        double z1 = zs.Count > 0 ? zs.Max() : baseZ + 13;
        if (z1 - z0 < 3) z1 = z0 + 13; // üst "sınırsız" ise ≈ 4 m kat yüksekliği
        return (z0, z1);
    }

    public static IEnumerable<XYZ> Corners(XYZ a, XYZ b)
    {
        for (int i = 0; i < 8; i++)
            yield return new XYZ((i & 1) == 0 ? a.X : b.X, (i & 2) == 0 ? a.Y : b.Y, (i & 4) == 0 ? a.Z : b.Z);
    }
}
