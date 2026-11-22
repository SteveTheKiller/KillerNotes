using System;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // ============================================================
    // PAGE INK (About card, Trash Polka only)
    //
    // How strongly the ink shows on the note and on its rendered view, one value for both
    // (TrashPolkaInk.PageOpacity, ThemeMotif.Page). Steve asked for a slider to find the best
    // number by eye (2026-11-21). Stored as a whole percent, 0 to 60.
    // ============================================================
    public partial class MainWindow
    {
        internal const string PageInkSetting = "TrashPolkaPageInk";
        internal const int PageInkMax = 60;

        private bool _initializingPageInk;
        private DispatcherTimer? _pageInkRepaint;

        /// <summary>The saved value, applied before the first paint. A missing or unreadable one keeps the default.</summary>
        internal static void LoadPageInk()
        {
            if (int.TryParse(App.GetSetting(PageInkSetting), NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent)
                && percent >= 0 && percent <= PageInkMax)
                TrashPolkaInk.PageOpacity = percent / 100.0;
        }

        private void InitPageInk()
        {
            _initializingPageInk = true;
            AboutPageInkSlider.Maximum = PageInkMax;
            AboutPageInkSlider.Value = Math.Round(TrashPolkaInk.PageOpacity * 100);
            _initializingPageInk = false;
            ShowPageInkValue((int)AboutPageInkSlider.Value);
            UpdatePageInkRow();
            ThemeManager.ThemeChanged += UpdatePageInkRow;
            Closed += (_, _) => ThemeManager.ThemeChanged -= UpdatePageInkRow;
        }

        /// <summary>Only Trash Polka has ink, so the row shows only there.</summary>
        private void UpdatePageInkRow() =>
            AboutPageInkRow.Visibility = ThemeManager.Current == Theme.TrashPolka ? Visibility.Visible : Visibility.Collapsed;

        private void ShowPageInkValue(int percent) =>
            AboutPageInkValue.Text = string.Format(CultureInfo.CurrentCulture, "{0}%", percent);

        private void AboutPageInk_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializingPageInk || AboutPageInkValue == null) return;
            int percent = (int)Math.Round(e.NewValue);
            ShowPageInkValue(percent);
            TrashPolkaInk.PageOpacity = percent / 100.0;
            App.SetSetting(PageInkSetting, percent.ToString(CultureInfo.InvariantCulture));
            // Every pane recomposes on a repaint, so a drag repaints once it pauses rather than on every step.
            if (_pageInkRepaint == null)
            {
                _pageInkRepaint = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
                _pageInkRepaint.Tick += (_, _) => { _pageInkRepaint.Stop(); ThemeMotif.Retune(); };
            }
            _pageInkRepaint.Stop();
            _pageInkRepaint.Start();
        }
    }
}
