using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    /// <summary>What a recording chip points at: its payload's hash and length. Lives in the
    /// chip's Tag for this session only; the stored note names the payload by marker.</summary>
    internal sealed class RecordingRef(string hash, int durationMs)
    {
        public string Hash { get; } = hash;
        public int DurationMs { get; } = durationMs;
    }

    // How recordings survive the note format (1.6.0).
    //
    // The playback chip is a Border of Grids and Paths, and the XamlPackage a rich note is stored
    // as keeps none of that: a saved chip came back as a single space, which is how recordings
    // used to vanish after the first autosave. What the format does keep is an Image and its
    // ToolTip (Services/Attachments.cs). So for the length of a save each chip is swapped for a
    // picture of itself carrying a "kn-rec:<hash>" marker, the document is serialized, and the
    // chip goes straight back; on open every marker picture becomes a live chip again. The swap
    // and the swap back happen in one dispatcher frame, never reach the undo stack, and are
    // hidden from the editor's change handler.
    //
    // A recording's payload is written by the save that first stores its marker, never earlier,
    // so payload cleanup can never remove audio the open note still shows. Payloads the editor
    // holds stay in memory for the open note, which is what lets Undo bring back a chip whose
    // audio a later save had already let go of.
    public partial class MainWindow
    {
        private Dictionary<string, Attachment>? _recordingCache;
        private Dictionary<string, Attachment> RecordingCache => _recordingCache ??= new(StringComparer.Ordinal);

        /// <summary>Recording hashes the last save stored for the open note.</summary>
        private HashSet<string>? _recordingsSaved;

        /// <summary>The open note's pre-1.6.0 recording rows were re-anchored as chips on open;
        /// the next save stores them as markers and removes the old rows.</summary>
        private bool _dropLegacyRecordings;

        /// <summary>Chips are being swapped for markers or back; not an edit.</summary>
        private bool _swappingChips;

        /// <summary>A recording's stored bytes, from the open note's memory or the database.</summary>
        private Attachment? RecordingPayload(string hash) =>
            _recordingCache != null && _recordingCache.TryGetValue(hash, out var held) ? held
            : NoteStore.GetAttachment(hash) is { Kind: AttachmentKind.Recording } stored ? Remember(stored)
            : null;

        private Attachment Remember(Attachment a) => RecordingCache[a.Hash] = a;

        /// <summary>The open rich note as XamlPackage bytes, with every sketch and recording
        /// marked, plus the payloads those markers name that the database may not hold yet.</summary>
        internal byte[] SerializeRichDocument(List<Attachment> attachments)
        {
            attachments.AddRange(Sketch.CaptureAttachments(Editor.Document.Blocks));
            var swapped = new List<(TextElement Container, Border Chip)>();
            _swappingChips = true;
            try
            {
                foreach (var (container, chip) in ChipContainers(Editor.Document))
                {
                    var rec = (RecordingRef)chip.Tag;
                    if ((_recordingsSaved == null || !_recordingsSaved.Contains(rec.Hash)) && RecordingPayload(rec.Hash) is { } payload)
                        attachments.Add(payload);
                    SetChild(container, MarkerPicture(chip, rec));
                    swapped.Add((container, chip));
                }
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var ms = new MemoryStream();
                range.Save(ms, DataFormats.XamlPackage);
                return ms.ToArray();
            }
            finally
            {
                foreach (var (container, chip) in swapped) SetChild(container, chip);
                _swappingChips = false;
            }
        }

        /// <summary>After SerializeRichDocument's bytes are stored.</summary>
        internal void OnRichDocumentSaved()
        {
            _dropLegacyRecordings = false;
            _recordingsSaved = new HashSet<string>(
                ChipContainers(Editor.Document).Select(c => ((RecordingRef)c.Chip.Tag).Hash), StringComparer.Ordinal);
        }

        /// <summary>Turns the loaded note's recording markers back into chips, and re-anchors
        /// recordings that versions before 1.6.0 left without a chip at the end of the note.</summary>
        internal void ReviveRecordings(long noteId)
        {
            _recordingCache = null;
            _dropLegacyRecordings = false;
            _recordingsSaved = new HashSet<string>(StringComparer.Ordinal);
            _swappingChips = true;
            try
            {
                foreach (var (container, image) in MarkerPictures(Editor.Document))
                {
                    Attachments.TryParseMarker(image.ToolTip, out _, out string hash);
                    int duration = RecordingPayload(hash)?.DurationMs ?? 0;
                    SetChild(container, BuildRecordingChip(hash, duration));
                    _recordingsSaved.Add(hash);
                }
            }
            finally { _swappingChips = false; }

            if (_recordingsSaved.Count > 0 || NoteStore.IsReadOnly || _currentInTrash) return;
            var legacy = NoteStore.LoadRecordingIndex(noteId);
            if (legacy.Count == 0) return;
            var paragraph = new Paragraph();
            foreach (var ord in legacy.Keys.OrderBy(k => k))
            {
                if (NoteStore.LoadRecordingRaw(noteId, ord) is not { } raw) continue;
                var a = Remember(new Attachment(AttachmentKind.Recording, raw, legacy[ord]));
                paragraph.Inlines.Add(new InlineUIContainer(BuildRecordingChip(a.Hash, a.DurationMs)));
                paragraph.Inlines.Add(new Run(" "));
            }
            if (paragraph.Inlines.Count == 0) return;
            Editor.Document.Blocks.Add(paragraph);
            _dropLegacyRecordings = true;
            MarkDirty();
            StatusText.Text = string.Format(Loc("Str_Dict_Recovered"), legacy.Count);
        }

        /// <summary>The chip at rest, as a picture carrying its marker. The picture is what other
        /// programs see in a copy or an export; KillerNotes only reads the marker.</summary>
        private static Image MarkerPicture(Border chip, RecordingRef rec)
        {
            BitmapSource source;
            if (chip.ActualWidth >= 1 && chip.ActualHeight >= 1)
            {
                var dpi = VisualTreeHelper.GetDpi(chip);
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(chip.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(chip.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                // Through a brush: rendering the chip itself would draw it at its offset inside
                // the paragraph rather than at the picture's origin.
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(chip), null, new Rect(0, 0, chip.ActualWidth, chip.ActualHeight));
                rtb.Render(visual);
                rtb.Freeze();
                source = rtb;
            }
            else
            {
                source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            }
            var image = new Image { Source = source, Stretch = Stretch.None };
            if (chip.ActualWidth >= 1) { image.Width = chip.ActualWidth; image.Height = chip.ActualHeight; }
            Sketch.MarkImage(image, Attachments.Marker(AttachmentKind.Recording, rec.Hash));
            return image;
        }

        private static IEnumerable<(TextElement Container, Border Chip)> ChipContainers(DependencyObject root) =>
            Containers(root).Where(c => c.Child is Border { Tag: RecordingRef }).Select(c => (c.Container, (Border)c.Child)).ToList();

        private static IEnumerable<(TextElement Container, Image Image)> MarkerPictures(DependencyObject root) =>
            Containers(root)
                .Where(c => c.Child is Image img && Attachments.TryParseMarker(img.ToolTip, out var kind, out _) && kind == AttachmentKind.Recording)
                .Select(c => (c.Container, (Image)c.Child))
                .ToList();

        /// <summary>Every embedded element with the container that holds it, inline or floated.</summary>
        private static IEnumerable<(TextElement Container, UIElement Child)> Containers(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is InlineUIContainer { Child: { } inline } iuc) yield return (iuc, inline);
                else if (child is BlockUIContainer { Child: { } block } buc) yield return (buc, block);
                else if (child is DependencyObject d)
                    foreach (var nested in Containers(d)) yield return nested;
            }
        }

        private static void SetChild(TextElement container, UIElement child)
        {
            if (container is InlineUIContainer iuc) iuc.Child = child;
            else if (container is BlockUIContainer buc) buc.Child = child;
        }
    }
}
