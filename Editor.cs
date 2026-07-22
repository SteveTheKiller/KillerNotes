using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes
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
            InitTableSizePicker();
            InitFormatBar();
            InitImageResize();   // click-to-resize handles on note images (ImageResize.cs)
            InitEditorView();    // remembered zoom + Ctrl+wheel (below)
            InitWordWrap();      // remembered word-wrap toggle (below)

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
        // no cell spacing - matching InsertTable.
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
            string raw = new string((sel.Text ?? "").Where(c =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.Format).ToArray());

            // Rows: split on line and cell breaks. Keep only rows with real content (a letter or
            // digit): this drops blank rows AND phantom rows made of invisible characters that
            // rich text / bulleted lists carry, which otherwise skip the word split below and
            // show up as a stray leading comma.
            var rows = raw
                .Split(new[] { '\r', '\n', '\t', '\v', '\f' }, StringSplitOptions.RemoveEmptyEntries)
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

        // ---- Insert table (with Office-style size picker) ----
        // Pressing the table button opens a hover grid: press-hold-drag-release OR
        // click-then-hover-then-click both select a rows x cols size. The inserted table is
        // a real FlowDocument Table; borders bind CardBorderBrush through SetResourceReference
        // so they follow live theme switches (net48 family gotcha: a snapshot would not).

        private const int TblMaxCols = 8;
        private const int TblMaxRows = 6;
        private int _tblCols, _tblRows;
        private int _tblOpenedAt;   // TickCount when the popup opened (click-through guard)

        private void InitTableSizePicker()
        {
            for (int i = 0; i < TblMaxRows * TblMaxCols; i++)
            {
                var cell = new Border
                {
                    Width = 14, Height = 14,
                    Margin = new Thickness(1),
                    BorderThickness = new Thickness(1),
                    Tag = i,
                };
                cell.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                cell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                cell.MouseEnter += TableCell_MouseEnter;
                cell.MouseLeftButtonUp += TableCell_Commit;    // release after press-drag
                cell.MouseLeftButtonDown += TableCell_Commit;  // click in hover mode
                TableSizeCells.Children.Add(cell);
            }
        }

        // e.Handled keeps the Button from capturing the mouse, so a held drag delivers
        // MouseEnter/MouseUp to the popup cells instead of dying inside the button.
        private void TableBtn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentId < 0) return;
            _tblCols = _tblRows = 0;
            TableSizeLabel.Text = Loc("Str_Lbl_Size");   // same key as its XAML default
            foreach (Border b in TableSizeCells.Children)
                b.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            TableSizePopup.IsOpen = true;
            _tblOpenedAt = Environment.TickCount;
            if (TableSizePopup.Child is UIElement ch) Anim.FadeIn(ch);
            e.Handled = true;
        }

        private void TableCell_MouseEnter(object sender, MouseEventArgs e)
        {
            if ((sender as Border)?.Tag is not int idx) return;
            _tblRows = idx / TblMaxCols + 1;
            _tblCols = idx % TblMaxCols + 1;
            TableSizeLabel.Text = $"{_tblCols} x {_tblRows}";
            int i = 0;
            foreach (Border b in TableSizeCells.Children)
            {
                int r = i / TblMaxCols, c = i % TblMaxCols; i++;
                b.SetResourceReference(Border.BackgroundProperty,
                    r < _tblRows && c < _tblCols ? "RowSelectedBrush" : "SurfaceBrush");
            }
        }

        private void TableCell_Commit(object sender, MouseButtonEventArgs e)
        {
            // Click-through guard: a quick click on the toolbar button releases over the
            // popup a moment later, which used to insert a table by accident. A release
            // within 300ms of opening just leaves the flyout open (for hovering the grid
            // or typing a custom size); the press-hold-DRAG gesture takes longer than
            // that, so it still commits on release.
            if (e.RoutedEvent == MouseLeftButtonUpEvent &&
                Environment.TickCount - _tblOpenedAt < 300) return;

            TableSizePopup.IsOpen = false;
            if (_tblCols > 0 && _tblRows > 0) InsertTable(_tblRows, _tblCols);
            e.Handled = true;
        }

        // Custom size row under the hover grid, for anything bigger than 8x6.
        private void TblCustomInsert_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TblColsBox.Text.Trim(), out int cols) ||
                !int.TryParse(TblRowsBox.Text.Trim(), out int rows) ||
                cols < 1 || rows < 1 || cols > 50 || rows > 200)
            {
                TableSizeLabel.Text = Loc("Str_St_CustomRange");
                return;
            }
            TableSizePopup.IsOpen = false;
            InsertTable(rows, cols);
        }

        private void TblCustom_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { TblCustomInsert_Click(sender, e); e.Handled = true; }
        }

        private void InsertTable(int rows, int cols)
        {
            if (_currentId < 0) return;

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6) };
            table.SetResourceReference(Table.BorderBrushProperty, "CardBorderBrush");
            table.BorderThickness = new Thickness(1, 1, 0, 0);

            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            for (int r = 0; r < rows; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < cols; c++)
                {
                    var cell = new TableCell(new Paragraph(new Run("")))
                    {
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(6, 3, 6, 3),
                    };
                    cell.SetResourceReference(TableCell.BorderBrushProperty, "CardBorderBrush");
                    row.Cells.Add(cell);
                }
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);

            var para = Editor.CaretPosition.Paragraph;
            if (para != null && para.Parent is FlowDocument doc) doc.Blocks.InsertAfter(para, table);
            else Editor.Document.Blocks.Add(table);
            EnsureEditableTail();

            MarkDirty();
            Editor.Focus();
        }

        /// <summary>A rule or table as the document's last block traps the caret - there is
        /// no position after it to click into, so the end of the note stops being editable.
        /// Keeps a plain paragraph at the tail (the hr paragraph is FontSize 2).</summary>
        private void EnsureEditableTail()
        {
            if (Editor.Document.Blocks.LastBlock is not Paragraph p || p.FontSize == 2)
                Editor.Document.Blocks.Add(new Paragraph());
        }

        // ---- Theme-adaptive colors ----
        // A XamlPackage blob bakes the EFFECTIVE colors at save time: a note typed in a
        // dark theme carries white text, which a light-theme reader (or a .knote
        // recipient) sees as white-on-white. On every load, neutral (grayscale-ish)
        // foregrounds/backgrounds are stripped so default text follows the live theme;
        // deliberately colored text and highlights are left alone.

        /// <summary>True for colors that read as "default text", not a chosen color:
        /// black, white, and the near-gray theme text tones.</summary>
        private static bool IsNeutralColor(Color c)
        {
            if (c.A == 0) return true;   // fully transparent background
            int spread = Math.Max(Math.Abs(c.R - c.G), Math.Max(Math.Abs(c.G - c.B), Math.Abs(c.R - c.B)));
            return spread <= 24;
        }

        private static void NormalizeThemeColors(FlowDocument doc)
        {
            doc.ClearValue(FlowDocument.ForegroundProperty);
            doc.ClearValue(FlowDocument.BackgroundProperty);
            foreach (var block in doc.Blocks.ToList()) NormalizeBlock(block);
        }

        private static void NormalizeBlock(Block block)
        {
            NormalizeElement(block);
            switch (block)
            {
                case Paragraph p:
                    foreach (var i in p.Inlines.ToList()) NormalizeInline(i);
                    break;
                case List list:
                    foreach (var li in list.ListItems.ToList())
                    {
                        NormalizeElement(li);
                        foreach (var b in li.Blocks.ToList()) NormalizeBlock(b);
                    }
                    break;
                case Table t:
                    foreach (var g in t.RowGroups.ToList())
                        foreach (var row in g.Rows.ToList())
                        {
                            NormalizeElement(row);
                            foreach (var cell in row.Cells.ToList())
                            {
                                NormalizeElement(cell);
                                foreach (var b in cell.Blocks.ToList()) NormalizeBlock(b);
                            }
                        }
                    break;
                case Section s:
                    foreach (var b in s.Blocks.ToList()) NormalizeBlock(b);
                    break;
            }
        }

        private static void NormalizeInline(Inline inline)
        {
            NormalizeElement(inline);
            if (inline is Span sp)
                foreach (var i in sp.Inlines.ToList()) NormalizeInline(i);
        }

        private static void NormalizeElement(TextElement te)
        {
            // Links: pasted/loaded hyperlinks carry the source's baked link-blue and
            // underline on themselves AND their runs. Clear both so the themed editor
            // style (Editor.Resources, accent color) paints them and they follow theme
            // switches live - same idea as the neutral-color rule below.
            if (te is Hyperlink link)
            {
                link.ClearValue(TextElement.ForegroundProperty);
                link.ClearValue(Inline.TextDecorationsProperty);
                foreach (var li in link.Inlines)
                {
                    li.ClearValue(TextElement.ForegroundProperty);
                    li.ClearValue(Inline.TextDecorationsProperty);
                }
                return;
            }
            if (te.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush f && IsNeutralColor(f.Color))
                te.ClearValue(TextElement.ForegroundProperty);
            if (te.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush b && IsNeutralColor(b.Color))
                te.ClearValue(TextElement.BackgroundProperty);
        }

        // ---- Character formatting (color, highlight, strikethrough, monospace) ----

        private void TextColorBtn_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = !ColorPopup.IsOpen;
            if (ColorPopup.IsOpen && ColorPopup.Child is UIElement ch) Anim.FadeIn(ch);
        }

        private void FgSwatch_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string tag) return;
            Brush b = tag == "accent"
                ? TryFindResource("PrimaryBrush") as Brush ?? Brushes.MediumPurple
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag));
            ApplyToSelection(TextElement.ForegroundProperty, b);
            ColorPopup.IsOpen = false;
        }

        private void FgAuto_Click(object sender, RoutedEventArgs e)
        {
            // Colored runs store a concrete color; "auto" writes the current theme text
            // color back (a theme-reactive reference cannot survive the XamlPackage).
            ApplyToSelection(TextElement.ForegroundProperty,
                TryFindResource("TextBrush") as Brush ?? Brushes.White);
            ColorPopup.IsOpen = false;
        }

        private void BgSwatch_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string tag) return;
            ApplyToSelection(TextElement.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag)));
            ColorPopup.IsOpen = false;
        }

        private void BgNone_Click(object sender, RoutedEventArgs e)
        {
            ApplyToSelection(TextElement.BackgroundProperty, null);
            ColorPopup.IsOpen = false;
        }

        private void Strike_Click(object sender, RoutedEventArgs e)
        {
            var cur = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            bool has = cur is TextDecorationCollection tdc &&
                       tdc.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            ApplyToSelection(Inline.TextDecorationsProperty,
                has ? new TextDecorationCollection() : TextDecorations.Strikethrough);
        }

        private void Mono_Click(object sender, RoutedEventArgs e)
        {
            var cur = Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
            bool mono = cur?.Source?.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0;
            ApplyToSelection(TextElement.FontFamilyProperty, new FontFamily(mono ? "Segoe UI" : "Consolas"));
        }

        private void ApplyToSelection(DependencyProperty prop, object? value)
        {
            if (_currentId < 0) return;
            Editor.Selection.ApplyPropertyValue(prop, value);
            MarkDirty();
            Editor.Focus();
        }

        // ---- Horizontal rule ----
        // A pure-FlowDocument rule (bottom-bordered empty paragraph) so it survives the
        // XamlPackage round trip. Concrete gray on purpose: package content cannot keep
        // theme-reactive references.
        private void InsertRule_Click(object sender, RoutedEventArgs e)
        {
            if (_currentId < 0) return;
            var hr = new Paragraph
            {
                FontSize = 2,
                Margin = new Thickness(0, 8, 0, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a)),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            var para = Editor.CaretPosition.Paragraph;
            if (para != null && para.Parent is FlowDocument doc) doc.Blocks.InsertAfter(para, hr);
            else Editor.Document.Blocks.Add(hr);
            EnsureEditableTail();
            // Land the caret below the rule, ready to keep typing (Word behavior).
            Editor.CaretPosition = hr.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            MarkDirty();
            Editor.Focus();
        }

        // ---- Font size / editor zoom / full color picker / spell check (1.0.1, #1) ----

        private static readonly int[] FontSizes = [10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48];
        private double _editorZoom = 1.0;

        /// <summary>Called once from InitEditor: restores the remembered editor zoom and
        /// wires Ctrl+wheel. Zoom is a view setting (LayoutTransform), not note content.</summary>
        private void InitEditorView()
        {
            if (int.TryParse(App.GetSetting("EditorZoom"), out int pct) && pct >= 50 && pct <= 300 && pct != 100)
            {
                _editorZoom = pct / 100.0;
                Editor.LayoutTransform = new ScaleTransform(_editorZoom, _editorZoom);
            }
            Editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
            // Keep the size dropdown showing the size under the caret/selection.
            Editor.SelectionChanged += (_, _) => UpdateFontSizeDisplay();
        }

        private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            SetEditorZoom(_editorZoom + (e.Delta > 0 ? 0.1 : -0.1));
            e.Handled = true;
        }

        /// <summary>Clamps to 50-300%, applies, persists, announces. Ctrl+0 resets to 100.</summary>
        private void SetEditorZoom(double zoom)
        {
            zoom = Math.Round(Math.Max(0.5, Math.Min(3.0, zoom)), 2);
            _editorZoom = zoom;
            Editor.LayoutTransform = zoom == 1.0 ? Transform.Identity : new ScaleTransform(zoom, zoom);
            App.SetSetting("EditorZoom", ((int)Math.Round(zoom * 100)).ToString());
            StatusText.Text = string.Format(Loc("Str_St_Zoom"), (int)Math.Round(zoom * 100));
            RebuildLineNumbers();   // LineNumbers.cs (numbers track the editor zoom)
        }

        // The size list is built lazily so startup never pays for it.
        private void FontSizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (FontSizeList.Children.Count == 0)
                foreach (int s in FontSizes)
                {
                    var b = new Button
                    {
                        Content = s.ToString(), Width = 52, Margin = new Thickness(1),
                        Style = TryFindResource("SurfaceButton") as Style,
                    };
                    int size = s;
                    b.Click += (_, _) => { FontSizePopup.IsOpen = false; ApplyFontSize(size); };
                    FontSizeList.Children.Add(b);
                }
            FontSizePopup.IsOpen = !FontSizePopup.IsOpen;
            if (FontSizePopup.IsOpen && FontSizePopup.Child is UIElement ch) Anim.FadeIn(ch);
        }

        // Hover the dropdown and scroll to step through the size ladder - no click needed.
        private void FontSizeBtn_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (_currentId < 0) return;
            double cur = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double d ? d : 13;
            int idx = ClosestSizeIndex(cur) + (e.Delta > 0 ? 1 : -1);
            idx = Math.Max(0, Math.Min(FontSizes.Length - 1, idx));
            ApplyFontSize(FontSizes[idx]);
        }

        private void ApplyFontSize(int size)
        {
            ApplyToSelection(TextElement.FontSizeProperty, (double)size);
            FontSizeText.Text = size.ToString();
        }

        /// <summary>"-" when the selection mixes sizes.</summary>
        private void UpdateFontSizeDisplay() =>
            FontSizeText.Text = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double d
                ? Math.Round(d).ToString() : "-";

        private static int ClosestSizeIndex(double size)
        {
            int best = 0;
            for (int i = 1; i < FontSizes.Length; i++)
                if (Math.Abs(FontSizes[i] - size) < Math.Abs(FontSizes[best] - size)) best = i;
            return best;
        }

        // "More..." in the color flyout: the full family picker (ColorPickerDialog).

        private void FgMore_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = false;
            var cur = (Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color
                      ?? (TryFindResource("TextBrush") as SolidColorBrush)?.Color ?? Colors.White;
            var dlg = new ColorPickerDialog(this, cur);
            if (dlg.ShowDialog() == true)
                ApplyToSelection(TextElement.ForegroundProperty, new SolidColorBrush(dlg.SelectedColor));
        }

        private void BgMore_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = false;
            var cur = (Editor.Selection.GetPropertyValue(TextElement.BackgroundProperty) as SolidColorBrush)?.Color
                      ?? Color.FromRgb(0x7A, 0x6A, 0x00);
            var dlg = new ColorPickerDialog(this, cur);
            if (dlg.ShowDialog() == true)
                ApplyToSelection(TextElement.BackgroundProperty, new SolidColorBrush(dlg.SelectedColor));
        }

        // ---- Spell check (per note, off by default; Windows spell checking APIs) ----

        private void Spell_Click(object sender, RoutedEventArgs e)
        {
            if (_currentId < 0) return;
            bool on = !Editor.SpellCheck.IsEnabled;
            ApplySpellCheck(on);
            NoteStore.SetSpellCheck(_currentId, on);
            if (_notes.FirstOrDefault(n => n.Id == _currentId) is Note meta) meta.SpellCheck = on;
            StatusText.Text = Loc(on ? "Str_St_SpellOn" : "Str_St_SpellOff");
        }

        /// <summary>Applies the state to the editor and lights the abc+check button in the
        /// accent while on. Called on every note open with the note's saved flag.
        /// TextElement.Foreground is inherited, so setting it on the icon Grid colors
        /// both the "abc" and the check mark at once.</summary>
        private void ApplySpellCheck(bool on)
        {
            try { Editor.SpellCheck.IsEnabled = on; }
            catch { on = false; }   // OS spell checking unavailable - stay off quietly
            if (on) SpellBtnIcon.SetResourceReference(TextElement.ForegroundProperty, "PrimaryBrush");
            else SpellBtnIcon.ClearValue(TextElement.ForegroundProperty);
        }

        // ---- Word wrap toggle (global view setting, remembered like zoom) ----
        // Wrap on (default): the document page width is auto, so text wraps to the editor
        // pane. Off: a wide fixed page width, so long lines and over-wide images/tables do
        // not wrap and the editor's horizontal scrollbar (MainWindow.xaml) can reach them.
        // The button lights in the accent while wrap is on. Editor.Document is reused across
        // note loads (Notes.cs OpenNote clears blocks, not the document), so the page width
        // persists; OpenNote re-asserts it after each load to be safe.
        private bool _wordWrap = true;
        private const double NoWrapPageWidth = 4000;

        private void InitWordWrap() => ApplyWordWrap(App.GetSetting("WordWrap") != "off");

        private void WordWrap_Click(object sender, RoutedEventArgs e)
        {
            ApplyWordWrap(!_wordWrap);
            App.SetSetting("WordWrap", _wordWrap ? "on" : "off");
            StatusText.Text = Loc(_wordWrap ? "Str_St_WrapOn" : "Str_St_WrapOff");
        }

        private void ApplyWordWrap(bool wrap)
        {
            _wordWrap = wrap;
            Editor.Document.PageWidth = wrap ? double.NaN : NoWrapPageWidth;
            if (wrap) WrapBtnIcon.SetResourceReference(TextElement.ForegroundProperty, "PrimaryBrush");
            else WrapBtnIcon.ClearValue(TextElement.ForegroundProperty);
        }
    }
}
