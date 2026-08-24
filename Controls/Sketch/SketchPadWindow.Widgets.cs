using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KillerNotes.Controls
{
    internal sealed partial class SketchPadWindow
    {
        // ---- Widget builders ----

        private static Style? Surface() => Application.Current.TryFindResource("SurfaceButton") as Style;

        private static void Tip(FrameworkElement fe, string tip)
        {
            fe.ToolTip = tip;
            ToolTipService.SetInitialShowDelay(fe, 350);
            ToolTipService.SetShowDuration(fe, 12000);
            ToolTipService.SetShowOnDisabled(fe, true);
        }

        private static TextBlock Glyph(int codepoint) => new()
        {
            Text = char.ConvertFromUtf32(codepoint),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private Button ToolButton(UIElement content, string tooltip, Tool tool)
        {
            var b = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = content, Style = Surface(),
            };
            Tip(b, tooltip);
            b.Click += (_, _) => SetTool(tool);
            _toolBtns[tool] = b;
            return b;
        }

        private static Button ActionButton(UIElement content, string tooltip, Action onClick)
        {
            var b = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = content, Style = Surface(),
            };
            Tip(b, tooltip);
            b.Click += (_, _) => onClick();
            return b;
        }

        private static Border Separator()
        {
            var b = new Border { Width = 1, Height = 22, Margin = new Thickness(3, 3, 7, 3) };
            b.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            return b;
        }

        private Button FillButton()
        {
            _fillSquare = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent };
            _fillSquare.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            var b = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = _fillSquare, Style = Surface(),
            };
            Tip(b, L("Str_Sketch_Fill", "Fill shapes (rectangle / ellipse)"));
            b.Click += (_, _) => ToggleFill();
            return b;
        }

        private Button OpacityButton()
        {
            _opacityText = new TextBlock { Text = OpacityLabel(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _opacityText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var b = new Button
            {
                Width = 42, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = _opacityText, Style = Surface(),
            };
            Tip(b, L("Str_Sketch_Opacity", "Fill / bucket opacity (click to cycle)"));
            b.Click += (_, _) => CycleOpacity();
            return b;
        }

        private Button WidthButton()
        {
            _widthDot = new Ellipse { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _widthDot.InkFill();
            SetWidthDot();
            _widthBtn = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = _widthDot, Style = Surface(),
            };
            Tip(_widthBtn, L("Str_Sketch_Width", "Brush size (click to cycle)"));
            _widthBtn.Click += (_, _) => CycleWidth();
            return _widthBtn;
        }

        private static Viewbox IconWrap(UIElement shape) => new() { Width = 17, Height = 17, Child = shape, Stretch = Stretch.Uniform };


        private static UIElement IconLine()
        {
            var l = new Line { X1 = 2, Y1 = 14, X2 = 14, Y2 = 2, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            l.InkStroke();
            return IconWrap(l);
        }

        // Line shaft + a solid filled arrowhead, so the arrow tool reads clearly apart from the line.
        private static UIElement IconArrow()
        {
            var g = new Grid { Width = 17, Height = 17 };
            var shaft = new Line { X1 = 2, Y1 = 15, X2 = 11, Y2 = 6, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            shaft.InkStroke();
            var head = new Polygon();
            foreach (var p in new[] { new Point(15.5, 1.5), new Point(8, 4), new Point(13, 9) }) head.Points.Add(p);
            head.InkFill();
            g.Children.Add(shaft);
            g.Children.Add(head);
            return g;
        }

        private static UIElement IconRect()
        {
            var r = new Rectangle { Width = 14, Height = 10, StrokeThickness = 2 };
            r.InkStroke();
            return IconWrap(r);
        }

        private static UIElement IconEllipse()
        {
            var el = new Ellipse { Width = 14, Height = 12, StrokeThickness = 2 };
            el.InkStroke();
            return IconWrap(el);
        }

        private static UIElement IconPolygon()
        {
            var pg = new Polygon { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            foreach (var p in new[] { new Point(8.5, 1), new Point(16, 6.5), new Point(13, 15.5), new Point(4, 15.5), new Point(1, 6.5) }) pg.Points.Add(p);
            pg.InkStroke();
            return IconWrap(pg);
        }

        // A HAND, not a marquee. The dashed rectangle is the universal "rubber-band a region"
        // icon, and this tool does not band a region - it picks a single object up and carries it,
        // which is what the app's own open-hand drag cursor already says everywhere else.
        //
        // Built from separate rounded shapes rather than one outline path: a hand drawn as a
        // single contour has to run back up the side of each finger, which self-overlaps and
        // renders holes under the default fill rule. Same composition IconImage and IconBucket
        // use, so it cannot misrender.
        private static UIElement IconSelect()
        {
            var c = new Canvas { Width = 16, Height = 16 };

            // Four fingers, tallest in the middle, then the palm over their base so the joins
            // disappear. Drawn before the palm so the palm covers the finger roots.
            foreach (var (x, top, h) in new[]
                     {
                         (3.5, 5.0, 5.0),
                         (5.4, 2.6, 7.0),
                         (7.3, 3.2, 6.6),
                         (9.2, 5.4, 4.6),
                     })
            {
                var f = new Rectangle { Width = 1.8, Height = h, RadiusX = 0.9, RadiusY = 0.9 };
                f.InkFill();
                Canvas.SetLeft(f, x); Canvas.SetTop(f, top);
                c.Children.Add(f);
            }

            // Thumb, tucked against the left of the palm.
            var thumb = new Rectangle { Width = 1.9, Height = 4.2, RadiusX = 0.95, RadiusY = 0.95 };
            thumb.InkFill();
            thumb.RenderTransform = new RotateTransform(-28, 0.95, 2.1);
            Canvas.SetLeft(thumb, 1.9); Canvas.SetTop(thumb, 8.2);
            c.Children.Add(thumb);

            var palm = new Rectangle { Width = 7.9, Height = 6.6, RadiusX = 2.4, RadiusY = 2.4 };
            palm.InkFill();
            Canvas.SetLeft(palm, 3.2); Canvas.SetTop(palm, 8.0);
            c.Children.Add(palm);

            return IconWrap(c);
        }

        // Magnifier: a ring and a handle. Drawn rather than a Segoe MDL2 glyph for the same reason
        // the other tool icons are - a glyph is only confirmed by rendering it, and these have to
        // be right the first time.
        private static UIElement IconZoom()
        {
            var c = new Canvas { Width = 16, Height = 16 };
            var ring = new Ellipse { Width = 10, Height = 10, StrokeThickness = 1.6, Fill = null };
            ring.InkStroke();
            Canvas.SetLeft(ring, 1); Canvas.SetTop(ring, 1);
            var handle = new Line { X1 = 9.6, Y1 = 9.6, X2 = 14.6, Y2 = 14.6, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            handle.InkStroke();
            c.Children.Add(ring);
            c.Children.Add(handle);
            return IconWrap(c);
        }

        // Crop: the photographer's two overlapping L brackets. The DASHED rectangle lives here
        // now - it is the marquee gesture's own icon, and this is the tool that actually bands a
        // region, which is why the select tool no longer wears it.
        private static UIElement IconCrop()
        {
            var c = new Canvas { Width = 16, Height = 16 };
            // Top-left bracket: down the left edge, then across the top.
            var tl = new System.Windows.Shapes.Path
            {
                StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Miter,
                Data = Geometry.Parse("M4,1 L4,12 L15,12"),
            };
            tl.InkStroke();
            // Bottom-right bracket, offset so the two cross and read as a crop frame.
            var br = new System.Windows.Shapes.Path
            {
                StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Miter,
                Data = Geometry.Parse("M1,4 L12,4 L12,15"),
            };
            br.InkStroke();
            c.Children.Add(tl);
            c.Children.Add(br);
            return IconWrap(c);
        }

        private static UIElement IconImage()
        {
            var c = new Canvas { Width = 16, Height = 16 };
            var frame = new Rectangle { Width = 16, Height = 13, RadiusX = 1.5, RadiusY = 1.5, StrokeThickness = 1.4, Fill = null };
            frame.InkStroke();
            Canvas.SetLeft(frame, 0); Canvas.SetTop(frame, 1.5);
            var mtn = new Polygon();
            foreach (var p in new[] { new Point(1, 13), new Point(6, 7.5), new Point(9.5, 11), new Point(11.5, 9), new Point(15, 13) }) mtn.Points.Add(p);
            mtn.InkFill();
            var sun = new Ellipse { Width = 3, Height = 3 };
            sun.InkFill();
            Canvas.SetLeft(sun, 3); Canvas.SetTop(sun, 4);
            c.Children.Add(frame); c.Children.Add(mtn); c.Children.Add(sun);
            return IconWrap(c);
        }

        // Text tool marker: a bold "A", the mini-Paint convention for a text tool.
        private static UIElement IconText()
        {
            var t = new TextBlock
            {
                Text = "A", FontSize = 16, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return t;
        }

        private static UIElement IconBucket()
        {
            // A proper paint bucket: wire handle, tapered body, and accent-colored paint in the opening.
            var c = new Canvas { Width = 17, Height = 17 };
            var handle = new System.Windows.Shapes.Path { StrokeThickness = 1.4, Data = Geometry.Parse("M5,5.5 C5,1.5 12,1.5 12,5.5") };
            handle.InkStroke();
            var body = new System.Windows.Shapes.Path { StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, Data = Geometry.Parse("M2.5,6 L4.3,15.2 Q8.5,16.6 12.7,15.2 L14.5,6 Z") };
            body.InkStroke();
            body.SetResourceReference(Shape.FillProperty, "MutedTextBrush");
            var rim = new Ellipse { Width = 12, Height = 3.4, StrokeThickness = 1.2 };
            rim.InkStroke();
            rim.SetResourceReference(Shape.FillProperty, "PrimaryBrush");
            Canvas.SetLeft(rim, 2.5); Canvas.SetTop(rim, 4.3);
            c.Children.Add(handle);
            c.Children.Add(body);
            c.Children.Add(rim);
            return IconWrap(c);
        }

        private Border Swatch(Color c)
        {
            var b = new Border
            {
                Width = 22, Height = 22,
                Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            // Radius from the theme, not a hardcoded 3. A square-cornered palette squares these off
            // with everything else in it - a Win98 color swatch is a hard-edged square.
            b.SetResourceReference(Border.CornerRadiusProperty, "SmallCornerRadius");
            b.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
            Tip(b, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
            b.MouseLeftButtonUp += (_, _) => PickColor(c);
            return b;
        }

        // The three wordmark runs. Pass a flat brush for the drop-shadow copy, or resource keys for the
        // real one so its colors follow a live theme change (DynamicResource).
        private static TextBlock WordmarkText(FontFamily? wf, string killerKey, string notesKey, string subKey, Brush? flat = null)
        {
            var tb = new TextBlock { FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            if (wf != null) tb.FontFamily = wf;
            var killer = new Run("Killer");
            var notes = new Run("Notes") { FontSize = 19.5, FontWeight = FontWeights.Bold };
            var sub = new Run("  " + L("Str_Sketch_Title", "SketchPad")) { FontSize = 18 };
            if (flat != null) { killer.Foreground = flat; notes.Foreground = flat; sub.Foreground = flat; }
            else
            {
                killer.SetResourceReference(TextElement.ForegroundProperty, killerKey);
                notes.SetResourceReference(TextElement.ForegroundProperty, notesKey);
                sub.SetResourceReference(TextElement.ForegroundProperty, subKey);
            }
            tb.Inlines.Add(killer); tb.Inlines.Add(notes); tb.Inlines.Add(sub);
            return tb;
        }

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        // The family close X, from DialogChrome. This used to be a 46x36 block that filled SOLID
        // RED with a white glyph on hover - the Windows caption treatment, which is the one thing
        // every other dialog in the app deliberately avoids, and it made the SketchPad the odd one
        // out. It also hardcoded a 36px height into a caption band that DialogTitleBarHeight sizes
        // to 28 (20 on 98SE), so it hung 8px into the toolbar row underneath.
        private FrameworkElement CloseButton(string tooltip)
            => DialogChrome.CloseGlyph(tooltip, Close);
    }

    /// <summary>
    /// Ink for the SketchPad's hand-drawn tool icons.
    ///
    /// These bind to the OWNING BUTTON's Foreground rather than pinning to TextBrush. The active
    /// tool is filled with SelectionBg and given SelectionFg; ink pinned to TextBrush ignored that,
    /// so on any theme whose text is dark the selected tool's icon went dark-on-dark and vanished -
    /// exactly the icon the highlight exists to point at. Unselected buttons carry no Foreground of
    /// their own, so the binding resolves to the button style's value, which is the TextBrush these
    /// used to read directly. Every icon here lives inside a Button.
    ///
    /// A separate static class because extension methods cannot live in a non-static one, and
    /// SketchPadWindow is a Window.
    /// </summary>
    internal static class SketchIcons
    {
        internal static void InkStroke(this Shape s) => Ink(s, Shape.StrokeProperty);
        internal static void InkFill(this Shape s) => Ink(s, Shape.FillProperty);

        private static void Ink(Shape s, DependencyProperty target) =>
            s.SetBinding(target, new System.Windows.Data.Binding(nameof(Control.Foreground))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(
                    System.Windows.Data.RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) }
            });
    }
}
