using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // SketchPad companion window: open, print to note, payload save/load.
    public partial class MainWindow
    {
        // The RAIL ICON toggles: clicking it with the pad open closes the pad (Steve,
        // 2026-08-08). F7 and the double-click edit entry points keep bring-to-front semantics.
        private void SketchRail_Click(object sender, RoutedEventArgs e)
        {
            if (_sketchPad != null) { _sketchPad.Close(); return; }
            OpenSketchPad();
        }

        // SketchPad (BACKLOG: SketchPad - "mini MS Paint"). A MODELESS companion window: F7 /
        // Ctrl+Shift+D / the rail button open it (or bring it forward if already open), and it stays
        // open all day while you click back into the note and keep typing. It draws a list of
        // SketchObjects (SketchModel); "Print to note" flattens them to an image WITHOUT closing the
        // pad (the window's own X closes it). A stamped sketch lives in the note as an ordinary
        // flattened Image (which round-trips); its editable object list rides off to the side
        // (Sketch.SetData, JSON) and persists to a DB side table by document ordinal on save
        // (SaveSketchPayloads), re-attached on load (LinkSketchPayloads). Fresh open = new sketch at
        // the caret; double-clicking a placed sketch edits THAT one and Print replaces it in place.
        private SketchPadWindow? _sketchPad;
        private Image? _sketchEditTarget;   // the placed sketch being edited in place; null = Print makes a new one
        private long _sketchNoteId = -1;    // the note this pad session belongs to - Print refuses any other

        // Fresh open (F7 / Ctrl+Shift+D / rail button): new-sketch mode - Print stamps at the caret.
        private void OpenSketchPad()
        {
            _sketchEditTarget = null;
            ShowSketchPad();
        }

        // Double-click a placed sketch: edit THAT one. Load its objects; Print replaces it in place.
        private void OpenSketchPadForEdit(Image target, IReadOnlyList<SketchObject> objects)
        {
            ShowSketchPad();
            _sketchEditTarget = target;
            int w = 0, h = 0;
            if (target.Source is System.Windows.Media.Imaging.BitmapSource bs) { w = bs.PixelWidth; h = bs.PixelHeight; }   // the flattened bitmap is the drawn canvas size
            _sketchPad!.LoadObjects(objects, w, h);
        }

        // Double-click a plain (non-sketch) image: open it in the pad as a full-canvas drawable
        // layer to annotate. Print then flattens the result back over THIS same image in place.
        private void OpenSketchPadForEditImage(Image target)
        {
            if (target.Source is not System.Windows.Media.Imaging.BitmapSource bs) return;
            ShowSketchPad();
            _sketchEditTarget = target;
            _sketchPad!.LoadImageAsBackdrop(bs);
        }

        private void ShowSketchPad()
        {
            // Every entry point (fresh open, edit sketch, edit image) comes through here, so this
            // is where the session binds to the OPEN note. Print checks it before stamping.
            _sketchNoteId = _currentId;
            if (_sketchPad == null)
            {
                _sketchPad = new SketchPadWindow(this, PrintSketchToNote);
                _sketchPad.Closed += (_, _) =>
                {
                    _sketchPad = null; _sketchEditTarget = null; _sketchNoteId = -1;
                    SketchRailBtn.Tag = null;
                };
                _sketchPad.Show();
                SketchRailBtn.Tag = "on";   // light the rail toggle while the pad is open (family pattern)
            }
            else
            {
                if (_sketchPad.WindowState == WindowState.Minimized) _sketchPad.WindowState = WindowState.Normal;
                _sketchPad.Activate();
            }
        }

        // "Print to note": flatten the pad's objects to a bitmap and either update the sketch being
        // edited in place (double-click flow) or stamp a new inline image at the caret (fresh flow) -
        // exactly like a pasted image (flows with text, movable, resizable by the adorner). The pad
        // stays open. The editable object list rides along (Sketch.SetData, JSON) so the sketch reopens
        // by double-click and persists on save.
        private void PrintSketchToNote(IReadOnlyList<SketchObject> objects, int w, int h)
        {
            if (_currentId < 0) { StatusText.Text = Loc("Str_St_CalcNoNote"); return; }
            // The pad is MODELESS: the user can switch notes while it is open, and the close fade
            // defers pad teardown past a quick note switch. Without this guard the new-sketch
            // fallthrough below stamped the drawing into WHATEVER note was open at that moment -
            // one note's sketch appearing inside an unrelated note (2026-08-08). A session prints
            // only into the note it was opened from.
            if (_currentId != _sketchNoteId) { StatusText.Text = Loc("Str_St_SketchWrongNote"); return; }
            if (objects.Count == 0) { StatusText.Text = Loc("Str_St_SketchEmpty"); return; }
            if (w < 1 || h < 1) { w = Sketch.CanvasW; h = Sketch.CanvasH; }   // pad's live canvas size

            var bmp = SketchModel.RenderObjects(objects, w, h);
            var payload = SketchModel.Serialize(objects);

            // Edit-in-place: update the placed sketch where it sits (if it is still in the note).
            if (_sketchEditTarget != null && ImageInDocument(_sketchEditTarget))
            {
                _sketchEditTarget.Source = bmp;
                Sketch.SetData(_sketchEditTarget, payload);
                MarkDirty();
                StatusText.Text = Loc("Str_St_SketchUpdated");
                return;
            }

            // New sketch: inline at the caret, like a pasted image.
            var img = new Image { Source = bmp, MaxWidth = 640, Stretch = Stretch.Uniform };
            FixImage(img);   // high-quality (Fant) scaling, same as pasted images (ImageResize.cs)
            Sketch.SetData(img, payload);
            _ = new InlineUIContainer(img, Editor.CaretPosition);
            MarkDirty();
            StatusText.Text = Loc("Str_St_CalcPrinted");
        }

        // True when the image is still present in the document (the edit target may have been deleted).
        private bool ImageInDocument(Image img)
        {
            foreach (var i in EnumerateImages(Editor.Document.Blocks))
                if (ReferenceEquals(i, img)) return true;
            return false;
        }

        // Images in document order - the shared walk that gives save and load matching ordinals.
        private static IEnumerable<Image> EnumerateImages(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case BlockUIContainer buc when buc.Child is Image img:
                        yield return img;
                        break;
                    case Paragraph p:
                        foreach (var inline in p.Inlines)
                            if (inline is InlineUIContainer iuc && iuc.Child is Image pimg)
                                yield return pimg;
                        break;
                    case List list:
                        foreach (var li in list.ListItems)
                            foreach (var img in EnumerateImages(li.Blocks))
                                yield return img;
                        break;
                    case Table t:
                        foreach (var g in t.RowGroups)
                            foreach (var row in g.Rows)
                                foreach (var cell in row.Cells)
                                    foreach (var img in EnumerateImages(cell.Blocks))
                                        yield return img;
                        break;
                }
            }
        }

        // On save: persist the ISF of every image we know is a sketch, keyed by its ordinal.
        private void SaveSketchPayloads(long id)
        {
            var byOrd = new Dictionary<int, byte[]>();
            int ord = 0;
            foreach (var img in EnumerateImages(Editor.Document.Blocks))
            {
                if (Sketch.TryGetData(img, out var payload)) byOrd[ord] = payload;
                ord++;
            }
            NoteStore.SaveSketches(id, byOrd);
        }

        // On load: re-attach saved strokes to images by the same ordinal, so a reloaded sketch is
        // editable again (double-click opens the editor with its strokes).
        private void LinkSketchPayloads(long id)
        {
            var byOrd = NoteStore.LoadSketches(id);
            if (byOrd.Count == 0) return;
            int ord = 0;
            foreach (var img in EnumerateImages(Editor.Document.Blocks))
            {
                if (byOrd.TryGetValue(ord, out var payload)) Sketch.SetData(img, payload);
                ord++;
            }
        }

        /// <summary>A rule or table as the document's last block traps the caret - there is
        /// no position after it to click into, so the end of the note stops being editable.
        /// Keeps a plain paragraph at the tail (the hr paragraph is FontSize 2).</summary>
        private void EnsureEditableTail()
        {
            if (Editor.Document.Blocks.LastBlock is not Paragraph p || p.FontSize == 2)
                Editor.Document.Blocks.Add(new Paragraph());
        }

    }
}
