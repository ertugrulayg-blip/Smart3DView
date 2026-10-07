using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Smart3DView;

/// <summary>Bir sahnenin Revit tarafı: hangi belge/görünüm/çerçeveden okunduğu (yeniden okuma ve görüntü kaydı için).</summary>
sealed class RevitContext
{
    public readonly Document Doc;
    public readonly ElementId ViewId;
    public readonly ClipBox Box;
    public RevitContext(Document doc, ElementId viewId, ClipBox box) { Doc = doc; ViewId = viewId; Box = box; }
}

/// <summary>Modeless pencere Revit API'sini doğrudan çağıramaz; istekler kuyruğa alınır ve Revit uygun olduğunda
/// ExternalEvent ile ana iş parçacığında çalıştırılır. Olay App.OnStartup'ta bir kez oluşturulur (yerel değişkende
/// tutulan ExternalEvent toplanıp hiç çalışmayabiliyor).</summary>
sealed class RevitBridge : IExternalEventHandler
{
    static readonly Queue<Action<UIApplication>> Pending = new();
    public static ExternalEvent? Event;

    public static void Post(Action<UIApplication> action)
    {
        Pending.Enqueue(action);
        Event?.Raise();
    }

    public void Execute(UIApplication app)
    {
        while (Pending.Count > 0)
        {
            var a = Pending.Dequeue();
            try { a(app); }
            catch (Exception ex) { TaskDialog.Show(Product.Name, ex.Message); }
        }
    }

    public string GetName() => Product.Name;
}

sealed class RevitHost : IViewerHost
{
    public static readonly RevitHost Instance = new();

    public void Recollect(object context, double[] min, double[] max, Action<SceneData?, string?> done)
    {
        var ctx = (RevitContext)context;
        RevitBridge.Post(_ =>
        {
            if (!ctx.Doc.IsValidObject) { done(null, L.T("Model kapatılmış.", "The model has been closed.")); return; }
            var box = new ClipBox
            {
                Frame = ctx.Box.Frame,
                Min = new XYZ(min[0], min[1], min[2]),
                Max = new XYZ(max[0], max[1], max[2]),
                Source = ctx.Box.Source,
            };
            var view = ctx.Doc.GetElement(ctx.ViewId) as View;
            var scene = SceneCollector.Collect(ctx.Doc, view, box);
            scene.Context = new RevitContext(ctx.Doc, ctx.ViewId, box);
            done(scene, null);
        });
    }


    public bool IsFromDoc(object context, object doc) => context is RevitContext c && doc is Document d && c.Doc.IsValidObject && c.Doc.Equals(d);
    public void SaveImage(object context, string pngPath, Action<string> done)
    {
        var ctx = (RevitContext)context;
        RevitBridge.Post(_ =>
        {
            if (!ctx.Doc.IsValidObject) { done(L.T("Model kapatılmış.", "The model has been closed.")); return; }
            string name = $"{Product.Name} - {DateTime.Now:yyyy-MM-dd HH.mm.ss}";
            using var t = new Transaction(ctx.Doc, Product.Name + L.T(" görüntü", " image"));
            t.Start();
            // Render penceresindeki "Save to Project" ile aynı tür: Proje Tarayıcısı'nda Renderings altında görünür.
            var view = ImageView.Create(ctx.Doc, new ImageTypeOptions(pngPath, false, ImageTypeSource.Import));
            try { view.Name = name; } catch { /* ad çakışırsa Revit'in verdiği ad kalsın */ }
            t.Commit();
            done(L.T($"Görüntü kaydedildi: Proje Tarayıcısı → Renderings → \"{view.Name}\"",
                     $"Image saved: Project Browser → Renderings → \"{view.Name}\""));
        });
    }
}
