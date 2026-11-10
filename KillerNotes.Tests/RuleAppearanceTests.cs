using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A horizontal rule's line style (solid, dotted, dashed), weight and color: applied to the
    /// rule paragraph, read back from it, kept through a save, and written by HTML export.
    /// </summary>
    public sealed class RuleAppearanceTests
    {
        private static readonly Color Red = (Color)ColorConverter.ConvertFromString("#DD504B");

        public static TheoryData<int, double> Styles()
        {
            var data = new TheoryData<int, double>();
            foreach (var pattern in new[] { TableLinePattern.Solid, TableLinePattern.Dotted, TableLinePattern.Dashed })
                foreach (double weight in RuleAppearance.Weights) data.Add((int)pattern, weight);
            return data;
        }

        [Theory]
        [MemberData(nameof(Styles))]
        public void ARuleReadsBackTheStyleItWasGiven(int pattern, double weight) => Sta.Run(() =>
        {
            var chosen = new RuleAppearance((TableLinePattern)pattern, weight, Red);
            Paragraph rule = RuleStyle.Create(chosen);

            Assert.True(MainWindow.IsRule(rule));
            Assert.Equal(chosen, RuleStyle.Read(rule));
        });

        [Theory]
        [MemberData(nameof(Styles))]
        public void ARuleKeepsItsStyleThroughASave(int pattern, double weight) => Sta.Run(() =>
        {
            var chosen = new RuleAppearance((TableLinePattern)pattern, weight, Red);
            var source = new FlowDocument();
            source.Blocks.Add(new Paragraph(new Run("Above")));
            source.Blocks.Add(RuleStyle.Create(chosen));
            source.Blocks.Add(new Paragraph(new Run("Below")));
            using var stream = new MemoryStream();
            new TextRange(source.ContentStart, source.ContentEnd).Save(stream, DataFormats.XamlPackage);

            var loaded = new FlowDocument();
            stream.Position = 0;
            new TextRange(loaded.ContentStart, loaded.ContentEnd).Load(stream, DataFormats.XamlPackage);

            Paragraph? rule = null;
            foreach (Block block in loaded.Blocks)
                if (MainWindow.IsRule(block)) rule = (Paragraph)block;
            Assert.NotNull(rule);
            Assert.Equal(chosen, RuleStyle.Read(rule!));
        });

        [Fact]
        public void AnUnstyledRuleReadsAsTheDefault() => Sta.Run(() =>
        {
            var old = new Paragraph { FontSize = 2, BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a)) };
            Assert.Equal(RuleAppearance.Default, RuleStyle.Read(old));
        });

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("Dashed|7|#FFDD504B")]
        [InlineData("Dashed|NaN|#FFDD504B")]
        [InlineData("Wavy|2|#FFDD504B")]
        [InlineData("Dashed|2|not-a-color")]
        public void ABadSavedStyleFallsBackToTheDefault(string? setting) =>
            Assert.Equal(RuleAppearance.Default, RuleAppearance.FromSetting(setting));

        [Fact]
        public void TheSavedStyleRoundTrips()
        {
            var chosen = new RuleAppearance(TableLinePattern.Dotted, 4, Red);
            Assert.Equal(chosen, RuleAppearance.FromSetting(chosen.ToSetting()));
        }

        [Fact]
        public void RestylingTouchesOnlyTheRulesInTheSelectionAsOneUndo() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            var first = RuleStyle.Create(RuleAppearance.Default);
            var second = RuleStyle.Create(RuleAppearance.Default);
            var outside = RuleStyle.Create(RuleAppearance.Default);
            var start = new Paragraph(new Run("start"));
            var end = new Paragraph(new Run("end"));
            doc.Blocks.Add(start);
            doc.Blocks.Add(first);
            doc.Blocks.Add(new Paragraph(new Run("middle")));
            doc.Blocks.Add(second);
            doc.Blocks.Add(end);
            doc.Blocks.Add(outside);
            var editor = new RichTextBox(doc);
            InputMethod.SetIsInputMethodEnabled(editor, false);
            // Undo needs a live editor: the same invisible host the Table Style undo tests use.
            var host = new Window { Content = editor, Width = 320, Height = 240, Opacity = 0,
                ShowInTaskbar = false, ShowActivated = false };
            host.Show();
            try
            {
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
                editor.Selection.Select(start.ContentStart, end.ContentEnd);
                var chosen = new RuleAppearance(TableLinePattern.Dashed, 2, Red);

                Assert.Equal(2, RuleStyle.Restyle(editor, editor.Selection, chosen));

                Assert.Equal(chosen, RuleStyle.Read(first));
                Assert.Equal(chosen, RuleStyle.Read(second));
                Assert.Equal(RuleAppearance.Default, RuleStyle.Read(outside));
                Assert.True(editor.Undo());
                Assert.Equal(RuleAppearance.Default, RuleStyle.Read(first));
                Assert.Equal(RuleAppearance.Default, RuleStyle.Read(second));
                Assert.False(editor.CanUndo);
            }
            finally { host.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });

        [Fact]
        public void HtmlExportDrawsAStyledRule() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(RuleStyle.Create(RuleAppearance.Default));
            doc.Blocks.Add(RuleStyle.Create(new RuleAppearance(TableLinePattern.Dashed, 4, Red)));

            string html = HtmlExport.FragmentFromDocument(doc);

            Assert.Contains("<hr/>", html);
            Assert.Contains("<hr style=\"border:none;border-top:4px dashed #DD504B\"/>", html);
        });
    }
}
