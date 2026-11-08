using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class TableStyleDialogTests
    {
        [Fact]
        public void ExactWidthMustMeetTheDisplayedMinimum() => WithDialog((dialog, _) =>
        {
            var sizing = Assert.IsType<ComboBox>(dialog.FindName("SizingBox"));
            var width = Assert.IsType<TextBox>(dialog.FindName("WidthBox"));
            var ok = Assert.IsType<Button>(dialog.FindName("OkButton"));
            sizing.SelectedIndex = 3;
            width.Text = "1";
            Assert.False(ok.IsEnabled);
            Assert.NotEmpty(Assert.IsType<TextBlock>(dialog.FindName("ValidationText")).Text);
            width.Text = "24";
            Assert.True(ok.IsEnabled);
        });

        [Theory]
        [InlineData(1, "Dotted")]
        [InlineData(2, "Dashed")]
        public void PatternChoiceReachesTheActualNotePreview(int selection, string expected) => WithDialog((dialog, table) =>
        {
            var editor = new RichTextBox((FlowDocument)table.Parent);
            using var session = new TableStyleSession(editor, table);
            dialog.PreviewChanged += (appearance, sizing) => session.Preview(appearance, sizing);

            Assert.IsType<ComboBox>(dialog.FindName("PatternBox")).SelectedIndex = selection;

            Assert.Equal(expected, TableStyle.ReadAppearance(session.PreviewTable).Pattern.ToString());
            Assert.Equal(TableLinePattern.Solid, TableStyle.ReadAppearance(table).Pattern);
        });

        private static void WithDialog(Action<TableStyleDialog, Table> check) => Sta.RunApplication(() =>
        {
            Application app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var resources = new List<ResourceDictionary>();
            TableStyleDialog? dialog = null;
            try
            {
                foreach (string path in new[] { "Themes/Black.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml",
                    "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                {
                    var dictionary = new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + path, UriKind.Relative) };
                    app.Resources.MergedDictionaries.Add(dictionary);
                    resources.Add(dictionary);
                }
                var table = new Table();
                table.Columns.Add(new TableColumn());
                table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { new TableCell(new Paragraph()) } } } });
                _ = new FlowDocument(table);
                TableStyle.ApplyAppearance(table, new TableAppearance());
                dialog = new TableStyleDialog(table, 0);
                check(dialog, table);
            }
            finally
            {
                dialog?.Close();
                foreach (var dictionary in resources) app.Resources.MergedDictionaries.Remove(dictionary);
            }
        });
    }
}
