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
    // Editor extras the stock RichTextBox doesn't do: pasting clipboard images inline,
    // and inserting real FlowDocument tables. Bold/italic/underline/lists are the
    // built-in EditingCommands, wired straight from the format bar in XAML.
    public partial class MainWindow
    {
        private void InitEditor()
        {
            DataObject.AddPastingHandler(Editor, Editor_OnPaste);
            InitLinks();         // Links.cs (clickable + pasted + Ctrl+K hyperlinks)
            InitTiltWheel();     // TiltWheel.cs (WM_MOUSEHWHEEL + Shift+wheel, issue #9)
            InitTableSizePicker();
            InitFormatBar();
            InitImageResize();   // click-to-resize handles on note images (ImageResize.cs)
            InitEditorView();    // remembered zoom + Ctrl+wheel (below)
            InitWordWrap();      // remembered word-wrap toggle (below)
            InitSyntaxHighlighting();
            InitWikiLinks();        // WikiLinkNav.cs (Ctrl+Click a [[link]] to follow it)
            InitWikiLinkComplete(); // WikiLinkComplete.cs (title picker after "[[")
            InitEditorClipboard();   // cut that cannot lose the race for the clipboard (Editor.Clipboard.cs, #16)

            // Drag-and-drop: text drops are native RichTextBox behavior; image files and
            // raw bitmaps need the handlers below.
            Editor.AllowDrop = true;
            Editor.PreviewDragOver += Editor_PreviewDragOver;
            Editor.PreviewDrop += Editor_PreviewDrop;

            // Ctrl+S saves immediately (autosave runs 2s after the last change anyway).
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) =>
            {
                SaveCurrentNote();
                StatusText.Text = Loc("Str_St_Saved");
            }));
        }

        // ---- Image paste ----
        // The stock RichTextBox drops a bare clipboard bitmap (screenshots, Snipping Tool).
        // Intercept those pastes and insert the image inline; text-bearing formats are left
        // to the default paste path.

        private void Editor_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            // CherryTree and browsers put links on the clipboard as HTML + plain text
            // with no RTF - and WPF's native paste has no HTML path, so those links
            // died to plain text. Convert the HTML ourselves when it carries links
            // (Links.cs); everything else stays on the native paste below.
            if (!e.DataObject.GetDataPresent(DataFormats.Rtf) &&
                !e.DataObject.GetDataPresent(DataFormats.XamlPackage) &&
                e.DataObject.GetDataPresent(DataFormats.Html) &&
                TryPasteHtml(e)) return;

            if (e.DataObject.GetDataPresent(DataFormats.Text) ||
                e.DataObject.GetDataPresent(DataFormats.Rtf)  ||
                e.DataObject.GetDataPresent(DataFormats.XamlPackage))
            {
                // Text/RTF/Xaml pastes (Excel, Word, the browser) bake the SOURCE's own colors
                // and table borders: black text disappears in the dark themes and Excel's bright
                // gridlines clash. Let WPF's native paste do the clipboard->document conversion
                // (it handles HTML tables, RTF, and images), then re-run the note's own theme
                // normalization once it lands (the same neutral-color rule as note load).
                Dispatcher.BeginInvoke(new Action(NormalizePastedContent),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            if (!Clipboard.ContainsImage()) return;

            e.CancelCommand();
            if (Clipboard.GetImage() is BitmapSource src) InsertImageAtCaret(src);
        }

        // After a text/RTF/Xaml paste lands: strip the source's baked-in neutral (black/white/gray)
        // text colors so they follow the live theme, drop the paragraph margins the paste converter
        // bakes on (so Notepad lines keep the editor's own line spacing), and give any pasted table
        // the app's subtle border styling instead of Excel's bright gridlines. Neutral-only on color,
        // so deliberately colored pasted text and highlights are left alone (as on note load).
        private void NormalizePastedContent()
        {
            if (_currentId < 0) return;
            NormalizeThemeColors(Editor.Document);
            foreach (var block in Editor.Document.Blocks.ToList()) NormalizePastedBlock(block);
            MarkDirty();
        }

        // Brings pasted blocks to the editor's own defaults. Paragraphs lose the margin the
        // text-to-document paste converter bakes onto every line (Notepad line breaks otherwise
        // paste with extra line spacing the editor's zero-margin typed paragraphs don't have); the
        // FontSize 2 rule paragraph keeps its margin, since that spacing is deliberate. Tables take
        // the family look: the theme card-border brush via SetResourceReference so it tracks live
        // theme switches (a baked snapshot would not - net48 family gotcha), a single-line grid, and
        // no cell spacing - matching InsertTable. Excel's cell fills are cleared too: the neutral-color
        // rule (NormalizeThemeColors) only drops neutral fills, so a colored Excel fill (a yellow header,
        // a red status cell) used to survive while its text was theme-normalized, leaving light-on-light
        // or dark-on-dark that read only after a manual theme switch (#11). Cleared, the cell shows the
        // theme surface like an inserted table and stays readable in either theme; colored TEXT and text
        // highlights are still left untouched.
        private static void NormalizePastedBlock(Block block)
        {
            switch (block)
            {
                case Paragraph p:
                    if (p.FontSize != 2) p.ClearValue(Block.MarginProperty);
                    break;
                case Table t:
                    t.CellSpacing = 0;
                    t.SetResourceReference(Table.BorderBrushProperty, "CardBorderBrush");
                    t.BorderThickness = new Thickness(1, 1, 0, 0);
                    foreach (var g in t.RowGroups)
                        foreach (var row in g.Rows)
                            foreach (var cell in row.Cells)
                            {
                                cell.SetResourceReference(TableCell.BorderBrushProperty, "CardBorderBrush");
                                cell.BorderThickness = new Thickness(0, 0, 1, 1);
                                cell.ClearValue(TableCell.BackgroundProperty);   // drop Excel's cell shading; adopt the theme surface (#11)
                                foreach (var b in cell.Blocks.ToList()) NormalizePastedBlock(b);
                            }
                    break;
                case Section s:
                    foreach (var b in s.Blocks.ToList()) NormalizePastedBlock(b);
                    break;
                case List list:
                    foreach (var li in list.ListItems)
                        foreach (var b in li.Blocks.ToList()) NormalizePastedBlock(b);
                    break;
            }
        }

    }
}
