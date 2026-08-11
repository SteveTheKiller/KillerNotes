using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KillerNotes.Controls
{
    /// <summary>
    /// KillerUI kit. A small themed RGB color picker ported from KillerPDF: saturation/value
    /// square + hue strip, RGB and HTML-hex inputs, a desktop-wide crosshair eyedropper, and
    /// a row of 9 user swatches persisted app-wide ("UserSwatches" setting). Replace overwrites
    /// one slot with the current color; Reset restores defaults. Opaque RGB only. Used by the
    /// text/highlight "More..." buttons and the per-note title color.
    /// </summary>
    internal sealed class ColorPickerDialog : Window
    {
        // Cancel the first close, fade out, then close for real (Anim.FadeOutAndClose).
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        public Color SelectedColor { get; private set; }

        /// <summary>True when OK confirmed the pick. Callers read THIS, never ShowDialog's
        /// return: the fade in OnClosing cancels the first close, and WPF nulls DialogResult
        /// whenever a close is canceled (Anim.cs header), so ShowDialog returns null and a
        /// `== true` check silently discards the pick - which is exactly how the tag color
        /// picker "didn't work" (2026-08-08). Same shape as ConfirmDialog/PasswordDialog's
        /// Confirmed.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>Fires on every color change (SV/hue drag, RGB/hex, eyedropper, swatch)
        /// so a caller can preview the color live. Not fired for the initial value set in
        /// the constructor (no subscriber is attached yet at that point).</summary>
        public event Action<Color>? ColorChanged;
        private double _h, _s = 1, _v = 1;     // HSV state (h 0..360, s/v 0..1)
        private bool _updating;                // guards the field<->thumb<->preview sync from feedback loops
        private Border _svArea = null!;
        private Canvas _svThumb = null!;
        private Border _hueThumb = null!;
        private Rectangle _svHue = null!;
        private TextBox _rBox = null!, _gBox = null!, _bBox = null!, _hexBox = null!;
        private Border _newSwatch = null!;
        private WrapPanel _savedRow = null!;
        private Border _replaceBtn = null!;
        private bool _replaceArmed;            // when on, the next swatch click is overwritten, not selected

        private const int SvW = 220, SvH = 170, HueW = 18;
        private const int SwatchCell = 24, SwatchCols = 9, SwatchMax = 9;   // one clean row of 9 fixed slots
        private const string SavedKey = "UserSwatches";

        // First-run / Reset palette: the flyout's fixed swatches plus useful extras, white last.
        private static readonly Color[] DefaultSwatches =
        [
            (Color)ColorConverter.ConvertFromString("#DD504B"),
            (Color)ColorConverter.ConvertFromString("#E8962C"),
            (Color)ColorConverter.ConvertFromString("#E8D44B"),
            (Color)ColorConverter.ConvertFromString("#1EA54C"),
            (Color)ColorConverter.ConvertFromString("#50AEE8"),
            (Color)ColorConverter.ConvertFromString("#9A6AE8"),
            (Color)ColorConverter.ConvertFromString("#E85CA8"),
            (Color)ColorConverter.ConvertFromString("#9A9A9A"),
            Colors.White,
        ];

        /// <summary>Returns Brush, NOT SolidColorBrush. Several palettes define BackgroundBrush and
        /// TitleBarBrush as a LinearGradientBrush, and the hard cast threw InvalidCastException the
        /// moment this dialog opened on one of them. Nothing here needs the concrete type - every
        /// caller assigns it to a Background/BorderBrush/Foreground, all of which take Brush.</summary>
        private static Brush? R(string key) => Application.Current.Resources[key] as Brush;
        private static string L(string key, string fallback) =>
            Application.Current.TryFindResource(key) as string ?? fallback;

        public ColorPickerDialog(Window? owner, Color initial)
        {
            Title = "KillerNotes - Color";
            Width = 300;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Owner = owner;
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SelectedColor = initial;
            (_h, _s, _v) = RgbToHsv(initial);
            BuildUi();
            SyncFromHsv();
            // No window-level Escape/Enter handler. The Cancel button is IsCancel and the OK button
            // IsDefault (see BuildUi), so WPF already routes both keys for us. The handler that used
            // to live here duplicated that: Escape ran DialogResult=false AND Close(), and assigning
            // DialogResult is itself a close request - so the first close was canceled by the fade in
            // OnClosing and the second arrived with _closeFaded already true and closed instantly.
            // Escape therefore skipped the fade that the X and Cancel both play. Same shape as the
            // duplicate Cancel_Click that was removed for the same reason.
        }

        // ---- UI ----

        /// <summary>A themed corner radius, falling back to the value this dialog used to
        /// hardcode. Every radius in here was a literal, so a square-cornered palette got a
        /// dialog full of rounded chips inside a square card.</summary>
        private static CornerRadius Rad(string key, double fallback) =>
            Application.Current.TryFindResource(key) is CornerRadius c ? c : new CornerRadius(fallback);

        private void BuildUi()
        {
            // Corner radius and shadow follow the THEME, not hardcoded 6 / 0.55 - a square-cornered
            // palette was getting a rounded card, and a flat one still got a drop shadow.
            CornerRadius radius = Application.Current.TryFindResource("PanelCornerRadius") is CornerRadius cr
                ? cr : new CornerRadius(6);
            double shadowOp = Application.Current.TryFindResource("FlyoutShadowOpacity") is double so ? so : 0.55;

            var card = new Border
            {
                // BackgroundBrush, the window face - NOT SurfaceBrush. SurfaceBrush is the raised
                // material used for panes and bars (Sepulchre's brown), so this dialog was the one
                // card in the family wearing a pane color instead of the window color. About,
                // Confirm and the rest all use BackgroundBrush.
                Background = R("BackgroundBrush"),
                BorderBrush = R("WindowEdgeBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = radius,
                Margin = new Thickness(14),
                Effect = shadowOp <= 0 ? null : new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = shadowOp }
            };
            // Top margin is small because the caption band above supplies the head room.
            var panel = new StackPanel { Margin = new Thickness(18, 12, 18, 16) };
            // Film grain over the card, same treatment as ConfirmDialog.
            var root = new Grid();
            if (Application.Current.TryFindResource("GrainTileBrush") is Brush grain)
            {
                double grainOp = Application.Current.TryFindResource("GrainOpacity") is double go ? go : 0.12;
                root.Children.Add(new Border { Background = grain, Opacity = grainOp, CornerRadius = radius, IsHitTestVisible = false });
            }
            root.Children.Add(panel);
            card.Child = root;

            // Raised edge as SIBLINGS of the card, sharing its margin so they land on its outer
            // edge - the family pattern. Transparent and zero-thickness except on beveled themes.
            var shellGrid = new Grid();
            shellGrid.Children.Add(card);
            foreach (var (brushKey, thickKey) in new[]
                     { ("BevelLightBrush", "BevelLightThickness"), ("BevelDarkBrush", "BevelDarkThickness") })
            {
                var bevel = new Border { Margin = new Thickness(14), IsHitTestVisible = false };
                bevel.SetResourceReference(Border.BorderBrushProperty, brushKey);
                bevel.SetResourceReference(Border.BorderThicknessProperty, thickKey);
                shellGrid.Children.Add(bevel);
            }
            Content = shellGrid;
            Opacity = 0;
            Loaded += (_, _) => Anim.FadeIn(this);

            // A real caption BAND, not a heading floating inside the padding. The band is its own
            // row spanning the card, so it can carry TitleBarBrush - a gradient on the themes that
            // define one, identical to the card face on the themes that do not. Same treatment the
            // SketchPad and the file picker got.
            // LEFT padding only, like DialogTitleBar - the right pad floated the close X off the
            // corner (same fix as WhisperModelDialog, 2026-08-08).
            var titleBand = new Border { Padding = new Thickness(14, 0, 0, 0), Cursor = Cursors.SizeAll };
            titleBand.SetResourceReference(Border.BackgroundProperty, "DialogTitleBarBrush");
            titleBand.SetResourceReference(FrameworkElement.HeightProperty, "DialogTitleBarHeight");
            titleBand.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            var title = new TextBlock
            {
                Text = L("Str_Dlg_PickColor", "Pick a color"),
                FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 1, Direction = 270, Opacity = 0.7 },
            };
            // ChromeTextBrush: the caption sits on the title band, which is dark on several themes.
            title.SetResourceReference(TextBlock.ForegroundProperty, "ChromeTextBrush");
            titleBand.Child = title;

            // The band goes above the padded content, so it reaches the card's edges.
            var cardRows = new Grid();
            cardRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardRows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(titleBand, 0);
            cardRows.Children.Add(titleBand);
            // Detach from root BEFORE re-parenting: WPF throws if an element that already has a
            // parent is added to another panel.
            root.Children.Remove(panel);
            Grid.SetRow(panel, 1);
            cardRows.Children.Add(panel);
            root.Children.Add(cardRows);

            // SV square + hue strip
            var pickRow = new StackPanel { Orientation = Orientation.Horizontal };
            _svHue = new Rectangle { Width = SvW, Height = SvH };
            var svWhite = new Rectangle { Width = SvW, Height = SvH, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0) };
            var svBlack = new Rectangle { Width = SvW, Height = SvH, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), 90) };
            _svThumb = new Canvas { Width = SvW, Height = SvH, IsHitTestVisible = false };
            var svDot = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 0, Opacity = 0.8 } };
            _svThumb.Children.Add(svDot);
            var svGrid = new Grid { Width = SvW, Height = SvH };
            svGrid.Children.Add(_svHue); svGrid.Children.Add(svWhite); svGrid.Children.Add(svBlack); svGrid.Children.Add(_svThumb);
            // ClipToBounds off so the indicator dot shows fully when it sits at an edge/corner.
            _svArea = new Border { Width = SvW, Height = SvH, CornerRadius = Rad("SmallCornerRadius", 3), ClipToBounds = false,
                BorderBrush = R("InputBorderBrush"), BorderThickness = new Thickness(1), Child = svGrid, Cursor = Cursors.Cross };
            _svArea.MouseLeftButtonDown += (s, e) => { _svArea.CaptureMouse(); SvPick(e.GetPosition(svGrid)); };
            _svArea.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) SvPick(e.GetPosition(svGrid)); };
            _svArea.MouseLeftButtonUp += (s, e) => _svArea.ReleaseMouseCapture();
            pickRow.Children.Add(_svArea);

            var hueRect = new Rectangle { Width = HueW, Height = SvH, Fill = HueStripBrush() };
            _hueThumb = new Border { Width = HueW + 6, Height = 6, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                Background = R("PrimaryBrush"), CornerRadius = Rad("SmallCornerRadius", 2), IsHitTestVisible = false };
            var hueCanvas = new Canvas { Width = HueW + 6, Height = SvH };
            Canvas.SetLeft(_hueThumb, -3);
            hueCanvas.Children.Add(_hueThumb);
            var hueGrid = new Grid { Margin = new Thickness(8, 0, 0, 0) };
            hueGrid.Children.Add(hueRect); hueGrid.Children.Add(hueCanvas);
            var hueArea = new Border { Child = hueGrid, Cursor = Cursors.SizeNS, CornerRadius = Rad("SmallCornerRadius", 3),
                BorderBrush = R("InputBorderBrush"), BorderThickness = new Thickness(1) };
            hueArea.MouseLeftButtonDown += (s, e) => { hueArea.CaptureMouse(); HuePick(e.GetPosition(hueRect)); };
            hueArea.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) HuePick(e.GetPosition(hueRect)); };
            hueArea.MouseLeftButtonUp += (s, e) => hueArea.ReleaseMouseCapture();
            pickRow.Children.Add(hueArea);
            panel.Children.Add(pickRow);

            // RGB + hex + preview + eyedropper
            var inputRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            _newSwatch = new Border { Width = 34, Height = 34, CornerRadius = Rad("SmallCornerRadius", 3),
                BorderBrush = R("InputBorderBrush"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
            inputRow.Children.Add(_newSwatch);
            _rBox = NumBox(); _gBox = NumBox(); _bBox = NumBox();
            inputRow.Children.Add(FieldGroup("R", _rBox));
            inputRow.Children.Add(FieldGroup("G", _gBox));
            inputRow.Children.Add(FieldGroup("B", _bBox));
            var eyedrop = new Button
            {
                Width = 28, Height = 22, Margin = new Thickness(8, 14, 0, 0),
                Background = R("BackgroundBrush"), BorderBrush = R("InputBorderBrush"), BorderThickness = new Thickness(1),
                Content = CrosshairIcon(), ToolTip = L("Str_TT_Eyedropper", "Pick a color from anywhere on screen"),
                Cursor = Cursors.Cross, Template = MakeBtnTemplate()
            };
            eyedrop.Click += (_, _) => RunEyedropper();
            inputRow.Children.Add(eyedrop);
            panel.Children.Add(inputRow);

            var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            hexRow.Children.Add(new TextBlock { Text = "Hex", Foreground = R("MutedTextBrush"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _hexBox = MakeTextBox(96);
            _hexBox.MaxLength = 7;
            _hexBox.LostFocus += (_, _) => CommitHex();
            _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
            hexRow.Children.Add(_hexBox);
            panel.Children.Add(hexRow);

            // Swatch header: Replace (assign current color to a slot) left, Reset far right.
            var swHeader = new Grid { Margin = new Thickness(0, 12, 0, 5), Width = SwatchCols * SwatchCell };
            // Icons, not words. Two text chips in a 216px header crowded the row and read as labels
            // rather than controls; the tooltip still carries the full localized wording, so nothing
            // is lost for a screen reader or a first-time user hovering them.
            _replaceBtn = Chip("", L("Str_TT_ReplaceSwatch", "Click, then click a swatch to set it to the current color"));
            _replaceBtn.HorizontalAlignment = HorizontalAlignment.Left;
            _replaceBtn.MouseLeftButtonUp += (_, _) => { _replaceArmed = !_replaceArmed; UpdateReplaceChip(); RebuildSavedRow(); };
            var resetBtn = Chip("", L("Str_TT_ResetSwatches", "Reset swatches to defaults"));
            resetBtn.HorizontalAlignment = HorizontalAlignment.Right;
            resetBtn.MouseLeftButtonUp += (_, _) => { StoreSaved([.. DefaultSwatches]); _replaceArmed = false; UpdateReplaceChip(); RebuildSavedRow(); };
            swHeader.Children.Add(_replaceBtn);
            swHeader.Children.Add(resetBtn);
            panel.Children.Add(swHeader);
            _savedRow = new WrapPanel { Width = SwatchCols * SwatchCell };
            panel.Children.Add(_savedRow);
            UpdateReplaceChip();
            RebuildSavedRow();

            // OK / Cancel - the shared family button styles (OutlineButton = primary).
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            // IsCancel ALONE. It already sets DialogResult=false and closes on click (and on Esc),
            // so the explicit Click handler that did the same fired a SECOND close: the first was
            // canceled by the fade in OnClosing, the second arrived with _closeFaded already true
            // and closed instantly - so Cancel skipped the fade and raced the fade timer into
            // closing an already-closed window.
            var cancel = new Button { Content = L("Str_Btn_Cancel", "Cancel"), Width = 74, IsCancel = true,
                Style = Application.Current.TryFindResource("SurfaceButton") as Style };
            var ok = new Button { Content = "OK", Width = 74, Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
                Style = Application.Current.TryFindResource("OutlineButton") as Style };
            ok.Click += (_, _) => Accept();
            btnRow.Children.Add(cancel); btnRow.Children.Add(ok);
            panel.Children.Add(btnRow);
        }

        // Confirmed + Close(), never DialogResult: the fade-canceled first close nulls
        // DialogResult (see the Confirmed doc above), so the result rides a plain property
        // and ONE Close() call keeps the single close request the fade needs.
        private void Accept() { SelectedColor = HsvToRgb(_h, _s, _v); Confirmed = true; Close(); }

        // ---- Interaction ----

        private void SvPick(Point p) { _s = Clamp01(p.X / SvW); _v = Clamp01(1 - p.Y / SvH); SyncFromHsv(); }
        private void HuePick(Point p) { _h = Clamp01(p.Y / SvH) * 360; SyncFromHsv(); }
        private void CommitHex() { if (TryParseHex(_hexBox.Text, out Color c)) SetFromColor(c); else SyncFromHsv(); }
        private void CommitRgb()
        {
            if (byte.TryParse(_rBox.Text, out byte r) && byte.TryParse(_gBox.Text, out byte g) && byte.TryParse(_bBox.Text, out byte b))
                SetFromColor(Color.FromRgb(r, g, b));
            else SyncFromHsv();
        }
        private void SetFromColor(Color c) { (_h, _s, _v) = RgbToHsv(c); SyncFromHsv(); }

        // Push current HSV out to every control (hue background, thumbs, RGB, hex, preview).
        private void SyncFromHsv()
        {
            if (_updating) return;
            _updating = true;
            var c = HsvToRgb(_h, _s, _v);
            _svHue.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
            Canvas.SetLeft((UIElement)_svThumb.Children[0], _s * SvW - 6);
            Canvas.SetTop((UIElement)_svThumb.Children[0], (1 - _v) * SvH - 6);
            Canvas.SetTop(_hueThumb, Math.Max(0, Math.Min(SvH - 6, _h / 360.0 * SvH - 3)));   // keep the handle inside the strip
            _rBox.Text = c.R.ToString(); _gBox.Text = c.G.ToString(); _bBox.Text = c.B.ToString();
            _hexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _newSwatch.Background = new SolidColorBrush(c);
            _updating = false;
            ColorChanged?.Invoke(c);
        }

        // ---- Eyedropper (desktop-wide) ----

        private void RunEyedropper()
        {
            var capture = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Cursor = Cursors.Cross,
                Left = SystemParameters.VirtualScreenLeft, Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth, Height = SystemParameters.VirtualScreenHeight, Owner = this
            };
            capture.MouseLeftButtonDown += (_, _) =>
            {
                // GetCursorPos returns physical screen pixels; the desktop DC's GetPixel uses the same
                // space, so this is correct regardless of per-monitor DPI scaling.
                if (GetCursorPos(out POINT pt))
                {
                    IntPtr dc = GetDC(IntPtr.Zero);
                    uint cref = GetPixel(dc, pt.X, pt.Y);
                    ReleaseDC(IntPtr.Zero, dc);
                    capture.DialogResult = true; capture.Close();
                    SetFromColor(Color.FromRgb((byte)(cref & 0xFF), (byte)((cref >> 8) & 0xFF), (byte)((cref >> 16) & 0xFF)));
                    return;
                }
                capture.DialogResult = false; capture.Close();
            };
            capture.KeyDown += (_, e) => { if (e.Key == Key.Escape) { capture.DialogResult = false; capture.Close(); } };
            capture.ShowDialog();
        }

        // ---- Saved swatches ----

        // Shared with the SketchPad tool strip so both show the same slots and the picker's
        // Replace / Reset edits them for both.
        public static List<Color> UserSwatches()
        {
            var raw = App.GetSetting(SavedKey);
            if (string.IsNullOrWhiteSpace(raw)) return [.. DefaultSwatches];   // first run = defaults
            var list = new List<Color>();
            foreach (var part in raw!.Split(','))
                if (TryParseHex(part.Trim(), out Color c)) list.Add(c);
            return list.Count > 0 ? list : [.. DefaultSwatches];
        }

        private List<Color> LoadSaved() => UserSwatches();

        private void StoreSaved(List<Color> list) =>
            App.SetSetting(SavedKey, string.Join(",", list.Take(SwatchMax).Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}")));

        private void UpdateReplaceChip()
        {
            if (_replaceBtn is null) return;
            // ChipFaceBrush / ChipEdgeBrush, matching Chip(). These were PaneBrush and
            // InputBorderBrush, so the first arm or disarm flipped the chip back to the client
            // color and undid the button face it was built with.
            _replaceBtn.Background = _replaceArmed ? R("RowSelectedBrush") : R("ChipFaceBrush");
            _replaceBtn.SetResourceReference(Border.BorderBrushProperty, _replaceArmed ? "PrimaryBrush" : "ChipEdgeBrush");
        }

        private void RebuildSavedRow()
        {
            _savedRow.Children.Clear();
            var saved = LoadSaved().Take(SwatchMax).ToList();
            for (int i = 0; i < saved.Count; i++)
            {
                var c = saved[i];
                int idx = i;
                var sw = new Border { Width = 20, Height = 20, CornerRadius = Rad("SmallCornerRadius", 3), Margin = new Thickness(0, 0, 4, 4),
                    Background = new SolidColorBrush(c), BorderThickness = new Thickness(_replaceArmed ? 2 : 1), Cursor = Cursors.Hand,
                    ToolTip = _replaceArmed
                        ? L("Str_TT_SwatchReplace", "Click to set this swatch to the current color")
                        : L("Str_TT_SwatchUse", "Click to use this color") };
                if (_replaceArmed) sw.SetResourceReference(Border.BorderBrushProperty, "PrimaryBrush"); else sw.BorderBrush = R("InputBorderBrush");
                sw.MouseLeftButtonUp += (_, _) =>
                {
                    if (_replaceArmed)
                    {
                        var list = LoadSaved();
                        if (idx < list.Count) { list[idx] = HsvToRgb(_h, _s, _v); StoreSaved(list); }
                        _replaceArmed = false; UpdateReplaceChip(); RebuildSavedRow();
                    }
                    else SetFromColor(c);
                };
                _savedRow.Children.Add(sw);
            }
        }

        // ---- Small themed control builders ----

        private StackPanel FieldGroup(string label, TextBox box)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = R("MutedTextBrush"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(box);
            return sp;
        }

        private TextBox NumBox()
        {
            var b = MakeTextBox(34);
            b.MaxLength = 3;
            b.TextAlignment = TextAlignment.Center;
            b.LostFocus += (_, _) => CommitRgb();
            b.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitRgb(); };
            return b;
        }

        private TextBox MakeTextBox(double width) => new()
        {
            Width = width, Height = 22, VerticalContentAlignment = VerticalAlignment.Center,
            // TextFieldBrush, not BackgroundBrush directly: an edit field is a CLIENT area, and on a
            // theme whose "darkest tone" is the button face the hex box came out the same gray as
            // the dialog behind it. Defaults to BackgroundBrush, so nothing else moves.
            Background = R("TextFieldBrush"), Foreground = R("TextBrush"),
            BorderBrush = R("InputBorderBrush"), BorderThickness = new Thickness(1),
            CaretBrush = R("TextBrush"), SelectionBrush = R("PrimaryBrush"),
            Padding = new Thickness(4, 0, 4, 0), Template = MakeTextBoxTemplate()
        };

        // A crosshair/target glyph drawn in vectors, matching the family look.
        private UIElement CrosshairIcon()
        {
            var g = new Grid { Width = 14, Height = 14 };
            var fg = R("TextBrush");
            g.Children.Add(new Rectangle { Width = 1.4, Fill = fg, HorizontalAlignment = HorizontalAlignment.Center });
            g.Children.Add(new Rectangle { Height = 1.4, Fill = fg, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new Ellipse { Width = 8, Height = 8, Stroke = fg, StrokeThickness = 1.4,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = Brushes.Transparent });
            return g;
        }

        /// <summary>
        /// A small square icon button. <paramref name="glyph"/> is a Segoe MDL2 character - E70F
        /// (edit) for Replace, E72C (refresh) for Reset - and the localized wording lives in the
        /// tooltip. Square rather than text-width so the two read as a matched pair of controls.
        /// </summary>
        private Border Chip(string glyph, string tip)
        {
            // ChipFaceBrush, not PaneBrush. A chip is a BUTTON, and on a theme where PaneBrush is
            // the white client color it came out looking like an empty text field with a label in
            // it rather than something pressable. Defaults to PaneBrush, so nothing else moves.
            var b = new Border { Height = 22, Width = 24, CornerRadius = Rad("SmallCornerRadius", 3), Cursor = Cursors.Hand,
                BorderBrush = R("ChipEdgeBrush"), BorderThickness = new Thickness(1), Background = R("ChipFaceBrush"),
                ToolTip = tip };

            // The RAISED bevel goes INSIDE the chip, alongside its label, rather than wrapping the
            // chip in an outer element. Wrapping would return a different Border than the one the
            // handlers below are attached to, so `_replaceBtn` would no longer be the object those
            // handlers compare against and the armed highlight would never fire. Margin -1 pushes
            // the bevel back over the chip's own 1px border so it rings the button instead of
            // sitting inside it. Draws nothing where the bevel brushes are transparent.
            var content = new Grid();
            content.Children.Add(new TextBlock { Text = glyph, Foreground = R("TextBrush"), FontSize = 12,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var light = new Border { IsHitTestVisible = false, Margin = new Thickness(-1) };
            light.SetResourceReference(Border.BorderBrushProperty, "BevelLightBrush");
            light.SetResourceReference(Border.BorderThicknessProperty, "BevelLightThickness");
            var dark = new Border { IsHitTestVisible = false, Margin = new Thickness(-1) };
            dark.SetResourceReference(Border.BorderBrushProperty, "BevelDarkBrush");
            dark.SetResourceReference(Border.BorderThicknessProperty, "BevelDarkThickness");
            content.Children.Add(light);
            content.Children.Add(dark);
            b.Child = content;

            // Unified hover, respecting Replace's armed highlight.
            b.MouseEnter += (_, _) => { if (b != _replaceBtn || !_replaceArmed) b.Background = R("ChipHoverBrush"); };
            b.MouseLeave += (_, _) => { b.Background = (b == _replaceBtn && _replaceArmed) ? R("RowSelectedBrush") : R("ChipFaceBrush"); };
            return b;
        }

        private static ControlTemplate MakeTextBoxTemplate()
        {
            var b = new FrameworkElementFactory(typeof(Border));
            foreach (var (dp, prop) in new[] { (Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush"), (Border.BorderThicknessProperty, "BorderThickness") })
                b.SetBinding(dp, new Binding(prop) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            b.SetValue(Border.CornerRadiusProperty, Rad("SmallCornerRadius", 3));
            var sv = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            sv.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
            b.AppendChild(sv);

            // The SUNKEN edge, crossed: the dark brush takes the LIGHT thickness (top/left) and the
            // light brush the dark one. That inversion is what makes an edit field read as pressed
            // into the dialog rather than sitting on it. Transparent on the other twelve themes.
            //
            // The bevels are siblings of the field inside a GRID, not extra children of the Border.
            // A Border accepts exactly one child and already has the content host, so appending them
            // to it throws ArgumentException the moment the dialog is constructed.
            var root = new FrameworkElementFactory(typeof(Grid));
            root.AppendChild(b);
            foreach (var (brushKey, thickKey) in new[] { ("PaneBevelDarkBrush", "BevelLightThickness"),
                                                         ("PaneBevelLightBrush", "BevelDarkThickness") })
            {
                var bevel = new FrameworkElementFactory(typeof(Border));
                bevel.SetResourceReference(Border.BorderBrushProperty, brushKey);
                bevel.SetResourceReference(Border.BorderThicknessProperty, thickKey);
                bevel.SetValue(UIElement.IsHitTestVisibleProperty, false);
                root.AppendChild(bevel);
            }
            return new ControlTemplate(typeof(TextBox)) { VisualTree = root };
        }

        private static ControlTemplate MakeBtnTemplate()
        {
            // Root is a Grid so the raised bevel can be a SIBLING of the face. The face is named so
            // the triggers below can repaint it: this template had NO triggers at all, which is why
            // the eyedropper looked like a button and behaved like a picture - no hover, no press.
            var root = new FrameworkElementFactory(typeof(Grid));

            var bf = new FrameworkElementFactory(typeof(Border), "face");
            foreach (var (dp, prop) in new[] { (Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush"), (Border.BorderThicknessProperty, "BorderThickness") })
                bf.SetBinding(dp, new Binding(prop) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            bf.SetValue(Border.CornerRadiusProperty, Rad("ControlCornerRadius", 4));
            root.AppendChild(bf);

            var bLight = new FrameworkElementFactory(typeof(Border), "bLight");
            bLight.SetResourceReference(Border.BorderBrushProperty, "BevelLightBrush");
            bLight.SetResourceReference(Border.BorderThicknessProperty, "BevelLightThickness");
            bLight.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(bLight);

            var bDark = new FrameworkElementFactory(typeof(Border), "bDark");
            bDark.SetResourceReference(Border.BorderBrushProperty, "BevelDarkBrush");
            bDark.SetResourceReference(Border.BorderThicknessProperty, "BevelDarkThickness");
            bDark.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(bDark);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(cp);

            var t = new ControlTemplate(typeof(Button)) { VisualTree = root };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, R("ChipHoverBrush"), "face"));
            t.Triggers.Add(hover);
            // Pressed reverses the bevel, the same swap every other button in the family performs.
            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, R("RowSelectedBrush"), "face"));
            pressed.Setters.Add(new Setter(Border.BorderBrushProperty, R("BevelDarkBrush"), "bLight"));
            pressed.Setters.Add(new Setter(Border.BorderBrushProperty, R("BevelLightBrush"), "bDark"));
            t.Triggers.Add(pressed);
            return t;
        }

        private static LinearGradientBrush HueStripBrush()
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int i = 0; i <= 6; i++) g.GradientStops.Add(new GradientStop(HsvToRgb(i * 60, 1, 1), i / 6.0));
            return g;
        }

        // ---- Color math / parsing ----

        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

        private static bool TryParseHex(string? s, out Color c)
        {
            c = Colors.Black;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s!.Trim().TrimStart('#');
            if (s.Length == 3) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
            if (s.Length != 6) return false;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            c = Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            return true;
        }

        private static (double h, double s, double v) RgbToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            double h = 0;
            if (d > 0.00001)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * (((b - r) / d) + 2);
                else h = 60 * (((r - g) / d) + 4);
            }
            if (h < 0) h += 360;
            double s = max <= 0 ? 0 : d / max;
            return (h, s, max);
        }

        private static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60.0 % 2) - 1)), m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hdc, int x, int y);
    }
}
