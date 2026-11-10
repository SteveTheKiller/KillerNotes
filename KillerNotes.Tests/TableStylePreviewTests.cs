using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;
using Xunit.Abstractions;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The Table Style preview draws the note exactly where the editor drew it: the copy sits in
    /// an adorner, outside the window's inherited text settings, and must not reflow the note.
    /// </summary>
    public sealed class TableStylePreviewTests
    {
        private readonly ITestOutputHelper _o;
        public TableStylePreviewTests(ITestOutputHelper o) => _o = o;

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.1)]
        [InlineData(1.25)]
        public void ThePreviewKeepsTheNoteWhereItWas(double zoom) => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn());
            var rg = new TableRowGroup(); var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run("a")))); row.Cells.Add(new TableCell(new Paragraph(new Run("b"))));
            rg.Rows.Add(row); table.RowGroups.Add(rg);
            var doc = new FlowDocument();
            for (int i = 0; i < 6; i++) { var para = new Paragraph(new Run("access key ID " + i)); para.Inlines.Add(new LineBreak()); para.Inlines.Add(new Run("68ccf86606c74666b16b5ee20a1fd748")); doc.Blocks.Add(para); }
            doc.Blocks.Add(table);
            var editor = new RichTextBox(doc) { Language = System.Windows.Markup.XmlLanguage.GetLanguage("de-DE"), FontSize = 13, FontFamily = new FontFamily("Segoe UI"), Padding = new Thickness(8, 0, 8, 0),
                Background = Brushes.Black, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            if (zoom != 1.0) editor.LayoutTransform = new ScaleTransform(zoom, zoom);
            var deco = new AdornerDecorator { Child = editor };
            var root = new Border { Width = 600, Height = 600, Background = Brushes.Black, Child = deco };
            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(root, TextRenderingMode.ClearType);
            void Layout() { root.Measure(new Size(600, 600)); root.Arrange(new Rect(0, 0, 600, 600)); root.UpdateLayout(); }
            Layout();
            string a = Bounds(root);
            using var session = new TableStyleSession(editor, table, null);
            var preview = new TableStylePreviewAdorner(editor, session.PreviewDocument);
            AdornerLayer.GetAdornerLayer(editor)!.Add(preview);
            editor.Opacity = 0;
            Layout();
            string b = Bounds(root);
            _o.WriteLine($"zoom {zoom}: editor {a}; preview {b}");
            Assert.Equal(TextOptions.GetTextFormattingMode(editor.Document), TextOptions.GetTextFormattingMode(session.PreviewDocument));
            Assert.Equal(a, b);
            Assert.Equal(editor.Document.Language, session.PreviewDocument.Language);
        });

        private static string Bounds(FrameworkElement root)
        {
            int w = 600, h = 600;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bmp.Render(root);
            var px = new byte[w * h * 4]; bmp.CopyPixels(px, w * 4, 0);
            var starts = new System.Collections.Generic.List<int>(); bool was = false;
            for (int y = 0; y < h; y++) { bool hit = false; for (int x = 0; x < w && !hit; x++) hit = px[(y * w + x) * 4 + 1] > 128; if (hit && !was) starts.Add(y); was = hit; }
            return "line tops " + string.Join(",", starts);
        }
    }
}
