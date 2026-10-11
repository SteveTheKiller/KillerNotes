using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;

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
    //
    // Setting and flushing are separate steps (#19). Setting hands the clipboard our data
    // object, and from that moment a paste works. Flushing renders every format into the
    // clipboard so the content outlives the app, but it has to reopen the clipboard right
    // after Windows clipboard history and other listeners have started reading the new
    // content, so it is the step that loses the race. Doing both in one
    // SetDataObject(data, true) call made a lost flush look like a failed copy: WPF retried
    // inside the call for about a second, the loop here retried that six times, cut left
    // the text in place, and "Clipboard is busy" appeared with the text already on the
    // clipboard. Now a successful set is a successful copy, and the flush is retried in the
    // background and once more when the app exits.
    public partial class MainWindow
    {
        [DllImport("ole32.dll")]
        private static extern int OleFlushClipboard();

        private IDataObject? _clipboardPendingFlush;
        private System.Windows.Threading.DispatcherTimer? _clipboardFlushTimer;
        private int _clipboardFlushTries;

        private void InitEditorClipboard()
        {
            // Instance bindings win over the RichTextBox's class-level editing commands,
            // so Ctrl+X/Ctrl+C route here. The context menu also handles image copies.
            Editor.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Cut, EditorCut_Executed, EditorCut_CanExecute));
            Editor.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Copy, EditorCopy_Executed, EditorCopy_CanExecute));
            // Last chance to make a copy outlive the app if every background flush lost.
            if (Application.Current != null)
                Application.Current.Exit += (_, _) => TryFlushClipboard();
        }

        /// <summary>Hands the data object to the clipboard, retrying briefly while another
        /// process holds it, then schedules the flush. Returns false only if the clipboard
        /// could not be set at all.</summary>
        private bool TryPutOnClipboard(IDataObject data)
        {
            // WPF already retries the set internally, so a short outer loop is enough.
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(data, false);
                    _clipboardPendingFlush = data;
                    _clipboardFlushTries = 0;
                    // Give the listeners that just woke up a moment to finish reading.
                    StartClipboardFlushTimer();
                    return true;
                }
                catch (ExternalException) { System.Threading.Thread.Sleep(30); }
            }
            return false;
        }

        private void StartClipboardFlushTimer()
        {
            if (_clipboardFlushTimer == null)
            {
                _clipboardFlushTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                { Interval = TimeSpan.FromMilliseconds(250) };
                _clipboardFlushTimer.Tick += (_, _) =>
                {
                    // About five seconds of attempts. If they all lose, the data object is
                    // still on the clipboard and pastes normally while the app runs.
                    if (TryFlushClipboard() || ++_clipboardFlushTries >= 20) _clipboardFlushTimer!.Stop();
                };
            }
            _clipboardFlushTimer.Stop();
            _clipboardFlushTimer.Start();
        }

        /// <summary>One flush attempt with no waiting. True when there is nothing left to
        /// flush: it succeeded, or something else has replaced our content since.</summary>
        private bool TryFlushClipboard()
        {
            var data = _clipboardPendingFlush;
            if (data == null) return true;
            try
            {
                if (!Clipboard.IsCurrent(data)) { _clipboardPendingFlush = null; return true; }
            }
            catch (ExternalException) { return false; }
            if (OleFlushClipboard() != 0) return false;
            _clipboardPendingFlush = null;
            return true;
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

        private void EditorCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxObject is Image { Source: BitmapSource bitmap })
            {
                var data = new DataObject();
                data.SetImage(bitmap);
                if (!TryPutOnClipboard(data)) FlashStatus(Loc("Str_St_ClipBusy"));
                return;
            }
            if (!Editor.Selection.IsEmpty && !TrySetClipboardFromSelection())
                FlashStatus(Loc("Str_St_ClipBusy"));
        }

        /// <summary>Serializes the selection to the formats WPF's native copy produces and
        /// puts them on the clipboard, retrying while another process holds it. Returns
        /// false only if the clipboard could not be set at all.</summary>
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
            byte[]? package = null;
            try
            {
                // Serialized once and reused below for the HTML copy; this is the costliest
                // format on a large selection. The DataObject gets its own stream over the
                // bytes, deliberately NOT disposed: it hands that stream to whoever pastes.
                // XamlPackage is the format that carries images, so a KillerNotes-to-KillerNotes
                // paste keeps them.
                using (var pkg = new MemoryStream())
                {
                    range.Save(pkg, DataFormats.XamlPackage);
                    package = pkg.ToArray();
                }
                data.SetData(DataFormats.XamlPackage, new MemoryStream(package, false));
            }
            catch { /* as above */ }
            try
            {
                // HTML is what browsers, mail clients and Office prefer, and unlike WPF's RTF
                // it keeps hyperlinks as real links instead of blue text. The selection is
                // rebuilt as its own document so partial paragraphs render cleanly.
                if (package == null) throw new InvalidOperationException();
                var doc = new FlowDocument();
                using var copy = new MemoryStream(package, false);
                new TextRange(doc.ContentStart, doc.ContentEnd).Load(copy, DataFormats.XamlPackage);
                string fragment = KillerNotes.Services.HtmlExport.FragmentFromDocument(doc);
                if (fragment.Length > 0)
                    data.SetData(DataFormats.Html, KillerNotes.Services.HtmlExport.ClipboardEnvelope(fragment));
            }
            catch { /* as above */ }

            // The flush that makes the content survive the app closing follows in the
            // background (TryPutOnClipboard).
            return TryPutOnClipboard(data);
        }
    }
}
