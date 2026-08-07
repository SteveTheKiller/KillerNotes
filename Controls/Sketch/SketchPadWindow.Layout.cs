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
            _outerBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
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

            // Red close button flush in the card's top-right corner (rounded only on that corner), so
            // it hugs the window edge like KillerPDF's dialogs rather than floating as a pill.
            _closeBtn = CloseButton(L("Str_Sketch_Close", "Close (Esc)"));
            _closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            _closeBtn.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(_closeBtn);

            // Resize-grip dots in the bottom-right corner - press them to start a corner resize.
            root.Children.Add(BuildResizeGrip());
            // Last, so the raised edge draws over everything else in the card.
            root.Children.Add(_bevelLight);
            root.Children.Add(_bevelDark);

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
            titleBar.SetResourceReference(FrameworkElement.HeightProperty, "DialogTitleBarHeight");
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.ClickCount == 2) ToggleMaximize();
                else DragMove();
            };

            var wf = Application.Current.TryFindResource("WordmarkFont") as FontFamily;
            var mark = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                                  Margin = new Thickness(16, 0, 0, 0) };
            var shadowInk = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0));
            var shadow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 2, 0, 0) };
            if (Application.Current.TryFindResource("IconShadowOpacity") is double sop) shadow.Opacity = sop;
            shadow.Effect = new BlurEffect { Radius = 3 };
            shadow.Children.Add(WordmarkText(wf, "", "", "", shadowInk));
            mark.Children.Add(shadow);
            // ChromeTextBrush, not TextBrush: this sits on the title BAND now, which on several
            // themes is a dark gradient. TextBrush is the colour for the content surface (black on
            // 98SE) and vanished against it. ChromeTextBrush is the title-bar text colour and is
            // what the main window's wordmark uses.
            mark.Children.Add(WordmarkText(wf, "ChromeTextBrush", "AccentLogo", "MutedTextBrush"));
            Grid.SetColumn(mark, 0);
            titleBar.Children.Add(mark);

            // (The red close button lives at the card's top-right corner - added in BuildUi. Reserve the
            // right column so the wordmark never runs under it.)
            var spacer = new Border { Width = 48, Background = Brushes.Transparent };
            Grid.SetColumn(spacer, 1);
            titleBar.Children.Add(spacer);

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
            canvasStack.ClipToBounds = false;
            canvasStack.SizeChanged += (_, e) =>
                canvasStack.Clip = new RectangleGeometry(new Rect(e.NewSize), 4, 4);

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = canvasStack,
            };
            frame.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

            // The shadow rides a SEPARATE sibling behind the pane, never the pane itself - an
            // Effect applies to an element's whole rendering, children included, and content
            // drawn through a bitmap effect loses ClearType. Family rule, same as the About card.
            var frameShadow = new Border
            {
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
                Effect = CardShadow(),
            };
            frameShadow.SetResourceReference(Border.BackgroundProperty, "PaneBrush");

            var frameHost = new Grid();
            frameHost.Children.Add(frameShadow);
            frameHost.Children.Add(frame);
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
            void Dot(double x, double y)
            {
                var d = new Ellipse { Width = 2.4, Height = 2.4 };
                d.SetResourceReference(Shape.FillProperty, "MutedTextBrush");
                Canvas.SetLeft(d, x); Canvas.SetTop(d, y);
                c.Children.Add(d);
            }
            Dot(15, 6);
            Dot(10.5, 10.5); Dot(15, 10.5);
            Dot(6, 15); Dot(10.5, 15); Dot(15, 15);
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
