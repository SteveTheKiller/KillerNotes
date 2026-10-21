using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KillerNotes.Controls
{
    // The quick-note surface (see QuickNoteWindow.xaml). It holds no notepad logic: the shell
    // decides where a note goes and whether it can be saved (Shell/QuickNote.cs), and gets the
    // text back through SaveRequested. Ctrl+Enter saves, Esc closes and keeps the draft.
    public partial class QuickNoteWindow : Window
    {
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        /// <summary>Raised by Save and Ctrl+Enter.</summary>
        public event Action? SaveRequested;

        /// <summary>Raised by the banner's Unlock button.</summary>
        public event Action? UnlockRequested;

        public string Text
        {
            get => NoteBox.Text;
            set => NoteBox.Text = value;
        }

        public int TargetIndex => TargetBox.SelectedIndex;

        public QuickNoteWindow()
        {
            InitializeComponent();
            // Ctrl+Enter on the preview pass: the multi-line box would otherwise take it as a
            // line break before the window ever saw it.
            DialogShortcuts.Describe(SaveBtn, "Ctrl+Enter");
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control || !SaveBtn.IsEnabled) return;
                e.Handled = true;
                SaveRequested?.Invoke();
            };
            DialogShortcuts.Button(this, PasteBtn, "Alt+P", Key.P, ModifierKeys.Alt);
            DialogShortcuts.Bind(this, CloseBtn, "Esc", Key.Escape, ModifierKeys.None, Close);
            DialogShortcuts.Bind(this, TargetBox, "Alt+T", Key.T, ModifierKeys.Alt, () => { TargetBox.Focus(); TargetBox.IsDropDownOpen = true; });
            DialogShortcuts.Describe(TitleBar, "Esc");
            Loaded += (_, _) =>
            {
                Anim.FadeIn(RootBorder);
                FocusText();
            };
        }

        public void FocusText()
        {
            NoteBox.Focus();
            NoteBox.CaretIndex = NoteBox.Text.Length;
        }

        public void SetTargets(IReadOnlyList<string> targets, int selected)
        {
            TargetBox.Items.Clear();
            foreach (string target in targets) TargetBox.Items.Add(new ComboBoxItem { Content = target });
            TargetBox.SelectedIndex = Math.Max(0, Math.Min(selected, targets.Count - 1));
        }

        /// <summary>Shows why saving is not possible right now, or hides the banner for null. The
        /// text box stays editable either way.</summary>
        public void SetBlocked(string? reason, bool canUnlock)
        {
            Banner.Visibility = reason == null ? Visibility.Collapsed : Visibility.Visible;
            BannerText.Text = reason ?? "";
            UnlockBtn.Visibility = canUnlock ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke();

        private void Unlock_Click(object sender, RoutedEventArgs e) => UnlockRequested?.Invoke();

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                NoteBox.SelectedText = Clipboard.GetText();
                NoteBox.CaretIndex = NoteBox.SelectionStart + NoteBox.SelectionLength;
                NoteBox.SelectionLength = 0;
                FocusText();
            }
            catch (System.Runtime.InteropServices.ExternalException) { /* another app holds the clipboard */ }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
