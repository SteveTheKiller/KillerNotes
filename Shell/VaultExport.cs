// ═══════════════════════════════════════════════════════════
//  VAULT EXPORT  -  the window's half of Services/VaultExporter.cs
// ═══════════════════════════════════════════════════════════
//
// Picks the folder, says what is about to happen (including that a password notepad's export is
// plain text), and runs the exporter on its own STA thread with its own read-only connection, so
// the app stays usable. The footer shows progress and a Cancel button; a canceled export removes
// its folder. Lock now cancels a running export, and closing the app cancels it and waits for the
// cleanup, so an interrupted export is never left looking finished.

using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        // The one background job at a time - a vault export, or a folder import (FolderImport.cs) -
        // and what the status line says while it stops. Both share the footer's Cancel button.
        private CancellationTokenSource? _jobCancel;
        private string _jobCancelingKey = "Str_St_VaultCanceling";

        private void ExportVault_Click(object sender, RoutedEventArgs e)
        {
            if (_jobCancel != null) { StatusText.Text = Loc("Str_St_JobBusy"); return; }
            if (!NoteStore.IsOpen) return;

            // Export what is on screen, not a stale copy - same rule as ExportNoteAs.
            if (!SaveCurrentNote(refreshList: false)) return;

            var notes = NoteStore.List();
            if (notes.Count == 0)
            {
                StatusText.Text = Loc("Str_St_VaultEmpty");
                return;
            }

            string? folder = FolderPicker.Show(this, null, Loc("Str_Dlg_VaultPickFolder"));
            if (string.IsNullOrWhiteSpace(folder)) return;

            // A new dated folder under the one picked, never loose files in it and never mixed
            // into an earlier export.
            string name = Path.GetFileNameWithoutExtension(NoteStore.ActiveDbFile);
            string root = VaultExporter.NewExportFolder(folder!, string.IsNullOrWhiteSpace(name) ? "KillerNotes" : name, DateTime.Now);
            string body = string.Format(Loc("Str_Dlg_VaultBody"), notes.Count, root);
            if (NoteStore.HasPassword) body += "\n\n" + Loc("Str_Dlg_VaultPlain");
            var confirm = new ConfirmDialog(Loc("Str_Dlg_VaultHead"), body, Loc("Str_Btn_Export")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            NoteStore.VaultSource source;
            try { source = NoteStore.OpenVaultSource(); }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Loc("Str_St_VaultFailed"), ex.Message);
                return;
            }
            var text = VaultExportText.From(Loc);
            var cancel = new CancellationTokenSource();
            _jobCancel = cancel;
            _jobCancelingKey = "Str_St_VaultCanceling";
            JobCancelBtn.Visibility = Visibility.Visible;
            _statusTimer.Stop();
            StatusText.Text = string.Format(Loc("Str_St_VaultProgress"), 0, notes.Count);
            // Created here, so its reports arrive on the UI thread.
            var progress = new Progress<int>(done =>
            {
                if (_jobCancel == cancel && !cancel.IsCancellationRequested)
                    StatusText.Text = string.Format(Loc("Str_St_VaultProgress"), done, notes.Count);
            });

            var thread = new Thread(() =>
            {
                VaultExportResult? result = null;
                Exception? error = null;
                try { result = VaultExporter.Export(root, notes, source, text, progress, cancel.Token); }
                catch (Exception ex) { error = ex; }
                finally { source.Dispose(); }
                Dispatcher.BeginInvoke(new Action(() => FinishVaultExport(cancel, root, result, error)));
                Dispatcher.CurrentDispatcher.InvokeShutdown();   // this thread's own, from the FlowDocuments
            })
            {
                // Foreground: closing the app cancels the export and waits for it to remove its folder.
                IsBackground = false,
                Name = "KillerNotes vault export",
            };
            thread.SetApartmentState(ApartmentState.STA);   // FlowDocument needs one
            thread.Start();
        }

        private void FinishVaultExport(CancellationTokenSource cancel, string root, VaultExportResult? result, Exception? error)
        {
            if (_jobCancel == cancel) _jobCancel = null;
            cancel.Dispose();
            JobCancelBtn.Visibility = Visibility.Collapsed;
            StatusText.Text =
                error != null ? string.Format(Loc("Str_St_VaultFailed"), error.Message)
                : result == null || result.Canceled ? Loc("Str_St_VaultCanceled")
                : result.Failed > 0 ? string.Format(Loc("Str_St_VaultDonePartial"), result.Written, result.Failed, root)
                : result.Problems.Count > 0 ? string.Format(Loc("Str_St_VaultDoneProblems"), result.Written, root, result.Problems.Count)
                : result.Lossy > 0 ? string.Format(Loc("Str_St_VaultDoneLossy"), result.Written, root, result.Lossy)
                : string.Format(Loc("Str_St_VaultDone"), result.Written, root);
        }

        private void JobCancel_Click(object sender, RoutedEventArgs e) => CancelBackgroundJob();

        /// <summary>Stops the running export or import: an export removes its own folder as it ends,
        /// and an import has written nothing yet.</summary>
        private void CancelBackgroundJob()
        {
            if (_jobCancel is not { } cancel || cancel.IsCancellationRequested) return;
            cancel.Cancel();
            StatusText.Text = Loc(_jobCancelingKey);
        }
    }
}
