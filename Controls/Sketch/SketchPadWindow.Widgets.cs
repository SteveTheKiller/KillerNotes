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
            _widthDot.SetResourceReference(Shape.FillProperty, "TextBrush");
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
            l.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(l);
        }

        // Line shaft + a solid filled arrowhead, so the arrow tool reads clearly apart from the line.
        private static UIElement IconArrow()
        {
            var g = new Grid { Width = 17, Height = 17 };
            var shaft = new Line { X1 = 2, Y1 = 15, X2 = 11, Y2 = 6, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            shaft.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            var head = new Polygon();
            foreach (var p in new[] { new Point(15.5, 1.5), new Point(8, 4), new Point(13, 9) }) head.Points.Add(p);
            head.SetResourceReference(Shape.FillProperty, "TextBrush");
            g.Children.Add(shaft);
            g.Children.Add(head);
            return g;
        }

        private static UIElement IconRect()
        {
            var r = new Rectangle { Width = 14, Height = 10, StrokeThickness = 2 };
            r.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(r);
        }

        private static UIElement IconEllipse()
        {
            var el = new Ellipse { Width = 14, Height = 12, StrokeThickness = 2 };
            el.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(el);
        }

        private static UIElement IconPolygon()
        {
            var pg = new Polygon { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            foreach (var p in new[] { new Point(8.5, 1), new Point(16, 6.5), new Point(13, 15.5), new Point(4, 15.5), new Point(1, 6.5) }) pg.Points.Add(p);
            pg.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(pg);
        }

        private static UIElement IconSelect()
        {
            var r = new Rectangle { Width = 13, Height = 11, StrokeThickness = 1.6, StrokeDashArray = [2, 2], Fill = null };
            r.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(r);
        }

        private static UIElement IconImage()
        {
            var c = new Canvas { Width = 16, Height = 16 };
            var frame = new Rectangle { Width = 16, Height = 13, RadiusX = 1.5, RadiusY = 1.5, StrokeThickness = 1.4, Fill = null };
            frame.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            Canvas.SetLeft(frame, 0); Canvas.SetTop(frame, 1.5);
            var mtn = new Polygon();
            foreach (var p in new[] { new Point(1, 13), new Point(6, 7.5), new Point(9.5, 11), new Point(11.5, 9), new Point(15, 13) }) mtn.Points.Add(p);
            mtn.SetResourceReference(Shape.FillProperty, "TextBrush");
            var sun = new Ellipse { Width = 3, Height = 3 };
            sun.SetResourceReference(Shape.FillProperty, "TextBrush");
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
            handle.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            var body = new System.Windows.Shapes.Path { StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, Data = Geometry.Parse("M2.5,6 L4.3,15.2 Q8.5,16.6 12.7,15.2 L14.5,6 Z") };
            body.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            body.SetResourceReference(Shape.FillProperty, "MutedTextBrush");
            var rim = new Ellipse { Width = 12, Height = 3.4, StrokeThickness = 1.2 };
            rim.SetResourceReference(Shape.StrokeProperty, "TextBrush");
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
                Width = 22, Height = 22, CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
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

        // Red close button in the card's top-right corner, KillerPDF ChromeCloseButton spec: 46x36,
        // red glyph at rest, solid red fill + white glyph on hover, rounded ONLY on the top-right
        // (0,7,0,0) so the fill hugs the window corner. Handles its own press so the title bar's
        // DragMove doesn't fire when clicked. Corner squares off when maximized (UpdateWindowCorners).
        private Border CloseButton(string tooltip)
        {
            var glyph = new TextBlock
            {
                Text = char.ConvertFromUtf32(0xE8BB),
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "DangerRed");
            var bd = new Border
            {
                Width = 46, Height = 36, CornerRadius = new CornerRadius(0, 7, 0, 0),
                Background = Brushes.Transparent, Child = glyph, Cursor = Cursors.Hand,
            };
            Tip(bd, tooltip);
            bd.MouseEnter += (_, _) => { bd.Background = R("DangerRed"); glyph.Foreground = Brushes.White; };
            bd.MouseLeave += (_, _) =>
            {
                bd.Background = Brushes.Transparent;
                glyph.SetResourceReference(TextBlock.ForegroundProperty, "DangerRed");
            };
            bd.MouseLeftButtonDown += (_, e) => e.Handled = true;   // don't let the title bar start a drag
            bd.MouseLeftButtonUp += (_, _) => Close();
            return bd;
        }
    }
}
