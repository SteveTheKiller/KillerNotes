using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KillerNotes.Models;

namespace KillerNotes.Controls
{
    internal sealed partial class SketchPadWindow
    {
        // ---- Layout ----

        /// <summary>True on a beveled theme (UseDialogCaption marker): the pad then uses the
        /// notepad treatment - minimal insets, no air around the rail, whole-button rail
        /// overflow. Margins re-apply on a live theme switch (ThemeChanged handler in the
        /// ctor); only the rail's overflow STRUCTURE (arrows vs fades) is build-once and picks
        /// up a cross-family switch on reopen.</summary>
        private bool _flatChrome;
        private Grid _contentGrid = null!;   // the padded content grid, for live margin re-apply

        private void BuildUi()
        {
            // Floating rounded card with a soft drop shadow, matching the KillerPDF dialog chrome. The
            // 20px transparent halo (Margin) is the room the shadow renders into; squared off and flush
            // when maximized (UpdateWindowCorners).
            // Halo and shadow follow the flat gate from the FIRST frame - the window shows
            // before Loaded's re-assert, and on 98SE a 20px halo with an effect attached
            // rendered ghost shadow in the band the theme says must not exist (2026-08-08).
            bool flatAtBuild = TryFindResource("UseDialogCaption") != null;
            _outerBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = CardRadius(),
                Margin = flatAtBuild ? new Thickness(0) : new Thickness(20),
                Effect = flatAtBuild ? null : CardShadowOrNull(),
            };
            _outerBorder.SetResourceReference(Border.BorderBrushProperty, "WindowEdgeBrush");
            _outerBorder.SetResourceReference(Border.BorderThicknessProperty, "WindowEdgeThickness");
            _outerBorder.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            var root = new Grid();
            // Film grain over the window background - RESOURCE REFERENCES, not values baked at
            // build: a pad built under a grainy theme kept its texture after a live switch to
            // 98SE, whose GrainOpacity is 0 (2026-08-08).
            _grainBorder = new Border { IsHitTestVisible = false, CornerRadius = CardRadius() };
            _grainBorder.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
            _grainBorder.SetResourceReference(UIElement.OpacityProperty, "GrainOpacity");
            root.Children.Add(_grainBorder);

            // Classic raised edge, drawn LAST so it sits over the card's own border rather than
            // inside it - the same sibling placement the context menus and the About card use.
            // Both are transparent and zero-thickness in every theme but 98SE.
            _bevelLight = new Border { IsHitTestVisible = false };
            _bevelLight.SetResourceReference(Border.BorderBrushProperty, "BevelLightBrush");
            _bevelLight.SetResourceReference(Border.BorderThicknessProperty, "BevelLightThickness");
            _bevelDark = new Border { IsHitTestVisible = false };
            _bevelDark.SetResourceReference(Border.BorderBrushProperty, "BevelDarkBrush");
            _bevelDark.SetResourceReference(Border.BorderThicknessProperty, "BevelDarkThickness");
            // The title bar is its OWN row on the card, not a row inside the padded content grid.
            // Inside the padding it could never span the card, so it could not carry a title-bar
            // background - it had to stay transparent. As a full-width band it takes TitleBarBrush
            // like the main window, which is what gives 98SE its gradient caption.
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // title band
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // content
            root.Children.Add(shell);

            // Tighter card padding everywhere - the old 16/6/16/12 wasted canvas space - and on a
            // beveled theme tighter still: a real Win98 client runs nearly flush to its frame
            // (Notepad's skinny right edge), so 98SE gets the notepad treatment with minimal
            // insets and no air around the tool rail. (2026-08-08)
            _flatChrome = TryFindResource("UseDialogCaption") != null;
            // TWO geometries, deliberately unequal (2026-08-08):
            // - Flat/98SE: notepad treatment. Right margin ZERO - the window frame's own padding
            //   is all the right edge keeps - and minimal insets everywhere else.
            // - Every other theme: the ORIGINAL 16/6/16/12. A first tightening pass cut these
            //   too, and the pane's floating-card depth died with the breathing room - the drop
            //   shadow, grain and rounded corners all read as "lost" with nothing around them.
            //   The standard look needs its air; do not shave these again.
            var grid = new Grid { Margin = _flatChrome ? new Thickness(3, 2, 0, 4) : new Thickness(16, 6, 16, 12) };
            _contentGrid = grid;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // (unused - title moved out)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // tools
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // canvas fills
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // buttons
            Grid.SetRow(grid, 1);
            shell.Children.Add(grid);

            // Built here, PARENTED INTO THE TITLE BAND (BuildTitleBar) - not onto the card. On the
            // card it was top-aligned against the window edge, which is why it never had the band
            // above and below it that the main window's close button has.
            _closeBtn = CloseButton(L("Str_Sketch_Close", "Close (Esc)"));

            // Shared corner grip (DialogChrome) - press it to start an OS corner resize.
            root.Children.Add(KillerNotes.Controls.DialogChrome.ResizeGrip(this));
            // Last, so the raised edge draws over everything else in the card.
            root.Children.Add(_bevelLight);
            root.Children.Add(_bevelDark);

            // ONE window frame for the whole app - DialogChrome.WindowFrame, the same builder the
            // Dictation pad, the About card and every dialog use. The card's _bevelLight/_bevelDark
            // pair above is the shared CONTROL bevel and is a different thing; it is why this
            // window read as flat beside the main one on a beveled theme.
            root.Children.Add(KillerNotes.Controls.DialogChrome.WindowFrame());
            KillerNotes.Controls.DialogChrome.InsetForFrame(shell);

            _outerBorder.Child = root;
            Content = _outerBorder;

            BuildTitleBar(shell);
            BuildToolBar(grid);
            BuildCanvas(grid);
            BuildButtons(grid);
        }

        private void BuildTitleBar(Grid shell)
        {
            // Padding, not Margin: the band runs the full width of the card and insets its own
            // contents, so the background reaches the edges. TitleBarBrush is a gradient on some
            // themes, so it must be a resource reference rather than a copied color.
            var titleBar = new Grid();
            titleBar.SetResourceReference(Panel.BackgroundProperty, "DialogTitleBarBrush");
            titleBar.SetResourceReference(FrameworkElement.HeightProperty, "TitleBarHeight");
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.ClickCount == 2) ToggleMaximize();
                else DragMove();
            };

            // Built by DialogChrome, not locally: this window had its own copy of the wordmark, so
            // the flat-caption swap that Dictation and the About card already got never reached it
            // and 98SE kept showing the typewriter logotype. One builder, one caption everywhere.
            var mark = (FrameworkElement)KillerNotes.Controls.DialogChrome.Wordmark(L("Str_Sketch_Title", "SketchPad"));
            mark.SetResourceReference(FrameworkElement.MarginProperty, "TitleBarPadding");
            Grid.SetColumn(mark, 0);
            titleBar.Children.Add(mark);

            // The close button lives IN the band, column 1 - the same place the main window puts it.
            // It used to be a child of the card with a 48px spacer reserved here, which is why it
            // sat hard against the top edge instead of centered in the bar.
            Grid.SetColumn(_closeBtn, 1);
            titleBar.Children.Add(_closeBtn);

            Grid.SetRow(titleBar, 0);
            shell.Children.Add(titleBar);
        }

        private void BuildToolBar(Grid grid)
        {
            // Top strip (row 1): the color palette on the left, undo / redo / clear on the right. The
            // drawing tools live in the left rail (BuildToolRail), MS-Paint style.
            var top = new Grid { Margin = new Thickness(0, 0, 0, _flatChrome ? 6 : 8) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // palette
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        // actions

            var palette = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _swatchRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            palette.Children.Add(_swatchRow);
            // The swatches' shadow (bevel on 98SE, drop shadow elsewhere) pulls their VISUAL
            // center below their layout center, so the buttons beside them read top-shifted on
            // every theme. A 4px render nudge re-centers them against the swatches without
            // touching layout. First shipped 98SE-only, then promoted to all themes
            // (2026-08-08). Same nudge on the undo/redo/clear group below.
            var moreColors = ActionButton(Glyph(0xE790), L("Str_Sketch_MoreColors", "Custom color..."), OpenColorPicker);
            moreColors.RenderTransform = new TranslateTransform(0, 4);
            palette.Children.Add(moreColors);
            Grid.SetColumn(palette, 0);
            top.Children.Add(palette);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            actions.RenderTransform = new TranslateTransform(0, 4);   // see the moreColors nudge above
            _undoBtn = ActionButton(Glyph(0xE7A7), L("Str_Sketch_Undo", "Undo (Ctrl+Z)"), Undo);
            _redoBtn = ActionButton(Glyph(0xE7A6), L("Str_Sketch_Redo", "Redo (Ctrl+Y)"), Redo);
            actions.Children.Add(_undoBtn);
            actions.Children.Add(_redoBtn);
            actions.Children.Add(Separator());
            // ZOOM, in the TOP bar rather than the tool rail: it is a view control, not a drawing
            // tool, so it does not belong beside the pen and the eraser. The button toggles
            // between 100% and fit; Ctrl+wheel over the canvas is the fine control.
            actions.Children.Add(ActionButton(IconZoom(),
                L("Str_Sketch_Zoom", "Zoom (Ctrl+Wheel) - click for 100%, again to fit"), ToggleZoomFit));
            _zoomReadout = new TextBlock
            {
                Text = "100%", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 6), MinWidth = 38,
                TextAlignment = TextAlignment.Right, FontSize = 11,
            };
            _zoomReadout.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            actions.Children.Add(_zoomReadout);
            actions.Children.Add(Separator());
            actions.Children.Add(ActionButton(Glyph(0xE894), L("Str_Sketch_Clear", "Clear all"), ClearAll));
            Grid.SetColumn(actions, 1);
            top.Children.Add(actions);

            RebuildSwatches();
            Grid.SetRow(top, 1);
            grid.Children.Add(top);
        }

        // MS-Paint-style vertical tool rail (left of the canvas). Grouped draw / brush / shapes /
        // content, with thin rules between; the eraser sits directly under the brush-size button.
        private UIElement BuildToolRail()
        {
            var rail = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, _flatChrome ? 2 : 8, 0) };
            void Add(FrameworkElement b) { b.Width = 42; rail.Children.Add(b); }   // uniform rail width (matches the opacity button)

            Add(ToolButton(IconSelect(), L("Str_Sketch_Select", "Select / move (V) - click an object, drag to move it, Delete removes it"), Tool.Select));
            Add(ToolButton(Glyph(0xE70F), L("Str_Sketch_Pen", "Pen - freehand (P)"), Tool.Pen));
            Add(ToolButton(IconLine(), L("Str_Sketch_Line", "Line - straight (L)"), Tool.Line));
            Add(ToolButton(IconArrow(), L("Str_Sketch_Arrow", "Arrow - line with an arrowhead (A)"), Tool.Arrow));
            rail.Children.Add(RailSeparator());
            Add(WidthButton());
            Add(ToolButton(Glyph(0xE75C), L("Str_Sketch_Eraser", "Eraser (E) - brush over ink, or touch a shape to remove it"), Tool.Eraser));
            rail.Children.Add(RailSeparator());
            Add(ToolButton(IconRect(), L("Str_Sketch_Rect", "Rectangle (R)"), Tool.Rect));
            Add(ToolButton(IconEllipse(), L("Str_Sketch_Ellipse", "Ellipse (O)"), Tool.Ellipse));
            Add(ToolButton(IconPolygon(), L("Str_Sketch_Polygon", "Polygon (G) - click each corner; click the first point or double-click to close (Backspace undoes a point, Esc cancels)"), Tool.Polygon));
            Add(OpacityButton());
            Add(FillButton());
            rail.Children.Add(RailSeparator());
            Add(ToolButton(IconBucket(), L("Str_Sketch_Bucket", "Paint bucket (B) - click inside a closed area to flood-fill it"), Tool.Bucket));
            Add(ToolButton(IconText(), L("Str_Sketch_Text", "Text (T) - click to place a label, type, Enter to set it (Shift+Enter for a new line)"), Tool.Text));
            Add(ToolButton(IconCrop(), L("Str_Sketch_Crop", "Crop (C) - drag a box to keep, everything outside it is trimmed away (Ctrl+Z restores it)"), Tool.Crop));
            Add(ActionButton(IconImage(), L("Str_Sketch_AddImage", "Add an image (I) - drag one onto the pad, or Ctrl+V to paste"), AddImageFromFile));

            // The rail holds fourteen buttons and the pad can be resized shorter than they
            // stack - a bare StackPanel just CLIPPED the bottom tools out of existence
            // (2026-08-08). Two overflow treatments, per theme kind:
            // - Standard themes: the sidebar's answer - wheel scrolling with overflow FADES
            //   from an OpacityMask recomputed on scroll. A rail that fits shows neither.
            // - 98SE: Win98 never faded OR cut a toolbar mid-button; it showed little arrow
            //   buttons and moved by WHOLE items. BuildFlatRailHost does exactly that.
            _railPanel = rail;   // both branches: FitDefaultHeightToRail measures it
            if (_flatChrome) return BuildFlatRailHost(rail);
            var scroller = new ScrollViewer
            {
                Content = rail,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
            };
            scroller.ScrollChanged += (_, _) => UpdateRailFades(scroller);
            _railViewport = scroller;
            return scroller;
        }

        /// <summary>Default HEIGHT hugs the tool rail: the shortest pad where every tool shows
        /// with no overflow arrows or fades (2026-08-08). Runs once after first layout;
        /// resizing afterwards goes anywhere MinHeight allows.</summary>
        private void FitDefaultHeightToRail()
        {
            // Sanity gates, both learned the hard way (2026-08-08): running before layout is
            // real returned a tiny DesiredSize and SHRANK the pad to MinHeight, and an EXACT
            // fit trips the overflow through rounding - hence the +2 cushion.
            if (_railPanel == null || _railViewport == null) return;
            if (_railViewport.ActualHeight < 50) return;    // layout not real yet - keep default
            _railPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double need = _railPanel.DesiredSize.Height;
            if (need < 100) return;                          // bogus measure - keep default
            double slack = need - _railViewport.ActualHeight + 2;
            if (Math.Abs(slack) < 2) return;
            Height = Math.Max(MinHeight, Math.Min(Height + slack, SystemParameters.WorkArea.Height - 20));
        }

        // ---- 98SE rail overflow: whole buttons only, arrow steppers, no fades ----

        private ScrollViewer _railSv = null!;
        private StackPanel _railPanel = null!;
        // The canvas pane's shadow sibling - a FIELD so a live theme switch can re-derive its
        // effect: built once, a pad opened under 98SE (PaneShadowOpacity 0) kept a null effect
        // forever and the pane sat shadowless on every theme after (2026-08-08).
        private Border _frameShadow = null!;
        private FrameworkElement _railViewport = null!;   // the element whose height is the rail's allotted space
        private FrameworkElement _railUp = null!, _railDown = null!;
        private int _railTop;   // index into the rail's children of the first visible item

        private UIElement BuildFlatRailHost(StackPanel rail)
        {
            _railPanel = rail;
            _railSv = new ScrollViewer
            {
                Content = rail,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _railUp = RailArrow(0xE70E, -1);
            _railDown = RailArrow(0xE70D, +1);

            var host = new Grid();
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_railUp, 0); Grid.SetRow(_railSv, 1); Grid.SetRow(_railDown, 2);
            host.Children.Add(_railUp); host.Children.Add(_railSv); host.Children.Add(_railDown);

            _railViewport = host;
            // Wheel steps by whole buttons, matching the arrows.
            _railSv.PreviewMouseWheel += (_, e) => { RailStep(e.Delta < 0 ? +1 : -1); e.Handled = true; };
            host.SizeChanged += (_, _) => FitRail(host);
            host.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => FitRail(host)),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return host;
        }

        private FrameworkElement RailArrow(int glyph, int dir)
        {
            var g = Glyph(glyph);
            g.FontSize = 8;
            var b = ActionButton(g, L("Str_Btn_More", "More..."), () => RailStep(dir));
            b.Width = 42; b.Height = 14;
            b.Visibility = Visibility.Collapsed;
            return b;
        }

        private void RailStep(int dir)
        {
            _railTop += dir;
            if (_railSv.Parent is Grid host) FitRail(host);
        }

        /// <summary>Sizes the rail viewport so it ends exactly on a button boundary - never a
        /// half-clipped tool - starting from the item _railTop. Arrows show only when items are
        /// hidden past that edge; when everything fits, both collapse and the viewport relaxes.</summary>
        private void FitRail(Grid host)
        {
            int n = _railPanel.Children.Count;
            if (n == 0 || host.ActualHeight <= 0) return;
            var bounds = new double[n + 1];
            for (int i = 0; i < n; i++)
            {
                var c = (FrameworkElement)_railPanel.Children[i];
                bounds[i + 1] = bounds[i] + c.ActualHeight + c.Margin.Top + c.Margin.Bottom;
            }
            if (bounds[n] <= 0) return;   // not laid out yet; the Loaded pass will come back

            if (bounds[n] <= host.ActualHeight + 0.5)
            {
                _railTop = 0;
                _railUp.Visibility = _railDown.Visibility = Visibility.Collapsed;
                _railSv.Height = double.NaN;
                _railSv.ScrollToVerticalOffset(0);
                return;
            }

            double arrows = 2 * 14;
            double avail = host.ActualHeight - arrows;
            _railTop = Math.Max(0, Math.Min(_railTop, n - 1));
            // Walk back if starting lower would still show the tail: keeps the rail packed.
            while (_railTop > 0 && bounds[n] - bounds[_railTop - 1] <= avail) _railTop--;
            int last = _railTop + 1;
            while (last < n && bounds[last + 1] - bounds[_railTop] <= avail) last++;

            _railSv.Height = Math.Max(bounds[last] - bounds[_railTop], 1);
            _railSv.ScrollToVerticalOffset(bounds[_railTop]);
            _railUp.Visibility = _railTop > 0 ? Visibility.Visible : Visibility.Collapsed;
            _railDown.Visibility = last < n ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Top/bottom overflow fades for the tool rail: transparent at an edge only
        /// while more rail is hidden past it. ScrollChanged also fires on viewport resizes,
        /// so the mask tracks window resizing without extra wiring.</summary>
        private static void UpdateRailFades(ScrollViewer sv)
        {
            // Standard themes only - the flat/98SE rail never reaches here (BuildFlatRailHost).
            double h = sv.ViewportHeight;
            bool up = sv.VerticalOffset > 0.5;
            bool down = sv.VerticalOffset < sv.ScrollableHeight - 0.5;
            if (h <= 0 || (!up && !down)) { sv.OpacityMask = null; return; }
            double f = System.Math.Min(18 / h, 0.4);
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop(up ? Colors.Transparent : Colors.Black, 0));
            g.GradientStops.Add(new GradientStop(Colors.Black, f));
            g.GradientStops.Add(new GradientStop(Colors.Black, 1 - f));
            g.GradientStops.Add(new GradientStop(down ? Colors.Transparent : Colors.Black, 1));
            g.Freeze();
            sv.OpacityMask = g;
        }

        // Horizontal 1px rule between rail groups, centered under the 42px buttons.
        private static Border RailSeparator()
        {
            var b = new Border { Width = 22, Height = 1, Margin = new Thickness(10, 4, 0, 5), HorizontalAlignment = HorizontalAlignment.Left };
            b.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            return b;
        }

        private void BuildCanvas(Grid grid)
        {
            // Canvas + its own film grain, so the drawing surface reads as textured paper (not a flat
            // fill) like the rest of the app. Grain sits at SCREEN resolution ON TOP of the Viewbox
            // (never inside it) and spans the whole frame, so the Uniform letterbox is textured to
            // match. It only dresses the live surface; flattened objects (SketchModel) carry no grain.
            // Row 2 = the tool rail (left) + the drawing surface (fills the rest).
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        // tool rail
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // canvas

            var rail = BuildToolRail();
            Grid.SetColumn(rail, 0);
            row.Children.Add(rail);

            // The canvas is CENTERED inside its scroller rather than stretched. At 100% the
            // scroller grows the logical canvas to its own viewport (ZoomHost_SizeChanged), so the
            // "resize the window, get more paper" behavior is unchanged; zoomed in, the canvas is
            // bigger than the viewport and the scrollbars are how you reach the rest of it.
            _canvas.HorizontalAlignment = HorizontalAlignment.Center;
            _canvas.VerticalAlignment = VerticalAlignment.Center;

            _zoomHost = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0),
                Content = _canvas,
            };
            _zoomHost.SizeChanged += (_, _) => GrowCanvasToViewport();
            // Ctrl+wheel is the zoom, and it is taken on the SCROLLER so it works over the
            // letterbox around a zoomed-out canvas too, not only over the artwork itself.
            _zoomHost.PreviewMouseWheel += ZoomHost_PreviewMouseWheel;

            var canvasStack = new Grid();
            canvasStack.Children.Add(_zoomHost);
            // Resource references, not baked values - see the root grain note in BuildUi.
            var canvasGrainB = new Border { IsHitTestVisible = false };
            canvasGrainB.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
            canvasGrainB.SetResourceReference(UIElement.OpacityProperty, "GrainOpacity");
            canvasStack.Children.Add(canvasGrainB);
            // A Border's ClipToBounds clips to its RECTANGLE, not to its CornerRadius, so the
            // canvas underneath kept painting square corners through the rounded frame. Clip the
            // content to a rounded geometry of its own instead, resized with the pane.
            // Radius comes from the theme (ControlCornerRadius, 0 on a squared-off theme) instead of
            // a hardcoded 4, so 98SE gets real square corners like every other pane.
            canvasStack.ClipToBounds = false;
            canvasStack.SizeChanged += (_, e) =>
            {
                double r = Application.Current?.TryFindResource("ControlCornerRadius") is CornerRadius cr
                    ? cr.TopLeft : 4;
                canvasStack.Clip = new RectangleGeometry(new Rect(e.NewSize), r, r);
            };

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                Child = canvasStack,
            };
            frame.SetResourceReference(Border.CornerRadiusProperty, "ControlCornerRadius");
            frame.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "PaneBorderBrush");
            frame.SetResourceReference(Border.BorderThicknessProperty, "AboutPanelBorderThickness");

            // The shadow rides a SEPARATE sibling behind the pane, never the pane itself - an
            // Effect applies to an element's whole rendering, children included, and content
            // drawn through a bitmap effect loses ClearType. Family rule, same as the About card.
            _frameShadow = new Border
            {
                IsHitTestVisible = false,
                Effect = CardShadowOrNull(),   // null on a 0-opacity theme, never an invisible effect
            };
            _frameShadow.SetResourceReference(Border.CornerRadiusProperty, "ControlCornerRadius");
            _frameShadow.SetResourceReference(Border.BackgroundProperty, "PaneBrush");

            var frameHost = new Grid();
            frameHost.Children.Add(_frameShadow);
            frameHost.Children.Add(frame);


            // SUNKEN bevel, same as the About card's info panel: the brushes are CROSSED relative to
            // a raised control - dark on the top/left, light on the bottom/right - so the drawing
            // pane reads as a recessed Win98 client area. Transparent and zero-thickness on every
            // theme that does not define bevels, so it draws nothing there.
            // TWO tones, the same PaneBevel* pair the main window's content pane and notes list
            // use - #808080 then #000000 down the top/left, #ffffff then the face up the
            // bottom/right. This drew a single flat gray off the shared control Bevel* keys, which
            // had the right width and the wrong depth: it read as a line rather than an edge, and
            // was visibly shallower than the panes in the main window. (2026-08-07)
            var sunkDark = new Border { IsHitTestVisible = false };
            sunkDark.SetResourceReference(Border.BorderBrushProperty, "PaneBevelDarkBrush");
            sunkDark.SetResourceReference(Border.BorderThicknessProperty, "PaneBevelLightThickness");
            var sunkLight = new Border { IsHitTestVisible = false };
            sunkLight.SetResourceReference(Border.BorderBrushProperty, "PaneBevelLightBrush");
            sunkLight.SetResourceReference(Border.BorderThicknessProperty, "PaneBevelDarkThickness");
            var sunkDark2 = new Border { IsHitTestVisible = false };
            sunkDark2.SetResourceReference(Border.BorderBrushProperty, "PaneBevelDark2Brush");
            sunkDark2.SetResourceReference(Border.BorderThicknessProperty, "PaneBevel2LightThickness");
            sunkDark2.SetResourceReference(FrameworkElement.MarginProperty, "PaneBevelInnerMargin");
            var sunkLight2 = new Border { IsHitTestVisible = false };
            sunkLight2.SetResourceReference(Border.BorderBrushProperty, "PaneBevelLight2Brush");
            sunkLight2.SetResourceReference(Border.BorderThicknessProperty, "PaneBevel2DarkThickness");
            sunkLight2.SetResourceReference(FrameworkElement.MarginProperty, "PaneBevelInnerMargin");
            frameHost.Children.Add(sunkDark);
            frameHost.Children.Add(sunkLight);
            frameHost.Children.Add(sunkDark2);
            frameHost.Children.Add(sunkLight2);
            Grid.SetColumn(frameHost, 1);
            row.Children.Add(frameHost);

            Grid.SetRow(row, 2);
            grid.Children.Add(row);
        }

        private void BuildButtons(Grid grid)
        {
            // Bottom action row, centered: Copy to clipboard (the same flattened image Print makes),
            // then Print to note (stamps the drawing inline at the caret, keeps the pad open; Ctrl+Enter).
            // The title-bar X closes the pad.
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, _flatChrome ? 8 : 12, 0, 0) };

            var copy = new Button { Content = L("Str_Sketch_CopyImage", "Copy to clipboard"), MinWidth = 110, Height = 30, Margin = new Thickness(0, 0, 8, 0), Style = Application.Current.TryFindResource("OutlineButton") as Style };
            Tip(copy, L("Str_Sketch_CopyImageTip", "Copy the drawing to the clipboard as an image"));
            copy.Click += (_, _) => CopyToClipboard();
            actions.Children.Add(copy);

            var print = new Button { Content = L("Str_Btn_CalcPrint", "Print to note"), MinWidth = 110, Height = 30, IsDefault = true, Style = Application.Current.TryFindResource("OutlineButton") as Style };
            Tip(print, L("Str_Sketch_Print", "Print to note (Ctrl+Enter)"));
            print.Click += (_, _) => _print(_objects, _canvasW, _canvasH);
            actions.Children.Add(print);

            Grid.SetRow(actions, 3);
            grid.Children.Add(actions);
        }

        // Copy the current drawing to the Windows clipboard as an image (the same flattened bitmap
        // Print produces). No-op on an empty canvas.
        private void CopyToClipboard()
        {
            if (_objects.Count == 0) return;
            try
            {
                Clipboard.SetImage(SketchModel.RenderObjects(_objects, _canvasW, _canvasH));
            }
            catch { /* clipboard busy - nothing to do */ }
        }

        // The corner resize grip moved to DialogChrome.ResizeGrip - ONE builder shared with the
        // Dictation pad, so the two grips cannot drift apart again.

        private void RebuildSwatches()
        {
            _swatchRow.Children.Clear();
            _swatchRow.Children.Add(Swatch(DefaultPen));   // bone (the default pen) pinned first, ahead of the user swatches
            foreach (var c in ColorPickerDialog.UserSwatches())
                _swatchRow.Children.Add(Swatch(c));
        }

        private void OpenColorPicker()
        {
            var picker = new ColorPickerDialog(this, _penColor);
            // Confirmed, not ShowDialog() == true: the close fade nulls DialogResult
            // (ColorPickerDialog.Confirmed doc).
            picker.ShowDialog();
            bool ok = picker.Confirmed;
            RebuildSwatches();   // Replace / Reset in the picker may have edited the shared swatches
            if (ok) PickColor(picker.SelectedColor);
        }
    }
}
