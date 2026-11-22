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
            ((ComboBox)window.FindName("PreviewModeBtn")).SelectedValue = "Rendered";
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var viewer = Elements((Border)window.FindName("PreviewPane")).OfType<FlowDocumentScrollViewer>().Single();
            Assert.NotNull(viewer.ContextMenu);
            var menu = viewer.ContextMenu;
            // The preview's card (Shell/NoteCards.cs): two tiles, and Copy follows the selection as the card opens.
            Button[] Tiles() => menu.Items.OfType<MenuItem>().SelectMany(i => Logical(i.Header as DependencyObject))
                .OfType<Button>().Where(b => b.Tag is string).ToArray();
            string? copied = null;
            DataObjectCopyingEventHandler copying = (_, e) => { copied = e.DataObject.GetData(DataFormats.UnicodeText) as string; e.CancelCommand(); };
            DataObject.AddCopyingHandler(viewer, copying);
            try
            {
                viewer.Focus();
                Open();
                var tiles = Tiles();
                Assert.Equal(2, tiles.Length);
                Assert.Equal(window.FindResource("Str_Ctx_Copy"), System.Windows.Automation.AutomationProperties.GetName(tiles[0]));
                Assert.Equal(window.FindResource("Str_Ctx_SelectAll"), System.Windows.Automation.AutomationProperties.GetName(tiles[1]));
                Assert.Equal(new[] { "Ctrl+C", "Ctrl+A" }, tiles.Select(System.Windows.Automation.AutomationProperties.GetAcceleratorKey));
                Assert.False(tiles[0].IsEnabled);
                Invoke(tiles[1]);
                Assert.Contains("Rendered heading", viewer.Selection.Text);
                Assert.Contains("Rendered body", viewer.Selection.Text);
                Assert.DoesNotContain("#", viewer.Selection.Text);
                Open();
                tiles = Tiles();
                Assert.True(tiles[0].IsEnabled);
                Invoke(tiles[0]);
                Assert.Contains("Rendered heading", copied);
                Assert.DoesNotContain("#", copied);
            }
            finally { menu.IsOpen = false; DataObject.RemoveCopyingHandler(viewer, copying); }

            void Open()
            {
                menu.IsOpen = false;
                menu.PlacementTarget = viewer;
                menu.IsOpen = true;
                CommandManager.InvalidateRequerySuggested();
                viewer.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            void Invoke(Button tile)
            {
                tile.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                viewer.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
        });

        private static IEnumerable<DependencyObject> Logical(DependencyObject? root)
        {
            if (root == null) yield break;
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var d in Logical(child)) yield return d;
        }

        private static IEnumerable<DependencyObject> Elements(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
