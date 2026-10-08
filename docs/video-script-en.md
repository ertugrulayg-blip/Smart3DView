# Smart3DView — Revit add-in video script (EN)

Length: about 8–9 minutes · Version 1.16.2 · Revit side only (Export to web is not covered).
Each scene lists what is **On screen** and the **Narration** to read.
Setup: an MEP model + at least one linked model (architecture/structure), Revit 2025/2026/2027 with the add-in installed, a second screen if you have one.

---

## 1. Opening (0:00 – 0:30)

**On screen:** the Smart3DView logo, then a colored MEP area slowly orbiting in 3D.

**Narration:**
Welcome! In this video we'll walk you through Smart3DView, our add-in for Revit, from start to finish.
Smart3DView opens the part of your Revit model you're working on in its own fast 3D window. You orbit, cut, measure and see clashes smoothly, without slowing down Revit's 3D view. And you can keep this window open on your second screen while you keep working in Revit.

---

## 2. Installation and first launch (0:30 – 1:10)

**On screen:** the downloaded ZIP, the year folder inside it, copying into `%AppData%\Autodesk\Revit\Addins\2026`; “Always Load” when Revit starts; the Smart3DView panel on the Add-Ins tab.

**Narration:**
Installation is very simple — there's no setup program. In the file you downloaded, open the folder for your Revit version and copy the two items inside it into Revit's Addins folder.
When you start Revit, if you see the unsigned add-in warning, choose “Always Load”.
The Smart3DView panel and the Smart 3D View button are now on the Add-Ins tab. Every feature is unlocked for the first 14 days — there's nothing to enter.

---

## 3. Choosing what to open (1:10 – 2:10)

**On screen:** (a) a few pipes and ducts selected in a plan, then the button → the window opens. (b) In a 3D view with a section box and nothing selected, the button → that box opens. (c) In a plan view with nothing selected, the button → a rectangle is dragged.

**Narration:**
Smart3DView knows what to open from where you are.
Select a few elements and click the button: the area around your selection opens, with thirty centimeters of margin on every side.
If nothing is selected and you're in a 3D view with an active section box, that box opens.
In a plan view, you drag a rectangle; the height comes from the plan's view range.
Linked models come along automatically, and categories you've hidden in the 3D view stay hidden.
Every click on the ribbon button opens a new window, so you can keep several areas open side by side.

---

## 4. Navigation (2:10 – 2:50)

**On screen:** Shift + middle-button orbit, middle-button pan, wheel zoom toward the cursor, double-click to fit, Home.

**Narration:**
Navigation works just like in Revit: hold Shift and drag with the middle button to orbit; drag with the middle button alone to pan. The wheel zooms toward the point under your cursor.
If you've selected an element, the view orbits around it.
Double-click or press F to fit the selected element or the whole area; Home takes you back to the default 3D angle.
Click the view cube at the top right to look exactly from the front, the side or the top.

---

## 5. Tones: Detailed, Colored, Paper (2:50 – 3:50)

**On screen:** the round buttons at the bottom right; switching with keys 1–7. Detailed → Colored (model colors at the top left) → Paper (pencil sketch) → the gray tones.

**Narration:**
The round buttons at the bottom right — or keys one to seven — change the look.
In the default Detailed tone every element type has its own color: walls gray, cable trays metallic, ducts galvanized, mechanical equipment violet, electrical panels amber, lighting yellow, sprinklers red. In a crowded ceiling void you can tell what's what at a glance.
In the Colored tone, the host model and every linked model get their own pastel color, and the top left shows which color is which file. No more hunting for which model an element comes from.
The Paper tone looks like a hand-drawn pencil sketch on an ice-white background — perfect for presentations and sheets.
White, light gray, gray and dark tones are there for a clean, simple look. In every tone, faces cut by the section box are drawn as solid poché.

---

## 6. Editing the section box (3:50 – 4:50)

**On screen:** Box (B); the strip that opens above it (Move, ↺); dragging a blue handle; Ctrl to move the whole box; growing the box outward with the percentage in the status bar and Esc to cancel.

**Narration:**
Click Box, or press B: the box outline and six blue handles appear. Drag a handle to pull that face in or out — the cut updates instantly.
With Move in the small strip that opens above the Box button — or by holding Ctrl while you drag — you slide the whole box. The back arrow resets it.
If you grow the box beyond the area that was first read, Smart3DView reads only the elements that are new from Revit. Reading happens in slices: Revit doesn't freeze, the status bar shows the percentage, and you can press Esc to cancel at any time.

---

## 7. Clash detection (4:50 – 6:00)

**On screen:** Clash (C) → red/blue elements, everything else faded; the tolerance button 5 → 10 → 25 → 50 mm → touch; clicking a clashing pipe → what it clashes with at the bottom; hiding a model from the model list.

**Narration:**
Now one of our favorite features: clash detection. Click Clash, or press C.
Clashes between different services show up in two colors: one side red, the other blue. Everything else fades, so the problem spots jump out right away.
Click a clashing element: it turns red, the elements clashing with it turn blue, and the bottom bar tells you what it clashes with.
The tolerance button next to it sets how deep two elements must overlap to count as a clash. The default is five millimeters, so equipment resting on a floor or a panel mounted on a wall doesn't show up as a false clash. Click it for ten, twenty-five or fifty millimeters, or “touch” to count touching too.
The rules are smart: a pipe with its own elbow or coupling, a duct with its own insulation, and elements connected through connectors are not reported. Elements in linked models are checked too, and models you hide in the list at the top left are left out.

---

## 8. Measure (6:00 – 6:50)

**On screen:** Measure (D); the cursor markers ✕ corner, ○ edge midpoint, ◉ pipe axis; two points; Z lock for the vertical distance between a pipe axis and a duct bottom; Backspace / Delete.

**Narration:**
Click Measure, or press D, and click two points. The cursor snaps like AutoCAD: corners, edge midpoints, pipe and duct axes, edges or faces — and the marker's shape shows what it's holding.
The distance appears on the line, and the X, Y and Z components in the status bar, in your project's length units.
Press X, Y or Z to lock the measurement to that axis. With Z, for example, you get the vertical distance between a pipe axis and the bottom of a duct in one go.
Backspace removes the last measurement, Delete removes them all. Measurements stay on screen and are included in your pictures.

---

## 9. Tags, Show in Revit, Reload (6:50 – 7:30)

**On screen:** T → category/type next to the cursor; selecting an element and clicking “Show in Revit” → selected in Revit; moving a duct in Revit → the Reload button turns orange → R reloads with the camera and box kept.

**Narration:**
Press T to turn on tags: hover over an element and its category and type appear right next to your cursor.
Select an element and click Show in Revit, and it's selected and brought into view in Revit.
When you change the model in Revit, the Reload button turns orange. Press R or F5 and the same area is read again — your camera, your box, your tone and your clash settings stay exactly as they were.

---

## 10. Take picture (7:30 – 8:00)

**On screen:** Take picture (P) → the saved window → Project Browser → Renderings → “Smart3DView - date time” → dragged onto a sheet.

**Narration:**
Click Take picture, or press P, and Smart3DView renders the view at about 4K and saves it straight into your model. You'll find it in the Project Browser under Renderings, and you can place it on a sheet like any rendering. A PNG copy stays on your computer too.

---

## 11. Trial and license (8:00 – 8:30)

**On screen:** the “Trial” button at the bottom of the window → the license window → pasting the key → Activate.

**Narration:**
Everything is unlocked for the first fourteen days. After the trial, grayscale 3D viewing stays free.
A full license — for the Detailed and Colored tones, clash detection, measuring, box editing and taking pictures — is a one-time four dollars, with no subscription, on two computers.
Your key arrives in your purchase email. Click the license button at the bottom of the window and paste it in — that's all.

---

## 12. Closing (8:30 – 8:50)

**On screen:** an area orbiting in the Paper tone; F1 and schema-tools.net/smart3dview on screen.

**Narration:**
If you get stuck, press F1 in the window to open the help page. We'd love to hear your feedback.
That's it — thanks for watching!

---

### Recording notes
- Show the shortcuts on screen with a small label (e.g. “C — Clash”).
- For the clash scene pick a busy ceiling void where pipes, ducts and trays cross; include equipment resting on a floor to show the tolerance.
- Use at least two linked models (architecture + structure) for the Colored tone so the pastel colors stand apart.
- To show that Revit doesn't freeze, select something in the Revit window while the box is growing.
