using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerNotes.Controls
{
    internal sealed class TableStylePreviewAdorner : Adorner
    {
        private readonly RichTextBox _view;
        internal TableStylePreviewAdorner(RichTextBox editor, FlowDocument document) : base(editor)
        {
            IsHitTestVisible = false;
            _view = new RichTextBox(document) {
                IsReadOnly = true, IsUndoEnabled = false, Focusable = false,
                FontFamily = editor.FontFamily, FontSize = editor.FontSize,
                Foreground = editor.Foreground, Background = editor.Background,
                BorderThickness = editor.BorderThickness, Padding = editor.Padding,
                Style = editor.Style, Template = editor.Template,
                VerticalScrollBarVisibility = editor.VerticalScrollBarVisibility,
                HorizontalScrollBarVisibility = editor.HorizontalScrollBarVisibility,
                FlowDirection = editor.FlowDirection, Language = editor.Language };
            // An adorner sits outside the window's inherited text settings, so without these the
            // copy fell back to Ideal text and spaced its lines and paragraphs differently from the
            // editor's Display text: the note visibly shifted while Table Style was open.
            TextOptions.SetTextFormattingMode(_view, TextOptions.GetTextFormattingMode(editor));
            TextOptions.SetTextRenderingMode(_view, TextOptions.GetTextRenderingMode(editor));
            TextOptions.SetTextHintingMode(_view, TextOptions.GetTextHintingMode(editor));
            foreach (DictionaryEntry entry in editor.Resources) _view.Resources[entry.Key] = entry.Value;
            AddVisualChild(_view);
            AddLogicalChild(_view);
            Loaded += (_, _) => { _view.ScrollToHorizontalOffset(editor.HorizontalOffset); _view.ScrollToVerticalOffset(editor.VerticalOffset); };
        }
        internal void Reveal(Table table)
        {
            _view.UpdateLayout();
            if (table.RowGroups.Count > 0 && table.RowGroups[0].Rows.Count > 0 && table.RowGroups[0].Rows[0].Cells.Count > 0)
                table.RowGroups[0].Rows[0].Cells[0].BringIntoView();
        }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _view : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size constraint) { _view.Measure(AdornedElement.RenderSize); return AdornedElement.RenderSize; }
        protected override Size ArrangeOverride(Size finalSize) { _view.Arrange(new Rect(finalSize)); return finalSize; }
    }
}
