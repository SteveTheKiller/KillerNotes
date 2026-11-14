using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class ManagerRefereeRegressionTests
    {
        [Fact]
        public void CaseOnlyRenameUndoRestoresBranchSpellingAndReachesPreviousUndo() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            string child = NoteStore.GroupPath("Work", "Child");
            NoteStore.AddGroup("Work");
            NoteStore.AddGroup(child, "Work");
            long id = NoteStore.Create("Child note");
            NoteStore.SetNoteGroup(id, child);
            host.SetGroupColor("Work", "#123456");
            AcceptDialog<InputDialog>(() => host.RenameGroup("Work"), dialog =>
            {
                ((TextBox)dialog.FindName("ValueBox")).Text = "WORK";
                ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Assert.Contains(NoteStore.ListGroupTree(), group => group.Path == "WORK");
            Assert.True(host.Undo());
            Assert.Contains(NoteStore.ListGroupTree(), group => group.Path == "Work");
            Assert.Contains(NoteStore.ListGroupTree(), group => group.Path == child && group.Parent == "Work");
            Assert.Equal(child, NoteStore.List().Single(note => note.Id == id).Notebook);
            Assert.True(host.Undo());
            Assert.Equal("", NoteStore.ListGroupTree().Single(group => group.Path == "Work").Color);
            Assert.False(host.Undo());
        });

        [Theory]
        [InlineData("GroupCardTemplatesToggle", "unrelated")]
        [InlineData("GroupCardTemplatesToggle", "affected")]
        [InlineData("GroupCardTemplatesToggle", "replace")]
        [InlineData("GroupCardTemplatesToggle", "clear")]
        [InlineData("GroupCardTemplatesToggle", "rekey")]
        [InlineData("GroupCardDailyToggle", "unrelated")]
        [InlineData("GroupCardDailyToggle", "affected")]
        [InlineData("GroupCardDailyToggle", "replace")]
        [InlineData("GroupCardDailyToggle", "clear")]
        [InlineData("GroupCardDailyToggle", "rekey")]
        public void DeleteUndoRestoresOnlyItsOwnUnchangedRoleMutation(string role, string scenario) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            NoteStore.AddGroup("Work");
            NoteStore.AddGroup("Before");
            NoteStore.AddGroup("After");
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            void Toggle(string path)
            {
                host.ShowGroupCard(path, 0, target, false);
                ((ToggleButton)window.FindName(role)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                ((ContextMenu)window.FindName("GroupCard")).IsOpen = false;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            bool Selected(string path)
            {
                host.ShowGroupCard(path, 0, target, false);
                bool selected = ((ToggleButton)window.FindName(role)).IsChecked == true;
                ((ContextMenu)window.FindName("GroupCard")).IsOpen = false;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                return selected;
            }
            try
            {
                Toggle(scenario == "unrelated" ? "Before" : "Work");
                AcceptDialog<ConfirmDialog>(() => host.DeleteGroup("Work"), dialog =>
                    ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                if (scenario is "unrelated" or "replace" or "clear") Toggle("After");
                if (scenario == "clear") Toggle("After");
                if (scenario == "rekey") NoteStore.SetPassword("role-undo-password");
                Assert.True(host.Undo());
                Assert.Contains(NoteStore.ListGroupTree(), group => group.Path == "Work");
                Assert.Equal(scenario is "affected" or "rekey", Selected("Work"));
                Assert.Equal(scenario is "unrelated" or "replace", Selected("After"));
                Assert.False(Selected("Before"));
            }
            finally { ((ContextMenu)window.FindName("GroupCard")).IsOpen = false; owner.Close(); }
        });

        private static void AcceptDialog<T>(Action action, Action<T> accept) where T : Window
        {
            bool accepted = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<T>().FirstOrDefault();
                if (dialog == null) return;
                timer.Stop();
                accepted = true;
                accept(dialog);
            };
            timer.Start();
            try { action(); }
            finally { timer.Stop(); }
            Assert.True(accepted);
        }
    }
}
