using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class PickerContextTargetTests
    {
        [Fact]
        public void KeyboardPinUsesTheFocusedFolderWhileThePointerIsOverAnotherFolder() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "picker-context-" + Guid.NewGuid().ToString("N"));
            string first = Directory.CreateDirectory(Path.Combine(folder, "First")).FullName;
            string second = Directory.CreateDirectory(Path.Combine(folder, "Second")).FullName;
            ThemeManager.SetSetting("FileDlgPinned", "");
            var dialog = new FileDialog();
            var tree = (TreeView)dialog.FindName("FolderTreeCtl");
            tree.Visibility = Visibility.Visible;
            tree.ItemsSource = new[] { new FolderNode(first, "First", false), new FolderNode(second, "Second", false) };
            var parent = (Panel)tree.Parent;
            parent.Children.Remove(tree);
            var view = new Window { Content = tree, Width = 400, Height = 250, Topmost = true, ShowInTaskbar = false };
            GetCursorPos(out var cursor);
            try
            {
                view.Show();
                view.Activate();
                view.UpdateLayout();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var focused = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                var parked = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(1);
                focused.IsSelected = true;
                focused.Focus();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Point at = parked.PointToScreen(new Point(parked.ActualWidth / 2, parked.ActualHeight / 2));
                Assert.True(parked.IsAncestorOf(view.InputHitTest(view.PointFromScreen(at)) as DependencyObject));
                SetCursorPos((int)Math.Round(at.X) + 1, (int)Math.Round(at.Y));
                Assert.True(SetCursorPos((int)Math.Round(at.X), (int)Math.Round(at.Y)));
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
                Mouse.Synchronize();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
                Assert.True(parked.IsMouseOver, $"The pointer must be over the other owned folder row ({parked.ActualWidth} x {parked.ActualHeight}, {at}, actual {Mouse.DirectlyOver}).");
                Assert.True(focused.IsKeyboardFocusWithin);

                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(focused), 0, Key.Apps)
                    { RoutedEvent = Keyboard.KeyUpEvent, Source = focused });
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.True(tree.ContextMenu.IsOpen);
                tree.ContextMenu.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(first, ThemeManager.GetSetting("FileDlgPinned"));
            }
            finally
            {
                tree.ContextMenu.IsOpen = false;
                view.Content = null;
                view.Close();
                parent.Children.Add(tree);
                dialog.Close();
                SetCursorPos(cursor.X, cursor.Y);
                Directory.Delete(folder, true);
            }
        });

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X; public int Y; }
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
    }
}
