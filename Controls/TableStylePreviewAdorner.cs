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
                FlowDirection = editor.FlowDirection };
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
