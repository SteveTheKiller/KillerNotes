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
            InitTableSizePicker();
            InitFormatBar();
            InitImageResize();   // click-to-resize handles on note images (ImageResize.cs)
            InitEditorView();    // remembered zoom + Ctrl+wheel (below)

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
            if (e.DataObject.GetDataPresent(DataFormats.Text) ||
                e.DataObject.GetDataPresent(DataFormats.Rtf)  ||
                e.DataObject.GetDataPresent(DataFormats.XamlPackage)) return;
            if (!Clipboard.ContainsImage()) return;

            e.CancelCommand();
            if (Clipboard.GetImage() is BitmapSource src) InsertImageAtCaret(src);
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
            TableSizeLabel.Text = "size";
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
    }
}
