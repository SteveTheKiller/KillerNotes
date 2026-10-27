using System.Windows;
using System.Windows.Input;

namespace KillerNotes.Controls
{
    // APP-OWNED: KillerNotes' side of the family confirm dialog (ConfirmDialog.xaml.cs).
    public partial class ConfirmDialog
    {
        // Cancel the first close, fade out, then close for real (Anim.FadeOutAndClose). A
        // DialogResult set before this survives the cancel and is delivered by the real close.
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        // KillerNotes' dialog keys: Enter answers OK, Esc cancels (also when Cancel is hidden),
        // and Alt+C toggles the first checkbox. Each key shows in its control's tooltip.
        partial void OnDialogSourceInitialized()
        {
            DialogShortcuts.Bind(this, OkButton, "Enter", Key.Enter, ModifierKeys.None,
                () => OK_Click(this, new RoutedEventArgs()), () => !CancelButton.IsDefault);
            DialogShortcuts.Describe(CancelButton, "Esc");
            KeyDown += (_, e) =>
            {
                if (e.Handled || e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return;
                e.Handled = true;
                Cancel_Click(this, new RoutedEventArgs());
            };
            DialogShortcuts.Bind(this, Check1, "Alt+C", Key.C, ModifierKeys.Alt,
                () => Check1.IsChecked = Check1.IsChecked != true);
        }
    }
}
