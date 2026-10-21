// ═══════════════════════════════════════════════════════════
//  FOLDER IMPORT  -  the window's half of Services/FolderImporter.cs
// ═══════════════════════════════════════════════════════════
//
// "Import a folder of notes..." on the notes menu (Alt+Shift+I). Reads the folder in the
// background, shows what will happen - how many notes, new groups, titles that already exist,
// what is skipped - and changes nothing until Import is pressed. The notes are then built on an
// STA thread behind the footer's Cancel and written in one transaction on the UI thread, so a
// cancel, a lock, a switch of notepad or a failure leaves the notepad exactly as it was.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private async void ImportFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_jobCancel != null) { StatusText.Text = Loc("Str_St_JobBusy"); return; }
            if (!NoteStore.IsOpen) return;
            if (NoteStore.IsReadOnly) { StatusText.Text = string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner); return; }
            if (!SaveCurrentNote(refreshList: false)) return;

            string? folder = FolderPicker.Show(this, null, Loc("Str_Dlg_ImportPickFolder"));
            if (string.IsNullOrWhiteSpace(folder)) return;

            string database = NoteStore.ActiveDbFile;
            var state = NoteStore.ImportState();
            var cancel = new CancellationTokenSource();
            _jobCancel = cancel;
            _jobCancelingKey = "Str_St_ImportCanceling";
            JobCancelBtn.Visibility = Visibility.Visible;
            _statusTimer.Stop();
            StatusText.Text = Loc("Str_St_ImportReading");

            ImportPlan? plan = null;
            List<PreparedImport>? prepared = null;
            try
            {
                plan = await Task.Run(() => FolderImporter.Plan(folder!, state, cancel.Token));
                if (!StillImporting(cancel, database)) return;
                if (plan.Notes.Count == 0)
                {
                    StatusText.Text = string.Format(Loc("Str_St_ImportNothing"), plan.AlreadyImported);
                    return;
                }

                var confirm = new ConfirmDialog(Loc("Str_Dlg_ImportHead"), PreviewText(plan), Loc("Str_Btn_Import")) { Owner = this };
                confirm.ShowDialog();
                if (!confirm.Confirmed || !StillImporting(cancel, database)) { StatusText.Text = Loc("Str_St_ImportCanceled"); return; }

                int total = plan.Notes.Count;
                var progress = new Progress<int>(done =>
                {
                    if (_jobCancel == cancel && !cancel.IsCancellationRequested)
                        StatusText.Text = string.Format(Loc("Str_St_ImportProgress"), done, total);
                });
                prepared = await RunOnSta(() => FolderImporter.Prepare(plan, progress, cancel.Token));
                if (!StillImporting(cancel, database)) { StatusText.Text = Loc("Str_St_ImportCanceled"); return; }

                var ids = NoteStore.ImportBatch(prepared);
                SearchBox.Text = "";   // a filtered list would hide the imports
                RefreshList();
                if (ids.Count > 0) OpenNote(ids[0]);
                StatusText.Text = string.Format(Loc("Str_St_ImportDone"), ids.Count, plan.Folder);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = Loc("Str_St_ImportCanceled");
            }
            catch (Exception ex)
            {
                // Nothing was written: the planner and preparer only read, and the batch is one
                // transaction that rolled back.
                StatusText.Text = string.Format(Loc("Str_St_ImportFolderFailed"), ex.Message);
            }
            finally
            {
                if (_jobCancel == cancel) _jobCancel = null;
                cancel.Dispose();
                JobCancelBtn.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Whether the import may go on: not canceled (the Cancel button, Lock now or
        /// closing), and the same notepad still open.</summary>
        private static bool StillImporting(CancellationTokenSource cancel, string database) =>
            !cancel.IsCancellationRequested && NoteStore.IsOpen && !NoteStore.IsReadOnly &&
            string.Equals(NoteStore.ActiveDbFile, database, StringComparison.OrdinalIgnoreCase);

        /// <summary>Runs work on its own STA thread (FlowDocument needs one) and completes on the
        /// caller's dispatcher.</summary>
        private static Task<T> RunOnSta<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>();
            var thread = new Thread(() =>
            {
                try { done.SetResult(work()); }
                catch (Exception ex) { done.SetException(ex); }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            })
            { IsBackground = true, Name = "KillerNotes folder import" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return done.Task;
        }

        /// <summary>The preview: what goes where, what is new, what clashes, what stays behind.
        /// Lists are cut to a few names so the dialog stays readable.</summary>
        private string PreviewText(ImportPlan plan)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(Loc("Str_Dlg_ImportBody"), plan.Notes.Count, plan.Folder,
                System.IO.Path.GetFileNameWithoutExtension(NoteStore.ActiveDbFile)));
            if (plan.NewGroups.Count > 0)
                sb.Append("\n\n").Append(string.Format(Loc("Str_Dlg_ImportGroups"), Few(plan.NewGroups)));
            if (plan.AlreadyImported > 0)
                sb.Append("\n\n").Append(string.Format(Loc("Str_Dlg_ImportAlready"), plan.AlreadyImported));
            if (plan.Clashes.Count > 0)
                sb.Append("\n\n").Append(string.Format(Loc("Str_Dlg_ImportClashes"), Few(plan.Clashes)));
            if (plan.Skipped.Count > 0)
                sb.Append("\n\n").Append(string.Format(Loc("Str_Dlg_ImportSkipped"), plan.Skipped.Count,
                    Few(plan.Skipped.Select(s => s.File + " (" + Loc("Str_Imp_Why_" + s.Reason) + ")").ToList())));
            sb.Append("\n\n").Append(Loc("Str_Dlg_ImportNothingYet"));
            return sb.ToString();
        }

        private string Few(IReadOnlyList<string> items) =>
            items.Count <= 5 ? string.Join(", ", items)
                : string.Join(", ", items.Take(5)) + " " + string.Format(Loc("Str_Dlg_ImportMore"), items.Count - 5);
    }
}
