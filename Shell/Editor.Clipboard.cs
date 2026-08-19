using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace KillerNotes.Shell
{
    // Robust Cut and Copy for the editor (#16: "cut sometimes does a copy" on large notes).
    //
    // WPF's built-in cut is copy-then-delete, and the copy phase writes UnicodeText + RTF +
    // Xaml + XamlPackage to the clipboard in one synchronous call. When that call throws
    // ExternalException - the clipboard held open for a moment by any listener: Win+V
    // clipboard history, OneDrive, PowerToys, AutoHotkey - WPF abandons the WHOLE cut
    // BEFORE the delete, so the text stays put and whatever last reached the clipboard
    // makes it read as "it copied instead of cutting". A large selection serializes
    // slowly, widening that race window, which is why it bit intermittently and mostly on
    // big notes. Killing clipboard tools does not fix it; Windows' own clipboard history
    // is enough to lose the race.
    //
    // These bindings build the same clipboard formats, retry the write briefly, and only
    // delete the selection once the write has actually succeeded. If the clipboard never
    // frees, the status line says so instead of the command half-working in silence.
    public partial class MainWindow
    {
        private void InitEditorClipboard()
        {
            // Instance bindings win over the RichTextBox's class-level editing commands,
            // so both Ctrl+X/Ctrl+C and the context menu route here.
            Editor.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Cut, EditorCut_Executed, EditorCut_CanExecute));
            Editor.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Copy, EditorCopy_Executed, EditorCopy_CanExecute));
        }

        private void EditorCut_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !Editor.Selection.IsEmpty && !Editor.IsReadOnly;
            e.Handled = true;
        }

        private void EditorCopy_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !Editor.Selection.IsEmpty;
            e.Handled = true;
        }

        private void EditorCut_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            if (Editor.Selection.IsEmpty) return;
            if (!TrySetClipboardFromSelection()) { FlashStatus(Loc("Str_St_ClipBusy")); return; }
            // Only now is it safe to remove the text. A TextRange edit lands on the
            // RichTextBox's own undo stack, so Ctrl+Z still restores a cut.
            Editor.Selection.Text = string.Empty;
        }

        private void EditorCopy_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            if (Editor.Selection.IsEmpty) return;
            if (!TrySetClipboardFromSelection()) FlashStatus(Loc("Str_St_ClipBusy"));
        }

        /// <summary>Serializes the selection to the formats WPF's native copy produces and
        /// pushes them to the clipboard, retrying while another process holds it. Returns
        /// false only if the clipboard stayed locked through every attempt.</summary>
        private bool TrySetClipboardFromSelection()
        {
            var range = new TextRange(Editor.Selection.Start, Editor.Selection.End);
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, range.Text);
            try
            {
                using var rtf = new MemoryStream();
                range.Save(rtf, DataFormats.Rtf);
                data.SetData(DataFormats.Rtf, Encoding.UTF8.GetString(rtf.ToArray()));
            }
            catch { /* content with no RTF form - plain text still goes up */ }
            try
            {
                using var xaml = new MemoryStream();
                range.Save(xaml, DataFormats.Xaml);
                data.SetData(DataFormats.Xaml, Encoding.UTF8.GetString(xaml.ToArray()));
            }
            catch { /* as above */ }
            try
            {
                // Deliberately NOT disposed: the DataObject holds this stream and hands it
                // to whoever pastes. XamlPackage is the format that carries images, so a
                // KillerNotes-to-KillerNotes paste keeps them.
                var pkg = new MemoryStream();
                range.Save(pkg, DataFormats.XamlPackage);
                pkg.Position = 0;
                data.SetData(DataFormats.XamlPackage, pkg);
            }
            catch { /* as above */ }

            // copy:true so the content survives the app closing, same as the native copy.
            // Six tries over ~180ms; clipboard listeners hold the lock for single-digit
            // milliseconds, so one retry usually wins.
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetDataObject(data, true); return true; }
                catch (ExternalException) { System.Threading.Thread.Sleep(30); }
            }
            return false;
        }
    }
}
