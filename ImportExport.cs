using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes
{
    // The file boundary. KillerNotes itself is a database - the notepad - but it can
    // pull ordinary files in as notes and push notes back out as ordinary files.
    //   Import: Ctrl+O, or drop files on the sidebar / empty editor space.
    //           .txt/.log/.md become text notes; .html/.htm notes carry the source
    //           (the preview renders it); .rtf loads with formatting; images become
    //           image notes; .knote/.kndb route to the share import paths (Sharing.cs).
    //   Export: F8 or right-click > "Export note as..." - .txt, .rtf, or .html.
    public partial class MainWindow
    {
        private static readonly string[] TextExts = [".txt", ".log", ".md"];
        private static readonly string[] HtmlExts = [".html", ".htm"];

        /// <summary>Set around the sidebar drag-OUT so dropping a note back onto the
        /// list, the editor, or the empty state does not re-import its own temp
        /// .knote as a duplicate.</summary>
        private bool _noteDragOut;

        // ---- Import ----

        /// <summary>Doc types ImportFiles handles beyond images (images stay inline-droppable).</summary>
        private static bool IsDocImport(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return TextExts.Contains(ext) || HtmlExts.Contains(ext) || ext == ".rtf" ||
                   ext == ".knote" || ext == ".kndb";
        }

        private void OpenFilesDialog()   // Ctrl+O
        {
            if (!NoteStore.IsOpen) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = Loc("Str_Filter_Open"),
            };
            if (dlg.ShowDialog(this) != true) return;
            ImportFiles(dlg.FileNames);
        }

        /// <summary>Each file becomes its own note (titled by filename); .knote/.kndb go
        /// through the share paths. Unknown extensions are read as text if possible.</summary>
        private void ImportFiles(string[] paths)
        {
            if (!NoteStore.IsOpen) return;
            SaveCurrentNote(refreshList: false);

            int made = 0;
            long lastId = -1;
            foreach (string path in paths)
            {
                try
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".kndb") { AddSharedDatabase(path); continue; }   // Sharing.cs
                    if (ext == ".knote") { ImportSharedNote(path); continue; }   // Sharing.cs

                    long id = ext == ".rtf" ? ImportRtf(path)
                            : ImgExts.Contains(ext) ? ImportImage(path)
                            : ImportText(path);
                    if (id >= 0) { made++; lastId = id; }
                }
                catch (Exception ex)
                {
                    StatusText.Text = string.Format(Loc("Str_St_ImportFailedFile"),
                        Path.GetFileName(path), ex.Message);
                }
            }
            if (made == 0) return;

            SearchBox.Text = "";   // a filtered list would hide the imports
            RefreshList();
            OpenNote(lastId);
            _syncingSelection = true;
            NotesList.SelectedItem = _notes.FirstOrDefault(x => x.Id == lastId);
            _syncingSelection = false;
            StatusText.Text = made == 1
                ? Loc("Str_St_Imported1")
                : string.Format(Loc("Str_St_ImportedN"), made);
        }

        /// <summary>Serializes a built document into a brand-new note. The editor's font
        /// is stamped on the root so code-built notes (imports, demo data) do not bake
        /// the FlowDocument default serif into the blob.</summary>
        private static long CreateNoteFromDocument(string title, FlowDocument doc)
        {
            doc.FontFamily = new FontFamily("Segoe UI");
            doc.FontSize = 13;
            long id = NoteStore.Create(title);
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            NoteStore.Save(id, title, ms.ToArray(), range.Text);
            return id;
        }

        // .txt/.log/.md and .html/.htm (html imports as SOURCE text - the preview pane
        // renders it, same as the markdown story: source in the editor, preview to view).
        private static long ImportText(string path)
        {
            var doc = new FlowDocument();
            foreach (string line in File.ReadAllLines(path))
                doc.Blocks.Add(new Paragraph(new Run(line)));
            if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph());
            return CreateNoteFromDocument(Path.GetFileNameWithoutExtension(path), doc);
        }

        private static long ImportRtf(string path)
        {
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using (var fs = File.OpenRead(path))
                range.Load(fs, DataFormats.Rtf);
            return CreateNoteFromDocument(Path.GetFileNameWithoutExtension(path), doc);
        }

        // PNG re-encode like InsertImageAtCaret: a decoded, frozen BitmapImage is the
        // only shape the XamlPackage serializer reliably persists.
        private static long ImportImage(string path)
        {
            var raw = new BitmapImage();
            raw.BeginInit();
            raw.CacheOption = BitmapCacheOption.OnLoad;
            raw.UriSource = new Uri(path);
            raw.EndInit();
            raw.Freeze();

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(raw));
            using var ms = new MemoryStream();
            enc.Save(ms);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(ms.ToArray());
            bmp.EndInit();
            bmp.Freeze();

            var doc = new FlowDocument();
            var para = new Paragraph();
            para.Inlines.Add(new InlineUIContainer(new Image
            {
                Source   = bmp,
                MaxWidth = 640,
                Stretch  = Stretch.Uniform,
            }));
            doc.Blocks.Add(para);
            return CreateNoteFromDocument(Path.GetFileNameWithoutExtension(path), doc);
        }

        // ---- Drop targets (sidebar list; the empty state and editor route here too) ----

        private void NotesList_DragOver(object sender, DragEventArgs e)
        {
            if (!_noteDragOut && NoteStore.IsOpen &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                files.Any(f => IsDocImport(f) || ImgExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void NotesList_Drop(object sender, DragEventArgs e)
        {
            if (_noteDragOut) return;   // our own drag-out landing back on the list
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                ImportFiles(files);
                e.Handled = true;
            }
        }

        // ---- Export ----

        private void ExportNoteAs_Click(object sender, RoutedEventArgs e)
        {
            var n = ((sender as MenuItem)?.DataContext as Note) ?? NotesList.SelectedItem as Note;
            if (n != null) ExportNoteAs(n);
        }

        private void ExportOpenNote()   // F8
        {
            var n = _notes.FirstOrDefault(x => x.Id == _currentId);
            if (n != null) ExportNoteAs(n);
        }

        private void ExportNoteAs(Note n)
        {
            SaveCurrentNote(refreshList: false);   // export what is on screen, not a stale copy

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = SafeFileName(n.Title) + ".txt",
                Filter = Loc("Str_Filter_Save"),
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var doc = LoadNoteDocument(n.Id);
                switch (Path.GetExtension(dlg.FileName).ToLowerInvariant())
                {
                    case ".rtf":
                        using (var fs = File.Create(dlg.FileName))
                            new TextRange(doc.ContentStart, doc.ContentEnd).Save(fs, DataFormats.Rtf);
                        break;
                    case ".html":
                        File.WriteAllText(dlg.FileName, DocumentToHtml(doc, n.Title), Encoding.UTF8);
                        break;
                    default:
                        File.WriteAllText(dlg.FileName,
                            new TextRange(doc.ContentStart, doc.ContentEnd).Text, Encoding.UTF8);
                        break;
                }
                StatusText.Text = string.Format(Loc("Str_St_ExportedTo"), dlg.FileName);
            }
            catch (Exception ex) { StatusText.Text = string.Format(Loc("Str_St_ExportFailed"), ex.Message); }
        }

        private static FlowDocument LoadNoteDocument(long id)
        {
            var doc = new FlowDocument();
            var blob = NoteStore.LoadContent(id);
            if (blob != null)
            {
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                using var ms = new MemoryStream(blob);
                range.Load(ms, DataFormats.XamlPackage);
            }
            // Same normalization as the editor load: neutral baked colors drop away so
            // exports carry only DELIBERATE colors (the HTML shell styles the rest).
            NormalizeThemeColors(doc);
            return doc;
        }

        // ---- FlowDocument -> HTML ----
        // Deliberately small: paragraphs (the FontSize-2 bottom-border rule becomes <hr>),
        // lists, tables, bold/italic/underline/strike, monospace, text color/highlight,
        // and inline images (base64 PNG). Enough for notes, not a Word clone. The page is
        // styled from the live theme so the export looks like the app (and the theme text
        // color the XamlPackage bakes into runs stays readable).

        private string DocumentToHtml(FlowDocument doc, string title)
        {
            string bg     = BrushHex("PaneBrush", "#1c1c1c");        // Preview.cs helpers
            string fg     = BrushHex("TextBrush", "#e0e0e0");
            string accent = BrushHex("PrimaryBrush", "#B982E3");
            string border = BrushHex("CardBorderBrush", "#3a3a3a");

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>")
              .Append(Html(title)).Append("</title><style>")
              .Append($"body{{background:{bg};color:{fg};font-family:'Segoe UI',sans-serif;")
              .Append("font-size:13px;max-width:900px;margin:24px auto;padding:0 16px}")
              .Append($"a{{color:{accent}}}")
              .Append($"table{{border-collapse:collapse}}td,th{{border:1px solid {border};padding:3px 8px}}")
              .Append("code{font-family:Consolas,monospace}img{max-width:100%}")
              .Append($"hr{{border:none;border-top:1px solid {border}}}")
              .Append("p{margin:0 0 2px 0}")
              .Append("</style></head><body>");
            foreach (var block in doc.Blocks) AppendBlock(sb, block);
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void AppendBlock(StringBuilder sb, Block block)
        {
            switch (block)
            {
                case Paragraph p when p.FontSize <= 2 && p.BorderThickness.Bottom > 0:
                    sb.Append("<hr/>");
                    break;
                case Paragraph p:
                    sb.Append("<p>");
                    AppendInlines(sb, p.Inlines);
                    sb.Append("</p>");
                    break;
                case List list:
                    string tag = list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul";
                    sb.Append('<').Append(tag).Append('>');
                    foreach (var li in list.ListItems)
                    {
                        sb.Append("<li>");
                        foreach (var b in li.Blocks)
                        {
                            if (b is Paragraph pp) AppendInlines(sb, pp.Inlines);
                            else AppendBlock(sb, b);
                        }
                        sb.Append("</li>");
                    }
                    sb.Append("</").Append(tag).Append('>');
                    break;
                case Table t:
                    sb.Append("<table>");
                    foreach (var g in t.RowGroups)
                    {
                        foreach (var row in g.Rows)
                        {
                            sb.Append("<tr>");
                            foreach (var cell in row.Cells)
                            {
                                sb.Append("<td>");
                                foreach (var b in cell.Blocks)
                                {
                                    if (b is Paragraph pp) AppendInlines(sb, pp.Inlines);
                                    else AppendBlock(sb, b);
                                }
                                sb.Append("</td>");
                            }
                            sb.Append("</tr>");
                        }
                    }
                    sb.Append("</table>");
                    break;
                case Section s:
                    foreach (var b in s.Blocks) AppendBlock(sb, b);
                    break;
                case BlockUIContainer bc when bc.Child is Image img:
                    AppendImage(sb, img);
                    break;
            }
        }

        private static void AppendInlines(StringBuilder sb, InlineCollection inlines)
        {
            foreach (var inline in inlines) AppendInline(sb, inline);
        }

        private static void AppendInline(StringBuilder sb, Inline inline)
        {
            switch (inline)
            {
                case Run r:
                    AppendRun(sb, r);
                    break;
                case LineBreak:
                    sb.Append("<br/>");
                    break;
                case InlineUIContainer iu when iu.Child is Image img:
                    AppendImage(sb, img);
                    break;
                case Span sp:   // includes Bold/Italic/Underline/Hyperlink containers
                    AppendInlines(sb, sp.Inlines);
                    break;
            }
        }

        private static void AppendRun(StringBuilder sb, Run r)
        {
            bool bold   = r.FontWeight.ToOpenTypeWeight() >= 600;
            bool italic = r.FontStyle == FontStyles.Italic;
            bool mono   = r.FontFamily?.Source?.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0;
            bool under  = false, strike = false;
            if (r.TextDecorations != null)
            {
                foreach (var d in r.TextDecorations)
                {
                    if (d.Location == TextDecorationLocation.Underline) under = true;
                    if (d.Location == TextDecorationLocation.Strikethrough) strike = true;
                }
            }

            // Local values only: after NormalizeThemeColors, a color that is still set
            // was chosen on purpose. Inherited defaults stay unstyled so the page's
            // theme-shell CSS colors them.
            string style = "";
            if (r.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush f)
                style += $"color:#{f.Color.R:X2}{f.Color.G:X2}{f.Color.B:X2};";
            if (r.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush b && b.Color.A > 0)
                style += $"background:#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2};";

            if (style.Length > 0) sb.Append("<span style=\"").Append(style).Append("\">");
            if (bold)   sb.Append("<b>");
            if (italic) sb.Append("<i>");
            if (under)  sb.Append("<u>");
            if (strike) sb.Append("<s>");
            if (mono)   sb.Append("<code>");

            sb.Append(Html(r.Text));

            if (mono)   sb.Append("</code>");
            if (strike) sb.Append("</s>");
            if (under)  sb.Append("</u>");
            if (italic) sb.Append("</i>");
            if (bold)   sb.Append("</b>");
            if (style.Length > 0) sb.Append("</span>");
        }

        private static void AppendImage(StringBuilder sb, Image img)
        {
            if (img.Source is not BitmapSource src) return;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            sb.Append("<img src=\"data:image/png;base64,")
              .Append(Convert.ToBase64String(ms.ToArray()))
              .Append("\"/>");
        }

        private static string Html(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
