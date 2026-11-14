using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class ManagerPickerReleaseTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ManagerColorPickerKeepsOwnerAndNotifiesAfterConfirmedWrite(int kind) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long id = NoteStore.Create("Picker note");
            long second = NoteStore.Create("Second picker note");
            NoteStore.AddGroup("Work");
            ((Button)window.FindName("OrganizeRailBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var manager = Application.Current.Windows.OfType<GroupManagerWindow>().Single();
            var target = (UIElement)manager.FindName("Table");
            string ReadColor() => kind == 0 ? NoteStore.ListGroupTree().Single(g => g.Path == "Work").Color
                : NoteStore.GetNote(id)!.TitleColor;
            var observed = new List<string>();
            host.GroupsChanged += () => observed.Add(ReadColor());
            if (kind == 0) host.ShowGroupCard("Work", 0, target, false);
            else host.ShowNoteCard(kind == 1 ? new[] { id } : new[] { id, second }, target, false);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var swatches = (SwatchRow)window.FindName(kind == 0 ? "GroupCardSwatches" : "CardSwatches");
            Window? actualOwner = null;
            bool accepted = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var picker = Application.Current.Windows.OfType<ColorPickerDialog>().FirstOrDefault();
                if (picker == null) return;
                timer.Stop();
                actualOwner = picker.Owner;
                var hex = Descendants<TextBox>(picker).Single(box => AutomationProperties.GetName(box) == "Hex color");
                hex.Text = "#123456";
                hex.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                accepted = true;
                Descendants<Button>(picker).Single(button => button.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { swatches.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop(); manager.Close(); }
            Assert.True(accepted);
            Assert.Same(manager, actualOwner);
            Assert.Equal("#123456", ReadColor());
            Assert.Contains("#123456", observed);
            if (kind == 2) Assert.Equal("#123456", NoteStore.GetNote(second)!.TitleColor);
        });

        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ManagerConversionKeepsOwnerAndNotifiesAfterConfirmedWrite(bool markdown) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long id = NoteStore.Create("Conversion note", markdown ? 1 : 0);
            ((Button)window.FindName("OrganizeRailBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var manager = Application.Current.Windows.OfType<GroupManagerWindow>().Single();
            var observed = new List<int>();
            host.GroupsChanged += () => observed.Add(NoteStore.GetNote(id)!.Format);
            host.ShowNoteCard(new[] { id }, (UIElement)manager.FindName("Table"), false);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Window? actualOwner = null;
            bool accepted = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var confirm = Application.Current.Windows.OfType<ConfirmDialog>().FirstOrDefault();
                if (confirm == null) return;
                timer.Stop();
                actualOwner = confirm.Owner;
                accepted = true;
                ((Button)confirm.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try
            {
                ((System.Windows.Controls.Primitives.ToggleButton)window.FindName(markdown ? "CardRichSeg" : "CardMarkdownSeg"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            finally { timer.Stop(); manager.Close(); }
            Assert.True(accepted);
            Assert.Same(manager, actualOwner);
            int expected = markdown ? 0 : 1;
            Assert.Equal(expected, NoteStore.GetNote(id)!.Format);
            Assert.Contains(expected, observed);
        });

        [Fact]
        public void NewGroupFromManagerNoteCardKeepsTheManagerAsDialogOwner() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long id = NoteStore.Create("Group note");
            ((Button)window.FindName("OrganizeRailBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var manager = Application.Current.Windows.OfType<GroupManagerWindow>().Single();
            host.ShowNoteCard(new[] { id }, (UIElement)manager.FindName("Table"), false);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var groupMenu = (MenuItem)window.FindName("GroupMenu");
            Window? actualOwner = null;
            bool accepted = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var input = Application.Current.Windows.OfType<InputDialog>().FirstOrDefault();
                if (input == null) return;
                timer.Stop();
                actualOwner = input.Owner;
                ((TextBox)input.FindName("ValueBox")).Text = "New work";
                accepted = true;
                ((Button)input.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { ((MenuItem)groupMenu.Items[groupMenu.Items.Count - 2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
            finally { timer.Stop(); manager.Close(); }
            Assert.True(accepted);
            Assert.Same(manager, actualOwner);
            Assert.Equal("New work", NoteStore.GetNote(id)!.Notebook);
        });
    }
}
