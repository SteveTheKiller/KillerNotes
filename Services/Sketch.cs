using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;
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
    // content XamlPackage). Its editable objects are held off to the side, attached to the live
    // Image object below, and stored by content hash (NoteStore attachments). Since 1.6.0 the image
    // carries a marker naming that hash in its ToolTip, the one property the note format keeps
    // (Services/Attachments.cs), so a sketch finds its drawing wherever the image moves, in history
    // versions and in pasted copies. Notes saved before markers existed keep the older link: the
    // image's ordinal in document order (NoteStore.SaveSketches/LoadSketches), used only when a
    // document carries no sketch markers at all.
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

        public static IEnumerable<Image> Images(IEnumerable<Block> blocks)
            => WalkBlocks(blocks, legacy: false);

        private static IEnumerable<Image> WalkBlocks(IEnumerable<Block> blocks, bool legacy)
        {
            foreach (var block in blocks)
            {
                if (block is BlockUIContainer { Child: Image image }) yield return image;
                else if (block is Paragraph paragraph)
                    foreach (var inline in paragraph.Inlines)
                        foreach (var child in WalkInline(inline, legacy)) yield return child;
                else if (block is Section section && !legacy)
                {
                    foreach (var child in WalkBlocks(section.Blocks, legacy)) yield return child;
                }
                else if (block is List list)
                {
                    foreach (var item in list.ListItems)
                        foreach (var child in WalkBlocks(item.Blocks, legacy)) yield return child;
                }
                else if (block is Table table)
                {
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var child in WalkBlocks(cell.Blocks, legacy)) yield return child;
                }
            }
        }

        private static IEnumerable<Image> WalkInline(Inline inline, bool legacy)
        {
            if (inline is InlineUIContainer { Child: Image image }) yield return image;
            else if (!legacy && inline is Span span)
            {
                foreach (var nested in span.Inlines)
                    foreach (var child in WalkInline(nested, legacy)) yield return child;
            }
            else if (!legacy && inline is AnchoredBlock anchor)
            {
                foreach (var child in WalkBlocks(anchor.Blocks, legacy)) yield return child;
            }
        }

        // Existing nonnegative keys retain the original walk's associations. Negative
        // keys count only images that the original walk could not reach, in document
        // order. This also survives export/import without a schema or blob migration.
        private static IEnumerable<(Image Image, int Key)> KeyedImages(IEnumerable<Block> blocks)
        {
            var snapshot = blocks.ToList();
            var legacy = WalkBlocks(snapshot, legacy: true).Select((image, index) => (image, index))
                .ToDictionary(pair => pair.image, pair => pair.index);
            int nested = -1;
            foreach (var image in Images(snapshot))
                yield return (image, legacy.TryGetValue(image, out int key) ? key : nested--);
        }

        /// <summary>Ordinal -> payload, the pre-1.6.0 storage shape. Still used to seed demo and
        /// template data written in that shape.</summary>
        public static Dictionary<int, byte[]> Capture(IEnumerable<Block> blocks)
        {
            var result = new Dictionary<int, byte[]>();
            foreach (var (image, key) in KeyedImages(blocks))
            {
                if (TryGetData(image, out var payload)) result[key] = payload;
            }
            return result;
        }

        /// <summary>Marks every sketch image with its payload's hash and returns the payloads, for
        /// the save that is about to serialize the document. Setting the ToolTip is invisible to
        /// the editor's undo and change tracking.</summary>
        public static List<Attachment> CaptureAttachments(IEnumerable<Block> blocks)
        {
            var result = new List<Attachment>();
            foreach (var image in Images(blocks))
            {
                if (!TryGetData(image, out var payload)) continue;
                var attachment = new Attachment(AttachmentKind.Sketch, payload);
                MarkImage(image, Attachments.Marker(AttachmentKind.Sketch, attachment.Hash));
                result.Add(attachment);
            }
            return result;
        }

        /// <summary>Re-attaches editable payloads after a load or paste: by marker, or by ordinal
        /// for a document saved before markers existed.</summary>
        public static void Link(IEnumerable<Block> blocks, IReadOnlyDictionary<int, byte[]> legacyByOrdinal,
            Func<string, Attachment?> lookup)
        {
            var snapshot = blocks.ToList();
            bool marked = false;
            foreach (var image in Images(snapshot))
            {
                if (!Attachments.TryParseMarker(image.ToolTip, out var kind, out var hash) || kind != AttachmentKind.Sketch) continue;
                marked = true;
                MarkImage(image, (string)image.ToolTip);
                if (!TryGetData(image, out _) && lookup(hash) is { } found) SetData(image, found.Payload);
            }
            if (!marked && legacyByOrdinal.Count > 0) Link(snapshot, legacyByOrdinal);
        }

        public static void Link(IEnumerable<Block> blocks, IReadOnlyDictionary<int, byte[]> payloads)
        {
            foreach (var (image, key) in KeyedImages(blocks))
            {
                if (payloads.TryGetValue(key, out var payload)) SetData(image, payload);
            }
        }

        /// <summary>Sets a payload marker on an image. The marker is storage, not a hint, so the
        /// tooltip popup stays off.</summary>
        internal static void MarkImage(Image image, string marker)
        {
            if (!Equals(image.ToolTip, marker)) image.ToolTip = marker;
            System.Windows.Controls.ToolTipService.SetIsEnabled(image, false);
        }

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
