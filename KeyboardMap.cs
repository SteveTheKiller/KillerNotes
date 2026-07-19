using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KillerNotes
{
    // Visual keyboard for the F1 shortcuts overlay, ported from KillerPDF's
    // KeyboardMapOverlay.cs. The board is generated from the tables below (one source
    // of truth) and every brush is wired with SetResourceReference so theme switches
    // repaint live. Category colors are the KnCat* theme brushes; keycap faces ride
    // PaneBrush / CardBorderBrush / TextBrush, so the board always looks native to
    // the active theme. Holding a real Ctrl / Shift previews that layer.
    public partial class MainWindow
    {
        private enum KbLayer { Base, Ctrl, CtrlShift }

        private KbLayer _kbLayer = KbLayer.Base;
        private bool _kbBuilt;
        private TextBlock? _kbDetail;
        private TextBlock? _kbHoverAct;   // caption of the key under the mouse
        private string? _kbHoverId;
        private readonly Dictionary<string, (Border Cap, TextBlock Act, Rectangle Bar)> _kbKeys = new();
        private readonly Dictionary<KbLayer, Button> _kbLayerBtns = new();
        private static readonly FontFamily KbMono = new("Consolas");

        private const string KsViewSetting = "ShortcutView";   // "list" (default) | "keyboard"

        // key id -> (category, caption resource key). Categories map 1:1 to the KnCat*
        // theme brushes; captions resolve through Loc() so language switches repaint.
        private static readonly Dictionary<KbLayer, Dictionary<string, (string Cat, string Label)>> KbMap = new()
        {
            [KbLayer.Base] = new()
            {
                ["F1"] = ("Help", "Str_Kb_Shortcuts"),
                ["F2"] = ("Note", "Str_KS_Rename"),
                ["F3"] = ("Search", "Str_KS_Search"),
                ["F4"] = ("View", "Str_Kb_Preview"),
                ["F6"] = ("View", "Str_Kb_FormatBar"),
                ["F8"] = ("File", "Str_Kb_Export"),
                ["F9"] = ("View", "Str_Kb_Sidebar"),
                ["F12"] = ("Help", "Str_KS_About"),
                ["Del"] = ("Note", "Str_Kb_DeleteNote"),
                ["Esc"] = ("Help", "Str_KS_Esc"),
            },
            [KbLayer.Ctrl] = new()
            {
                ["N"] = ("Note", "Str_KS_NewNote"),
                ["O"] = ("File", "Str_KS_OpenFiles"),
                ["S"] = ("File", "Str_Kb_SaveNow"),
                ["F"] = ("Search", "Str_KS_Search"),
                ["B"] = ("Format", "Str_Kb_Bold"),
                ["I"] = ("Format", "Str_Kb_Italic"),
                ["U"] = ("Format", "Str_Kb_Underline"),
                ["V"] = ("Format", "Str_Kb_Paste"),
            },
            [KbLayer.CtrlShift] = new()
            {
                ["S"] = ("Format", "Str_KS_Strike"),
                ["M"] = ("Format", "Str_KS_Mono"),
                ["H"] = ("Format", "Str_Kb_Highlight"),
                ["R"] = ("Format", "Str_Kb_Rule"),
                ["L"] = ("Format", "Str_Kb_Bullets"),
                ["N"] = ("Format", "Str_Kb_Numbered"),
            },
        };

        // Physical layout (same board as KillerPDF's). (id, cap text, width units);
        // id "" = spacer. Numpad omitted. The arrow/backspace/menu caps are literal
        // Unicode on purpose - common BMP chars round-trip fine through the tooling;
        // only PUA icon-font glyphs need char-cast escapes (family rule).
        private static readonly (string Id, string Cap, double W)[][] KbRows =
        [
            [("Esc","Esc",1), ("","",0.8), ("F1","F1",1),("F2","F2",1),("F3","F3",1),("F4","F4",1), ("","",0.6),
             ("F5","F5",1),("F6","F6",1),("F7","F7",1),("F8","F8",1), ("","",0.6),
             ("F9","F9",1),("F10","F10",1),("F11","F11",1),("F12","F12",1)],
            [("Grave","`",1),("D1","1",1),("D2","2",1),("D3","3",1),("D4","4",1),("D5","5",1),("D6","6",1),
             ("D7","7",1),("D8","8",1),("D9","9",1),("D0","0",1),("Minus","-",1),("Equals","=",1),("Back","⌫",2),
             ("","",0.6), ("Ins","Ins",1),("Home","Home",1),("PgUp","PgUp",1)],
            [("Tab","Tab",1.5),("Q","Q",1),("W","W",1),("E","E",1),("R","R",1),("T","T",1),("Y","Y",1),("U","U",1),
             ("I","I",1),("O","O",1),("P","P",1),("LBr","[",1),("RBr","]",1),("Bslash","\\",1.5),
             ("","",0.6), ("Del","Del",1),("End","End",1),("PgDn","PgDn",1)],
            [("Caps","Caps",1.8),("A","A",1),("S","S",1),("D","D",1),("F","F",1),("G","G",1),("H","H",1),("J","J",1),
             ("K","K",1),("L","L",1),("Semi",";",1),("Quote","'",1),("Enter","Enter",2.2)],
            [("Shift","Shift",2.3),("Z","Z",1),("X","X",1),("C","C",1),("V","V",1),("B","B",1),("N","N",1),("M","M",1),
             ("Comma",",",1),("Period",".",1),("Slash","/",1),("RShift","Shift",2.7),
             ("","",1.6), ("Up","↑",1)],
            [("Ctrl","Ctrl",1.5),("Win","Win",1.2),("Alt","Alt",1.5),("Space","",6.8),("RAlt","Alt",1.5),("Menu","☰",1),("RCtrl","Ctrl",1.5),
             ("","",0.6), ("Left","←",1),("Down","↓",1),("Right","→",1)],
        ];

        private static readonly (KbLayer Layer, string Caption)[] KbLayerButtons =
        [
            (KbLayer.Base, "BASE"), (KbLayer.Ctrl, "CTRL"), (KbLayer.CtrlShift, "CTRL+SHIFT"),
        ];

        // Modifier keycaps that light up per layer (they define it rather than carry a binding).
        private static readonly Dictionary<KbLayer, string[]> KbLayerMods = new()
        {
            [KbLayer.Base] = [],
            [KbLayer.Ctrl] = ["Ctrl", "RCtrl"],
            [KbLayer.CtrlShift] = ["Ctrl", "RCtrl", "Shift", "RShift"],
        };

        private static string KbSectionKeyFor(string cat) => cat switch
        {
            "File" => "Str_Sec_File", "Note" => "Str_Sec_Notes", "Format" => "Str_Sec_Format",
            "View" => "Str_Sec_View", "Search" => "Str_Sec_Search", _ => "Str_Sec_Help",
        };

        // ---- View toggle (LIST / KEYBOARD) ----

        private void KsViewList_Click(object sender, RoutedEventArgs e) => ApplyShortcutView(keyboard: false, persist: true);
        private void KsViewKeyboard_Click(object sender, RoutedEventArgs e) => ApplyShortcutView(keyboard: true, persist: true);

        /// <summary>Shows the list or the keyboard inside the shortcuts card. Called on
        /// every overlay open with the persisted choice, and by the two toggle captions.</summary>
        private void ApplyShortcutView(bool keyboard, bool persist = false)
        {
            if (keyboard && !_kbBuilt) BuildKeyboardView();
            ShortcutListHost.Visibility     = keyboard ? Visibility.Collapsed : Visibility.Visible;
            ShortcutKeyboardHost.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
            ShortcutCardGrid.MaxWidth       = keyboard ? 1000 : 380;
            KsViewListBtn.SetResourceReference(ForegroundProperty, keyboard ? "MutedTextBrush" : "PrimaryBrush");
            KsViewKeyboardBtn.SetResourceReference(ForegroundProperty, keyboard ? "PrimaryBrush" : "MutedTextBrush");
            if (keyboard) SetKbLayer(KbLayer.Base);
            if (persist) App.SetSetting(KsViewSetting, keyboard ? "keyboard" : "list");
        }

        private void ApplyPersistedShortcutView() =>
            ApplyShortcutView(App.GetSetting(KsViewSetting) == "keyboard");

        // ---- Board construction (once, lazily) ----

        // Minimal chip-button template: border + centered content, no stock hover chrome.
        private static ControlTemplate KbButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        private void BuildKeyboardView()
        {
            _kbBuilt = true;
            var host = ShortcutKeyboardHost;
            host.Children.Clear();
            _kbKeys.Clear();
            _kbLayerBtns.Clear();

            // Layer captions row.
            var layerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var (layer, caption) in KbLayerButtons)
            {
                var b = new Button
                {
                    Content = caption, FontFamily = KbMono, FontSize = 11,
                    Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0),
                    BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                    FocusVisualStyle = null, Template = KbButtonTemplate(),
                };
                b.SetResourceReference(BackgroundProperty, "PaneBrush");
                b.SetResourceReference(ForegroundProperty, "MutedTextBrush");
                b.SetResourceReference(BorderBrushProperty, "CardBorderBrush");
                var l = layer;
                b.Click += (_, _2) => SetKbLayer(l);
                _kbLayerBtns[layer] = b;
                layerRow.Children.Add(b);
            }
            var hint = new TextBlock
            {
                Text = Loc("Str_KS_HoldHint"),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            layerRow.Children.Add(hint);
            host.Children.Add(layerRow);

            // The board. A DownOnly Viewbox keeps it fitting smaller windows without scrollbars.
            const double U = 46;   // one key unit incl. its 4px gap
            var board = new StackPanel();
            foreach (var row in KbRows)
            {
                var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                foreach (var (id, cap, w) in row)
                {
                    if (id.Length == 0) { r.Children.Add(new Border { Width = U * w }); continue; }
                    var capText = new TextBlock
                    {
                        Text = cap, FontFamily = KbMono,   // symbols render via font fallback
                        FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0),
                    };
                    capText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    var act = new TextBlock
                    {
                        FontSize = 8.5, HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed,
                        RenderTransform = new TranslateTransform(),
                    };
                    var actHost = new Border   // clips the caption so it can marquee on hover
                    {
                        ClipToBounds = true, VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(2, 0, 2, 5), Child = act,
                    };
                    var bar = new Rectangle
                    {
                        Height = 3, VerticalAlignment = VerticalAlignment.Bottom, RadiusX = 1.5, RadiusY = 1.5,
                        Margin = new Thickness(3, 0, 3, 0), Visibility = Visibility.Collapsed,
                    };
                    var inner = new Grid();
                    inner.Children.Add(capText);
                    inner.Children.Add(actHost);
                    inner.Children.Add(bar);
                    var key = new Border
                    {
                        Width = U * w - 4, Height = 44, CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0),
                        Child = inner,
                    };
                    key.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
                    key.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                    // Hover: the keycap lifts a few pixels (killertools.net card behavior).
                    var lift = new TranslateTransform();
                    key.RenderTransform = lift;
                    string keyId = id;
                    key.MouseEnter += (_, _2) =>
                    {
                        _kbHoverAct = act; _kbHoverId = keyId;
                        KbShowDetail(keyId);
                        if (KbMap[_kbLayer].ContainsKey(keyId))   // only bound keys lift
                        {
                            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, TimeSpan.FromMilliseconds(90)));
                            KbMarqueeStart(act);   // a cut-off caption scrolls, marquee-style
                        }
                    };
                    key.MouseLeave += (_, _2) =>
                    {
                        _kbHoverAct = null; _kbHoverId = null;
                        if (_kbDetail is not null) _kbDetail.Text = " ";
                        lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)));
                        KbMarqueeStop(act);
                    };
                    _kbKeys[id] = (key, act, bar);
                    r.Children.Add(key);
                }
                board.Children.Add(r);
            }
            host.Children.Add(new Viewbox
            {
                Child = board, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            _kbDetail = new TextBlock
            {
                Text = " ", FontFamily = KbMono, FontSize = 12.5,
                Margin = new Thickness(2, 10, 0, 0), Height = 18,
            };
            _kbDetail.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            host.Children.Add(_kbDetail);
        }

        private void KbShowDetail(string id)
        {
            if (_kbDetail is null) return;
            if (KbMap[_kbLayer].TryGetValue(id, out var b))
                _kbDetail.Text = $"{Loc(KbSectionKeyFor(b.Cat))} :: {Loc(b.Label)}";
            else _kbDetail.Text = " ";
        }

        // ---- Caption marquee (hover a lit key whose caption is cut off) ----

        /// <summary>Scrolls a truncated caption back and forth inside its clipped host while
        /// the key is hovered. No-op when the full text already fits.</summary>
        private void KbMarqueeStart(TextBlock act)
        {
            if (act.Visibility != Visibility.Visible || act.Parent is not Border host) return;
            // Measure with a probe TextBlock, NOT FormattedText: the probe inherits the same
            // text formatting mode as the live control, so its width matches what actually
            // renders (FormattedText under-reports by a couple of pixels).
            var probe = new TextBlock
            {
                Text = act.Text, FontFamily = act.FontFamily, FontSize = act.FontSize,
                FontStyle = act.FontStyle, FontWeight = act.FontWeight, FontStretch = act.FontStretch,
            };
            TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(act));
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double over = probe.DesiredSize.Width - host.ActualWidth;
            if (over <= 0.5) return;
            // Reparent the caption into a Canvas for the ride: a Canvas measures children
            // with infinite space, so the TextBlock escapes WPF's layout clip and renders
            // the whole caption; the host border clips the viewport. (Arranged directly in
            // the too-small host, the TextBlock is clipped to its slot BEFORE the transform
            // runs, so the animation just slides a pre-cut snapshot.)
            double h = act.ActualHeight;
            act.TextTrimming = TextTrimming.None;
            host.Child = null;
            var cv = new Canvas { Height = h };
            cv.Children.Add(act);
            Canvas.SetLeft(act, 0);
            Canvas.SetTop(act, 0);
            host.Child = cv;
            var tt = (TranslateTransform)act.RenderTransform;
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, -over, TimeSpan.FromMilliseconds(Math.Max(600, over * 40)))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(350) });
        }

        private void KbMarqueeStop(TextBlock act)
        {
            var tt = (TranslateTransform)act.RenderTransform;
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = 0;
            act.TextTrimming = TextTrimming.CharacterEllipsis;
            if (act.Parent is Canvas cv && cv.Parent is Border host)
            {
                cv.Children.Clear();
                host.Child = act;   // back to the plain centered, ellipsized layout
            }
        }

        // ---- Layer painting ----

        private void SetKbLayer(KbLayer layer)
        {
            _kbLayer = layer;
            if (!_kbBuilt) return;
            var map = KbMap[layer];
            foreach (var kv in _kbKeys)   // no KeyValuePair deconstruction on net48
            {
                var vis = kv.Value;
                if (map.TryGetValue(kv.Key, out var b))
                {
                    vis.Cap.SetResourceReference(Border.BorderBrushProperty, "KnCat" + b.Cat);
                    vis.Bar.SetResourceReference(Shape.FillProperty, "KnCat" + b.Cat);
                    vis.Bar.Visibility = Visibility.Visible;
                    vis.Act.Text = Loc(b.Label);
                    vis.Act.SetResourceReference(TextBlock.ForegroundProperty, "KnCat" + b.Cat);
                    vis.Act.Visibility = Visibility.Visible;
                }
                else
                {
                    vis.Cap.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                    vis.Bar.Visibility = Visibility.Collapsed;
                    vis.Act.Visibility = Visibility.Collapsed;
                }
            }
            // Modifier caps that define the layer glow accent; the captions follow suit.
            string[] allMods = ["Ctrl", "RCtrl", "Shift", "RShift", "Alt", "RAlt"];
            foreach (var m in allMods)
                if (_kbKeys.TryGetValue(m, out var vis))
                    vis.Cap.SetResourceReference(Border.BorderBrushProperty,
                        Array.IndexOf(KbLayerMods[layer], m) >= 0 ? "PrimaryBrush" : "CardBorderBrush");
            foreach (var kv in _kbLayerBtns)   // no KeyValuePair deconstruction on net48
            {
                kv.Value.SetResourceReference(ForegroundProperty, kv.Key == layer ? "PrimaryBrush" : "MutedTextBrush");
                kv.Value.SetResourceReference(BorderBrushProperty, kv.Key == layer ? "PrimaryBrush" : "CardBorderBrush");
            }
            // Layer changed while a key is hovered (holding Ctrl / Shift): restart that
            // key's marquee for its NEW caption - MouseEnter alone never re-fires.
            // Deferred one layout pass so the caption reflects the new layer first.
            if (_kbHoverAct is not null && _kbHoverId is not null)
            {
                KbMarqueeStop(_kbHoverAct);
                KbShowDetail(_kbHoverId);
                var act = _kbHoverAct;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(act, _kbHoverAct)) KbMarqueeStart(act);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>Maps the live modifier state to a layer while the keyboard view shows -
        /// called from the Preview key handlers so holding Ctrl / Shift previews that layer.</summary>
        private void KbSyncLayerFromModifiers()
        {
            if (!_kbBuilt || ShortcutKeyboardHost.Visibility != Visibility.Visible) return;
            var m = Keyboard.Modifiers;
            var layer = m.HasFlag(ModifierKeys.Control) && m.HasFlag(ModifierKeys.Shift) ? KbLayer.CtrlShift
                      : m.HasFlag(ModifierKeys.Control) ? KbLayer.Ctrl
                      : KbLayer.Base;
            if (layer != _kbLayer) SetKbLayer(layer);
        }
    }
}
