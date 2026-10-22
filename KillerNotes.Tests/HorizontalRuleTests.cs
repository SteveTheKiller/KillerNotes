using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class HorizontalRuleTests
    {
        [Theory]
        [InlineData(Key.Up, true)]
        [InlineData(Key.Left, true)]
        [InlineData(Key.Down, false)]
        [InlineData(Key.Right, false)]
        [InlineData(Key.None, false)]
        public void EnteringARuleMovesTypingIntoANormalParagraph(Key key, bool backward) => Sta.Run(() =>
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            var before = new Paragraph(new Run("Before"));
            var rule = new Paragraph { FontSize = 2, BorderThickness = new Thickness(0, 0, 0, 1) };
            var after = new Paragraph(new Run("After"));
            var doc = new FlowDocument { FontSize = 13 };
            doc.Blocks.Add(before);
            doc.Blocks.Add(rule);
            doc.Blocks.Add(after);
            var editor = new RichTextBox(doc);
            typeof(MainWindow).GetField("Editor", fields)!.SetValue(shell, editor);
            typeof(MainWindow).GetMethod("InitRuleGuard", fields)!.Invoke(shell, null);
            typeof(MainWindow).GetField("_ruleLastKey", fields)!.SetValue(shell, key);

            editor.CaretPosition = rule.ContentStart;

            Assert.Same(backward ? before : after, editor.CaretPosition.Paragraph);
            editor.Selection.Text = "typed";
            Assert.Equal(13d, editor.CaretPosition.Paragraph!.FontSize);
            Assert.True(MainWindow.IsRule(rule));
            Assert.DoesNotContain("typed", new TextRange(rule.ContentStart, rule.ContentEnd).Text);
        });
    }
}
