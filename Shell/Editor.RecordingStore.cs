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
    /// <summary>What a recording chip points at: its payload's hash and length. The chip's Tag
    /// holds it as a plain string ("kn-rec:&lt;hash&gt;|&lt;ms&gt;"), never as an object: when the
    /// editor removes an embedded element it serializes it for Undo with XamlWriter, which refuses
    /// non-public types, so an object Tag made every save and every deleted chip throw.</summary>
    internal sealed class RecordingRef(string hash, int durationMs)
    {
        private const string Prefix = "kn-rec:";

        public string Hash { get; } = hash;
        public int DurationMs { get; } = durationMs;

        public string ToTag() => Prefix + Hash + "|" + DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The recording a chip's Tag names, or null for anything else (the volume
        /// knob's "vol" Tag included).</summary>
        public static RecordingRef? From(object? tag)
        {
            if (tag is not string s || !s.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            int bar = s.IndexOf('|');
            if (bar <= Prefix.Length) return null;
            int.TryParse(s.Substring(bar + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int ms);
            return new RecordingRef(s.Substring(Prefix.Length, bar - Prefix.Length), ms);
        }

        /// <summary>The recording chip a Border is, or null.</summary>
        public static RecordingRef? Of(UIElement? element) =>
            element is Border { Child: Grid } b ? From(b.Tag) : null;
    }

    // How recordings survive the note format (1.6.0).
    //
    // The playback chip is a Border of Grids and Paths, and the XamlPackage a rich note is stored
    // as keeps none of that: a saved chip came back as a single space, which is how recordings
    // used to vanish after the first autosave. What the format does keep is an Image and its
    // ToolTip (Services/Attachments.cs). So a save stores each chip as a picture of itself carrying
    // a "kn-rec:<hash>" marker, and on open every marker picture becomes a live chip again. The
    // pictures are put into a detached copy of the note (SaveMarked), never into the live one:
    // anything changed in the live document lands on the editor's undo history, and a swap there
    // made Ctrl+Z after an autosave turn chips into pictures.
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
            foreach (var (_, chip) in ChipContainers(Editor.Document))
            {
                var rec = RecordingRef.Of(chip)!;
                if ((_recordingsSaved == null || !_recordingsSaved.Contains(rec.Hash)) && RecordingPayload(rec.Hash) is { } payload)
                    attachments.Add(payload);
            }
            return SaveMarked(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        }

        private static byte[] SavePackage(TextPointer start, TextPointer end)
        {
            using var ms = new MemoryStream();
            new TextRange(start, end).Save(ms, DataFormats.XamlPackage);
            return ms.ToArray();
        }

        /// <summary>A range of the open note as XamlPackage bytes with every recording chip
        /// stored as its marked picture, WITHOUT changing the live document. Anything done to the
        /// live document lands on the editor's undo history: swapping chips there turned the
        /// first Ctrl+Z after every autosave into "chips become pictures".</summary>
        private byte[] SaveMarked(TextPointer start, TextPointer end)
        {
            var doc = Editor.Document;
            if (!ChipContainers(doc).Any()) return SavePackage(start, end);
            if (MarkedCopy() is { } copy)
                return SavePackage(copy.ContentStart.GetPositionAtOffset(doc.ContentStart.GetOffsetToPosition(start))!,
                                   copy.ContentStart.GetPositionAtOffset(doc.ContentStart.GetOffsetToPosition(end))!);

            // A document the copy could not mirror exactly: swap in place, as ONE change, so
            // undoing it is a single step that changes nothing visible.
            Editor.BeginChange();
            try { return WithChipsAsMarkers(() => SavePackage(start, end)); }
            finally { Editor.EndChange(); }
        }

        /// <summary>A detached copy of the open note with each chip's placeholder replaced by its
        /// marked picture. The note format saves a chip as a one-space run, which takes exactly
        /// the room the chip's container takes, so each chip sits at the same offset in the copy;
        /// null when the copy does not line up exactly.</summary>
        private FlowDocument? MarkedCopy()
        {
            var doc = Editor.Document;
            var copy = new FlowDocument();
            using (var ms = new MemoryStream(SavePackage(doc.ContentStart, doc.ContentEnd)))
                new TextRange(copy.ContentStart, copy.ContentEnd).Load(ms, DataFormats.XamlPackage);
            if (copy.ContentStart.GetOffsetToPosition(copy.ContentEnd) != doc.ContentStart.GetOffsetToPosition(doc.ContentEnd)) return null;

            var placeholders = new List<(Run Placeholder, Image Picture)>();
            foreach (var (container, chip) in ChipContainers(doc))
            {
                if (container is not InlineUIContainer) return null;
                var at = copy.ContentStart.GetPositionAtOffset(doc.ContentStart.GetOffsetToPosition(container.ElementStart));
                if (at == null || at.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.ElementStart ||
                    at.GetAdjacentElement(LogicalDirection.Forward) is not Run { Text: " " } run || run.SiblingInlines == null)
                    return null;
                placeholders.Add((run, MarkerPicture(chip, RecordingRef.Of(chip)!)));
            }
            foreach (var (run, picture) in placeholders)
            {
                run.SiblingInlines.InsertBefore(run, new InlineUIContainer(picture));
                run.SiblingInlines.Remove(run);
            }
            return copy;
        }

        /// <summary>The fallback: every chip replaced by its marked picture in the live document
        /// for the length of a serialization, and put back whatever happens.</summary>
        private T WithChipsAsMarkers<T>(Func<T> serialize)
        {
            var swapped = new List<(TextElement Container, Border Chip)>();
            _swappingChips = true;
            try
            {
                foreach (var (container, chip) in ChipContainers(Editor.Document))
                {
                    SetChild(container, MarkerPicture(chip, RecordingRef.Of(chip)!));
                    swapped.Add((container, chip));
                }
                return serialize();
            }
            finally
            {
                foreach (var (container, chip) in swapped) SetChild(container, chip);
                _swappingChips = false;
            }
        }

        /// <summary>Every recording marker picture in the open note becomes a live chip, as one
        /// editor change. Returns the hashes revived.</summary>
        private List<string> ReviveMarkerPictures()
        {
            var revived = new List<string>();
            _swappingChips = true;
            Editor.BeginChange();
            try
            {
                foreach (var (container, image) in MarkerPictures(Editor.Document))
                {
                    Attachments.TryParseMarker(image.ToolTip, out _, out string hash);
                    int duration = RecordingPayload(hash)?.DurationMs ?? 0;
                    SetChild(container, BuildRecordingChip(hash, duration));
                    revived.Add(hash);
                }
            }
            finally { Editor.EndChange(); _swappingChips = false; }
            return revived;
        }

        // ---- Copy and paste (U002) ----
        // A copy carries the payloads its markers name in a private format, so the paste can
        // rebuild editable sketches and playable chips even in another database.

        internal const string AttachmentsFormat = "KillerNotes.Attachments";

        /// <summary>Payloads offered by the paste in progress, until NormalizePastedContent
        /// adopts them.</summary>
        private Dictionary<string, Attachment>? _pastedAttachments;

        /// <summary>The payloads a copied package names, for the clipboard.</summary>
        private byte[] AttachmentEnvelope(byte[] package, IEnumerable<Attachment> sketches)
        {
            var bySketch = sketches.GroupBy(a => a.Hash).ToDictionary(g => g.Key, g => g.First());
            var carried = new List<Attachment>();
            foreach (string hash in Attachments.MarkersIn(package))
            {
                if (bySketch.TryGetValue(hash, out var sketch)) carried.Add(sketch);
                else if (RecordingPayload(hash) is { } recording) carried.Add(recording);
                else if (NoteStore.GetAttachment(hash) is { } stored) carried.Add(stored);
            }
            return Attachments.Encode(carried);
        }

        /// <summary>Called as a paste starts: keeps whatever payloads the copy carried.</summary>
        internal void AcceptPastedAttachments(IDataObject data)
        {
            _pastedAttachments = null;
            try
            {
                if (!data.GetDataPresent(AttachmentsFormat) || data.GetData(AttachmentsFormat) is not MemoryStream ms) return;
                _pastedAttachments = Attachments.Decode(ms.ToArray()).GroupBy(a => a.Hash).ToDictionary(g => g.Key, g => g.First());
            }
            catch { _pastedAttachments = null; /* a foreign or damaged envelope: pictures stay pictures */ }
        }

        /// <summary>After the paste lands: pasted sketches get their drawings back and pasted
        /// recording pictures become chips again.</summary>
        private void AdoptPastedAttachments()
        {
            var offered = _pastedAttachments ?? new Dictionary<string, Attachment>();
            _pastedAttachments = null;
            foreach (var recording in offered.Values.Where(a => a.Kind == AttachmentKind.Recording)) Remember(recording);
            Sketch.Link(Editor.Document.Blocks, new Dictionary<int, byte[]>(),
                hash => offered.TryGetValue(hash, out var a) ? a : NoteStore.GetAttachment(hash));
            ReviveMarkerPictures();
        }

        /// <summary>After SerializeRichDocument's bytes are stored.</summary>
        internal void OnRichDocumentSaved()
        {
            _dropLegacyRecordings = false;
            _recordingsSaved = new HashSet<string>(
                ChipContainers(Editor.Document).Select(c => RecordingRef.Of(c.Chip)!.Hash), StringComparer.Ordinal);
        }

        /// <summary>Turns the loaded note's recording markers back into chips, and re-anchors
        /// recordings that versions before 1.6.0 left without a chip at the end of the note.</summary>
        internal void ReviveRecordings(long noteId)
        {
            try { ReviveRecordingsCore(noteId); }
            finally
            {
                // A freshly opened note starts with an empty undo history. Without this, Ctrl+Z
                // right after switching notes undid the LOAD, putting the previous note's text,
                // or these chips' pictures, back into the note just opened. Toggling the flag is
                // the RichTextBox's own way to clear its history.
                if (Editor.IsUndoEnabled) { Editor.IsUndoEnabled = false; Editor.IsUndoEnabled = true; }
            }
        }

        private void ReviveRecordingsCore(long noteId)
        {
            _recordingCache = null;
            _dropLegacyRecordings = false;
            _recordingsSaved = new HashSet<string>(ReviveMarkerPictures(), StringComparer.Ordinal);

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
            Containers(root).Where(c => RecordingRef.Of(c.Child) != null).Select(c => (c.Container, (Border)c.Child)).ToList();

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
