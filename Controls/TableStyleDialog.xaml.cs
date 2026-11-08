using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    public partial class TableStyleDialog : Window
    {
        private bool _ready;
        private bool _closeFaded;
        private readonly double[] _widths;
        internal bool Confirmed { get; private set; }
        internal bool MakeDefault { get; private set; }
        internal TableAppearance Appearance { get; private set; } = new();
        internal TableSizing Sizing { get; private set; } = new();
        internal event Action<TableAppearance, TableSizing>? PreviewChanged;

        internal TableStyleDialog(Table table, int column, TableSizingMode initialSizing = TableSizingMode.Keep)
        {
            InitializeComponent();
            Appearance = TableStyle.ReadAppearance(table);
            LayoutBox.SelectedIndex = Appearance.Lines switch { TableLines.Rows => 1, TableLines.Outline => 2, TableLines.None => 3, _ => 0 };
            PatternBox.SelectedIndex = (int)Appearance.Pattern;
            SpacingBox.SelectedIndex = (int)Appearance.Spacing;
            ThicknessBox.Text = Appearance.Thickness.ToString(CultureInfo.CurrentCulture);
            HeaderRowBox.IsChecked = Appearance.HeaderRow;
            HeaderColumnBox.IsChecked = Appearance.HeaderColumn;
            _widths = table.Columns.Select(c => c.Width.IsAbsolute ? c.Width.Value : 120).ToArray();
            for (int i = 0; i < _widths.Length; i++) ColumnBox.Items.Add((i + 1).ToString(CultureInfo.CurrentCulture));
            ColumnBox.SelectedIndex = Math.Max(0, Math.Min(column, _widths.Length - 1));
            WidthBox.Text = (_widths.Length > 0 ? _widths[ColumnBox.SelectedIndex] : 120).ToString(CultureInfo.CurrentCulture);
            SizingBox.IsEnabled = _widths.Length > 0;
            SizingBox.SelectedIndex = _widths.Length > 0 ? (int)initialSizing : 0;
            _ready = true;
            ReadChoices();
            DialogShortcuts.Button(this, CancelButton, "Esc", Key.Escape);
            Loaded += (_, _) => { Anim.FadeIn(RootBorder); LayoutBox.Focus(); if (initialSizing != TableSizingMode.Keep) Changed(this, new RoutedEventArgs()); };
        }

        private bool ReadChoices()
        {
            WidthFields.Visibility = SizingBox.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            string? error = null;
            double.TryParse(ThicknessBox.Text, out double thickness);
            double.TryParse(WidthBox.Text, out double width);
            if (double.IsNaN(thickness) || double.IsInfinity(thickness) || thickness <= 0 || thickness > 8)
                error = "Str_TableStyle_InvalidThickness";
            else if (SizingBox.SelectedIndex == 3 && (ColumnBox.SelectedIndex < 0 || width < 24 || !TableStyle.IsValidColumnWidth(width)))
                error = "Str_TableStyle_InvalidWidth";
            ValidationText.Text = error == null ? "" : (string)FindResource(error);
            OkButton.IsEnabled = error == null;
            if (error != null) return false;
            Appearance = new TableAppearance {
                Lines = LayoutBox.SelectedIndex switch { 1 => TableLines.Rows, 2 => TableLines.Outline, 3 => TableLines.None, _ => TableLines.Grid },
                Pattern = (TableLinePattern)PatternBox.SelectedIndex, Thickness = thickness,
                Spacing = (TableSpacing)SpacingBox.SelectedIndex, HeaderRow = HeaderRowBox.IsChecked == true,
                HeaderColumn = HeaderColumnBox.IsChecked == true };
            Sizing = new TableSizing { Mode = (TableSizingMode)SizingBox.SelectedIndex, Column = ColumnBox.SelectedIndex, Width = width };
            return true;
        }

        private void Changed(object sender, RoutedEventArgs e)
        {
            if (_ready && ReadChoices()) PreviewChanged?.Invoke(Appearance, Sizing);
        }

        private void ColumnChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || ColumnBox.SelectedIndex < 0) return;
            WidthBox.Text = _widths[ColumnBox.SelectedIndex].ToString(CultureInfo.CurrentCulture);
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!ReadChoices()) return;
            PreviewChanged?.Invoke(Appearance, Sizing);
            MakeDefault = DefaultBox.IsChecked == true;
            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; Close(); }

        private void Dialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Cancel_Click(sender, e); e.Handled = true; }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Fields.IsEnabled = false;
            OkButton.IsEnabled = CancelButton.IsEnabled = false;
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }
    }
}
