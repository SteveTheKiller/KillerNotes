using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KillerNotes.Models;

namespace KillerNotes.Controls
{
    internal sealed partial class SketchPadWindow
    {
        // Live preview element for the in-progress gesture (never part of _objects).
        private void UpdatePreview(Point cur)
        {
            RemovePreview();
            UIElement? el = _tool switch
            {
                Tool.Pen => SketchModel.BuildElement(new SketchObject
                {
                    Kind = SketchKind.Freehand, Color = SketchModel.HexOf(_penColor), Width = _penWidth,
                    Pts = [.._gesturePts],
                }),
                Tool.Line or Tool.Arrow or Tool.Rect or Tool.Ellipse => SketchModel.BuildElement(TempShape(cur)),
                _ => null,
            };
            if (el != null) { el.IsHitTestVisible = false; _previewEl = el; _canvas.Children.Add(el); }
        }

        private void RemovePreview()
        {
            if (_previewEl != null) { _canvas.Children.Remove(_previewEl); _previewEl = null; }
        }

        private SketchObject TempShape(Point end)
        {
            var o = new SketchObject
            {
                Color = SketchModel.HexOf(_penColor), Width = _penWidth,
                Pts = [_start.X, _start.Y, end.X, end.Y],
                Kind = _tool switch
                {
                    Tool.Line => SketchKind.Line,
                    Tool.Arrow => SketchKind.Arrow,
                    Tool.Rect => SketchKind.Rect,
                    Tool.Ellipse => SketchKind.Ellipse,
                    _ => SketchKind.Line,
                },
            };
            if (o.Kind is SketchKind.Rect or SketchKind.Ellipse) o.Fill = FillHex();
            return o;
        }

        private void CommitGesture(Point end)
        {
            SketchObject? obj = null;
            if (_tool == Tool.Pen)
            {
                if (_gesturePts.Count >= 4)
                    obj = new SketchObject
                    {
                        Kind = SketchKind.Freehand, Color = SketchModel.HexOf(_penColor),
                        Width = _penWidth, Pts = [.._gesturePts],
                    };
            }
            else if ((end - _start).Length >= 3)
            {
                obj = TempShape(end);
            }
            if (obj != null) { PushUndo(); _objects.Add(obj); }
            RenderCanvas();
        }

        private void EraseStep(Point p)
        {
            var res = SketchModel.EraseAt(_objects, p, EraseRadius);
            _objects.Clear();
            _objects.AddRange(res);
            RenderCanvas();
        }

        // Eraser ring: a thin outline the size of the erase area, drawn under the pointer so the edges
        // of what the eraser will remove are visible. Hit-test-invisible so it never blocks a drag.
        private Ellipse MakeEraseCursor()
        {
            var ring = new Ellipse
            {
                Width = EraseRadius * 2,
                Height = EraseRadius * 2,
                StrokeThickness = 1,
                Fill = null,
                IsHitTestVisible = false,
            };
            ring.SetResourceReference(Shape.StrokeProperty, "TextBrush");   // follows the live theme
            return ring;
        }

        private void ShowEraseCursor(Point p)
        {
            _eraseCursor ??= MakeEraseCursor();
            if (!_canvas.Children.Contains(_eraseCursor)) _canvas.Children.Add(_eraseCursor);
            Canvas.SetLeft(_eraseCursor, p.X - EraseRadius);
            Canvas.SetTop(_eraseCursor, p.Y - EraseRadius);
        }

        private void HideEraseCursor()
        {
            if (_eraseCursor != null && _canvas.Children.Contains(_eraseCursor)) _canvas.Children.Remove(_eraseCursor);
        }

        // Paint bucket: a raster flood fill from the click point (MS Paint style). Rasterize the current
        // drawing, flood the connected region the click lands in - bounded by whatever strokes/shapes
        // surround it - and add just that region as a fill layer behind the lines. An OPEN scribble
        // leaks out to the canvas edge exactly like a real bucket; enclose the area to contain the fill.
        private void BucketFill(Point p)
        {
            int w = _canvasW, h = _canvasH;
            int cx = Math.Max(0, Math.Min(w - 1, (int)p.X));
            int cy = Math.Max(0, Math.Min(h - 1, (int)p.Y));

            var src = SketchModel.RenderObjects(_objects, w, h);
            var straight = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var px = new int[w * h];
            straight.CopyPixels(px, w * 4, 0);

            int fillInt = unchecked((_fillAlpha << 24) | (_penColor.R << 16) | (_penColor.G << 8) | _penColor.B);
            int target = px[cy * w + cx];
            if (ColorClose(target, fillInt)) return;   // the region is already this color

            var region = new int[w * h];
            var stack = new Stack<int>();
            stack.Push(cy * w + cx);
            int minx = w, miny = h, maxx = -1, maxy = -1;
            bool any = false;
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                if (region[idx] != 0 || !ColorClose(px[idx], target)) continue;
                region[idx] = fillInt;
                any = true;
                int x = idx % w, y = idx / w;
                if (x < minx) minx = x; if (x > maxx) maxx = x;
                if (y < miny) miny = y; if (y > maxy) maxy = y;
                if (x > 0) stack.Push(idx - 1);
                if (x < w - 1) stack.Push(idx + 1);
                if (y > 0) stack.Push(idx - w);
                if (y < h - 1) stack.Push(idx + w);
            }
            if (!any) return;

            int cw = maxx - minx + 1, ch = maxy - miny + 1;
            var crop = new int[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    crop[y * cw + x] = region[(miny + y) * w + (minx + x)];
            var bmp = BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, crop, cw * 4);
            bmp.Freeze();
            string? b64 = ToPngB64(bmp);
            if (b64 == null) return;

            // Sit the fill behind the strokes (but above any backdrop) so the lines stay as its edges.
            PushUndo();
            int at = 0;
            while (at < _objects.Count && _objects[at].Kind == SketchKind.Image && _objects[at].Backdrop) at++;
            _objects.Insert(at, new SketchObject { Kind = SketchKind.Image, Img = b64, X = minx, Y = miny, W = cw, H = ch });
            RenderCanvas();
        }

        private static bool ColorClose(int a, int b)
        {
            const int tol = 48;
            return Math.Abs(((a >> 24) & 0xFF) - ((b >> 24) & 0xFF)) <= tol
                && Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) <= tol
                && Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) <= tol
                && Math.Abs((a & 0xFF) - (b & 0xFF)) <= tol;
        }

        // ---- Polygon (multi-click: click each corner, click the first / double-click / Enter to close) ----

        private void PolyClick(Point p)
        {
            if (_polyPts.Count == 0)
            {
                _polyLine = new Polyline { Stroke = new SolidColorBrush(_penColor), StrokeThickness = _penWidth, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
                _polyLine.Points.Add(p);
                _polyRubber = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(0x88, _penColor.R, _penColor.G, _penColor.B)),
                    StrokeThickness = Math.Max(1, _penWidth / 2), StrokeDashArray = [4, 3],
                    IsHitTestVisible = false, X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y,
                };
                _polySnap = new Ellipse { Width = 14, Height = 14, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _polySnap.SetResourceReference(Shape.StrokeProperty, "PrimaryBrush");
                Canvas.SetLeft(_polySnap, p.X - 7); Canvas.SetTop(_polySnap, p.Y - 7);
                _canvas.Children.Add(_polyLine);
                _canvas.Children.Add(_polyRubber);
                _canvas.Children.Add(_polySnap);
                _polyPts.Add(p);
                return;
            }
            if (_polyPts.Count >= 3 && (p - _polyPts[0]).Length <= PolySnapPx) { CommitPoly(); return; }
            _polyPts.Add(p);
            _polyLine!.Points.Add(p);
            _polyRubber!.X1 = p.X; _polyRubber.Y1 = p.Y;
        }

        private void PolyRubber(Point p)
        {
            if (_polyRubber == null) return;
            _polyRubber.X2 = p.X; _polyRubber.Y2 = p.Y;
            if (_polySnap != null)
                _polySnap.Visibility = _polyPts.Count >= 3 && (p - _polyPts[0]).Length <= PolySnapPx
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CommitPoly()
        {
            var pts = new List<Point>(_polyPts);
            ResetPoly();
            while (pts.Count >= 2 && (pts[^1] - pts[^2]).Length < 3) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) { RenderCanvas(); return; }
            var o = new SketchObject { Kind = SketchKind.Polygon, Color = SketchModel.HexOf(_penColor), Width = _penWidth };
            foreach (var pt in pts) { o.Pts.Add(pt.X); o.Pts.Add(pt.Y); }
            if (_fill) o.Fill = FillHex();
            PushUndo();
            _objects.Add(o);
            RenderCanvas();
        }

        private void CancelPoly()
        {
            if (!PolyActive) return;
            ResetPoly();
            RenderCanvas();
        }

        private void PolyBackspace()
        {
            if (_polyPts.Count == 0) return;
            if (_polyPts.Count == 1) { CancelPoly(); return; }
            _polyPts.RemoveAt(_polyPts.Count - 1);
            _polyLine!.Points.RemoveAt(_polyLine.Points.Count - 1);
            var last = _polyPts[^1];
            _polyRubber!.X1 = last.X; _polyRubber.Y1 = last.Y;
        }

        private void ResetPoly()
        {
            if (_polyLine != null) _canvas.Children.Remove(_polyLine);
            if (_polyRubber != null) _canvas.Children.Remove(_polyRubber);
            if (_polySnap != null) _canvas.Children.Remove(_polySnap);
            _polyPts.Clear();
            _polyLine = null; _polyRubber = null; _polySnap = null;
        }
    }
}
