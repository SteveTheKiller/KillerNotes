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
    // Theme-adaptive colors, character formatting, horizontal rule.
    public partial class MainWindow
    {
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
                has ? [] : TextDecorations.Strikethrough);
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

    }
}
