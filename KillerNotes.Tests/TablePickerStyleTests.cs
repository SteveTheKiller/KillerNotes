using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class TablePickerStyleTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PickerStyleActionTargetsTheCurrentTableAndClosesBeforeTheDialog(bool inTable) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            var cell = new TableCell(new Paragraph(new Run("Current table")));
            var table = new Table { RowGroups = { new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } } } };
            table.Columns.Add(new TableColumn());
            if (inTable) editor.Document.Blocks.Add(table);
            var tail = new Paragraph(new Run("Text"));
            editor.Document.Blocks.Add(tail);
            editor.CaretPosition = (inTable ? cell.ContentStart : tail.ContentStart).GetInsertionPosition(LogicalDirection.Forward);
            editor.Focus();
            ((Button)window.FindName("TableBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(editor.IsVisible, "The real editor must be visible before opening its table style dialog.");
            Assert.NotNull(AdornerLayer.GetAdornerLayer(editor));
            var popup = (Popup)window.FindName("TableSizePopup");
            var button = Assert.IsType<Button>(window.FindName("TablePickerStyleBtn"));
            Assert.Equal(inTable, button.IsEnabled);
            bool opened = false, closedBeforeDialog = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<TableStyleDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (dialog == null) return;
                timer.Stop();
                opened = true;
                closedBeforeDialog = !popup.IsOpen;
                ((Button)dialog.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            try
            {
                if (inTable)
                {
                    timer.Start();
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(opened, "Table style did not open. Selection: " + editor.Selection.Start.Parent.GetType().Name
                        + "; status: " + ((TextBlock)window.FindName("StatusText")).Text);
                    Assert.True(closedBeforeDialog);
                    Assert.Single(editor.Document.Blocks.OfType<Table>());
                }
            }
            finally { timer.Stop(); popup.IsOpen = false; }
        });
    }
}
