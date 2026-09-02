using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    // Version history for one note (1.3.1): every version NoteStore kept, newest first, a
    // read-only preview of the selected one, and Restore. The dialog only CHOOSES; the caller
    // (Shell/History.cs) performs the restore through the store and reloads the editor, so the
    // reload path stays in one place.
    public partial class HistoryDialog : Window
    {
        // Cancel the first close, fade out, then close for real (Anim.FadeOutAndClose). A
        // DialogResult set before this survives the cancel and is delivered by the real close.
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        private readonly long _noteId;

        /// <summary>The version the user chose to restore, or -1 when they just closed.</summary>
        public long ChosenVersion { get; private set; } = -1;
        public DateTime ChosenSaved { get; private set; }

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        public HistoryDialog(long noteId)
        {
            _noteId = noteId;
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            Refresh();
        }

        private void Refresh()
        {
            VersionList.Items.Clear();
            var entries = NoteStore.ListHistory(_noteId);
            foreach (var v in entries)
            {
                // Date and size on one row; the title only when the version had a different
                // one, so a rename shows up and an unchanged title does not repeat down the list.
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var when = new TextBlock { Text = v.Saved.ToString("yyyy-MM-dd HH:mm"), FontSize = 12 };
                when.SetResourceReference(TextBlock.FontFamilyProperty, "SidebarFont");
                var meta = new TextBlock
                {
                    Text = $"   {Math.Max(1, v.Size / 1024):N0} KB" + (v.Format == Models.Note.FormatMarkdown ? "   MD" : ""),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                meta.SetResourceReference(TextBlock.FontFamilyProperty, "SidebarFont");
                meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                row.Children.Add(when);
                row.Children.Add(meta);
                var item = new ListBoxItem { Tag = v, Content = row, ToolTip = v.Title.Length > 0 ? v.Title : null };
                VersionList.Items.Add(item);
            }

            bool any = entries.Count > 0;
            EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            Preview.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            DlgStatus.Text = any
                ? string.Format(Loc("Str_Hist_Count"), entries.Count, (int)NoteStore.HistoryInterval.TotalMinutes, NoteStore.HistoryCap)
                : "";
            RestoreBtn.IsEnabled = false;
            if (any) VersionList.SelectedIndex = 0;
        }

        private NoteStore.HistoryEntry? Selected =>
            (VersionList.SelectedItem as ListBoxItem)?.Tag is NoteStore.HistoryEntry e ? e : null;

        private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Preview.Document = new FlowDocument();
            if (Selected is not NoteStore.HistoryEntry v) { RestoreBtn.IsEnabled = false; return; }
            RestoreBtn.IsEnabled = !NoteStore.IsReadOnly;

            var version = NoteStore.LoadVersion(v.Id);
            if (version == null) return;
            var doc = Preview.Document;
            try
            {
                if (version.Value.Format == Models.Note.FormatMarkdown)
                {
                    MarkdownBlob.Fill(doc, MarkdownBlob.Decode(version.Value.Content));
                }
                else if (version.Value.Content != null)
                {
                    using var ms = new MemoryStream(version.Value.Content);
                    new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
                }
                // Saved colors are the theme's at save time; show the version in today's.
                Shell.MainWindow.NormalizeThemeColors(doc);
            }
            catch
            {
                // A blob the deserializer rejects still has its plain text; show that.
                doc.Blocks.Clear();
                doc.Blocks.Add(new Paragraph(new Run(version.Value.Plain)));
            }
        }

        private void VersionList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RestoreBtn.IsEnabled) Restore_Click(sender, e);
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not NoteStore.HistoryEntry v) return;
            ChosenVersion = v.Id;
            ChosenSaved = v.Saved;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
