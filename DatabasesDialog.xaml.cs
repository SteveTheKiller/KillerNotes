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
                nameText.SetResourceReference(TextBlock.ForegroundProperty, active ? "PrimaryBrush" : "TextBrush");
                var meta = new TextBlock
                {
                    Text = $"   {fi.Length / 1024:N0} KB   {fi.LastWriteTime:yyyy-MM-dd HH:mm}"
                         + (enc ? "   [encrypted]" : "") + (active ? "   [active]" : ""),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                row.Children.Add(nameText);
                row.Children.Add(meta);

                var item = new ListBoxItem { Tag = name, Content = row };
                DbList.Items.Add(item);
                if (select != null
                        ? string.Equals(name, select, StringComparison.OrdinalIgnoreCase)
                        : active)
                    DbList.SelectedItem = item;
            }
            DlgStatus.Text = NoteStore.DbDir;
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
            if (SelectedFile is not string name) { DlgStatus.Text = "Select a database first"; return; }
            try
            {
                var files = new System.Collections.Specialized.StringCollection
                    { Path.Combine(NoteStore.DbDir, name) };
                Clipboard.SetFileDropList(files);
                DlgStatus.Text = $"{name} copied - paste it into Explorer, Teams, or an email";
            }
            catch (Exception ex) { DlgStatus.Text = "Copy failed: " + ex.Message; }
        }

        // Share a whole database: a .kndb is the .db verbatim (encryption travels with it);
        // the extension is what makes it double-clickable into KillerNotes on the other end.
        private void ExportMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { DlgStatus.Text = "Select a database first"; return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "KillerNotes database (*.kndb)|*.kndb",
                FileName = Path.GetFileNameWithoutExtension(name) + ".kndb",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.Copy(Path.Combine(NoteStore.DbDir, name), dlg.FileName, overwrite: true);
                DlgStatus.Text = "Exported to " + dlg.FileName;
            }
            catch (Exception ex) { DlgStatus.Text = "Export failed: " + ex.Message; }
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
                DlgStatus.Text = "That name has characters Windows does not allow";
                return false;
            }
            string dest = Path.Combine(NoteStore.DbDir, newName);
            if (File.Exists(dest)) { DlgStatus.Text = $"{newName} already exists"; return false; }
            try
            {
                File.Move(Path.Combine(NoteStore.DbDir, oldName), dest);
                // Renaming the active file must retarget the setting or the app would
                // silently create a fresh empty db at the old name on reopen.
                if (string.Equals(oldName, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
                    App.SetSetting("ActiveDatabase", newName);
                RefreshDbList(newName);
                DlgStatus.Text = $"{oldName} renamed to {newName}";
                return true;
            }
            catch (Exception ex) { DlgStatus.Text = "Rename failed: " + ex.Message; return false; }
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
            catch (Exception ex) { DlgStatus.Text = "Create failed: " + ex.Message; }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { DlgStatus.Text = "Select a database first"; return; }

            var confirm = new ConfirmDialog(
                $"Delete \"{name}\"?",
                "Every note inside it is permanently deleted. This cannot be undone.",
                "Delete") { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            try
            {
                File.Delete(Path.Combine(NoteStore.DbDir, name));
                RefreshDbList();
                DlgStatus.Text = $"{name} deleted";
            }
            catch (Exception ex) { DlgStatus.Text = "Delete failed: " + ex.Message; }
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

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile is not string name) { DlgStatus.Text = "Select a database first"; return; }
            SelectedDatabase = name;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}
