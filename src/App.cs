using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace Smart3DView;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        var panel = app.CreateRibbonPanel(Product.Name);
        string dll = Assembly.GetExecutingAssembly().Location;
        var button = new PushButtonData("Smart3DView", "Smart\n3D View", dll, typeof(Smart3DViewCommand).FullName)
        {
            ToolTip = L.T(
                "Seçili elemanların (ya da 3B kesit kutusunun / kesit görünümünün / planda çizilen alanın) çevresini ayrı bir pencerede gri tonlu 3B olarak açar.",
                "Opens the area around the selected elements (or the 3D section box / the section view / a box drawn in plan) as a grayscale 3D view in a separate window.")
                + "\n\n" + L.T("Sürüm: ", "Version: ") + AddinVersion.Text,
            LargeImage = Icons.Cube(32),
            Image = Icons.Cube(16),
        };
        // F1: Revit, düğmenin üzerindeyken çevrimiçi hızlı başlangıç sayfasını açar (Autodesk yönergesi: contextual help).
        button.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, Help.OnlineWithLang));
        panel.AddItem(button);
        // Tüm model → .glb (standart glTF; bilgisayarda kalır, Smart3DView Web ya da herhangi bir glTF görüntüleyicide açılır).
        // Seçilen bağlı modellerle, dilim dilim. 2026-10-30: "Web'e aktar" adı "GLB'ye aktar" oldu, mağaza paketine de girdi.
        var web = new PushButtonData("Smart3DViewWeb", L.T("GLB'ye\naktar", "Export\nGLB"), dll, typeof(WebExportCommand).FullName)
        {
            ToolTip = L.T(
                "Tüm modeli (seçtiğiniz bağlı modellerle) standart 3B formatta tek bir .glb (glTF) dosyasına aktarır. Dosya bilgisayarınızda kalır; herhangi bir glTF görüntüleyicide ya da tarayıcıda Smart3DView Web ile açılır. Okuma sırasında Revit kullanılabilir, Esc ile iptal edilir.",
                "Exports the whole model (with the linked models you choose) to a single .glb (glTF) file, a standard 3D format. The file stays on your computer; open it in any glTF viewer or in the browser with Smart3DView Web. Revit stays usable while reading; Esc cancels.")
                + "\n\n" + L.T("Sürüm: ", "Version: ") + AddinVersion.Text,
            LargeImage = Icons.Globe(32),
            Image = Icons.Globe(16),
        };
        web.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, Help.OnlineWithLang));
        panel.AddItem(web);
        RevitBridge.Event = ExternalEvent.Create(new RevitBridge());
        // Model değişince açık pencere "Yenile" düğmesini uyarır. Kendi işlemlerimiz (Görüntü al → ImageView) sayılmaz.
        app.ControlledApplication.DocumentChanged += (_, e) =>
        {
            if (e.GetTransactionNames().Any(n => n.StartsWith(Product.Name))) return;
            // Yenile yalnız bunları yeniden okur (silinenler sahneden çıkar).
            var ids = new List<long>();
            foreach (var id in e.GetAddedElementIds()) ids.Add(id.Value);
            foreach (var id in e.GetModifiedElementIds()) ids.Add(id.Value);
            foreach (var id in e.GetDeletedElementIds()) ids.Add(id.Value);
            ModelWatch.Raise(e.GetDocument(), ids);
        };
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}

static class Icons
{
    /// <summary>Gri tonlu izometrik küp + etrafında kesik çizgili kutu.</summary>
    public static ImageSource Cube(int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = px / 32.0;
            Point P(double x, double y) => new(x * s, y * s);
            var top = Poly(P(16, 6), P(26, 11), P(16, 16), P(6, 11));
            var left = Poly(P(6, 11), P(16, 16), P(16, 27), P(6, 22));
            var right = Poly(P(16, 16), P(26, 11), P(26, 22), P(16, 27));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), System.Math.Max(1, 1.2 * s)) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)), pen, top);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xB4)), pen, left);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x72, 0x72, 0x72)), pen, right);
            if (px >= 24)
            {
                var dash = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
                dc.DrawRectangle(null, dash, new Rect(P(2.5, 2.5), P(29.5, 29.5)));
            }
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Küre (enlem/boylam çizgileri) — "GLB'ye aktar".</summary>
    public static ImageSource Globe(int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = px / 32.0;
            var c = new Point(16 * s, 16 * s);
            double r = 12.5 * s;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0x89)), System.Math.Max(1, 1.4 * s));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xDC, 0xEB, 0xFA)), pen, c, r, r);
            dc.DrawEllipse(null, pen, c, r * 0.45, r);
            dc.DrawLine(pen, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));
            dc.DrawLine(pen, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
            if (px >= 24)
            {
                double y = r * 0.55, x = System.Math.Sqrt(r * r - y * y);
                dc.DrawLine(pen, new Point(c.X - x, c.Y - y), new Point(c.X + x, c.Y - y));
                dc.DrawLine(pen, new Point(c.X - x, c.Y + y), new Point(c.X + x, c.Y + y));
            }
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    static Geometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i], true, true);
        }
        g.Freeze();
        return g;
    }
}
