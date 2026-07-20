using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Services;

namespace KillerNotes
{
    // Manage databases: list every .db in the data folder with size / modified /
    // [encrypted] / [active] flags; create (+), delete (X, with confirm), open the folder
    // in Explorer, rename inline via double-click, or pick one to switch to
    // (SelectedDatabase for the caller). The note store is closed while this dialog is
    // up, so file operations - active file included - are safe.
    public partial class DatabasesDialog : Window
    {
        /// <summary>File name the user chose to open, or null if they just closed.</summary>
        public string? SelectedDatabase { get; private set; }

        /// <summary>Look up a localized string; falls back to the key name if missing.</summary>
        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        public DatabasesDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            RefreshDbList();
        }

        private void RefreshDbList(string? select = null)
        {
            DbList.Items.Clear();
            Directory.CreateDirectory(NoteStore.DbDir);
            foreach (string f in Directory.GetFiles(NoteStore.DbDir, "*.db").OrderBy(x => x))
            {
                var fi = new FileInfo(f);
                string name = fi.Name;
                bool active = string.Equals(name, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase);
                bool enc = NoteStore.IsEncryptedFile(f);

                // Name and metadata are separate TextBlocks so inline rename can swap
                // just the name part for a TextBox.
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var nameText = new TextBlock { Text = name, FontSize = 12 };
                var meta = new TextBlock
                {
                    Text = $"   {fi.Length / 1024:N0} KB   {fi.LastWriteTime:yyyy-MM-dd HH:mm}"
                         + (enc ? "   [" + Loc("Str_Db_FlagEncrypted") + "]" : "")
                         + (active ? "   [" + Loc("Str_Db_FlagActive") + "]" : ""),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                row.Children.Add(nameText);
                row.Children.Add(meta);

                // A selected row fills with the accent (RowSelectedBrush); force white text
                // so name + meta stay readable. In the Light accents that fill and the
                // accent/muted text are the same hue, so a selected row was unreadable.
                var item = new ListBoxItem { Tag = name, Content = row };
                SetRowColors(nameText, meta, active, selected: false);
                item.Selected   += (_, _) => SetRowColors(nameText, meta, active, selected: true);
                item.Unselected += (_, _) => SetRowColors(nameText, meta, active, selected: false);
                DbList.Items.Add(item);
                if (select != null
                        ? string.Equals(name, select, StringComparison.OrdinalIgnoreCase)
                        : active)
                    DbList.SelectedItem = item;
            }
            DlgStatus.Text = NoteStore.DbDir;
        }

        // Selection fills the row with the accent; white text keeps it readable (the notes
        // sidebar does the same). Unselected: active db name in the accent, others normal.
        private static void SetRowColors(TextBlock name, TextBlock meta, bool active, bool selected)
        {
            if (selected)
            {
                name.Foreground = Brushes.White;
                meta.Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                name.SetResourceReference(TextBlock.ForegroundProperty, active ? "PrimaryBrush" : "TextBrush");
                meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            }
        }

        private ListBoxItem? SelectedItem => DbList.SelectedItem as ListBoxItem;
        private string? SelectedFile => SelectedItem?.Tag as string;

        // ---- Inline rename (double-click the name, or right-click > Rename) ----

        private void DbList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedItem is ListBoxItem item) BeginRename(item);
        }

        // Right-click selects the row under the cursor before the context menu opens.
        private void DbList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem)
                d = VisualTreeHelper.GetParent(d);
            if (d is ListBoxItem item) item.IsSelected = true;
        }

        private void RenameMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem is ListBoxItem item) BeginRename(item);
        }

        // "Share": puts the .db on the clipboard as a real file drop, so pasting into
        // Explorer, Teams, or an email attaches/copies it.
        private void CopyFileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { DlgStatus.Text = Loc("Str_Db_SelectFirst"); return; }
            try
            {
                var files = new System.Collections.Specialized.StringCollection
                    { Path.Combine(NoteStore.DbDir, name) };
                Clipboard.SetFileDropList(files);
                DlgStatus.Text = string.Format(Loc("Str_Db_Copied"), name);
            }
            catch (Exception ex) { DlgStatus.Text = string.Format(Loc("Str_Db_CopyFailed"), ex.Message); }
        }

        // Share a whole database: a .kndb is the .db verbatim (encryption travels with it);
        // the extension is what makes it double-clickable into KillerNotes on the other end.
        private void ExportMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { DlgStatus.Text = Loc("Str_Db_SelectFirst"); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = Loc("Str_Filter_Kndb"),
                FileName = Path.GetFileNameWithoutExtension(name) + ".kndb",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.Copy(Path.Combine(NoteStore.DbDir, name), dlg.FileName, overwrite: true);
                DlgStatus.Text = string.Format(Loc("Str_St_ExportedTo"), dlg.FileName);
            }
            catch (Exception ex) { DlgStatus.Text = string.Format(Loc("Str_St_ExportFailed"), ex.Message); }
        }

        private void RevealMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { Explorer_Click(sender, e); return; }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{Path.Combine(NoteStore.DbDir, name)}\"") { UseShellExecute = true });
            }
            catch { /* best-effort */ }
        }

        private void BeginRename(ListBoxItem item)
        {
            if (item.Tag is not string oldName || item.Content is not StackPanel row) return;

            var box = new TextBox
            {
                Text = oldName,
                FontSize = 12,
                MinWidth = 160,
                Padding = new Thickness(2, 0, 2, 0),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent,
            };
            box.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
            box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
            box.SetResourceReference(TextBox.BorderBrushProperty, "PrimaryBrush");
            box.SetResourceReference(TextBox.SelectionBrushProperty, "PrimaryBrush");
            box.SelectionOpacity = 0.35;

            bool done = false;   // guard: Enter commits, then LostFocus fires again
            void Finish(bool commit)
            {
                if (done) return;
                done = true;
                if (!commit || !TryRename(oldName, box.Text)) RefreshDbList(oldName);
            }
            box.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter) { Finish(true); ke.Handled = true; }
                else if (ke.Key == Key.Escape) { Finish(false); ke.Handled = true; }
            };
            box.LostFocus += (_, _) => Finish(true);

            row.Children.RemoveAt(0);          // the name TextBlock
            row.Children.Insert(0, box);
            box.Focus();
            // Preselect the name without its extension, ready to overtype.
            int stem = oldName.LastIndexOf(".db", StringComparison.OrdinalIgnoreCase);
            box.Select(0, stem > 0 ? stem : oldName.Length);
        }

        /// <summary>Validates and applies a rename; refreshes the list on success.</summary>
        private bool TryRename(string oldName, string newNameRaw)
        {
            string newName = newNameRaw.Trim();
            if (newName.Length == 0) return false;
            if (!newName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) newName += ".db";
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return false;
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                DlgStatus.Text = Loc("Str_Db_BadName");
                return false;
            }
            string dest = Path.Combine(NoteStore.DbDir, newName);
            if (File.Exists(dest)) { DlgStatus.Text = string.Format(Loc("Str_Db_Exists"), newName); return false; }
            try
            {
                File.Move(Path.Combine(NoteStore.DbDir, oldName), dest);
                // Renaming the active file must retarget the setting or the app would
                // silently create a fresh empty db at the old name on reopen.
                if (string.Equals(oldName, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
                    App.SetSetting("ActiveDatabase", newName);
                RefreshDbList(newName);
                DlgStatus.Text = string.Format(Loc("Str_Db_Renamed"), oldName, newName);
                return true;
            }
            catch (Exception ex) { DlgStatus.Text = string.Format(Loc("Str_Db_RenameFailed"), ex.Message); return false; }
        }

        // ---- New (+): auto-named, then straight into inline rename ----

        private void New_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = "notes-new.db";
                for (int i = 2; File.Exists(Path.Combine(NoteStore.DbDir, name)); i++)
                    name = $"notes-new-{i}.db";
                // A zero-byte file is a valid empty SQLite database; it initializes on first open.
                File.Create(Path.Combine(NoteStore.DbDir, name)).Dispose();
                RefreshDbList(name);
                if (SelectedItem is ListBoxItem item) BeginRename(item);
            }
            catch (Exception ex) { DlgStatus.Text = string.Format(Loc("Str_Db_CreateFailed"), ex.Message); }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { DlgStatus.Text = Loc("Str_Db_SelectFirst"); return; }

            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_DeleteNoteHead"), name),
                Loc("Str_Dlg_DeleteDbBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            try
            {
                File.Delete(Path.Combine(NoteStore.DbDir, name));
                RefreshDbList();
                DlgStatus.Text = string.Format(Loc("Str_Db_Deleted"), name);
            }
            catch (Exception ex) { DlgStatus.Text = string.Format(Loc("Str_Db_DeleteFailed"), ex.Message); }
        }

        private void Explorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", NoteStore.DbDir) { UseShellExecute = true });
            }
            catch { /* best-effort */ }
        }

        // ---- Change data folder (#6): repoint where the .db files live ----
        // The store is closed while this dialog is up (the caller guarantees it), so the
        // active file can be moved like any other. Both dialog choices proceed with the
        // folder change - "Move" brings every .db along, "Leave them" just switches.

        private void DataFolder_Click(object sender, RoutedEventArgs e)
        {
            string current = NoteStore.DbDir;
            string? picked = FolderPicker.Show(this, current, Loc("Str_Db_PickFolderTitle"));
            if (picked == null) return;

            string full;
            try { full = Path.GetFullPath(picked).TrimEnd(Path.DirectorySeparatorChar); }
            catch { DlgStatus.Text = Loc("Str_Db_BadName"); return; }
            if (string.Equals(full, Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)) return;

            string[] dbs;
            try { dbs = Directory.GetFiles(current, "*.db"); } catch { dbs = new string[0]; }
            int failed = 0;
            if (dbs.Length > 0)
            {
                var confirm = new ConfirmDialog(
                    string.Format(Loc("Str_Dlg_MoveDbsHead"), dbs.Length),
                    Loc("Str_Dlg_MoveDbsBody"),
                    Loc("Str_Btn_Move"), cancelText: Loc("Str_Btn_LeaveFiles")) { Owner = this };
                confirm.ShowDialog();
                if (confirm.Confirmed)
                {
                    foreach (string f in dbs)
                    {
                        try
                        {
                            string dest = Path.Combine(full, Path.GetFileName(f));
                            if (File.Exists(dest)) { failed++; continue; }   // never clobber
                            File.Move(f, dest);
                        }
                        catch { failed++; }
                    }
                }
            }

            App.SetSetting("DataFolder", full);
            RefreshDbList();
            DlgStatus.Text = failed == 0
                ? string.Format(Loc("Str_Db_FolderChanged"), full)
                : string.Format(Loc("Str_Db_FolderChangedPartial"), full, failed);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { DlgStatus.Text = Loc("Str_Db_SelectFirst"); return; }
            SelectedDatabase = name;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}
