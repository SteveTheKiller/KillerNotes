using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KillerNotes.Models;

namespace KillerNotes.Controls
{
    internal sealed partial class SketchPadWindow
    {
        private void RenderCanvas()
        {
            _canvas.Children.Clear();
            foreach (var o in _objects)
            {
                if (ReferenceEquals(o, _textEditTarget)) continue;   // its inline editor is open; don't draw the original underneath
                _canvas.Children.Add(SketchModel.BuildElement(o));
            }
            if (_sel != null && _objects.Contains(_sel))
            {
                var b = SketchModel.BoundsOf(_sel);
                if (!b.IsEmpty)
                {
                    var box = new Rectangle
                    {
                        Width = b.Width + 8, Height = b.Height + 8, StrokeThickness = 1.2,
                        StrokeDashArray = [4, 3], IsHitTestVisible = false, Fill = null,
                    };
                    box.SetResourceReference(Shape.StrokeProperty, "PrimaryBrush");
                    Canvas.SetLeft(box, b.X - 4); Canvas.SetTop(box, b.Y - 4);
                    _canvas.Children.Add(box);
                    if (_arcTarget == null)   // hide handles while a click-to-solidify arc is in progress
                    {
                        // Corner handles: drag to scale (images stay proportional, shapes free).
                        AddHandle(b.Left, b.Top, 0, Cursors.SizeNWSE);
                        AddHandle(b.Right, b.Top, 1, Cursors.SizeNESW);
                        AddHandle(b.Right, b.Bottom, 2, Cursors.SizeNWSE);
                        AddHandle(b.Left, b.Bottom, 3, Cursors.SizeNESW);
                        // A line/arrow also gets a round "bend" handle at its control point (or midpoint).
                        if (_sel.Kind is SketchKind.Line or SketchKind.Arrow) AddCurveHandle(_sel);
                    }
                }
            }
            // Keep the eraser ring on top after a re-render while the pointer is over the canvas.
            if (_tool == Tool.Eraser && _eraseCursor != null && _canvas.IsMouseOver && !_canvas.Children.Contains(_eraseCursor))
                _canvas.Children.Add(_eraseCursor);
            _previewEl = null;   // cleared with the children; gesture handlers re-add if needed
        }

        private void AddHandle(double x, double y, int index, Cursor cursor)
        {
            var h = new Rectangle { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 1, Cursor = cursor };
            h.SetResourceReference(Shape.FillProperty, "PrimaryBrush");
            Canvas.SetLeft(h, x - 5); Canvas.SetTop(h, y - 5);
            h.MouseLeftButtonDown += (_, e) => { ResizeStart(index); e.Handled = true; };
            _canvas.Children.Add(h);
        }

        private void AddCurveHandle(SketchObject o)
        {
            double hx, hy;
            if (o.Pts.Count >= 6) { hx = o.Pts[2]; hy = o.Pts[3]; }
            else { hx = (o.Pts[0] + o.Pts[^2]) / 2; hy = (o.Pts[1] + o.Pts[^1]) / 2; }
            var h = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 1, Cursor = Cursors.Hand };
            h.SetResourceReference(Shape.FillProperty, "PrimaryBrush");
            Canvas.SetLeft(h, hx - 6); Canvas.SetTop(h, hy - 6);
            h.MouseLeftButtonDown += (_, e) => { CurveStart(Clamp(e.GetPosition(_canvas))); e.Handled = true; };
            _canvas.Children.Add(h);
        }

        // "Arc line": enter live-bend mode. Select the line, hide its handles, and let mouse movement
        // shape the curve (CanvasMove -> CurveMove) until a click solidifies it (CanvasDown). PushUndo
        // now so the whole bend is a single undo step.
        private void StartArc(SketchObject o)
        {
            PushUndo();
            _sel = o;
            SetTool(Tool.Select);
            _arcTarget = o;
            RenderCanvas();
            // Capture the mouse so the live bend tracks - and the solidify click registers - no matter
            // where the cursor is, even off the canvas or off the window. Deferred so the closing context
            // menu releases its own capture first.
            Dispatcher.BeginInvoke(new Action(() => { if (_arcTarget != null) _canvas.CaptureMouse(); }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        // "Straighten line": drop the control point, back to a plain two-point line/arrow.
        private void Straighten(SketchObject o)
        {
            if (o.Pts.Count < 6) return;
            PushUndo();
            double x0 = o.Pts[0], y0 = o.Pts[1], x1 = o.Pts[^2], y1 = o.Pts[^1];
            o.Pts.Clear();
            o.Pts.Add(x0); o.Pts.Add(y0); o.Pts.Add(x1); o.Pts.Add(y1);
            RenderCanvas();
        }

        private void CurveStart(Point p)
        {
            if (_sel == null) return;
            PushUndo();
            _curving = true;
            _canvas.CaptureMouse();
            CurveMove(p);
        }

        private void CurveMove(Point p)
        {
            if (_sel == null || _sel.Kind is not (SketchKind.Line or SketchKind.Arrow)) return;
            var o = _sel;
            double x0 = o.Pts[0], y0 = o.Pts[1];
            double x1 = o.Pts[^2], y1 = o.Pts[^1];
            o.Pts.Clear();
            o.Pts.Add(x0); o.Pts.Add(y0);
            o.Pts.Add(p.X); o.Pts.Add(p.Y);
            o.Pts.Add(x1); o.Pts.Add(y1);
            RenderCanvas();
        }

        private string? FillHex()
            => _fill ? SketchModel.HexOf(Color.FromArgb(_fillAlpha, _penColor.R, _penColor.G, _penColor.B)) : null;

        // ---- Text tool ----

        // Open an inline TextBox over the canvas: a new label at `at`, or `existing` re-edited in
        // place (its original is hidden by RenderCanvas until the edit commits). Enter or a click
        // elsewhere commits; Esc cancels; an empty commit places nothing (or deletes the object).
        private void BeginTextEntry(Point at, SketchObject? existing)
        {
            if (_textBox != null) CommitTextEntry();
            _textAt = at;
            _textEditTarget = existing;
            if (existing != null) { _sel = null; RenderCanvas(); }   // hide the original while its editor is open

            var color = existing != null ? ColorOf(existing.Color) : _penColor;
            var tb = new TextBox
            {
                Text = existing?.Text ?? "",
                FontSize = existing?.FontSize ?? 24,
                FontWeight = (existing?.Bold ?? false) ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(color),
                CaretBrush = new SolidColorBrush(color),
                Background = R("PaneBrush"),               // matches the canvas, so only the accent border reads
                BorderBrush = R("PrimaryBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                AcceptsReturn = true,                      // Shift+Enter adds a line; Enter commits
                MinWidth = 24,
            };
            tb.PreviewKeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { CommitTextEntry(); ke.Handled = true; }
                else if (ke.Key == Key.Escape) { CancelTextEntry(); ke.Handled = true; }
            };
            tb.LostKeyboardFocus += (_, _) => CommitTextEntry();

            _textBox = tb;
            Canvas.SetLeft(tb, at.X); Canvas.SetTop(tb, at.Y);
            _canvas.Children.Add(tb);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.Focus();
                if (existing != null) tb.SelectAll(); else tb.CaretIndex = tb.Text.Length;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // Bank the typed label: update the edited object, drop it if emptied, or add a new one.
        private void CommitTextEntry()
        {
            if (_textBox == null) return;
            var tb = _textBox;
            _textBox = null;
            _canvas.Children.Remove(tb);
            string text = tb.Text.Replace("\r\n", "\n").Trim();
            var target = _textEditTarget;
            _textEditTarget = null;

            SketchObject? result = null;
            if (target != null)
            {
                if (text.Length == 0) { PushUndo(); _objects.Remove(target); }
                else if (text != target.Text) { PushUndo(); target.Text = text; result = target; }
                else result = target;
            }
            else if (text.Length > 0)
            {
                PushUndo();
                result = new SketchObject { Kind = SketchKind.Text, Text = text, X = _textAt.X, Y = _textAt.Y, Color = SketchModel.HexOf(_penColor) };
                _objects.Add(result);
            }

            _sel = result;
            if (result != null) SetTool(Tool.Select);   // land in Select so the new label can be nudged / resized
            RenderCanvas();
        }

        // Abandon the edit; the original (if any) comes back untouched.
        private void CancelTextEntry()
        {
            if (_textBox == null) return;
            var tb = _textBox;
            _textBox = null;
            _textEditTarget = null;
            _canvas.Children.Remove(tb);
            RenderCanvas();
        }

        private static Color ColorOf(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return DefaultPen; }
        }
    }
}
