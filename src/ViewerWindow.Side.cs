using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace Smart3DView;

/// <summary>Alt çubukta Etiket ve "Revit'te göster"; model aç/kapa listesi sol üstte OpenGL üstüne çizilir (GlView).
/// Sağ panel (kategori filtresi + parametreler) 2026-10-07'de kaldırıldı — kullanıcı: "parametre işini boşver, panel
/// hiç olmasın; sol üstte ana dosya ve link adları olsun, komple aç kapa".</summary>
sealed partial class ViewerWindow
{
    readonly HashSet<string> _hidden = new(StringComparer.CurrentCultureIgnoreCase);   // gizli modeller (dosya adı)
    Border? _btnTag, _btnShow;
    bool _tagOn;

    void BuildSide()
    {
        _btnTag = MakeTool("🏷  " + L.T("Etiket", "Tag"),
            L.T("İmlecin altındaki elemanın kategori ve tipini anlık göster  (T)", "Show category and type of the element under the cursor  (T)"), ToggleTag);
        _btnShow = MakeTool("🎯  " + L.T("Revit'te göster", "Show in Revit"),
            L.T("Seçili elemanı Revit'te seç ve göster (ana modeldeki elemanlar)", "Select and show the selected element in Revit (host model elements)"), ShowInRevit);
        _tools.Children.Insert(Math.Min(2, _tools.Children.Count), _btnShow);
        _tools.Children.Insert(Math.Min(2, _tools.Children.Count), _btnTag);
        _view.TagMode = _tagOn;
        _view.ModelToggled += name =>
        {
            if (!_hidden.Remove(name)) _hidden.Add(name);
            _view.SetHiddenModels(_hidden);
            if (_clashOn) _ = RunClash();   // gizlenen modeller çakışmaya girmez
        };
        _view.HiddenChanged += () =>
        {
            int n = _view.HiddenElementCount;
            Flash(n == 0 ? L.T("Gizlenen elemanlar gösterildi.", "Hidden elements are shown again.")
                         : L.T($"{n} eleman gizli — sağ tık → Tümünü göster (Shift+H).", $"{n} element(s) hidden — right-click → Show all (Shift+H)."));
            if (_clashOn) _ = RunClash();   // gizlenen elemanlar çakışmaya girmez
        };
        _view.ContextRequested += ShowElementMenu;
    }

    /// <summary>Sağ tık menüsü (kullanıcı isteği 2026-10-09: "objeyi sağ tık hide seçeneği olsun"). p = GL istemci pikseli.</summary>
    void ShowElementMenu(uint id, System.Windows.Point p)
    {
        var menu = new ContextMenu();
        var hide = new MenuItem { Header = L.T("Gizle", "Hide"), InputGestureText = "H", IsEnabled = id != 0 };
        hide.Click += (_, _) => _view.HideElement(id);
        int n = _view.HiddenElementCount;
        var show = new MenuItem { Header = L.T($"Tümünü göster ({n})", $"Show all ({n})"), InputGestureText = "Shift+H", IsEnabled = n > 0 };
        show.Click += (_, _) => _view.ShowAllElements();
        menu.Items.Add(hide);
        menu.Items.Add(show);
        var dpi = VisualTreeHelper.GetDpi(_view);
        menu.PlacementTarget = _view;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = p.X / dpi.DpiScaleX;
        menu.VerticalOffset = p.Y / dpi.DpiScaleY;
        menu.Closed += (_, _) => _view.FocusGl();
        menu.IsOpen = true;
    }

    void ToggleTag()
    {
        _tagOn = !_tagOn;
        _view.TagMode = _tagOn;
        RefreshTools();
        Flash(_tagOn ? L.T("Etiket açık: imleci bir elemanın üstünde gezdir.", "Tag on: hover over an element.") : null);
        _view.FocusGl();
    }

    void ShowInRevit()
    {
        uint id = _view.Selected;
        var scene = _scene;
        if (id == 0 || scene?.Context == null || _host == null) { Flash(L.T("Önce bir elemana tıkla.", "Click an element first.")); return; }
        _host.ShowInRevit(scene.Context, scene, id, msg => Dispatcher.BeginInvoke(() => Flash(msg ?? L.T("Revit'te seçildi.", "Selected in Revit."))));
        _view.FocusGl();
    }

    void RefreshSide()
    {
        if (_btnTag == null || _btnShow == null) return;
        bool dark = Luma(_pal.BgBottom) < 0.45;
        var ink = dark ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20);
        foreach (var (b, on) in new[] { (_btnTag, _tagOn), (_btnShow, false) })
        {
            ((TextBlock)b.Child).Foreground = new SolidColorBrush(on ? Colors.White : ink);
            b.Background = on ? new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xD8)) : new SolidColorBrush(Color.FromArgb(dark ? (byte)0x22 : (byte)0x10, ink.R, ink.G, ink.B));
            b.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x2B, 0x6C, 0xD8) : Color.FromArgb(0x40, ink.R, ink.G, ink.B));
        }
        _btnShow.Opacity = _view.Selected > 0 ? 1 : 0.45;   // seçim yoksa soluk
    }
}
