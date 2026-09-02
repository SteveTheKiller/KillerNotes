// ═══════════════════════════════════════════════════════════
//  HEADINGS AND THE OUTLINE  -  section titles the app knows about
// ═══════════════════════════════════════════════════════════
//
// Alt+1, Alt+2 and Alt+3 make the current line (or every line in the selection) a heading of
// that level; Alt+0 puts it back to normal text; the format bar's H button steps through all
// four. In a rich note that means the bold-at-a-known-size shape Services/Headings.cs defines,
// so a heading is the same whether it was typed here or imported from markdown. In a markdown
// note it is the "# " marker.
//
// The outline pane (Alt+O, or the rail button) lists the headings of the open note beside it,
// indented by level; a click scrolls the editor to that heading and puts the caret on it. It
// is rebuilt when a note opens and, debounced, as the text changes, from one walk over the
// paragraphs - cheap enough to run on every pause in typing.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        // ---- Heading level ----

        /// <summary>Makes every selected paragraph a heading of `level` (1 to 3), or normal
        /// text for 0. A collapsed selection means the caret's line.</summary>
        private void SetHeadingLevel(int level)
        {
            if (_currentId < 0 || _currentInTrash) return;
            var paras = ParagraphsInSelection();   // Checklist.cs
            if (paras.Count == 0) return;
            level = Math.Max(0, Math.Min(Headings.MaxLevel, level));
            bool md = CurrentIsMarkdown;
            Editor.BeginChange();   // one undo step for the whole selection
            try
            {
                foreach (var p in paras)
                {
                    if (md) SetMarkdownHeading(p, level);
                    else SetRichHeading(p, level);
                }
            }
            finally { Editor.EndChange(); }
            MarkDirty();
            Editor.Focus();
            QueueOutlineRefresh();
            FlashStatus(level == 0 ? Loc("Str_St_HeadingOff") : string.Format(Loc("Str_St_Heading"), level));
        }

        /// <summary>The format bar button: normal -> h1 -> h2 -> h3 -> normal on the caret's line.</summary>
        private void Heading_Click(object sender, RoutedEventArgs e)
        {
            if (_currentId < 0 || _currentInTrash) return;
            int current = Editor.CaretPosition.Paragraph is Paragraph p ? HeadingLevelOf(p) : 0;
            SetHeadingLevel((current + 1) % (Headings.MaxLevel + 1));
        }

        private int HeadingLevelOf(Paragraph p) =>
            CurrentIsMarkdown ? Headings.LevelOfMarkdown(TextOf(p)) : Headings.LevelOf(p, Editor.FontSize);

        private void SetRichHeading(Paragraph p, int level)
        {
            var range = new TextRange(p.ContentStart, p.ContentEnd);
            if (level == 0)
            {
                // Back to inherited values rather than baking the base size into every run.
                foreach (var inline in p.Inlines.ToList()) ClearHeadingProps(inline);
                p.ClearValue(TextElement.FontSizeProperty);
                p.ClearValue(TextElement.FontWeightProperty);
                p.ClearValue(Block.MarginProperty);
                return;
            }
            double size = Headings.SizeFor(level, Editor.FontSize);
            // The runs carry it (what the XamlPackage stores), and the paragraph carries it too
            // so text typed at the end of the line, or into an empty heading line, inherits it.
            range.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            p.FontSize = size;
            p.FontWeight = FontWeights.Bold;
            p.Margin = new Thickness(0, level == 1 ? 2 : 8, 0, 4);   // the same breath MarkdownConvert gives
        }

        private static void ClearHeadingProps(Inline inline)
        {
            inline.ClearValue(TextElement.FontSizeProperty);
            inline.ClearValue(TextElement.FontWeightProperty);
            if (inline is Span span)
                foreach (var i in span.Inlines.ToList()) ClearHeadingProps(i);
        }

        private void SetMarkdownHeading(Paragraph p, int level)
        {
            string line = TextOf(p);
            string next = Headings.SetMarkdownLevel(line, level);
            if (next == line) return;
            new TextRange(p.ContentStart, p.ContentEnd).Text = next;
            Editor.CaretPosition = p.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        }

        // ---- Outline pane ----

        private bool _outlineOpen;
        private const string OutlineSetting = "OutlineOpen";
        private readonly DispatcherTimer _outlineTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

        private void InitOutline()
        {
            _outlineTimer.Tick += (_, _) => { _outlineTimer.Stop(); RefreshOutline(); };
            _outlineOpen = App.GetSetting(OutlineSetting) == "1";
            ApplyOutlineState();
        }

        private void Outline_Click(object sender, RoutedEventArgs e) => ToggleOutline();

        private void ToggleOutline()
        {
            _outlineOpen = !_outlineOpen;
            App.SetSetting(OutlineSetting, _outlineOpen ? "1" : "0");
            ApplyOutlineState();
            if (_outlineOpen) RefreshOutline();
        }

        private void ApplyOutlineState()
        {
            OutlinePane.Visibility = _outlineOpen ? Visibility.Visible : Visibility.Collapsed;
            OutlineCol.Width = new GridLength(_outlineOpen ? 220 : 0);
            OutlineRailBtn.SetResourceReference(ForegroundProperty, _outlineOpen ? "PrimaryBrush" : "TextBrush");
        }

        /// <summary>Rebuilds the outline shortly after the text stops changing.</summary>
        private void QueueOutlineRefresh()
        {
            if (!_outlineOpen) return;
            _outlineTimer.Stop();
            _outlineTimer.Start();
        }

        private void RefreshOutline()
        {
            if (!_outlineOpen) return;
            OutlineList.Children.Clear();
            if (_currentId < 0) { OutlineEmpty.Visibility = Visibility.Collapsed; return; }

            bool md = CurrentIsMarkdown;
            double base_ = Editor.FontSize;
            int count = 0;
            foreach (var p in AllParagraphs(Editor.Document.Blocks))   // Checklist.cs
            {
                string text = TextOf(p);
                int level = md ? Headings.LevelOfMarkdown(text) : Headings.LevelOf(p, base_);
                if (level == 0) continue;
                string label = (md ? text.Substring(level + 1) : text).Trim();
                if (label.Length == 0) continue;

                var tb = new TextBlock
                {
                    Text = label,
                    ToolTip = label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = level == 1 ? 12.5 : 11.5,
                    FontWeight = level == 1 ? FontWeights.SemiBold : FontWeights.Normal,
                    Margin = new Thickness(10 + (level - 1) * 12, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                tb.SetResourceReference(TextBlock.FontFamilyProperty, "SidebarFont");
                var row = new Border
                {
                    Child = tb,
                    Padding = new Thickness(0, 4, 0, 4),
                    Cursor = Cursors.Hand,
                    Style = (Style)OutlinePane.FindResource("OutlineRow"),
                };
                var target = p;
                row.MouseLeftButtonUp += (_, _) => JumpToParagraph(target);
                OutlineList.Children.Add(row);
                count++;
            }
            OutlineEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Scrolls the editor so the heading sits at the top and puts the caret on it.</summary>
        private void JumpToParagraph(Paragraph p)
        {
            try
            {
                var rect = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Editor.ScrollToVerticalOffset(Editor.VerticalOffset + rect.Top - 8);
                Editor.CaretPosition = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                Editor.Focus();
            }
            catch (InvalidOperationException)
            {
                RefreshOutline();   // the paragraph is gone from the document; the list was stale
            }
        }
    }
}
