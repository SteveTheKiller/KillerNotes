using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerNotes.Services
{
    internal enum TableStyleCommitResult { Unchanged, Applied, TargetChanged }

    internal enum TableSizingMode { Keep, FitPage, FitContent, ColumnWidth }

    internal sealed class TableSizing
    {
        internal TableSizingMode Mode { get; set; }
        internal int Column { get; set; }
        internal double Width { get; set; }
    }

    internal sealed class TableStyleSession : IDisposable
    {
        private readonly RichTextBox _editor;
        private readonly Table _table;
        private readonly Table _previewTable;
        private bool _resizeColumns;
        private readonly List<(DependencyObject Target, DependencyProperty Property, bool Local, object? Value)> _baseline = new();
        internal FlowDocument PreviewDocument { get; }
        internal Table PreviewTable => _previewTable;

        internal TableStyleSession(RichTextBox editor, Table table, byte[]? previewPackage = null)
        {
            _editor = editor;
            _table = table;
            FlowDocument source = editor.Document;
            int index = Tables(source).ToList().IndexOf(table);
            using var stream = new MemoryStream();
            if (previewPackage == null)
                new TextRange(source.ContentStart, source.ContentEnd).Save(stream, DataFormats.XamlPackage);
            else stream.Write(previewPackage, 0, previewPackage.Length);
            PreviewDocument = new FlowDocument { FontFamily = source.FontFamily, FontSize = source.FontSize,
                FontWeight = source.FontWeight, Foreground = source.Foreground, Background = source.Background,
                PagePadding = source.PagePadding, PageWidth = source.PageWidth, ColumnWidth = source.ColumnWidth };
            stream.Position = 0;
            new TextRange(PreviewDocument.ContentStart, PreviewDocument.ContentEnd).Load(stream, DataFormats.XamlPackage);
            foreach (string key in new[] { "TextBrush", "TableHeaderBrush" })
                if (table.TryFindResource(key) is Brush brush) PreviewDocument.Resources[key] = brush.CloneCurrentValue();
            _previewTable = Tables(PreviewDocument).ElementAt(index);
            for (TextPointer? at = _previewTable.ContentStart; at != null && at.CompareTo(_previewTable.ContentEnd) < 0;
                 at = at.GetNextContextPosition(LogicalDirection.Forward))
                if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                    at.GetAdjacentElement(LogicalDirection.Forward) is TextElement element)
                    Capture(element, TextElement.FontWeightProperty);
            Capture(_previewTable, Table.BorderBrushProperty, Table.BorderThicknessProperty);
            foreach (TableColumn column in _previewTable.Columns) Capture(column, TableColumn.WidthProperty);
            foreach (TableRow row in _previewTable.RowGroups.SelectMany(g => g.Rows))
            {
                Capture(row, TextElement.FontWeightProperty, TextElement.BackgroundProperty);
                foreach (TableCell cell in row.Cells)
                    Capture(cell, TableCell.BorderBrushProperty, TableCell.BorderThicknessProperty,
                        TableCell.PaddingProperty, TextElement.FontWeightProperty, TextElement.BackgroundProperty);
            }
        }

        internal void Preview(TableAppearance appearance, TableSizing? sizing = null)
        {
            Cancel();
            TableStyle.ApplyAppearance(_previewTable, appearance);
            _resizeColumns = sizing != null && sizing.Mode != TableSizingMode.Keep;
            if (sizing?.Mode == TableSizingMode.ColumnWidth)
            {
                double left = PreviewDocument.PagePadding.Left, right = PreviewDocument.PagePadding.Right;
                double available = _editor.ViewportWidth - (double.IsNaN(left) ? 0 : left) - (double.IsNaN(right) ? 0 : right);
                TableStyle.SetColumnWidth(_previewTable, sizing.Column, sizing.Width, available);
            }
            else if (sizing?.Mode == TableSizingMode.FitPage) TableStyle.FitToPage(_previewTable);
            else if (sizing?.Mode == TableSizingMode.FitContent)
                TableStyle.FitToContent(_previewTable, _editor.FontSize, _editor.FontFamily,
                    VisualTreeHelper.GetDpi(_editor).PixelsPerDip);
        }

        private static IEnumerable<Table> Tables(FlowDocument document)
        {
            for (TextPointer? at = document.ContentStart; at != null && at.CompareTo(document.ContentEnd) < 0;
                 at = at.GetNextContextPosition(LogicalDirection.Forward))
                if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                    at.GetAdjacentElement(LogicalDirection.Forward) is Table table) yield return table;
        }

        private void Capture(DependencyObject target, params DependencyProperty[] properties)
        {
            foreach (DependencyProperty property in properties)
            {
                object? value = target.GetValue(property);
                _baseline.Add((target, property, target.ReadLocalValue(property) != DependencyProperty.UnsetValue,
                    value is Freezable freezable ? freezable.CloneCurrentValue() : value));
            }
        }

        internal void Cancel()
        {
            foreach (var (target, property, local, value) in _baseline)
                if (local) target.SetValue(property, value);
                else target.ClearValue(property);
        }

        internal TableStyleCommitResult Commit()
        {
            if (!Tables(_editor.Document).Contains(_table) ||
                (_resizeColumns && _table.Columns.Count != _previewTable.Columns.Count)) return TableStyleCommitResult.TargetChanged;
            TableAppearance appearance = TableStyle.ReadAppearance(_previewTable);
            if (TableStyle.ReadAppearance(_table).ToSetting() == appearance.ToSetting() &&
                HeaderTextMatches(_table, appearance) &&
                (!_resizeColumns || _table.Columns.Select(c => c.Width).SequenceEqual(_previewTable.Columns.Select(c => c.Width))))
                return TableStyleCommitResult.Unchanged;
            _editor.BeginChange();
            try
            {
                TableStyle.ApplyAppearance(_table, appearance);
                if (_resizeColumns)
                {
                    BlockCollection blocks = _table.Parent switch
                    {
                        FlowDocument document => document.Blocks,
                        Section section => section.Blocks,
                        ListItem item => item.Blocks,
                        TableCell cell => cell.Blocks,
                        Figure figure => figure.Blocks,
                        Floater floater => floater.Blocks,
                        _ => throw new InvalidOperationException("The table is no longer in the note.")
                    };
                    Block? next = _table.NextBlock;
                    blocks.Remove(_table);
                    try
                    {
                        for (int i = 0; i < _table.Columns.Count; i++) _table.Columns[i].Width = _previewTable.Columns[i].Width;
                    }
                    finally
                    {
                        if (next != null) blocks.InsertBefore(next, _table);
                        else blocks.Add(_table);
                    }
                }
            }
            finally { _editor.EndChange(); }
            return TableStyleCommitResult.Applied;
        }

        private static bool HeaderTextMatches(Table table, TableAppearance appearance)
        {
            var rows = table.RowGroups.SelectMany(g => g.Rows).ToArray();
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Cells.Count; c++)
                    if ((appearance.HeaderRow && r == 0) || (appearance.HeaderColumn && c == 0))
                    {
                        TableCell cell = rows[r].Cells[c];
                        if (!Equals(new TextRange(cell.ContentStart, cell.ContentEnd)
                            .GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold)) return false;
                    }
            return true;
        }

        public void Dispose() => Cancel();
    }
}
