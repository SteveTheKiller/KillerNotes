using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace KillerNotes
{
    // SketchPad object model (BACKLOG: SketchPad - "mini MS Paint" upgrade). The pad moved off a
    // pure InkCanvas to a Canvas + list-of-objects model so shapes, fill, text, images, and
    // per-object select/move/resize all work uniformly. A sketch is a List<SketchObject>; it
    // serializes to JSON (DataContractJson, a built-in net48 framework type) and rides the same
    // per-note side table the old ink used. Legacy ISF payloads still load: Deserialize detects
    // them and migrates each stroke to a Freehand object, so existing sketches keep working.

    public enum SketchKind { Freehand, Line, Arrow, Rect, Ellipse, Text, Image, Polygon }

    // One drawable object. A single flat DTO (Kind discriminates) keeps JSON round-tripping simple.
    // Pts holds x,y pairs: Freehand = every point; Line/Arrow/Rect/Ellipse = two defining corners
    // [x0,y0,x1,y1]. Text uses X,Y (+ FontSize, Bold); Image uses X,Y,W,H (or Backdrop = fill canvas).
    [DataContract]
    public sealed class SketchObject
    {
        [DataMember(Order = 0)] public SketchKind Kind { get; set; }
        [DataMember(Order = 1)] public string Color { get; set; } = "#FF50AEE8";
        [DataMember(Order = 2)] public double Width { get; set; } = 3;
        [DataMember(Order = 3)] public string? Fill { get; set; }          // null = no fill
        [DataMember(Order = 4)] public List<double> Pts { get; set; } = [];
        [DataMember(Order = 5)] public string? Text { get; set; }
        [DataMember(Order = 6)] public double FontSize { get; set; } = 24;
        [DataMember(Order = 7)] public bool Bold { get; set; }
        [DataMember(Order = 8)] public string? Img { get; set; }           // base64 PNG for Image kind
        [DataMember(Order = 9)] public double X { get; set; }
        [DataMember(Order = 10)] public double Y { get; set; }
        [DataMember(Order = 11)] public double W { get; set; }
        [DataMember(Order = 12)] public double H { get; set; }
        [DataMember(Order = 13)] public bool Backdrop { get; set; }
        [DataMember(Order = 14)] public double Opacity { get; set; } = 1;   // images; 0 (legacy/missing) reads as fully opaque

        public SketchObject Clone() => new()
        {
            Kind = Kind, Color = Color, Width = Width, Fill = Fill, Pts = [..Pts],
            Text = Text, FontSize = FontSize, Bold = Bold, Img = Img, X = X, Y = Y, W = W, H = H,
            Backdrop = Backdrop, Opacity = Opacity,
        };
    }

    // Serialization, WPF element building, flatten-to-bitmap, and geometry/hit-test helpers for the
    // SketchPad object model. The SAME BuildElement feeds both the live editing Canvas and the
    // flatten pass, so what prints is exactly what was drawn.
    internal static class SketchModel
    {
        public static readonly Color DefaultColor = Color.FromRgb(0x50, 0xAE, 0xE8);

        private static readonly DataContractJsonSerializer Ser = new(typeof(List<SketchObject>));

        // ---- Persistence ----

        public static byte[] Serialize(IEnumerable<SketchObject> objs)
        {
            using var ms = new MemoryStream();
            Ser.WriteObject(ms, new List<SketchObject>(objs));
            return ms.ToArray();
        }

        // JSON (a serialized List) starts with '['; anything else is a legacy ISF ink payload.
        public static List<SketchObject> Deserialize(byte[]? payload)
        {
            if (payload == null || payload.Length == 0) return [];
            if (LooksLikeJson(payload))
            {
                try
                {
                    using var ms = new MemoryStream(payload);
                    return (List<SketchObject>?)Ser.ReadObject(ms) ?? [];
                }
                catch { /* corrupt JSON - fall through and try ISF */ }
            }
            return FromIsf(payload);
        }

        private static bool LooksLikeJson(byte[] b)
        {
            foreach (var t in b)
            {
                if (t is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') continue;
                return t is (byte)'[' or (byte)'{';
            }
            return false;
        }

        // Legacy migration: every saved ink stroke becomes a Freehand object with its color/width.
        public static List<SketchObject> FromIsf(byte[] isf)
        {
            var list = new List<SketchObject>();
            foreach (var s in Sketch.StrokesFromIsf(isf))
            {
                var o = new SketchObject
                {
                    Kind = SketchKind.Freehand,
                    Color = HexOf(s.DrawingAttributes.Color),
                    Width = s.DrawingAttributes.Width,
                };
                foreach (var sp in s.StylusPoints) { o.Pts.Add(sp.X); o.Pts.Add(sp.Y); }
                if (o.Pts.Count >= 4) list.Add(o);
            }
            return list;
        }

        public static Color ParseColor(string? hex)
        {
            try { if (!string.IsNullOrEmpty(hex)) return (Color)ColorConverter.ConvertFromString(hex); }
            catch { /* bad string - fall back */ }
            return DefaultColor;
        }

        public static string HexOf(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        public static List<SketchObject> CloneList(IEnumerable<SketchObject> objs)
        {
            var list = new List<SketchObject>();
            foreach (var o in objs) list.Add(o.Clone());
            return list;
        }

        // ---- Element building (live canvas AND flatten share this) ----

        public static UIElement BuildElement(SketchObject o) => o.Kind switch
        {
            SketchKind.Line    => LineEl(o),
            SketchKind.Arrow   => ArrowEl(o),
            SketchKind.Rect    => RectEl(o),
            SketchKind.Ellipse => EllipseEl(o),
            SketchKind.Polygon => PolygonEl(o),
            SketchKind.Text    => TextEl(o),
            SketchKind.Image   => ImageEl(o),
            _                  => FreehandEl(o),
        };

        private static Brush Stroke(SketchObject o) => new SolidColorBrush(ParseColor(o.Color));
        private static Brush? Fill(SketchObject o) => o.Fill != null ? new SolidColorBrush(ParseColor(o.Fill)) : null;

        private static Polyline FreehandEl(SketchObject o)
        {
            var pl = new Polyline
            {
                Stroke = Stroke(o), StrokeThickness = o.Width,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            for (int i = 0; i + 1 < o.Pts.Count; i += 2) pl.Points.Add(new Point(o.Pts[i], o.Pts[i + 1]));
            return pl;
        }

        private static Polyline LineEl(SketchObject o)
        {
            var pl = new Polyline
            {
                Stroke = Stroke(o), StrokeThickness = o.Width, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            foreach (var pt in ShaftPoints(o)) pl.Points.Add(pt);
            return pl;
        }

        private static Polyline ArrowEl(SketchObject o)
        {
            var shaft = ShaftPoints(o);
            var end = shaft[^1];
            var prev = shaft[^2];   // tangent at the tip = last segment direction
            double ang = Math.Atan2(end.Y - prev.Y, end.X - prev.X);
            double head = Math.Max(15, o.Width * 4.6), spread = 0.5;
            var left = new Point(end.X - head * Math.Cos(ang - spread), end.Y - head * Math.Sin(ang - spread));
            var right = new Point(end.X - head * Math.Cos(ang + spread), end.Y - head * Math.Sin(ang + spread));
            var pl = new Polyline
            {
                Stroke = Stroke(o), StrokeThickness = o.Width,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            foreach (var pt in shaft) pl.Points.Add(pt);
            pl.Points.Add(left); pl.Points.Add(end); pl.Points.Add(right);
            return pl;
        }

        private static Rectangle RectEl(SketchObject o)
        {
            var r = RectOf(o);
            var el = new Rectangle { Width = r.Width, Height = r.Height, Stroke = Stroke(o), StrokeThickness = o.Width, Fill = Fill(o) };
            Canvas.SetLeft(el, r.X); Canvas.SetTop(el, r.Y);
            return el;
        }

        private static Ellipse EllipseEl(SketchObject o)
        {
            var r = RectOf(o);
            var el = new Ellipse { Width = r.Width, Height = r.Height, Stroke = Stroke(o), StrokeThickness = o.Width, Fill = Fill(o) };
            Canvas.SetLeft(el, r.X); Canvas.SetTop(el, r.Y);
            return el;
        }

        private static Polygon PolygonEl(SketchObject o)
        {
            var pg = new Polygon { Stroke = Stroke(o), StrokeThickness = o.Width, Fill = Fill(o), StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i + 1 < o.Pts.Count; i += 2) pg.Points.Add(new Point(o.Pts[i], o.Pts[i + 1]));
            return pg;
        }

        private static TextBlock TextEl(SketchObject o)
        {
            var tb = new TextBlock
            {
                Text = o.Text ?? "", FontSize = o.FontSize, Foreground = Stroke(o),
                FontWeight = o.Bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.NoWrap,
            };
            Canvas.SetLeft(tb, o.X); Canvas.SetTop(tb, o.Y);
            return tb;
        }

        private static Image ImageEl(SketchObject o)
        {
            var img = new Image
            {
                Source = FromB64(o.Img),
                Stretch = o.Backdrop ? Stretch.Uniform : Stretch.Fill,
                Opacity = o.Opacity <= 0 ? 1.0 : Math.Min(1.0, o.Opacity),   // 0 = legacy/missing = fully opaque
            };
            if (o.Backdrop) { Canvas.SetLeft(img, 0); Canvas.SetTop(img, 0); img.Width = Sketch.CanvasW; img.Height = Sketch.CanvasH; }
            else { Canvas.SetLeft(img, o.X); Canvas.SetTop(img, o.Y); img.Width = o.W; img.Height = o.H; }
            return img;
        }

        public static BitmapImage? FromB64(string? b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;
            try
            {
                var bytes = Convert.FromBase64String(b64);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // Flatten the whole object list to a bitmap for the in-note image.
        public static BitmapSource RenderObjects(IEnumerable<SketchObject> objs, int w, int h)
        {
            var canvas = new Canvas { Width = w, Height = h };
            foreach (var o in objs) canvas.Children.Add(BuildElement(GuardForFlatten(o)));
            canvas.Measure(new Size(w, h));
            canvas.Arrange(new Rect(0, 0, w, h));
            canvas.UpdateLayout();
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvas);
            rtb.Freeze();
            return rtb;
        }

        // Legibility guard for the FLATTENED image only: a near-white or near-black stroke color is
        // pulled toward a mid-tone so it never vanishes on the note paper, whatever theme the note is
        // viewed in. The live pad and the stored objects keep their true colors - only this baked copy
        // is adjusted. Fills (alpha washes) and images (already-composited pixels) are left as-is.
        private static SketchObject GuardForFlatten(SketchObject o)
        {
            if (o.Kind == SketchKind.Image) return o;
            var c = ParseColor(o.Color);
            var g = GuardColor(c);
            if (g == c) return o;
            var clone = o.Clone();
            clone.Color = HexOf(g);
            return clone;
        }

        private static Color GuardColor(Color c)
        {
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;   // 0..255 (perceptual)
            if (lum >= 209)   // ~0.82: near-white, would wash out on light paper
            {
                double f = 184.0 / lum;   // darken proportionally to ~0.72 luminance (hue kept)
                return Color.FromArgb(c.A, (byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
            }
            if (lum <= 41)    // ~0.16: near-black, would vanish on dark paper
            {
                double t = (77.0 - lum) / (255.0 - lum);   // blend toward white to ~0.30 luminance
                return Color.FromArgb(c.A,
                    (byte)(c.R + (255 - c.R) * t),
                    (byte)(c.G + (255 - c.G) * t),
                    (byte)(c.B + (255 - c.B) * t));
            }
            return c;
        }

        // ---- Geometry / hit testing (eraser + select) ----

        // First and LAST point - so a line/arrow with a middle control point (arced) still reports its
        // true endpoints, while a 2-point straight line/rect/ellipse is unchanged.
        private static (Point a, Point b) Ends(SketchObject o) => o.Pts.Count >= 4
            ? (new Point(o.Pts[0], o.Pts[1]), new Point(o.Pts[^2], o.Pts[^1]))
            : (new Point(0, 0), new Point(0, 0));

        // The shaft as a polyline: two points for a straight line/arrow, or a sampled quadratic bezier
        // (Pts = start, control, end) when a control point is present for an arced one.
        private static List<Point> ShaftPoints(SketchObject o)
        {
            var pts = new List<Point>();
            if (o.Pts.Count >= 6)
            {
                var p0 = new Point(o.Pts[0], o.Pts[1]);
                var c = new Point(o.Pts[2], o.Pts[3]);
                var p1 = new Point(o.Pts[4], o.Pts[5]);
                const int n = 24;
                for (int i = 0; i <= n; i++)
                {
                    double t = i / (double)n, u = 1 - t;
                    pts.Add(new Point(u * u * p0.X + 2 * u * t * c.X + t * t * p1.X,
                                      u * u * p0.Y + 2 * u * t * c.Y + t * t * p1.Y));
                }
            }
            else
            {
                var (a, b) = Ends(o);
                pts.Add(a); pts.Add(b);
            }
            return pts;
        }

        public static Rect RectOf(SketchObject o)
        {
            var (a, b) = Ends(o);
            return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        }

        public static Rect BoundsOf(SketchObject o)
        {
            switch (o.Kind)
            {
                case SketchKind.Freehand:
                case SketchKind.Polygon:
                case SketchKind.Line:
                case SketchKind.Arrow:
                    if (o.Pts.Count < 2) return Rect.Empty;
                    double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
                    for (int i = 0; i + 1 < o.Pts.Count; i += 2)
                    {
                        double x = o.Pts[i], y = o.Pts[i + 1];
                        if (x < minx) minx = x; if (y < miny) miny = y;
                        if (x > maxx) maxx = x; if (y > maxy) maxy = y;
                    }
                    return new Rect(minx, miny, Math.Max(0, maxx - minx), Math.Max(0, maxy - miny));
                case SketchKind.Rect:
                case SketchKind.Ellipse:
                    return RectOf(o);
                case SketchKind.Text:
                {
                    var lines = (o.Text ?? "").Split('\n');
                    int longest = 1;
                    foreach (var ln in lines) longest = Math.Max(longest, ln.Length);
                    return new Rect(o.X, o.Y, Math.Max(20, longest * o.FontSize * 0.6), Math.Max(1, lines.Length) * o.FontSize * 1.4);
                }
                case SketchKind.Image:
                    return o.Backdrop ? new Rect(0, 0, Sketch.CanvasW, Sketch.CanvasH) : new Rect(o.X, o.Y, o.W, o.H);
            }
            return Rect.Empty;
        }

        public static bool HitTest(SketchObject o, Point p, double tol)
        {
            double t = tol + o.Width / 2;
            switch (o.Kind)
            {
                case SketchKind.Freehand:
                    return NearPolyline(o.Pts, p, t);
                case SketchKind.Line:
                case SketchKind.Arrow:
                {
                    var sp = ShaftPoints(o);
                    for (int i = 0; i + 1 < sp.Count; i++)
                        if (DistToSeg(p, sp[i], sp[i + 1]) <= t) return true;
                    return false;
                }
                case SketchKind.Rect:
                {
                    var r = RectOf(o);
                    if (o.Fill != null) return Inflate(r, t).Contains(p);
                    return NearRectEdge(r, p, t);
                }
                case SketchKind.Ellipse:
                {
                    var r = RectOf(o);
                    double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                    double rx = Math.Max(1, r.Width / 2), ry = Math.Max(1, r.Height / 2);
                    double d = Math.Sqrt(Math.Pow((p.X - cx) / rx, 2) + Math.Pow((p.Y - cy) / ry, 2));
                    double band = t / Math.Min(rx, ry);
                    return o.Fill != null ? d <= 1 + band : Math.Abs(d - 1) <= band;
                }
                case SketchKind.Polygon:
                    if (o.Fill != null && PointInPolygon(o.Pts, p)) return true;
                    return NearPolyClosed(o.Pts, p, t);
                case SketchKind.Text:
                case SketchKind.Image:
                    return Inflate(BoundsOf(o), tol).Contains(p);
            }
            return false;
        }

        // Eraser: brush point-erase on freehand (split into runs so gaps read like a real eraser),
        // whole-object removal for shapes/text/images the brush touches. Returns the new object list.
        public static List<SketchObject> EraseAt(IEnumerable<SketchObject> objs, Point e, double radius)
        {
            var result = new List<SketchObject>();
            foreach (var o in objs)
            {
                if (o.Kind == SketchKind.Freehand)
                {
                    double r = radius + o.Width / 2;
                    var run = new List<double>();
                    void Flush()
                    {
                        if (run.Count >= 4)
                            result.Add(new SketchObject { Kind = SketchKind.Freehand, Color = o.Color, Width = o.Width, Pts = [..run] });
                        run.Clear();
                    }
                    for (int i = 0; i + 1 < o.Pts.Count; i += 2)
                    {
                        var pt = new Point(o.Pts[i], o.Pts[i + 1]);
                        if ((pt - e).Length <= r) Flush();
                        else { run.Add(pt.X); run.Add(pt.Y); }
                    }
                    Flush();
                }
                else if (!HitTest(o, e, radius))
                {
                    result.Add(o);
                }
            }
            return result;
        }

        private static Rect Inflate(Rect r, double d)
        {
            if (r.IsEmpty) return r;
            return new Rect(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);
        }

        private static bool NearPolyline(List<double> pts, Point p, double tol)
        {
            for (int i = 0; i + 3 < pts.Count; i += 2)
                if (DistToSeg(p, new Point(pts[i], pts[i + 1]), new Point(pts[i + 2], pts[i + 3])) <= tol)
                    return true;
            if (pts.Count == 2) return (p - new Point(pts[0], pts[1])).Length <= tol;
            return false;
        }

        // Distance to a CLOSED polyline (all edges plus the last->first closing edge).
        private static bool NearPolyClosed(List<double> pts, Point p, double tol)
        {
            int n = pts.Count;
            for (int i = 0; i + 3 < n; i += 2)
                if (DistToSeg(p, new Point(pts[i], pts[i + 1]), new Point(pts[i + 2], pts[i + 3])) <= tol) return true;
            if (n >= 6 && DistToSeg(p, new Point(pts[n - 2], pts[n - 1]), new Point(pts[0], pts[1])) <= tol) return true;
            return false;
        }

        private static bool PointInPolygon(List<double> pts, Point p)
        {
            int n = pts.Count / 2;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = pts[2 * i], yi = pts[2 * i + 1], xj = pts[2 * j], yj = pts[2 * j + 1];
                if (((yi > p.Y) != (yj > p.Y)) && (p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi)) inside = !inside;
            }
            return inside;
        }

        private static bool NearRectEdge(Rect r, Point p, double tol)
        {
            var tl = new Point(r.Left, r.Top); var tr = new Point(r.Right, r.Top);
            var br = new Point(r.Right, r.Bottom); var bl = new Point(r.Left, r.Bottom);
            return DistToSeg(p, tl, tr) <= tol || DistToSeg(p, tr, br) <= tol
                || DistToSeg(p, br, bl) <= tol || DistToSeg(p, bl, tl) <= tol;
        }

        private static double DistToSeg(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-6) return (p - a).Length;
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return (p - new Point(a.X + t * dx, a.Y + t * dy)).Length;
        }
    }
}
