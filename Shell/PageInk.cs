using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // ============================================================
    // PAGE INK (About card, Trash Polka only)
    //
    // How strongly the ink shows on the note (TrashPolkaInk.PageOpacity, ThemeMotif.Page) and on
    // its rendered view (TrashPolkaInk.PreviewOpacity, ThemeMotif.Preview), one slider each. Steve
    // asked for sliders to find the best numbers by eye (2026-11-21). Stored as whole percents,
    // 0 to 60. The preview had no setting of its own at first, so a missing one starts at the
    // note's saved value.
    // ============================================================
    public partial class MainWindow
    {
        internal const string PageInkSetting = "TrashPolkaPageInk";
        internal const string PreviewInkSetting = "TrashPolkaPreviewInk";
        internal const int PageInkMax = 60;

        private bool _initializingPageInk;
        private DispatcherTimer? _pageInkRepaint;

        /// <summary>The saved values, applied before the first paint. A missing or unreadable one keeps the default.</summary>
        internal static void LoadPageInk()
        {
            if (TryReadInk(PageInkSetting, out int page))
                TrashPolkaInk.PageOpacity = page / 100.0;
            if (TryReadInk(PreviewInkSetting, out int preview))
                TrashPolkaInk.PreviewOpacity = preview / 100.0;
            else if (TryReadInk(PageInkSetting, out page))
                TrashPolkaInk.PreviewOpacity = page / 100.0;
        }

        private static bool TryReadInk(string setting, out int percent) =>
            int.TryParse(App.GetSetting(setting), NumberStyles.Integer, CultureInfo.InvariantCulture, out percent)
            && percent >= 0 && percent <= PageInkMax;

        private void InitPageInk()
        {
            _initializingPageInk = true;
            AboutPageInkSlider.Maximum = PageInkMax;
            AboutPageInkSlider.Value = Math.Round(TrashPolkaInk.PageOpacity * 100);
            AboutPreviewInkSlider.Maximum = PageInkMax;
            AboutPreviewInkSlider.Value = Math.Round(TrashPolkaInk.PreviewOpacity * 100);
            _initializingPageInk = false;
            ShowInkValue(AboutPageInkValue, (int)AboutPageInkSlider.Value);
            ShowInkValue(AboutPreviewInkValue, (int)AboutPreviewInkSlider.Value);
            UpdatePageInkRow();
            ThemeManager.ThemeChanged += UpdatePageInkRow;
            Closed += (_, _) => ThemeManager.ThemeChanged -= UpdatePageInkRow;
        }

        /// <summary>Only Trash Polka has ink, so the row shows only there.</summary>
        private void UpdatePageInkRow() =>
            AboutPageInkRow.Visibility = ThemeManager.Current == Theme.TrashPolka ? Visibility.Visible : Visibility.Collapsed;

        private static void ShowInkValue(TextBlock value, int percent) =>
            value.Text = string.Format(CultureInfo.CurrentCulture, "{0}%", percent);

        private void AboutPageInk_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializingPageInk || AboutPageInkValue == null) return;
            int percent = (int)Math.Round(e.NewValue);
            ShowInkValue(AboutPageInkValue, percent);
            TrashPolkaInk.PageOpacity = percent / 100.0;
            App.SetSetting(PageInkSetting, percent.ToString(CultureInfo.InvariantCulture));
            QueueInkRepaint();
        }

        private void AboutPreviewInk_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializingPageInk || AboutPreviewInkValue == null) return;
            int percent = (int)Math.Round(e.NewValue);
            ShowInkValue(AboutPreviewInkValue, percent);
            TrashPolkaInk.PreviewOpacity = percent / 100.0;
            App.SetSetting(PreviewInkSetting, percent.ToString(CultureInfo.InvariantCulture));
            QueueInkRepaint();
        }

        /// <summary>Every pane recomposes on a repaint, so a drag repaints once it pauses rather than on every step.</summary>
        private void QueueInkRepaint()
        {
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
