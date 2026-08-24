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
    // Font size, editor zoom, full color picker, spell check, word wrap.
    public partial class MainWindow
    {
        // ---- Font size / editor zoom / full color picker / spell check (1.0.1, #1) ----

        private static readonly int[] FontSizes = [10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48];
        private double _editorZoom = 1.0;
        private bool _syncingFontSizeSlider;

        /// <summary>Called once from InitEditor: restores the remembered editor zoom and
        /// wires Ctrl+wheel. Zoom is a view setting (LayoutTransform), not note content.</summary>
        private void InitEditorView()
        {
            if (int.TryParse(App.GetSetting("EditorZoom"), out int pct) && pct >= 50 && pct <= 300 && pct != 100)
            {
                _editorZoom = pct / 100.0;
                Editor.LayoutTransform = new ScaleTransform(_editorZoom, _editorZoom);
            }
            Editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
            // Keep the size dropdown showing the size under the caret/selection.
            Editor.SelectionChanged += (_, _) => UpdateFontSizeDisplay();
            InitSelectionTextOverlay();   // 98SE white-on-navy selection (EditorSelectionText.cs)
            InitFindBar();                // in-note find match highlights (FindBar.cs)
        }

        private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            SetEditorZoom(_editorZoom + (e.Delta > 0 ? 0.1 : -0.1));
            e.Handled = true;
        }

        /// <summary>Clamps to 50-300%, applies, persists, announces. Ctrl+0 resets to 100.</summary>
        private void SetEditorZoom(double zoom)
        {
            zoom = Math.Round(Math.Max(0.5, Math.Min(3.0, zoom)), 2);
            _editorZoom = zoom;
            Editor.LayoutTransform = zoom == 1.0 ? Transform.Identity : new ScaleTransform(zoom, zoom);
            App.SetSetting("EditorZoom", ((int)Math.Round(zoom * 100)).ToString());
            FlashStatus(string.Format(Loc("Str_St_Zoom"), (int)Math.Round(zoom * 100)));
            RebuildLineNumbers();   // LineNumbers.cs (numbers track the editor zoom)
        }

        private void FontSizeBtn_Click(object sender, RoutedEventArgs e)
        {
            double size = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double d ? d : 13;
            _syncingFontSizeSlider = true;
            FontSizeSlider.Value = Math.Max(FontSizeSlider.Minimum, Math.Min(FontSizeSlider.Maximum, Math.Round(size)));
            FontSizeSliderValue.Text = Math.Round(FontSizeSlider.Value).ToString();
            _syncingFontSizeSlider = false;
            FontSizePopup.PlacementTarget = FontSizeBtn;
            FontSizePopup.IsOpen = !FontSizePopup.IsOpen;
            if (FontSizePopup.IsOpen) Anim.FadeIn(FontSizePopup);
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncingFontSizeSlider || FontSizeSliderValue == null) return;
            int size = (int)Math.Round(e.NewValue);
            FontSizeSliderValue.Text = size.ToString();
            if (FontSizePopup.IsOpen) ApplyFontSize(size);
        }

        // Hover the dropdown and scroll to step through the size ladder - no click needed.
        private void FontSizeBtn_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (_currentId < 0) return;
            double cur = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double d ? d : 13;
            int idx = ClosestSizeIndex(cur) + (e.Delta > 0 ? 1 : -1);
            idx = Math.Max(0, Math.Min(FontSizes.Length - 1, idx));
            ApplyFontSize(FontSizes[idx]);
        }

        private void ApplyFontSize(int size)
        {
            ApplyToSelection(TextElement.FontSizeProperty, (double)size);
            FontSizeText.Text = size.ToString();
        }

        /// <summary>"-" when the selection mixes sizes.</summary>
        private void UpdateFontSizeDisplay() =>
            FontSizeText.Text = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double d
                ? Math.Round(d).ToString() : "-";

        private static int ClosestSizeIndex(double size)
        {
            int best = 0;
            for (int i = 1; i < FontSizes.Length; i++)
                if (Math.Abs(FontSizes[i] - size) < Math.Abs(FontSizes[best] - size)) best = i;
            return best;
        }

        // "More..." in the color flyout: the full family picker (ColorPickerDialog).

        private void FgMore_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = false;
            var cur = (Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color
                      ?? (TryFindResource("TextBrush") as SolidColorBrush)?.Color ?? Colors.White;
            var dlg = new ColorPickerDialog(this, cur);
            // Confirmed, not ShowDialog() == true: the close fade nulls DialogResult
            // (ColorPickerDialog.Confirmed doc).
            dlg.ShowDialog();
            if (dlg.Confirmed)
                ApplyToSelection(TextElement.ForegroundProperty, new SolidColorBrush(dlg.SelectedColor));
        }

        private void BgMore_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = false;
            var cur = (Editor.Selection.GetPropertyValue(TextElement.BackgroundProperty) as SolidColorBrush)?.Color
                      ?? Color.FromRgb(0x7A, 0x6A, 0x00);
            var dlg = new ColorPickerDialog(this, cur);
            dlg.ShowDialog();
            if (dlg.Confirmed)
                ApplyToSelection(TextElement.BackgroundProperty, new SolidColorBrush(dlg.SelectedColor));
        }

        // ---- Spell check (per note, off by default; Windows spell checking APIs) ----

        // WPF's spell checker walks the WHOLE document on the UI thread. On a huge pasted
        // script it grinds for minutes before the first squiggle appears, which reads as
        // spell check not working at all AND the app being unusable at once
        // (2026-08-08) - and the setting persists per note, so that note re-hung the app on
        // every load. Above this many characters the toggle refuses and says so, and the
        // load path quietly stays off.
        private const int MaxSpellCheckChars = 50_000;

        private bool NoteTooBigForSpellCheck()
            => new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
                   .Text.Length > MaxSpellCheckChars;

        private void Spell_Click(object sender, RoutedEventArgs e)
        {
            if (_currentId < 0) return;
            bool on = !Editor.SpellCheck.IsEnabled;
            if (on && NoteTooBigForSpellCheck())
            {
                FlashStatus(Loc("Str_St_SpellTooBig"));
                return;
            }
            ApplySpellCheck(on);
            NoteStore.SetSpellCheck(_currentId, on);
            if (_notes.FirstOrDefault(n => n.Id == _currentId) is Note meta) meta.SpellCheck = on;
            FlashStatus(Loc(on ? "Str_St_SpellOn" : "Str_St_SpellOff"));
        }

        /// <summary>Applies the state to the editor and updates the note-menu checkmark.
        /// TextElement.Foreground is inherited, so setting it on the icon Grid colors
        /// both the "abc" and the check mark at once.</summary>
        private void ApplySpellCheck(bool on)
        {
            // The load path's rescue: a note saved with spell check on that has since grown
            // past the cap must not re-hang the app the moment it opens.
            if (on && NoteTooBigForSpellCheck()) on = false;
            try { Editor.SpellCheck.IsEnabled = on; }
            catch { on = false; }   // OS spell checking unavailable - stay off quietly
            SpellCheckMenuItem?.IsChecked = on;
        }

        // ---- Word wrap toggle (global view setting, remembered like zoom) ----
        // Wrap on (default): the document page width is auto, so text wraps to the editor
        // pane. Off: a wide fixed page width, so long lines and over-wide images/tables do
        // not wrap and the editor's horizontal scrollbar (MainWindow.xaml) can reach them.
        // The button lights in the accent while wrap is on. Editor.Document is reused across
        // note loads (Notes.cs OpenNote clears blocks, not the document), so the page width
        // persists; OpenNote re-asserts it after each load to be safe.
        private bool _wordWrap = true;
        private const double NoWrapPageWidth = 4000;

        private void InitWordWrap() => ApplyWordWrap(App.GetSetting("WordWrap") != "off");

        private void WordWrap_Click(object sender, RoutedEventArgs e)
        {
            ApplyWordWrap(!_wordWrap);
            App.SetSetting("WordWrap", _wordWrap ? "on" : "off");
            FlashStatus(Loc(_wordWrap ? "Str_St_WrapOn" : "Str_St_WrapOff"));
        }

        private void ApplyWordWrap(bool wrap)
        {
            _wordWrap = wrap;
            Editor.Document.PageWidth = wrap ? double.NaN : NoWrapPageWidth;
            WordWrapMenuItem?.IsChecked = wrap;
        }
    }
}
