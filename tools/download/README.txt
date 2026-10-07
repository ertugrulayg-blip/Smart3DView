Smart3DView {VER} — Revit 2025 / 2026 / 2027
https://schema-tools.net/smart3dview/

==========================================================================
TÜRKÇE — KURULUM (kurulum programı yok, kopyalamak yeterli)
==========================================================================
1. Revit'i kapatın.
2. Bu ZIP'i bir klasöre çıkarın (ZIP'e sağ tık → Tümünü ayıkla).
3. Revit sürümünüzün klasörünü açın (ör. Revit 2026 için "2026").
   İçinde iki şey var:  Smart3DView.addin  ve  Smart3DView  klasörü.
4. Bu ikisini birlikte kopyalayın (Ctrl+A, Ctrl+C).
5. Windows Gezgini'nin adres çubuğuna şunu yazıp Enter'a basın
   (2026 yerine kendi sürümünüzü yazın):
       %APPDATA%\Autodesk\Revit\Addins\2026
   Klasör yoksa oluşturun. Açılan klasöre yapıştırın (Ctrl+V).
   Sonuç şöyle olmalı:
       ...\Addins\2026\Smart3DView.addin
       ...\Addins\2026\Smart3DView\Smart3DView.dll
6. Revit'i açın. "İmzasız eklenti / Security - Unsigned Add-In" uyarısı
   çıkarsa "Always Load" (Her zaman yükle) seçin.
7. Eklentiler (Add-Ins) sekmesi → Smart3DView düğmesi.

Birden çok Revit sürümü varsa 3–5. adımları her sürüm için kendi
klasörüyle tekrarlayın.

Güncelleme: Revit kapalıyken aynı iki öğeyi üzerine kopyalayın
(değiştir deyin). Lisans ve ayarlar korunur.
Kaldırma: Revit kapalıyken Addins\<yıl>\ altındaki Smart3DView.addin
dosyasını ve Smart3DView klasörünü silin.

Deneme: 14 gün tüm özellikler açık; sonrasında gri 3B görüntüleme
ücretsiz kalır. Tam lisans: https://schema-tools.net/smart3dview/
Yardım: Smart3DView-Help.html (eklentide F1 ya da "?" düğmesi)
Destek: schematoolssupp@outlook.com

==========================================================================
ENGLISH — INSTALLATION (no installer, just copy)
==========================================================================
1. Close Revit.
2. Extract this ZIP to a folder (right-click the ZIP → Extract All).
3. Open the folder of your Revit version (e.g. "2026" for Revit 2026).
   It contains two items:  Smart3DView.addin  and the  Smart3DView  folder.
4. Copy both of them together (Ctrl+A, Ctrl+C).
5. Type this into the address bar of Windows Explorer and press Enter
   (use your own version instead of 2026):
       %APPDATA%\Autodesk\Revit\Addins\2026
   Create the folder if it does not exist. Paste there (Ctrl+V).
   The result must look like this:
       ...\Addins\2026\Smart3DView.addin
       ...\Addins\2026\Smart3DView\Smart3DView.dll
6. Start Revit. If a "Security - Unsigned Add-In" warning appears,
   choose "Always Load".
7. Add-Ins tab → Smart3DView button.

If you have several Revit versions, repeat steps 3–5 for each one with
its own folder.

Update: with Revit closed, copy the same two items over the old ones
(choose Replace). License and settings are kept.
Uninstall: with Revit closed, delete Smart3DView.addin and the
Smart3DView folder from Addins\<year>\.

Trial: all features for 14 days; afterwards grayscale 3D viewing stays
free. Full license: https://schema-tools.net/smart3dview/
Help: Smart3DView-Help.html (F1 or the "?" button in the add-in)
Support: schematoolssupp@outlook.com
