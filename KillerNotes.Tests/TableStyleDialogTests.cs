using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
            var validation = Assert.IsType<TextBlock>(dialog.FindName("ValidationText"));
            Assert.Equal(Visibility.Collapsed, validation.Visibility);
            width.Text = "1";
            Assert.False(ok.IsEnabled);
            Assert.NotEmpty(validation.Text);
            Assert.Equal(Visibility.Visible, validation.Visibility);
            width.Text = "24";
            Assert.True(ok.IsEnabled);
            Assert.Equal(Visibility.Collapsed, validation.Visibility);
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

        [Fact]
        public void ThicknessIsAShortListThatReachesThePreview() => WithDialog((dialog, table) =>
        {
            var thickness = Assert.IsType<ComboBox>(dialog.FindName("ThicknessBox"));
            Assert.Equal(new[] { 1d, 2d, 3d }, thickness.Items.Cast<ComboBoxItem>().Select(i => (double)i.Tag));
            Assert.Equal(1d, (double)((ComboBoxItem)thickness.SelectedItem).Tag);
            var editor = new RichTextBox((FlowDocument)table.Parent);
            using var session = new TableStyleSession(editor, table);
            dialog.PreviewChanged += (appearance, sizing) => session.Preview(appearance, sizing);

            thickness.SelectedIndex = 2;

            Assert.Equal(3d, TableStyle.ReadAppearance(session.PreviewTable).Thickness);
            Assert.Equal(1d, TableStyle.ReadAppearance(table).Thickness);
        });

        [Fact]
        public void AnExistingUnlistedThicknessStaysSelected() => WithDialog((dialog, _) =>
        {
            var thickness = Assert.IsType<ComboBox>(dialog.FindName("ThicknessBox"));
            Assert.Equal(new[] { 1d, 1.5d, 2d, 3d }, thickness.Items.Cast<ComboBoxItem>().Select(i => (double)i.Tag));
            Assert.Equal(1.5d, (double)((ComboBoxItem)thickness.SelectedItem).Tag);
            Assert.True(Assert.IsType<Button>(dialog.FindName("OkButton")).IsEnabled);
        }, new TableAppearance { Thickness = 1.5 });

        [Fact]
        public void TheDialogSizesToItsFieldsWithCrispTextAndAPlainCaption() => WithDialog((dialog, _) =>
        {
            Assert.Equal(SizeToContent.Height, dialog.SizeToContent);
            Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(dialog));
            Assert.True(dialog.SnapsToDevicePixels);
            var caption = (string)dialog.FindResource("Str_TableStyle_Caption");
            Assert.False(caption.EndsWith(".", StringComparison.Ordinal));
            Assert.Equal(caption, dialog.Title);
        });

        private static void WithDialog(Action<TableStyleDialog, Table> check, TableAppearance? appearance = null) => Sta.RunApplication(() =>
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
                TableStyle.ApplyAppearance(table, appearance ?? new TableAppearance());
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
