using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Smart3DView;

/// <summary>Şerit düğmesi "Web'e aktar" (kullanıcı isteği 2026-10-08): tüm model + seçilen bağlı modeller, görünümden
/// bağımsız, tek .glb dosyasına. Okuma dilim dilim (Revit kullanılmaya devam eder), ilerleme penceresinde İptal / Esc.
/// Komut hemen döner; asıl iş RevitBridge sırasında yürür.</summary>
[Transaction(TransactionMode.Manual)]   // ana modelin düz tavaları için geçici Fine görünüm açılıp geri alınır
public class WebExportCommand : IExternalCommand
{
    static bool _running;

    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var uidoc = data.Application.ActiveUIDocument;
        if (uidoc == null) return Result.Cancelled;
        var doc = uidoc.Document;
        IntPtr hwnd = data.Application.MainWindowHandle;
        if (doc.IsFamilyDocument)
        {
            TaskDialog.Show(Product.Name, L.T("Aile belgesi aktarılamaz; bir proje açın.", "A family document cannot be exported; open a project."));
            return Result.Cancelled;
        }
        if (_running)
        {
            TaskDialog.Show(Product.Name, L.T("Bir aktarım zaten sürüyor.", "An export is already running."));
            return Result.Cancelled;
        }
        if (!License.FullFeatures)
        {
            var lw = new LicenseWindow(null, L.T("\"Web'e aktar\" tam lisans gerektirir (deneme süresi bitti).", "\"Export to web\" needs a full license (the trial has ended)."));
            new WindowInteropHelper(lw).Owner = hwnd;
            lw.ShowDialog();
            if (!License.FullFeatures) return Result.Cancelled;
        }

        // Yüklü bağlı modeller (aynı dosya birden çok kez bağlıysa listede bir kez; seçilirse hepsi gelir).
        var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
            .Select(li => li.GetLinkDocument()?.Title).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
        var chosen = WebExport.AskModels(hwnd, doc.Title, links);
        if (chosen == null) return Result.Cancelled;
        var set = new HashSet<string>(chosen, StringComparer.CurrentCultureIgnoreCase);
        bool host = set.Contains(doc.Title);

        string? path = WebExport.AskPath(null, doc.Title);
        if (path == null) return Result.Cancelled;

        // Revit'in kendi aktarıcısı (CustomExporter) tek seferde çalışır; ilerleme penceresi ayrı iş parçacığında.
        var ctl = new ReadControl();
        int total = EstimateElements(doc, host, set);
        WebExportContext NewCtx() => new(doc, host, d => set.Contains(d.Title), ctl);
        var ctx = NewCtx();
        var prog = new ProgressThread(ctl, () => (ctx.Done, total, ctx.CurrentModel));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _running = true;
        string? err = null;
        try { err = Export(doc, ref ctx, NewCtx, uidoc.ActiveView); }
        catch (Exception ex) { err = ex.Message; }
        finally { _running = false; }

        if (ctl.Cancel) { prog.Close(); return Result.Cancelled; }
        if (err != null || ctx.Model.ElementCount == 0)
        {
            prog.Close();
            MessageBox.Show(err ?? L.T("Aktarılacak 3B eleman bulunamadı.", "No 3D elements found to export."), Product.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return Result.Failed;
        }
        prog.SetText(L.T($"{ctx.Model.ElementCount:N0} eleman okundu — dosya yazılıyor…", $"{ctx.Model.ElementCount:N0} elements read — writing the file…"));
        try { WebModelGlb.Write(ctx.Model, path); }
        catch (Exception ex)
        {
            prog.Close();
            MessageBox.Show(L.T("Dosya yazılamadı: ", "Could not write the file: ") + ex.Message, Product.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return Result.Failed;
        }
        prog.Close();
        WebExport.ShowSaved(null, hwnd, path, ctx.Model.ElementCount, sw.Elapsed.TotalSeconds);
        return Result.Succeeded;
    }

    /// <summary>Geçici bir 3B görünümle aktarır: şablonsuz, Fine, kesit kutusuz → kullanıcının görünüm ayarları
    /// (gizlenen kategoriler, kutu) aktarımı etkilemez. Görünüm bir işlem grubunda açılıp sonunda geri alınır → modelde iz
    /// kalmaz. Grup içinde aktarım olmazsa: görünüm ayrı işlemle açılır, aktarımdan sonra silinir. Belge salt-okunursa
    /// etkin 3B görünüm kullanılır. Hata metni döner (yoksa null).</summary>
    static string? Export(Document doc, ref WebExportContext ctx, Func<WebExportContext> fresh, View active)
    {
        var c = ctx;
        void Run(View v)
        {
            var ex = new CustomExporter(doc, c) { IncludeGeometricObjects = false, ShouldStopOnError = false };
            ex.Export(v);
        }
        if (doc.IsReadOnly)
        {
            if (active is View3D v3 && !v3.IsTemplate) { Run(v3); return null; }
            return L.T("Belge salt okunur ve etkin görünüm 3B değil — bir 3B görünüm açıp yeniden deneyin.", "The document is read-only and the active view is not 3D — open a 3D view and try again.");
        }
        using (var tg = new TransactionGroup(doc, Product.Name + " web export"))
        {
            tg.Start();
            try
            {
                var view = CreateView(doc);
                Run(view);
                return null;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* grup içinde aktarım yok: aşağıdaki yol, temiz veriyle */ }
            finally { if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack(); }
        }
        ctx = c = fresh();
        ElementId? id = null;
        try
        {
            id = CreateView(doc).Id;
            Run((View)doc.GetElement(id));
            return null;
        }
        finally
        {
            if (id != null)
            {
                using var t = new Transaction(doc, Product.Name + " temp view");
                if (t.Start() == TransactionStatus.Started) { try { doc.Delete(id); t.Commit(); } catch { t.RollBack(); } }
            }
        }
    }

    static View3D CreateView(Document doc)
    {
        using var t = new Transaction(doc, Product.Name + " temp view");
        t.Start();
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
        var v = View3D.CreateIsometric(doc, vft.Id);
        if (v.ViewTemplateId != ElementId.InvalidElementId) v.ViewTemplateId = ElementId.InvalidElementId;
        v.DetailLevel = ViewDetailLevel.Fine;
        if (v.IsSectionBoxActive) v.IsSectionBoxActive = false;
        t.Commit();
        return v;
    }

    /// <summary>İlerleme yüzdesi için yaklaşık eleman sayısı (önbellekteki bağlı modeller hızlı geçer, sayılmaz).</summary>
    static int EstimateElements(Document doc, bool host, HashSet<string> links)
    {
        static int Count(Document d)
        {
            int n = 0;
            try
            {
                foreach (var e in new FilteredElementCollector(d).WhereElementIsNotElementType().WhereElementIsViewIndependent())
                    if (e.Category is { CategoryType: CategoryType.Model }) n++;
            }
            catch { }
            return n;
        }
        int total = host ? Count(doc) : 0;
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (RevitLinkInstance li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
            if (li.GetLinkDocument() is { } ld && links.Contains(ld.Title) && seen.Add(ld.Title)) total += Count(ld);
        return Math.Max(1, total);
    }
}
