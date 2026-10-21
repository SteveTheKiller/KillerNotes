using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A paste is tidied to the editor's look (theme text colors, no converter margins, family
    /// table borders), but only the pasted content: everything already in the note keeps its own
    /// spacing and table fills. A paste whose note is switched away before the tidy-up runs
    /// leaves the newly opened note alone.
    /// </summary>
    public sealed class PasteScopeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Fact]
        public void OnlyThePastedContentIsNormalized() => Sta.Run(() =>
        {
            var (shell, editor) = Shell();
            var spaced = new Paragraph(new Run("existing spaced")) { Margin = new Thickness(0, 20, 0, 20) };
            var cell = new TableCell(new Paragraph(new Run("filled"))) { Background = Brushes.Red };
            var table = new Table { RowGroups = { new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } } } };
            var tail = new Paragraph(new Run("end"));
            editor.Document = new FlowDocument(spaced) { Blocks = { table, tail } };
            editor.CaretPosition = tail.ContentEnd;

            Invoke(shell, "CapturePasteRange");
            // The way the native paste lands content: loaded at the selection, splitting the
            // paragraph there, with the converter's margins and the source's black text.
            var clip = new FlowDocument();
            foreach (string line in new[] { "one", "two", "three" })
                clip.Blocks.Add(new Paragraph(new Run(line) { Foreground = Brushes.Black }) { Margin = new Thickness(0, 12, 0, 12) });
            using (var ms = new System.IO.MemoryStream())
            {
                new TextRange(clip.ContentStart, clip.ContentEnd).Save(ms, DataFormats.XamlPackage);
                ms.Position = 0;
                editor.Selection.Load(ms, DataFormats.XamlPackage);
            }
            Invoke(shell, "NormalizePastedContent");

            Assert.Equal(new Thickness(0, 20, 0, 20), spaced.Margin);
            Assert.Equal(Brushes.Red, cell.Background);
            var middle = editor.Document.Blocks.OfType<Paragraph>().Single(p => new TextRange(p.ContentStart, p.ContentEnd).Text == "two");
            Assert.Equal(DependencyProperty.UnsetValue, middle.ReadLocalValue(Block.MarginProperty));
            Assert.All(middle.Inlines.OfType<Run>(), r => Assert.NotEqual(Colors.Black, (r.ReadLocalValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color));
        });

        [Fact]
        public void APasteFromANoteSwitchedAwayFromChangesNothing() => Sta.Run(() =>
        {
            var (shell, editor) = Shell();
            editor.Document = new FlowDocument(new Paragraph(new Run("first note")));
            Invoke(shell, "CapturePasteRange");

            var other = new Paragraph(new Run("other note") { Foreground = Brushes.Black }) { Margin = new Thickness(0, 9, 0, 9) };
            editor.Document = new FlowDocument(other);   // the user switched notes before the tidy-up ran
            Invoke(shell, "NormalizePastedContent");

            Assert.Equal(new Thickness(0, 9, 0, 9), other.Margin);
            Assert.Equal(Brushes.Black, ((Run)other.Inlines.FirstInline).Foreground);
            Assert.False((bool)typeof(MainWindow).GetField("_dirty", Members)!.GetValue(shell)!);
        });

        private static (MainWindow, RichTextBox) Shell()
        {
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            foreach (var field in typeof(MainWindow).GetFields(Members))
                if (field.GetValue(shell) == null && field.FieldType.IsGenericType && !field.FieldType.IsInterface &&
                    field.FieldType.Namespace == "System.Collections.Generic" && field.FieldType.GetConstructor(Type.EmptyTypes) != null &&
                    !field.Name.StartsWith("_recording", StringComparison.Ordinal) && !field.Name.StartsWith("_pasted", StringComparison.Ordinal))
                    field.SetValue(shell, Activator.CreateInstance(field.FieldType));
            var editor = new RichTextBox();
            typeof(MainWindow).GetField("Editor", Members)!.SetValue(shell, editor);
            typeof(MainWindow).GetField("_currentId", Members)!.SetValue(shell, 1L);
            typeof(MainWindow).GetField("_saveTimer", Members)!.SetValue(shell, new DispatcherTimer());
            typeof(MainWindow).GetField("_syntaxTimer", Members)!.SetValue(shell, new DispatcherTimer());
            typeof(MainWindow).GetField("StatusText", Members)!.SetValue(shell, new TextBlock());
            return (shell, editor);
        }

        private static object? Invoke(MainWindow shell, string name, params object[] args) =>
            (typeof(MainWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(shell, args);
    }
}
