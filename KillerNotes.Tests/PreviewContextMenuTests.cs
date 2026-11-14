using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class PreviewContextMenuTests
    {
        [Fact]
        public void RenderedMenuUsesLocalizedCommandsAndSelectsAndCopiesTheRenderedDocument() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new Run("# Rendered heading\n\nRendered body")));
            ((ButtonBase)window.FindName("PreviewRenderedSeg")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var viewer = Elements((Border)window.FindName("PreviewPane")).OfType<FlowDocumentScrollViewer>().Single();
            Assert.NotNull(viewer.ContextMenu);
            var menu = viewer.ContextMenu;
            var rows = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(2, rows.Length);
            Assert.Equal(window.FindResource("Str_Ctx_Copy"), rows[0].Header);
            Assert.Equal(window.FindResource("Str_Ctx_SelectAll"), rows[1].Header);
            Assert.Equal(new[] { "Ctrl+C", "Ctrl+A" }, rows.Select(r => r.InputGestureText));
            Assert.All(rows, row => Assert.Same(viewer, row.CommandTarget));
            string? copied = null;
            DataObjectCopyingEventHandler copying = (_, e) => { copied = e.DataObject.GetData(DataFormats.UnicodeText) as string; e.CancelCommand(); };
            DataObject.AddCopyingHandler(viewer, copying);
            try
            {
                viewer.Focus();
                Open();
                Assert.False(rows[0].IsEnabled);
                Invoke(rows[1]);
                Assert.Contains("Rendered heading", viewer.Selection.Text);
                Assert.Contains("Rendered body", viewer.Selection.Text);
                Assert.DoesNotContain("#", viewer.Selection.Text);
                Open();
                Assert.True(rows[0].IsEnabled);
                Invoke(rows[0]);
                Assert.Contains("Rendered heading", copied);
                Assert.DoesNotContain("#", copied);
            }
            finally { menu.IsOpen = false; DataObject.RemoveCopyingHandler(viewer, copying); }

            void Open()
            {
                menu.PlacementTarget = viewer;
                menu.IsOpen = true;
                CommandManager.InvalidateRequerySuggested();
                viewer.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            void Invoke(MenuItem row)
            {
                ((IInvokeProvider)new MenuItemAutomationPeer(row).GetPattern(PatternInterface.Invoke)).Invoke();
                viewer.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
        });

        private static IEnumerable<DependencyObject> Elements(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
