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
    // Convert-to-list, image insertion, and editor drag-and-drop.
    public partial class MainWindow
    {
        // ---- Convert selection to a comma-separated list ----
        // Turns the selection into PC1,PC2,PC3 for dropping into scripts. Multiple lines (or a
        // selected table column) split on line and cell breaks; a single highlighted sentence
        // splits on its spaces/commas instead, so a run of words on one line becomes a list too.
        // Items are trimmed and blanks dropped. A plain-text selection is rewritten in place
        // (what "convert" reads as); a selection spanning table cells can't be replaced with one
        // string, so there the list goes to the clipboard instead. Right-click, or Ctrl+Shift+J.
        private void ConvertToList_Click(object sender, RoutedEventArgs e) => ConvertSelectionToList();

        private void ConvertSelectionToList()
        {
            if (_currentId < 0) return;
            var sel = Editor.Selection;
            if (sel.IsEmpty) { StatusText.Text = Loc("Str_St_ListNoSel"); return; }

            // Strip invisible formatting characters (zero-width spaces, BOM, etc.) that rich
            // text and bulleted lists sometimes carry. Left in, they survive trimming as a
            // phantom item and show up as a stray leading comma.
            string raw = new([.. (sel.Text ?? "").Where(c =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.Format)]);

            // Rows: split on line and cell breaks. Keep only rows with real content (a letter or
            // digit): this drops blank rows AND phantom rows made of invisible characters that
            // rich text / bulleted lists carry, which otherwise skip the word split below and
            // show up as a stray leading comma.
            var rows = raw
                .Split(['\r', '\n', '\t', '\v', '\f'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Any(char.IsLetterOrDigit))
                .ToArray();

            // One line (a highlighted sentence) becomes its words - split on any whitespace plus
            // commas and semicolons, so "PC1 PC2 PC3" or "PC1, PC2" both give PC1,PC2,PC3. Several
            // lines keep one item per line. The final filter drops any blank so a stray separator
            // can never produce a leading/empty comma.
            var items = (rows.Length == 1
                    ? rows[0].Replace(',', ' ').Replace(';', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    : rows)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            if (items.Length == 0) { StatusText.Text = Loc("Str_St_ListNoSel"); return; }

            string list = string.Join(",", items);

            // Rewrite plain-text selections in place; fall back to the clipboard when the
            // selection spans table cells (setting Text across cells throws).
            try
            {
                sel.Text = list;
                StatusText.Text = string.Format(Loc("Str_St_ListMade"), items.Length);
                MarkDirty();
            }
            catch
            {
                try { Clipboard.SetText(list); } catch { /* clipboard busy - nothing to do */ }
                StatusText.Text = string.Format(Loc("Str_St_ListCopied"), items.Length);
            }
        }

        private void InsertImageAtCaret(BitmapSource src)
        {
            // Re-encode to PNG: clipboard images arrive as InteropBitmap, which the
            // XamlPackage serializer can't persist. A decoded, frozen BitmapImage can.
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(ms.ToArray());
            bmp.EndInit();
            bmp.Freeze();

            var img = new Image
            {
                Source   = bmp,
                MaxWidth = 640,
                Stretch  = Stretch.Uniform,
            };
            FixImage(img);   // high-quality (Fant) downscale rendering (ImageResize.cs)
            _ = new InlineUIContainer(img, Editor.CaretPosition);
            MarkDirty();
        }

        // Format-bar image button: pick an image file and drop it in at the caret. Paste (Ctrl+V)
        // and drag-and-drop are the other two ways an image gets into a note.
        private void InsertImageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentId < 0) { StatusText.Text = Loc("Str_St_CalcNoNote"); return; }
            var dlg = new KillerPDF.Controls.FileDialog(KillerPDF.Controls.FileDialogMode.Open)
            {
                Title = Loc("Str_TT_Image"),
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource   = new Uri(dlg.FileName);
                bmp.EndInit();
                bmp.Freeze();
                InsertImageAtCaret(bmp);
                Editor.Focus();
            }
            catch { StatusText.Text = Loc("Str_St_OnlyImages"); }
        }

        // ---- Drag and drop ----
        // Claim only what the RichTextBox can't handle natively (file drops, raw bitmaps
        // from apps like browsers); plain dragged text keeps the built-in behavior.

        private static readonly string[] ImgExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];

        private void Editor_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (_noteDragOut) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
                (!e.Data.GetDataPresent(DataFormats.Text) && e.Data.GetDataPresent(DataFormats.Bitmap)))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Editor_PreviewDrop(object sender, DragEventArgs e)
        {
            if (HandleEditorDrop(e)) e.Handled = true;
        }

        /// <summary>Inserts dropped image files / bitmaps at the drop point. Shared with the
        /// empty-state drop target (Notes.cs). Returns true when the drop was consumed.</summary>
        private bool HandleEditorDrop(DragEventArgs e)
        {
            if (_noteDragOut) return true;   // our own drag-out - swallow it, never self-import
            if (_currentId < 0) return false;

            // Land images where the mouse is, not wherever the caret last sat.
            var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor), true);
            if (pos != null) Editor.CaretPosition = pos;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                // Document files dropped on an open note still become their own notes
                // (ImportExport.cs) - only images land inline at the drop point.
                var docs = files.Where(IsDocImport).ToArray();
                if (docs.Length > 0)
                {
                    ImportFiles(docs);
                    return true;
                }
                bool any = false;
                foreach (var f in files)
                {
                    if (!ImgExts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(f);
                        bmp.EndInit();
                        bmp.Freeze();
                        InsertImageAtCaret(bmp);
                        any = true;
                    }
                    catch { /* unreadable file - skip it */ }
                }
                if (!any) StatusText.Text = Loc("Str_St_OnlyImages");
                return true;
            }
            if (!e.Data.GetDataPresent(DataFormats.Text) &&
                e.Data.GetData(DataFormats.Bitmap) is BitmapSource src)
            {
                InsertImageAtCaret(src);
                return true;
            }
            return false;
        }

    }
}
