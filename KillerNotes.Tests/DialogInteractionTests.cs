using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// How the dialogs react to the mouse and keyboard. Each dialog is built with the app's real
    /// resources and driven through its own handlers, and is never shown.
    /// </summary>
    [Collection("Application resources")]
    public sealed class DialogInteractionTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class TestPresentationSource : PresentationSource
        {
            public override Visual RootVisual { get; set; } = new DrawingVisual();
            public override bool IsDisposed => false;
            protected override CompositionTarget GetCompositionTargetCore() => null!;
        }

        /// <summary>Runs the body with the app's themes, styles and English strings merged in, and
        /// puts the application's dictionaries back afterward.</summary>
        private static void WithResources(Action body) => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var previous = app.Resources.MergedDictionaries.ToArray();
            try
            {
                foreach (string resource in new[]
                {
                    "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml",
                    "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml",
                })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative)
                    });
                body();
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in previous)
                    app.Resources.MergedDictionaries.Add(dictionary);
            }
        });

        private static T Named<T>(Window window, string name) where T : class =>
            (T)window.FindName(name)!;

        private static MouseButtonEventArgs DoubleClick(object source) =>
            new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent,
                Source = source,
            };

        private static object? Call(object target, string method, params object[] args)
        {
            try { return target.GetType().GetMethod(method, Any)!.Invoke(target, args); }
            catch (TargetInvocationException ex)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                throw;
            }
        }

        // ---- Manage databases ----

        /// <summary>A data folder holding the given databases, with the dialog's list limited to the
        /// first so no real database is ever listed.</summary>
        private sealed class DatabaseFolder : IDisposable
        {
            public string Dir { get; } = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

            public DatabaseFolder(params string[] names)
            {
                Directory.CreateDirectory(Dir);
                foreach (string name in names) File.WriteAllBytes(Path.Combine(Dir, name), new byte[16]);
                NoteStore.DbDirOverride = Dir;
                NoteStore.DemoDbFile = names[0];
            }

            public void Dispose()
            {
                NoteStore.DbDirOverride = null;
                NoteStore.DemoDbFile = null;
                try { Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void DoubleClickingBlankSpaceInTheDatabaseListDoesNotOpenTheSelectedDatabase() => WithResources(() =>
        {
            using var folder = new DatabaseFolder("one.db");
            var dialog = new KillerNotes.Controls.DatabasesDialog();
            try
            {
                var list = Named<ListBox>(dialog, "DbList");
                Assert.NotNull(list.SelectedItem);

                // On the list itself or its scroll bar, not on a row.
                Call(dialog, "DbList_MouseDoubleClick", list, DoubleClick(list));

                Assert.Null(dialog.SelectedDatabase);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void DoubleClickingADatabaseRowStillOpensIt() => WithResources(() =>
        {
            using var folder = new DatabaseFolder("one.db");
            var dialog = new KillerNotes.Controls.DatabasesDialog();
            try
            {
                var list = Named<ListBox>(dialog, "DbList");
                var item = (ListBoxItem)list.SelectedItem;
                item.Measure(new Size(300, 60));
                var inner = ((StackPanel)item.Content).Children[1];

                // The dialog was never shown, so handing back its result throws after the choice is recorded.
                Assert.Throws<InvalidOperationException>(() =>
                    Call(dialog, "DbList_MouseDoubleClick", list, DoubleClick(inner)));
                Assert.Equal("one.db", dialog.SelectedDatabase);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void ARenameThatFailsLeavesItsReasonInTheStatusLine() => WithResources(() =>
        {
            using var folder = new DatabaseFolder("one.db", "two.db");
            var dialog = new KillerNotes.Controls.DatabasesDialog();
            try
            {
                var list = Named<ListBox>(dialog, "DbList");
                var status = Named<TextBlock>(dialog, "DlgStatus");
                var row = (StackPanel)((ListBoxItem)list.SelectedItem).Content;
                Call(dialog, "BeginRename", list.SelectedItem);
                var box = (TextBox)row.Children[0];

                box.Text = "two.db";   // already taken
                box.RaiseEvent(new RoutedEventArgs(FrameworkElement.LostFocusEvent, box));

                Assert.Contains("two.db", status.Text);
                Assert.True(File.Exists(Path.Combine(folder.Dir, "one.db")));
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void ACancelledRenameClearsTheStatusLine() => WithResources(() =>
        {
            using var folder = new DatabaseFolder("one.db");
            var dialog = new KillerNotes.Controls.DatabasesDialog();
            try
            {
                var list = Named<ListBox>(dialog, "DbList");
                var status = Named<TextBlock>(dialog, "DlgStatus");
                status.Text = "left over from an earlier action";
                var row = (StackPanel)((ListBoxItem)list.SelectedItem).Content;
                Call(dialog, "BeginRename", list.SelectedItem);
                var box = (TextBox)row.Children[0];

                box.Text = "   ";   // nothing to rename to, and no reason worth showing
                box.RaiseEvent(new RoutedEventArgs(FrameworkElement.LostFocusEvent, box));

                Assert.Equal("", status.Text);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void TheFolderButtonsShowTheDataFolderOnceTheShortcutIsAdded() => WithResources(() =>
        {
            using var folder = new DatabaseFolder("one.db");
            var dialog = new KillerNotes.Controls.DatabasesDialog();
            try
            {
                string leaf = Path.GetFileName(folder.Dir);
                foreach (string name in new[] { "ExplorerBtn", "DataFolderBtn" })
                {
                    var button = Named<Button>(dialog, name);
                    button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    Assert.Contains(leaf, (string)button.ToolTip);
                    Assert.EndsWith(")", (string)button.ToolTip);
                }
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void ATooltipChangedAfterTheShortcutWasAddedKeepsItsNewText() => Sta.Run(() =>
        {
            var button = new Button { ToolTip = "Open" };
            KillerNotes.Controls.DialogShortcuts.Describe(button, "Alt+E");
            button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal("Open (Alt+E)", button.ToolTip);

            button.ToolTip = "Open in C:/Notes";
            button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal("Open in C:/Notes (Alt+E)", button.ToolTip);

            // Refreshing again with nothing changed must not stack the shortcut twice.
            button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal("Open in C:/Notes (Alt+E)", button.ToolTip);
        });

        // ---- Backups ----

        /// <summary>Lets queued dispatcher work run until the condition holds, or fails after a while.</summary>
        private static void PumpUntil(Func<bool> done)
        {
            var frame = new DispatcherFrame();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                if (!done() && clock.Elapsed < TimeSpan.FromSeconds(30)) return;
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Assert.True(done(), "The work never finished");
        }

        /// <summary>A closed notepad with a backup folder, the backup settings held in a table so
        /// the machine's own are never read.</summary>
        private sealed class BackupFolder : IDisposable
        {
            private readonly DatabaseFolder _databases = new("one.db");
            private readonly System.Collections.Generic.Dictionary<string, string> _settings = new();
            public string Dir { get; } = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

            public BackupFolder()
            {
                File.WriteAllBytes(Path.Combine(_databases.Dir, "one.db"), new byte[4096]);
                BackupService.Settings = new BackupSettings(
                    name => _settings.TryGetValue(name, out var v) ? v : null,
                    (name, value) => _settings[name] = value,
                    name => _settings.Remove(name));
                Directory.CreateDirectory(Dir);
                BackupService.Settings.Set(BackupService.FolderSetting, Dir);
            }

            public void Dispose()
            {
                BackupService.Settings = BackupSettings.App;
                _databases.Dispose();
                try { Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void BackUpNowKeepsTheDialogFreeWhileTheCopyRuns() => WithResources(() =>
        {
            using var backups = new BackupFolder();
            var dialog = new KillerNotes.Controls.BackupDialog("one.db");
            try
            {
                var button = Named<Button>(dialog, "BackupNowBtn");
                var status = Named<TextBlock>(dialog, "DlgStatus");
                Assert.True(button.IsEnabled);

                Call(dialog, "BackupNow_Click", dialog, new RoutedEventArgs());

                // Back on the message loop with the copy still going: no second click can start another.
                Assert.False(button.IsEnabled);
                PumpUntil(() => button.IsEnabled);
                Assert.Single(Directory.GetFiles(backups.Dir, "*.kndb"));
                Assert.StartsWith("Backed up to", status.Text);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void BackupDialogWillNotCloseFromTheButtonOrEscapeWhileTheCopyRuns() => WithResources(() =>
        {
            using var backups = new BackupFolder();
            var dialog = new KillerNotes.Controls.BackupDialog("one.db");
            int closing = 0;
            dialog.Closing += (_, _) => closing++;
            try
            {
                var button = Named<Button>(dialog, "BackupNowBtn");
                Call(dialog, "BackupNow_Click", dialog, new RoutedEventArgs());
                Assert.False(button.IsEnabled);   // the copy is still running

                Call(dialog, "Close_Click", dialog, new RoutedEventArgs());
                dialog.RaiseEvent(KeyDown(Key.Escape, Named<Button>(dialog, "DoneBtn")));
                Assert.Equal(0, closing);

                PumpUntil(() => button.IsEnabled);
                Call(dialog, "Close_Click", dialog, new RoutedEventArgs());
                Assert.Equal(1, closing);   // once the copy is done, Close works again
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void TestRestoreKeepsTheDialogFreeWhileTheCopyIsChecked() => WithResources(() =>
        {
            using var backups = new BackupFolder();
            // A real database for the check to open.
            string source = Path.Combine(backups.Dir, "source.db");
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + source + ";Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "CREATE TABLE notes(id INTEGER PRIMARY KEY); INSERT INTO notes DEFAULT VALUES;";
                cmd.ExecuteNonQuery();
            }
            File.Copy(source, Path.Combine(backups.Dir, BackupService.FileNameFor("one.db", DateTime.Now)));
            File.Delete(source);
            var dialog = new KillerNotes.Controls.BackupDialog("one.db");
            try
            {
                var button = Named<Button>(dialog, "TestRestoreBtn");
                var status = Named<TextBlock>(dialog, "DlgStatus");
                Assert.True(button.IsEnabled);

                Call(dialog, "TestRestore_Click", dialog, new RoutedEventArgs());

                Assert.False(button.IsEnabled);
                PumpUntil(() => button.IsEnabled);
                Assert.Contains("passed: 1 notes", status.Text);
            }
            finally { dialog.Close(); }
        });

        // ---- Image resize ----

        [Fact]
        public void LosingCaptureMidResizeDropsThePreviewAndKeepsTheOldSize() => Sta.Run(() =>
        {
            var image = new System.Windows.Controls.Image { Width = 200 };
            var adorner = new KillerNotes.Controls.ImageResizeAdorner(image, () => 1000);
            int resized = 0;
            adorner.Resized += () => resized++;

            // A drag from the bottom right corner that is partway through its preview. The press and
            // the move need the real mouse position, so the state they leave behind is set directly.
            SetField(adorner, "_dragging", true);
            SetField(adorner, "_corner", 3);
            SetField(adorner, "_startWidth", 200d);
            SetField(adorner, "_pendingW", 300d);
            SetField(adorner, "_previewing", true);
            image.RenderTransform = new ScaleTransform(1.5, 1.5);

            // Capture is lost to an alt-tab or a dialog before the button comes up.
            typeof(UIElement).GetMethod("OnLostMouseCapture", Any)!.Invoke(adorner,
                new object[] { new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) });

            Assert.True(image.RenderTransform == null || image.RenderTransform.Value.IsIdentity);
            Assert.Equal(200, image.Width);
            Assert.Equal(0, resized);
            Assert.False((bool)GetField(adorner, "_previewing"));
        });

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, Any)!.SetValue(target, value);

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, Any)!.GetValue(target)!;

        // ---- Keyboard ----

        private static KeyEventArgs KeyDown(Key key, object source) =>
            new(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
                Source = source,
            };

        [Fact]
        public void EscapeDismissesThePromptFromTheButtons() => WithResources(() =>
        {
            var dialog = new KillerNotes.Controls.InputDialog("Name", "value", "OK");
            int closing = 0;
            dialog.Closing += (_, _) => closing++;

            // Focus on a button, not the text box: the key travels up to the window.
            var cancel = Named<Button>(dialog, "CancelButton");
            dialog.RaiseEvent(KeyDown(Key.Escape, cancel));

            Assert.Equal(1, closing);
            Assert.False(dialog.Confirmed);
        });

        // ---- Tags ----

        [Fact]
        public void ATagCanBeRenamedToDifferentCapitals() => WithResources(() =>
        {
            using var store = new TempStore();
            NoteStore.AddTag("work", "#ff0000");
            var dialog = new KillerNotes.Controls.TagsDialog();
            try
            {
                Call(dialog, "CommitRename", "work", "Work");

                var names = NoteStore.ListTags().Select(t => t.Name).ToList();
                Assert.Contains("Work", names);
                Assert.DoesNotContain("work", names);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void ATagCannotBeRenamedOntoAnotherTagByCapitalsAlone() => WithResources(() =>
        {
            using var store = new TempStore();
            NoteStore.AddTag("work", "#ff0000");
            NoteStore.AddTag("home", "#00ff00");
            var dialog = new KillerNotes.Controls.TagsDialog();
            try
            {
                Call(dialog, "CommitRename", "work", "HOME");

                var names = NoteStore.ListTags().Select(t => t.Name).ToList();
                Assert.Contains("work", names);
                Assert.Contains("home", names);
                Assert.Equal(Application.Current.TryFindResource("Str_Tags_Exists"), Named<TextBlock>(dialog, "DlgStatus").Text);
            }
            finally { dialog.Close(); }
        });

        // ---- Version history ----

        private static KillerNotes.Controls.HistoryDialog HistoryWithOneVersion(out ListBox list, out ListBoxItem item)
        {
            var dialog = new KillerNotes.Controls.HistoryDialog(noteId: 1);
            list = Named<ListBox>(dialog, "VersionList");
            item = new ListBoxItem
            {
                Tag = new NoteStore.HistoryEntry(42, new DateTime(2026, 10, 1, 9, 30, 0), "Plan", 0, 2048),
                Content = new StackPanel { Children = { new TextBlock { Text = "2026-10-01 09:30" } } },
            };
            list.Items.Add(item);
            list.SelectedItem = item;
            return dialog;
        }

        [Fact]
        public void DoubleClickingBlankSpaceInTheVersionListDoesNotRestoreTheSelectedVersion() => WithResources(() =>
        {
            using var store = new TempStore();
            var dialog = HistoryWithOneVersion(out var list, out _);
            try
            {
                Assert.True(Named<Button>(dialog, "RestoreBtn").IsEnabled);

                Call(dialog, "VersionList_MouseDoubleClick", list, DoubleClick(list));

                Assert.Equal(-1, dialog.ChosenVersion);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void DoubleClickingAVersionRowStillRestoresIt() => WithResources(() =>
        {
            using var store = new TempStore();
            var dialog = HistoryWithOneVersion(out var list, out var item);
            try
            {
                item.Measure(new Size(300, 40));
                var inner = ((StackPanel)item.Content).Children[0];

                Call(dialog, "VersionList_MouseDoubleClick", list, DoubleClick(inner));

                Assert.Equal(42, dialog.ChosenVersion);
            }
            finally { dialog.Close(); }
        });

        // ---- Password prompt ----

        [Fact]
        public void EnterInTheFirstPasswordBoxMovesOnToTheConfirmBox() => WithResources(() =>
        {
            var dialog = new KillerNotes.Controls.PasswordDialog("Set a password", "", "OK", showConfirm: true);
            try
            {
                var first = Named<PasswordBox>(dialog, "PwBox");
                var confirm = Named<PasswordBox>(dialog, "PwConfirmBox");
                first.Password = "secret";

                Call(dialog, "PwBox_KeyDown", first, KeyDown(Key.Enter, first));

                Assert.False(dialog.Confirmed);
                Assert.Same(confirm, FocusManager.GetFocusedElement(dialog));

                confirm.Password = "secret";
                Call(dialog, "PwBox_KeyDown", confirm, KeyDown(Key.Enter, confirm));

                Assert.True(dialog.Confirmed);
                Assert.Equal("secret", dialog.PasswordConfirm);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void EnterSubmitsAPasswordPromptThatHasNoConfirmBox() => WithResources(() =>
        {
            var dialog = new KillerNotes.Controls.PasswordDialog("Unlock", "", "OK");
            try
            {
                var first = Named<PasswordBox>(dialog, "PwBox");
                first.Password = "secret";

                Call(dialog, "PwBox_KeyDown", first, KeyDown(Key.Enter, first));

                Assert.True(dialog.Confirmed);
                Assert.Equal("secret", dialog.Password);
            }
            finally { dialog.Close(); }
        });
    }
}
