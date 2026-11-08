using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Features.Security;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Xunit;

namespace KillerNotes.Tests
{
    [CollectionDefinition("Quick note settings isolation", DisableParallelization = true)]
    public sealed class QuickNoteSettingsCollection { }

    [Collection("Quick note settings isolation")]
    public sealed class QuickNoteSaveRegressionTests
    {
        [Fact]
        public void SaveKeepsTheDestinationChosenInTheWindow()
        {
            WithWindow((shell, window) =>
            {
                var targets = (ComboBox)window.FindName("TargetBox");
                targets.SelectedIndex = targets.Items.Count - 1;
                ((Button)window.FindName("SaveBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(targets.Items.Count - 1, targets.SelectedIndex);
                Assert.NotEmpty(((TextBlock)window.FindName("BannerText")).Text);
            });
        }

        [Fact]
        public void RefreshKeepsGroupIdentityWhenOtherGroupsAreAddedAndFallsBackWhenItIsDeleted()
        {
            WithWindow((shell, window) =>
            {
                NoteStore.AddGroup("Work");
                shell.Window.ShowQuickNote();
                var targets = (ComboBox)window.FindName("TargetBox");
                targets.SelectedItem = targets.Items.Cast<ComboBoxItem>().Single(item => item.Content.ToString().Contains("Work"));
                NoteStore.AddGroup("Another group");
                shell.Window.ShowQuickNote();
                Assert.Contains("Work", ((ComboBoxItem)targets.SelectedItem).Content.ToString());
                NoteStore.DeleteGroup("Work");
                shell.Window.ShowQuickNote();
                Assert.Equal(0, targets.SelectedIndex);
            });
        }

        [Fact]
        public void FirstWindowUsesTheSavedDestination()
        {
            WithWindow((shell, window) =>
            {
                var targets = (ComboBox)window.FindName("TargetBox");
                Assert.Equal(targets.Items.Count - 1, targets.SelectedIndex);
            }, () => App.SetSetting("QuickNoteTarget", "today"));
        }

        [Fact]
        public void FailedCaptureKeepsEarlierEditorChangesAndRetryDoesNotDuplicate()
        {
            WithWindow((shell, window) =>
            {
                string title = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                long id = QuickCapture.CreateNote("Inbox", title, "Untitled");
                var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run("original"))));
                editor.ApplyTemplate();
                editor.Measure(new Size(600, 400));
                editor.Arrange(new Rect(0, 0, 600, 400));
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
                editor.CaretPosition = editor.Document.Blocks.FirstBlock.ContentEnd;
                editor.BeginChange();
                editor.Selection.Text = " earlier edit";
                editor.EndChange();
                editor.Undo();
                Assert.Equal("original", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd());
                editor.Redo();
                Assert.Equal("original earlier edit", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd());
                shell.Set("_currentId", id);
                shell.Set("_currentFormat", 1);
                shell.Set("Editor", editor);
                shell.Set("TitleBox", new TextBox { Text = title });
                shell.Set("_dirty", true);
                shell.Set("_syntaxTimer", new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) });
                shell.Set("_syntaxHighlight", true);
                using var fault = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = NoteStore.DbPath }.ConnectionString);
                fault.Open();
                using var command = fault.CreateCommand();
                command.CommandText = "CREATE TRIGGER fail_capture BEFORE UPDATE ON notes BEGIN SELECT RAISE(ABORT, 'controlled capture failure'); END";
                command.ExecuteNonQuery();
                var targets = (ComboBox)window.FindName("TargetBox");
                targets.SelectedIndex = targets.Items.Count - 1;
                window.Text = "captured thought";
                var save = (Button)window.FindName("SaveBtn");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("controlled capture failure", ((TextBlock)window.FindName("BannerText")).Text);
                Assert.Equal("captured thought", window.Text);
                Assert.Equal("original earlier edit", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd());
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("original earlier edit", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd());
                editor.Undo();
                Assert.Equal("original", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd());
                editor.Redo();
                Assert.Equal("original earlier edit", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd());
            });
        }

        private static void WithWindow(Action<ShellProbe, QuickNoteWindow> body, Action? beforeShow = null) => Sta.RunApplication(() =>
        {
            using var settings = new IsolatedSettings();
            using var store = new TempStore();
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var previous = app.Resources.MergedDictionaries.ToArray();
            QuickNoteWindow? window = null;
            ShellProbe? shell = null;
            try
            {
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml",
                    "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative)
                    });
                shell = new ShellProbe();
                shell.Set("_quickDraft", "");
                shell.Set("_quickTargets", new List<(string Label, string? Group)>());
                shell.Set("_security", new SecurityController(null!));
                beforeShow?.Invoke();
                shell.Window.ShowQuickNote();
                window = shell.Get<QuickNoteWindow>("_quickNote");
                body(shell, window);
            }
            finally
            {
                shell?.Get<DispatcherTimer>("_saveTimer").Stop();
                shell?.Get<DispatcherTimer?>("_syntaxTimer")?.Stop();
                if (window != null)
                {
                    window.Text = "";
                    window.Opacity = 0;
                    window.Hide();
                    window.Close();
                }
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in previous) app.Resources.MergedDictionaries.Add(dictionary);
            }
        });

        // Redirect this process's HKCU reads and writes before the command can read preferences.
        private sealed class IsolatedSettings : IDisposable
        {
            private static readonly IntPtr CurrentUser = new(unchecked((int)0x80000001));
            private readonly string path = @"Software\KillerNotesTests\" + Guid.NewGuid().ToString("N");
            private readonly RegistryKey key;

            public IsolatedSettings()
            {
                key = Registry.CurrentUser.CreateSubKey(path);
                int result = RegOverridePredefKey(CurrentUser, key.Handle.DangerousGetHandle());
                if (result != 0) throw new System.ComponentModel.Win32Exception(result);
            }

            public void Dispose()
            {
                int result = RegOverridePredefKey(CurrentUser, IntPtr.Zero);
                key.Dispose();
                if (result != 0) throw new System.ComponentModel.Win32Exception(result);
                Registry.CurrentUser.DeleteSubKeyTree(path);
            }

            [DllImport("advapi32.dll")]
            private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);
        }
    }
}
