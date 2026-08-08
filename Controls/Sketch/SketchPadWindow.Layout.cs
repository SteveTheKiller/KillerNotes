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

        private void BuildUi()
        {
            // Floating rounded card with a soft drop shadow, matching the KillerPDF dialog chrome. The
            // 20px transparent halo (Margin) is the room the shadow renders into; squared off and flush
            // when maximized (UpdateWindowCorners).
            _outerBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = CardRadius(),
                Margin = new Thickness(20),
                Effect = CardShadow(),
            };
            _outerBorder.SetResourceReference(Border.BorderBrushProperty, "WindowEdgeBrush");
            _outerBorder.SetResourceReference(Border.BorderThicknessProperty, "WindowEdgeThickness");
            _outerBorder.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            var root = new Grid();
            // Film grain over the window background, same treatment as the rest of the app.
            if (Application.Current.TryFindResource("GrainTileBrush") is Brush grain)
            {
                double grainOp = Application.Current.TryFindResource("GrainOpacity") is double go ? go : 0.12;
                _grainBorder = new Border { Background = grain, Opacity = grainOp, IsHitTestVisible = false, CornerRadius = CardRadius() };
                root.Children.Add(_grainBorder);
            }

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

            var grid = new Grid { Margin = new Thickness(16, 6, 16, 12) };
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

            // Resize-grip dots in the bottom-right corner - press them to start a corner resize.
            root.Children.Add(BuildResizeGrip());
            // Last, so the raised edge draws over everything else in the card.
            root.Children.Add(_bevelLight);
            root.Children.Add(_bevelDark);

            // ONE window frame for the whole app - DialogChrome.WindowFrame, the same builder the
            // Dictation pad, the About card and every dialog use. The card's _bevelLight/_bevelDark
            // pair above is the shared CONTROL bevel and is a different thing; it is why this
            // window read as flat beside the main one on a bevelled theme.
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
            // themes, so it must be a resource reference rather than a copied colour.
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
            // sat hard against the top edge instead of centred in the bar.
            Grid.SetColumn(_closeBtn, 1);
            titleBar.Children.Add(_closeBtn);

            Grid.SetRow(titleBar, 0);
            shell.Children.Add(titleBar);
        }

        private void BuildToolBar(Grid grid)
        {
            // Top strip (row 1): the color palette on the left, undo / redo / clear on the right. The
            // drawing tools live in the left rail (BuildToolRail), MS-Paint style.
            var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // palette
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        // actions

            var palette = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _swatchRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            palette.Children.Add(_swatchRow);
            palette.Children.Add(ActionButton(Glyph(0xE790), L("Str_Sketch_MoreColors", "Custom color..."), OpenColorPicker));
            Grid.SetColumn(palette, 0);
            top.Children.Add(palette);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            _undoBtn = ActionButton(Glyph(0xE7A7), L("Str_Sketch_Undo", "Undo (Ctrl+Z)"), Undo);
            _redoBtn = ActionButton(Glyph(0xE7A6), L("Str_Sketch_Redo", "Redo (Ctrl+Y)"), Redo);
            actions.Children.Add(_undoBtn);
            actions.Children.Add(_redoBtn);
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
            var rail = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0) };
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
            Add(ActionButton(IconImage(), L("Str_Sketch_AddImage", "Add an image (I) - drag one onto the pad, or Ctrl+V to paste"), AddImageFromFile));
            return rail;
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

            var canvasStack = new Grid();
            canvasStack.Children.Add(_canvas);   // fills the frame 1:1 and grows with the window
            if (Application.Current.TryFindResource("GrainTileBrush") is Brush canvasGrain)
            {
                double cop = Application.Current.TryFindResource("GrainOpacity") is double cg ? cg : 0.12;
                canvasStack.Children.Add(new Border { Background = canvasGrain, Opacity = cop, IsHitTestVisible = false });
            }
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
            var frameShadow = new Border
            {
                IsHitTestVisible = false,
                Effect = CardShadow(),
            };
            frameShadow.SetResourceReference(Border.CornerRadiusProperty, "ControlCornerRadius");
            frameShadow.SetResourceReference(Border.BackgroundProperty, "PaneBrush");

            var frameHost = new Grid();
            frameHost.Children.Add(frameShadow);
            frameHost.Children.Add(frame);

            // SUNKEN bevel, same as the About card's info panel: the brushes are CROSSED relative to
            // a raised control - dark on the top/left, light on the bottom/right - so the drawing
            // pane reads as a recessed Win98 client area. Transparent and zero-thickness on every
            // theme that does not define bevels, so it draws nothing there.
            // TWO tones, the same PaneBevel* pair the main window's content pane and notes list
            // use - #808080 then #000000 down the top/left, #ffffff then the face up the
            // bottom/right. This drew a single flat grey off the shared control Bevel* keys, which
            // had the right width and the wrong depth: it read as a line rather than an edge, and
            // was visibly shallower than the panes in the main window. (Steve, 2026-08-07.)
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
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };

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

        // Classic diagonal grip dots in the bottom-right corner - a REAL handle: pressing it starts a
        // bottom-right corner resize through the OS. The window's own resize border lives out in the
        // transparent shadow halo (easy to miss), so grabbing the visible dots is the reliable way.
        private UIElement BuildResizeGrip()
        {
            var c = new Canvas
            {
                Width = 18, Height = 18, Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
            };
            // TWO grips in the same 18x18 slot, each shown by its own visibility key - the same
            // pair the main window uses (MainWindow.xaml). The family standard is six 2x2 dots;
            // a Win98-style theme swaps in the era's diagonal bevelled bands instead. This window
            // only ever had the dots, so it stayed on them while the main window changed.
            // (Steve, 2026-08-07.)
            var dots = new Canvas { Width = 18, Height = 18, IsHitTestVisible = false };
            dots.SetResourceReference(UIElement.VisibilityProperty, "GripDotsVisibility");
            void Dot(double x, double y)
            {
                var d = new Ellipse { Width = 2.4, Height = 2.4 };
                d.SetResourceReference(Shape.FillProperty, "MutedTextBrush");
                Canvas.SetLeft(d, x); Canvas.SetTop(d, y);
                dots.Children.Add(d);
            }
            Dot(15, 6);
            Dot(10.5, 10.5); Dot(15, 10.5);
            Dot(6, 15); Dot(10.5, 15); Dot(15, 15);
            c.Children.Add(dots);

            // Diagonal bands. Half-pixel centres so a 1px line lands on one row instead of
            // smearing across two, and each band is a dark line with a light one under it - the
            // bevelled hatch, not a set of plain strokes.
            var hatch = new Canvas { Width = 18, Height = 18, IsHitTestVisible = false };
            hatch.SetResourceReference(UIElement.VisibilityProperty, "GripHatchVisibility");
            void Band(double off, string brushKey)
            {
                var l = new System.Windows.Shapes.Line
                {
                    X1 = 16.5, Y1 = off, X2 = off, Y2 = 16.5, StrokeThickness = 1,
                };
                l.SetResourceReference(Shape.StrokeProperty, brushKey);
                hatch.Children.Add(l);
            }
            Band(5.5, "BevelDarkBrush");  Band(6.5, "BevelLightBrush");
            Band(9.5, "BevelDarkBrush");  Band(10.5, "BevelLightBrush");
            Band(13.5, "BevelDarkBrush"); Band(14.5, "BevelLightBrush");
            c.Children.Add(hatch);

            c.MouseLeftButtonDown += (_, e) => { StartCornerResize(); e.Handled = true; };
            return c;
        }

        // Kick off an OS-driven bottom-right corner resize (from the grip dots).
        private void StartCornerResize()
        {
            if (WindowState == WindowState.Maximized) return;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)17 /* HTBOTTOMRIGHT */, IntPtr.Zero);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

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
            bool ok = picker.ShowDialog() == true;
            RebuildSwatches();   // Replace / Reset in the picker may have edited the shared swatches
            if (ok) PickColor(picker.SelectedColor);
        }
    }
}
