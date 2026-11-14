using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class DatabasesDialogSpacingTests
    {
        [Fact]
        public void EmptyStatusTakesNoSpaceAndTheRetentionRowStaysCloseToTheActions() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var dialog = new DatabasesDialog();
            try
            {
                dialog.Show();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var status = (TextBlock)dialog.FindName("DlgStatus");
                var retention = (TextBox)dialog.FindName("TrashRetentionBox");
                var open = (Button)dialog.FindName("OpenBtn");
                var list = (ListBox)dialog.FindName("DbList");
                var root = (UIElement)dialog.Content;
                Assert.False(status.IsVisible);
                double gap = open.TranslatePoint(new Point(), root).Y - retention.TranslatePoint(new Point(0,retention.ActualHeight), root).Y;
                Assert.InRange(gap, 4, 12);
                var body = (StackPanel)((Grid)((Border)list.Parent).Parent).Parent;
                Assert.InRange(body.Margin.Left, 8, 16);
                Assert.InRange(body.Margin.Top, 4, 10);
                status.Text = "An operation needs attention.";
                dialog.UpdateLayout();
                Assert.True(status.IsVisible);
                status.Text = "";
                dialog.UpdateLayout();
                Assert.False(status.IsVisible);
            }
            finally { dialog.Opacity = 0; dialog.Close(); }
        });
    }
}
