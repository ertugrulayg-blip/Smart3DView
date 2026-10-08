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

        var ctl = new ReadControl();
        var win = new ExportProgressWindow(hwnd, ctl);
        ctl.Progress = win.SetProgress;
        win.Show();
        _running = true;

        RevitHost.ReadWholeModel(doc, (li, d) => li == null ? host : set.Contains(d.Title), ctl, (scene, err) =>
        {
            win.Finished = true;
            if (scene == null)
            {
                _running = false;
                win.Close();
                if (!ctl.Cancel) MessageBox.Show(err ?? "?", Product.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            win.SetText(L.T($"{scene.ElementCount:N0} eleman okundu — dosya yazılıyor…", $"{scene.ElementCount:N0} elements read — writing the file…"));
            // Yazma arka planda; bitince pencere iş parçacığına dönülür (Revit API kullanılmaz).
            WebExport.Write(scene, path).ContinueWith(t => win.Dispatcher.Invoke(() =>
            {
                _running = false;
                win.Close();
                if (t.Exception != null)
                    MessageBox.Show(L.T("Dosya yazılamadı: ", "Could not write the file: ") + t.Exception.GetBaseException().Message,
                        Product.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                else WebExport.ShowSaved(null, hwnd, path, scene.ElementCount);
            }));
        });
        return Result.Succeeded;
    }
}
