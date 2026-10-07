using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Smart3DView;

/// <summary>Sağ panel (kullanıcı isteği, 2026-10-07): kategori filtresi (tavaları, mekanik cihazları… aç/kapa) ve seçili
/// elemanın özellikleri (boyut, genişlik, kot, sistem…) + "Revit'te göster". Ayrıca alt çubukta Panel ve Etiket düğmeleri.
/// Panel WPF'tir, OpenGL penceresinin yanında durur (üstüne binmez).</summary>
sealed partial class ViewerWindow
{
    readonly Border _side = new() { Width = 300, BorderThickness = new Thickness(1, 0, 0, 0) };
    readonly StackPanel _filterList = new();
    readonly StackPanel _props = new();
    readonly HashSet<string> _hidden = new(StringComparer.CurrentCultureIgnoreCase);
    Border? _btnSide, _btnTag;
    bool _sideOpen, _tagOn;
    int _propsReq;   // eski özellik isteğinin geç gelen cevabı yenisini ezmesin

    Brush Ink => new SolidColorBrush(Luma(_pal.BgBottom) < 0.45 ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20));

    void BuildSide()
    {
        _btnSide = MakeTool("☰  " + L.T("Panel", "Panel"),
            L.T("Sağ panel: kategori filtresi (aç/kapa) ve seçili elemanın özellikleri", "Side panel: category filter (show/hide) and properties of the selected element"), ToggleSide);
        _btnTag = MakeTool("🏷  " + L.T("Etiket", "Tag"),
            L.T("İmlecin altındaki elemanın kategori, tip ve boyutunu anlık göster  (T)", "Show category, type and size of the element under the cursor  (T)"), ToggleTag);
        _tools.Children.Insert(Math.Min(2, _tools.Children.Count), _btnTag);
        _tools.Children.Insert(Math.Min(2, _tools.Children.Count), _btnSide);

        var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        stack.Children.Add(Header(L.T("Filtre", "Filter")));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        row.Children.Add(SmallButton(L.T("Tümünü göster", "Show all"), () => { _hidden.Clear(); ApplyFilter(); }));
        row.Children.Add(SmallButton(L.T("Hiçbiri", "None"), () =>
        {
            foreach (var (n, _) in _view.Categories()) _hidden.Add(n);
            ApplyFilter();
        }));
        stack.Children.Add(row);
        stack.Children.Add(_filterList);
        stack.Children.Add(Header(L.T("Özellikler", "Properties"), top: 16));
        stack.Children.Add(_props);
        _side.Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _side.Visibility = _sideOpen ? Visibility.Visible : Visibility.Collapsed;
        _view.TagMode = _tagOn;
    }

    TextBlock Header(string t, double top = 0) => new()
    {
        Text = t, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, top, 0, 4), Foreground = Ink,
    };

    Button SmallButton(string t, Action click)
    {
        var b = new Button { Content = t, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), FontSize = 11.5 };
        b.Click += (_, _) => { click(); _view.FocusGl(); };
        return b;
    }

    void ToggleSide()
    {
        _sideOpen = !_sideOpen;
        _side.Visibility = _sideOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_sideOpen) { RebuildFilter(); ShowProps(); }
        RefreshTools();
        _view.FocusGl();
    }

    void ToggleTag()
    {
        _tagOn = !_tagOn;
        _view.TagMode = _tagOn;
        RefreshTools();
        Flash(_tagOn ? L.T("Etiket açık: imleci bir elemanın üstünde gezdir.", "Tag on: hover over an element.") : null);
        _view.FocusGl();
    }

    void ApplyFilter()
    {
        _view.SetHiddenCategories(_hidden);
        RebuildFilter();
        if (_clashOn) _ = RunClash();   // gizlenen kategoriler çakışmaya girmez
    }

    void RebuildFilter()
    {
        _filterList.Children.Clear();
        var ink = Ink;
        foreach (var (name, count) in _view.Categories())
        {
            var cb = new CheckBox
            {
                Content = $"{name}  ({count:N0})", IsChecked = !_hidden.Contains(name), Foreground = ink,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 12,
            };
            string n = name;
            cb.Click += (_, _) =>
            {
                if (cb.IsChecked == true) _hidden.Remove(n); else _hidden.Add(n);
                ApplyFilter();
                _view.FocusGl();
            };
            _filterList.Children.Add(cb);
        }
    }

    /// <summary>Seçili elemanın özellikleri: önce sahnedeki bilgi (anında), sonra Revit'ten tam liste.</summary>
    void ShowProps()
    {
        _props.Children.Clear();
        var ink = Ink;
        uint id = _view.Selected;
        if (id == 0 || _scene == null)
        {
            _props.Children.Add(new TextBlock { Text = L.T("Bir elemana tıkla.", "Click an element."), Foreground = ink, Opacity = 0.6, FontSize = 12 });
            return;
        }
        _props.Children.Add(new TextBlock { Text = _view.SelectedLabel ?? "", Foreground = ink, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        if (_host == null || _scene.Context == null) return;
        var loading = new TextBlock { Text = L.T("Revit'ten okunuyor…", "Reading from Revit…"), Foreground = ink, Opacity = 0.6, FontSize = 12 };
        _props.Children.Add(loading);
        int req = ++_propsReq;
        var scene = _scene;
        _host.GetInfo(scene.Context, scene, id, (rows, err) => Dispatcher.BeginInvoke(() =>
        {
            if (req != _propsReq || scene != _scene) return;
            _props.Children.Remove(loading);
            if (rows == null) { _props.Children.Add(new TextBlock { Text = err ?? "?", Foreground = ink, FontSize = 12, TextWrapping = TextWrapping.Wrap }); return; }
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bool sep = false;
            foreach (var (name, value, key) in rows)
            {
                if (!key && !sep)
                {
                    sep = true;
                    grid.RowDefinitions.Add(new RowDefinition());
                    var hdr = new TextBlock { Text = L.T("Diğer parametreler", "Other parameters"), FontWeight = FontWeights.Bold, FontSize = 12, Foreground = ink, Margin = new Thickness(0, 10, 0, 3) };
                    Grid.SetRow(hdr, grid.RowDefinitions.Count - 1); Grid.SetColumnSpan(hdr, 2);
                    grid.Children.Add(hdr);
                }
                grid.RowDefinitions.Add(new RowDefinition());
                int r = grid.RowDefinitions.Count - 1;
                var a = new TextBlock { Text = name, Foreground = ink, Opacity = 0.65, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 6, 1) };
                var b = new TextBox
                {
                    Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = ink,
                    FontSize = 11.5, FontWeight = key ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0),
                };
                Grid.SetRow(a, r); Grid.SetRow(b, r); Grid.SetColumn(b, 1);
                grid.Children.Add(a); grid.Children.Add(b);
            }
            _props.Children.Add(SmallButton(L.T("Revit'te seç ve göster", "Select and show in Revit"), () =>
                _host.ShowInRevit(scene.Context!, scene, id, msg => Dispatcher.BeginInvoke(() => Flash(msg ?? L.T("Revit'te seçildi.", "Selected in Revit."))))));
            _props.Children.Add(grid);
        }));
    }

    void RefreshSide()
    {
        if (_btnSide == null || _btnTag == null) return;
        bool dark = Luma(_pal.BgBottom) < 0.45;
        var ink = dark ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20);
        foreach (var (b, on) in new[] { (_btnSide, _sideOpen), (_btnTag, _tagOn) })
        {
            ((TextBlock)b.Child).Foreground = new SolidColorBrush(on ? Colors.White : ink);
            b.Background = on ? new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xD8)) : new SolidColorBrush(Color.FromArgb(dark ? (byte)0x22 : (byte)0x10, ink.R, ink.G, ink.B));
            b.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x2B, 0x6C, 0xD8) : Color.FromArgb(0x40, ink.R, ink.G, ink.B));
        }
        _side.Background = new SolidColorBrush(dark ? Color.FromRgb(0x24, 0x24, 0x24) : Color.FromRgb(0xF6, 0xF6, 0xF5));
        _side.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0));
        foreach (var c in _filterList.Children) if (c is CheckBox cb) cb.Foreground = new SolidColorBrush(ink);
    }
}
