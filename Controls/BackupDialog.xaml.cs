using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    // Backups for one database (1.3.1): the schedule settings, the copies that exist in the
    // backup folder, Back up now, and Restore, which copies a backup into the data folder as a
    // NEW database and hands its name back so the Databases dialog can select it. Settings are
    // saved as they change; there is no OK to forget.
    public partial class BackupDialog : Window
    {
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        private readonly string _dbFile;
        private bool _loading = true;   // the combos fire SelectionChanged while being set up

        /// <summary>The database file a restore created, or null.</summary>
        public string? RestoredDatabase { get; private set; }

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        public BackupDialog(string dbFile)
        {
            _dbFile = dbFile;
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);

            DbName.Text = dbFile;
            EnableBox.IsChecked = BackupService.Hours > 0;
            FolderBox.Text = BackupService.Folder ?? "";
            SelectByTag(IntervalBox, (BackupService.Hours > 0 ? BackupService.Hours : BackupService.DefaultHours).ToString());
            SelectByTag(KeepBox, BackupService.Keep.ToString());
            _loading = false;
            Refresh();
        }

        private static void SelectByTag(ComboBox box, string tag)
        {
            foreach (var obj in box.Items)
                if (obj is ComboBoxItem item && (item.Tag as string) == tag) { box.SelectedItem = item; return; }
            box.SelectedIndex = 0;
        }

        private static int TagOf(ComboBox box, int fallback) =>
            box.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag as string, out int v) ? v : fallback;

        // ---- Settings ----

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            int hours = EnableBox.IsChecked == true ? TagOf(IntervalBox, BackupService.DefaultHours) : 0;
            App.SetSetting(BackupService.HoursSetting, hours.ToString());
            App.SetSetting(BackupService.KeepSetting, TagOf(KeepBox, BackupService.DefaultKeep).ToString());
            UpdateStatus();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            string? picked = FolderPicker.Show(this, BackupService.Folder, Loc("Str_Bk_PickFolderTitle"));
            if (string.IsNullOrWhiteSpace(picked)) return;
            App.SetSetting(BackupService.FolderSetting, picked!);
            FolderBox.Text = picked;
            Refresh();
        }

        // ---- The list ----

        private void Refresh()
        {
            BackupList.Items.Clear();
            string? folder = BackupService.Folder;
            var backups = folder == null ? [] : BackupService.ListBackups(folder, _dbFile);
            foreach (var f in backups)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var name = new TextBlock { Text = f.Name, FontSize = 12 };
                name.SetResourceReference(TextBlock.FontFamilyProperty, "SidebarFont");
                var meta = new TextBlock
                {
                    Text = $"   {Math.Max(1, f.Length / 1024):N0} KB",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                meta.SetResourceReference(TextBlock.FontFamilyProperty, "SidebarFont");
                meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                row.Children.Add(name);
                row.Children.Add(meta);
                BackupList.Items.Add(new ListBoxItem { Tag = f.FullName, Content = row });
            }
            EmptyText.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RestoreBtn.IsEnabled = false;
            BackupNowBtn.IsEnabled = folder != null;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (BackupService.Folder == null) { DlgStatus.Text = Loc("Str_Bk_NoFolder"); return; }
            DlgStatus.Text = BackupService.LastBackup(_dbFile) is DateTime t
                ? string.Format(Loc("Str_Bk_Last"), t.ToString("yyyy-MM-dd HH:mm"))
                : Loc("Str_Bk_Never");
        }

        private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            RestoreBtn.IsEnabled = BackupList.SelectedItem != null;

        // ---- Actions ----

        private void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            if (BackupService.Folder is not string folder) { DlgStatus.Text = Loc("Str_Bk_NoFolder"); return; }
            try
            {
                string path = BackupService.BackupNow(folder, _dbFile, DateTime.Now, BackupService.Keep);
                Refresh();
                DlgStatus.Text = string.Format(Loc("Str_Bk_Done"), Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                DlgStatus.Text = string.Format(Loc("Str_Bk_Failed"), ex.Message);
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if ((BackupList.SelectedItem as ListBoxItem)?.Tag is not string path) return;
            try
            {
                RestoredDatabase = BackupService.RestoreToDataFolder(path, DateTime.Now);
                DlgStatus.Text = string.Format(Loc("Str_Bk_Restored"), RestoredDatabase);
            }
            catch (Exception ex)
            {
                DlgStatus.Text = string.Format(Loc("Str_Bk_Failed"), ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
