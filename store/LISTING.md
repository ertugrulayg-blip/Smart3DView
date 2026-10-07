# Smart3DView — Autodesk App Store (Design and Make Marketplace) form metinleri

Form: Desktop-based App · Win64 · English. Dosyalar:
- App File: `out\Smart3DView-<sürüm>.zip` (`tools\make-bundle.ps1` üretir; Smart3DView.bundle + PackageContents.xml + Smart3DView-Help.html)
- App Icon: `store\icon-120.png` (büyük kopya `store\icon-512.png`)
- Screenshots: gerçek Revit modelinden alınacak (her biri ≤ 2 MB, png/jpg)

---

## App Name (≤ 50)
Smart3DView – Fast 3D Section Box Viewer

## App Short Description (≤ 200)
Open any part of your Revit model in its own fast 3D window: clean grayscale tones, colored MEP systems, clash detection between services, live section box editing and one-click pictures.

## App Description (≤ 4000)
Smart3DView opens the area you are working on in a separate, GPU-accelerated 3D window that you can keep on a second screen while you model. It is built to make 3D easy to read: no materials, no clutter, just clean tones, crisp edges and solid poché where the section box cuts.

**Open exactly what you need**
- Select elements → the area around them opens (with a small margin).
- In a 3D view with an active section box → that box opens.
- In a plan view → drag a rectangle; the height comes from the view range.
- Linked models are included; elements you hide in a 3D view stay hidden.

**Tones made for reading 3D**
White, Light gray, Gray, Dark, Black and Paper (hidden-line look) — switch with one click or keys 1–7.

**Colored MEP mode**
The building stays gray while services are colored: chilled/cooling water pipes and their insulation blue, fire protection red, supply air ducts magenta, return and exhaust ducts green. Systems are recognized from Revit system classifications and system names.

**Clash detection between services**
One click turns touching or overlapping elements of different services red — cooling vs. ducts, ducts vs. fire protection, cable trays vs. mechanical, MEP vs. beams and columns. Parts of the same run (a pipe and its own elbow or coupling), elements connected through connectors and a pipe with its own insulation are not reported. Click a red element to see what it clashes with. Works across linked models.

**Live section box**
Drag the face handles to shrink or grow the box, or move the whole box along an axis. Cutting happens on the graphics card, so changes are instant.

**Take picture**
Saves a high-resolution image (about 4K) of the current view into your model under Project Browser → Renderings, ready to place on sheets.

**Fast on any graphics card**
Geometry is uploaded to the GPU once; navigation stays smooth even on integrated graphics thanks to adaptive resolution while you orbit.

**Navigation like Revit**
Shift + middle mouse to orbit (around the selected element), middle mouse to pan, wheel to zoom towards the cursor, double-click or F to fit.

**Trial and license**
Every feature is available for 14 days. After the trial, grayscale 3D viewing remains free forever. A one-time full license (USD 4, two computers, no subscription) unlocks Colored mode, Clash detection, Take picture and Box editing.

## Publisher Privacy Policy
https://schema-tools.net/privacy-policy.html

## App Version
- Version Number: 1.7.0
- Version Description: Initial release — supports Revit 2025, 2026 and 2027.

## Commands (Add Commands)
- Command: **Smart 3D View** — Ribbon: Add-Ins tab → Smart3DView panel.
  Description: Opens the selected elements, the active 3D section box, or an area dragged in a plan view in the Smart3DView 3D window. Press F1 on the button for help.

## General Usage Instructions
1. In Revit open the Add-Ins tab and click Smart 3D View in the Smart3DView panel.
2. What opens depends on the context: the selected elements (plus a small margin), the section box of the active 3D view, or — in a plan view — a rectangle you drag (height from the view range).
3. Navigate: Shift + middle mouse to orbit, middle mouse to pan, wheel to zoom, double-click or F to fit. Left click selects an element and shows its category, type and ID.
4. Choose a tone at the bottom right (keys 1–7). Colored mode colors cooling, fire protection, supply and return/exhaust systems.
5. Box (B): drag the blue handles to move faces; Move (M) or Ctrl + drag slides the whole box. Areas beyond the loaded region are read from Revit when you release the mouse.
6. Clash (C): elements clashing with a different service turn red; click one to see its partner.
7. Take picture (P): the view is saved into the model under Project Browser → Renderings.
8. Help: F1 or the ? button. The license window opens from the Trial / Free version button.

## Installation/Uninstallation
The installer copies Smart3DView to the ApplicationPlugins folder (%APPDATA%\Autodesk\ApplicationPlugins\Smart3DView.bundle). Restart Revit after installing; the Smart3DView panel appears on the Add-Ins tab. No other setup is needed — the 14-day trial starts on first use.
To uninstall, close Revit and remove Smart3DView in Windows Settings → Apps → Installed apps (or Control Panel → Programs and Features). Settings and license information in %LOCALAPPDATA%\Smart3DView can be deleted afterwards.

## Support Information
Support: schematoolssupp@outlook.com or https://schema-tools.net/contact.html. We usually reply within two business days (Monday–Friday, CET/TRT). Please include your Revit version, the Smart3DView version (shown in the window title) and, if possible, a screenshot. Help: https://schema-tools.net/smart3dview/help/

## Additional Information
- Compatible with Revit 2025, 2026 and 2027 (Windows 64-bit). The compatibility list in the submission form only offers versions up to 2016, so 2016 was selected only to place the app in the Revit store; the package (PackageContents.xml) targets R2025, R2026 and R2027.
- Requires a graphics card with OpenGL 3.3 (any GPU from the last decade, including integrated graphics).
- User interface in English and Turkish (follows the Windows language).
- Privacy: everything runs on your computer; the model is never uploaded. License activation and an approximately weekly check contact Polar (api.polar.sh) with the license key and an anonymous device code only.
- Pictures are also kept as PNG files in %LOCALAPPDATA%Smart3DViewimages.
- Quick start and help: https://schema-tools.net/smart3dview/help/

## Known Issues
- Requires OpenGL 3.3; in some remote desktop sessions or virtual machines without a graphics driver the 3D window cannot start (a message is shown).
- Opening a very large area can take several seconds while geometry is read from Revit; navigation is fast once it is open.
- Colored mode recognizes systems by classification and name; systems with unusual names stay gray.

## Learn More Url
https://schema-tools.net/smart3dview/

## Ekran görüntüleri (gerçek Revit modelinden, ≤ 2 MB)
1. Gri tonlarda bir bina bölümü (Açık gri ya da Beyaz), kesit poşesi görünür.
2. Renkli mod: soğutma (mavi), yangın (kırmızı), üfleme (magenta), dönüş (yeşil) bir arada.
3. Çakışma: kırmızı elemanlar + alt çubukta "clashes with …" yazısı.
4. Kutu düzenleme: mavi tutamaçlar görünür.
5. Revit Proje Tarayıcısı → Renderings altında kaydedilmiş Smart3DView görüntüsü.
6. (İsteğe bağlı) Siyah ya da Kağıt tonu.
