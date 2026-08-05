using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerNotes.Services
{
    // SketchPad storage helper (BACKLOG: SketchPad).
    //
    // A sketch lives in the note as an ordinary flattened Image (which round-trips through the
    // content XamlPackage) and its editable strokes ride in a DB side table keyed by the image's
    // ordinal in document order (NoteStore.SaveSketches/LoadSketches). The note format strips any
    // marker we set on the image (tested: a Tag came back null), so we cannot label sketch images
    // in the document. Instead we hold the strokes off to the side, attached to the live Image
    // object below; on save we walk images in order and persist any image we know is a sketch, and
    // on load we re-attach payloads to images by that same ordinal.
    internal static class Sketch
    {
        // Native canvas size: the coordinate space strokes are drawn and stored in, and the pixel
        // size of the flattened image. The in-note image's display Width/Height can differ (the
        // user can resize it); Stretch=Uniform scales this native bitmap to that display size.
        public const int CanvasW = 800, CanvasH = 500;

        // Live Image -> its ISF strokes. Not serialized; rebuilt from the DB on note load. A weak
        // table so it never keeps a closed note's images alive.
        private static readonly ConditionalWeakTable<Image, byte[]> _data = new();

        public static void SetData(Image img, byte[] payload)
        {
            _data.Remove(img);
            _data.Add(img, payload);
        }

        public static bool TryGetData(Image img, out byte[] payload) => _data.TryGetValue(img, out payload);

        public static byte[] StrokesToIsf(StrokeCollection strokes)
        {
            using var ms = new MemoryStream();
            strokes.Save(ms);
            return ms.ToArray();
        }

        public static StrokeCollection StrokesFromIsf(byte[] isf)
        {
            if (isf == null || isf.Length == 0) return [];
            try
            {
                using var ms = new MemoryStream(isf);
                return new StrokeCollection(ms);
            }
            catch { return []; }
        }

        // Flatten strokes to a bitmap for the in-document image.
        public static BitmapSource Render(StrokeCollection strokes, int w, int h)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                strokes.Draw(dc);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
