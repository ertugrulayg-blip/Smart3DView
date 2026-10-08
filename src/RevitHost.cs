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

    /// <summary>Her çağrıda TEK iş: sıradaki iş Revit bir sonraki boşta kaldığında çalışır → arada Revit ve
    /// Smart3DView penceresi (aynı iş parçacığı) mesajlarını işler, ön yükleme dilimleri gezinmeyi dondurmaz.</summary>
    public void Execute(UIApplication app)
    {
        if (Pending.Count == 0) return;
        var a = Pending.Dequeue();
        try { a(app); }
        catch (Exception ex) { TaskDialog.Show(Product.Name, ex.Message); }
        if (Pending.Count > 0) Event?.Raise();
    }

    public string GetName() => Product.Name;
}

sealed class RevitHost : IViewerHost
{
    public static readonly RevitHost Instance = new();

    const double SliceSeconds = 0.1;   // bir dilimde en fazla bu kadar okunur, sonra Revit'e (ve pencereye) bırakılır
    const int ConfirmAbove = 3000;     // bundan çok yeni eleman varsa okumadan önce sorulur
    const double ConfirmSeconds = 20;  // ya da tahmini süre bundan uzunsa
    public void Recollect(object context, double[] min, double[] max, Action<SceneData?, string?> done, SceneData? append = null, ReadControl? ctl = null)
    {
        var ctx = (RevitContext)context;
        string closed = L.T("Model kapatılmış.", "The model has been closed.");
        string cancelled = L.T("Okuma iptal edildi — kutu önceki alana döndü.", "Reading cancelled — the box is back to the loaded area.");
        RevitBridge.Post(_ =>
        {
            if (!ctx.Doc.IsValidObject) { done(null, closed); return; }
            var box = new ClipBox
            {
                Frame = ctx.Box.Frame,
                Min = new XYZ(min[0], min[1], min[2]),
                Max = new XYZ(max[0], max[1], max[2]),
                Source = ctx.Box.Source,
            };
            var view = ctx.Doc.GetElement(ctx.ViewId) as View;
            // Kurulum yalnız eleman listesini çıkarır (hızlı) → sayıya göre sorulur, sonra dilim dilim okunur.
            var job = new SceneJob(ctx.Doc, view, box, append);
            // Tahmin: bu pencerenin gerçek okuma hızı (yeterli örnek yoksa ~2,6 ms/eleman — 8.465 eleman 22 sn).
            double per = append != null && append.CumRead >= 300 ? append.CumSeconds / append.CumRead : 0.0026;
            double est = job.Total * per;
            if (ctl?.Confirm != null && (job.Total >= ConfirmAbove || est >= ConfirmSeconds))
            {
                if (!ctl.Confirm(job.Total, est)) { done(null, cancelled); return; }
            }

            Run(job, ctx.Doc, ctl, cancelled, done, scene => scene.Context = new RevitContext(ctx.Doc, ctx.ViewId, box));
        });
    }

    /// <summary>İşi dilim dilim yürütür: her dilimden sonra Revit'e bırakır, ilerlemeyi bildirir, iptale bakar.</summary>
    static void Run(SceneJob job, Document doc, ReadControl? ctl, string cancelled, Action<SceneData?, string?> done, Action<SceneData> complete)
    {
        void Next(UIApplication _)
        {
            if (!doc.IsValidObject) { done(null, L.T("Model kapatılmış.", "The model has been closed.")); return; }
            if (ctl?.Cancel == true) { done(null, cancelled); return; }
            bool finished;
            try { finished = job.Step(ctl == null ? double.PositiveInfinity : SliceSeconds); }
            catch (Exception ex) { done(null, ex.Message); return; }
            if (!finished)
            {
                ctl?.Progress?.Invoke(job.Done, job.Total);
                RevitBridge.Post(Next);   // sıradaki dilim Revit bir sonraki boşta kaldığında
                return;
            }
            complete(job.Result);
            done(job.Result, null);
        }
        Next(null!);
    }

    /// <summary>Tüm model: ana model + bağlı modeller, görünümden bağımsız (gizli kategoriler de), sınırsız kutu,
    /// Revit koordinatında. Web'e aktarım için (kullanıcı isteği 2026-10-08).</summary>
    public static void ReadWholeModel(Document doc, Func<RevitLinkInstance?, Document, bool> include, ReadControl ctl, Action<SceneData?, string?> done)
    {
        RevitBridge.Post(_ =>
        {
            if (!doc.IsValidObject) { done(null, L.T("Model kapatılmış.", "The model has been closed.")); return; }
            const double R = 1e6;   // ft — modelin tamamı
            var box = new ClipBox { Frame = Transform.Identity, Min = new XYZ(-R, -R, -R), Max = new XYZ(R, R, R), Source = doc.Title };
            SceneJob job;
            try { job = new SceneJob(doc, null, box, null, include); }
            catch (Exception ex) { done(null, ex.Message); return; }
            Run(job, doc, ctl, L.T("Aktarım iptal edildi.", "Export cancelled."), done, _ => { });
        });
    }


    public void ShowInRevit(object context, SceneData scene, uint sceneId, Action<string?> done)
    {
        RevitBridge.Post(app =>
        {
            int i = (int)sceneId - 1;
            if (i < 0 || i >= scene.ElemRevitId.Count) { done("?"); return; }
            if (scene.ElemDoc[i] != 0) { done(L.T("Bağlı modeldeki eleman Revit'te seçilemez.", "Elements of linked models cannot be selected in Revit.")); return; }
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null || scene.Docs[0] is not Document d || !uidoc.Document.Equals(d)) { done(L.T("Bu modeli Revit'te etkin pencere yapın.", "Make this model the active window in Revit.")); return; }
            var id = new ElementId(scene.ElemRevitId[i]);
            if (d.GetElement(id) == null) { done(L.T("Eleman artık yok — Yenile.", "The element no longer exists — Reload.")); return; }
            var ids = new List<ElementId> { id };
            uidoc.Selection.SetElementIds(ids);
            try { uidoc.ShowElements(ids); } catch { }
            done(null);
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
