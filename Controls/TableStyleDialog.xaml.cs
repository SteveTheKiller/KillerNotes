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
            // A short list of line weights. A table drawn at another weight keeps it as an extra
            // choice, so opening the dialog never changes a table the user only looks at.
            foreach (double weight in new[] { 1d, 2d, 3d }.Concat(new[] { Appearance.Thickness }).Distinct().OrderBy(w => w))
            {
                var item = new ComboBoxItem { Content = weight.ToString(CultureInfo.CurrentCulture), Tag = weight };
                ThicknessBox.Items.Add(item);
                if (weight == Appearance.Thickness) ThicknessBox.SelectedItem = item;
            }
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
            double thickness = ThicknessBox.SelectedItem is ComboBoxItem { Tag: double weight } ? weight : 1;
            double.TryParse(WidthBox.Text, out double width);
            if (SizingBox.SelectedIndex == 3 && (ColumnBox.SelectedIndex < 0 || width < 24 || !TableStyle.IsValidColumnWidth(width)))
                error = "Str_TableStyle_InvalidWidth";
            string message = error == null ? "" : (string)FindResource(error);
            bool announce = message.Length > 0 && message != ValidationText.Text;
            ValidationText.Text = message;
            ValidationText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = error == null;
            // LiveSetting alone announces nothing on .NET Framework; the change has to be raised.
            if (announce)
                (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(ValidationText)
                 ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(ValidationText))
                    ?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
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
            // The same width text raises no TextChanged, and the preview still has to move to
            // the newly chosen column.
            Changed(sender, e);
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

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Fields.IsEnabled = false;
            OkButton.IsEnabled = CancelButton.IsEnabled = false;
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }
    }
}
