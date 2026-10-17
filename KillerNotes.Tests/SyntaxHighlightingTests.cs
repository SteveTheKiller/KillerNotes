using System;
using System.Collections.Generic;
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
    [Collection(NoteStoreCollection.Name)]
    public class SyntaxHighlightingTests
    {
        private const string Command = "$User=\"user@example.test\"; $Policy=(Get-Mailbox -Identity $User).RoleAssignmentPolicy; Get-ManagementRoleAssignment -RoleAssignee $Policy | Where-Object {$_.Role -eq \"MyDistributionGroups\"} | Format-Table Name,Role,RoleAssigneeName,Enabled";
        private const string Prose = "Open the Members tab. Add members and save. This is for the mailbox owner.";
        private static readonly Color Variable = Color.FromRgb(156, 220, 254);
        private static readonly Color Type = Color.FromRgb(78, 201, 176);
        private static readonly Color Operator = Color.FromRgb(197, 134, 192);
        private static readonly Color Keyword = Color.FromRgb(86, 156, 214);
        private static readonly Color Comment = Color.FromRgb(106, 153, 85);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MixedTicketNotesHighlightCommandsAndLeaveProseAlone(bool softBreaks) => Sta.Run(() =>
        {
            using var shell = new SyntaxEditor(softBreaks, Prose, "---", Command, "---",
                "Add-DistributionGroupMember -Identity \"Mailbox Access\" -Member $User", Prose);
            shell.Paint();
            shell.AssertColor("$User", Variable);
            shell.AssertColor("Get-Mailbox", Type);
            shell.AssertColor("Get-ManagementRoleAssignment", Type);
            shell.AssertColor("Add-DistributionGroupMember", Type);
            shell.AssertColor("-eq", Operator);
            shell.AssertColor("|", Operator);
            shell.AssertPlain(Prose);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BlockCommentsCloseBeforeCommandsAndProse(bool softBreaks) => Sta.Run(() =>
        {
            using var shell = new SyntaxEditor(softBreaks, "<#", "for compatibility", "#>", Command, Prose);
            shell.Paint();
            shell.AssertColor("for compatibility", Color.FromRgb(106, 153, 85));
            shell.AssertColor("Get-Mailbox", Type);
            shell.AssertPlain(Prose);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EachLineOfAMixedLanguageNoteKeepsItsOwnLanguage(bool softBreaks) => Sta.Run(() =>
        {
            // A "# comment" line looks like a markdown heading. It must not turn the whole note
            // into markdown, and inside PowerShell it is a comment, not a heading.
            using var shell = new SyntaxEditor(softBreaks, Command, "# Who can manage the group",
                "SELECT Name FROM Users WHERE Active = 1;", "def check(user):", "    return len(user)",
                "export PATH=$HOME/bin", Prose);
            shell.Paint();
            shell.AssertColor("Get-Mailbox", Type);
            shell.AssertColor("# Who can manage the group", Comment);
            shell.AssertColor("SELECT", Keyword);
            shell.AssertColor("def", Keyword);
            shell.AssertColor("return", Keyword);
            shell.AssertColor("export", Keyword);
            shell.AssertPlain(Prose);
        });

        [Fact]
        public void ACommentAtTheTopTakesTheLanguageOfTheCodeBelowIt() => Sta.Run(() =>
        {
            using var shell = new SyntaxEditor(false, "# Check the mailbox policy", Command, Prose);
            shell.Paint();
            shell.AssertColor("# Check the mailbox policy", Comment);
            shell.AssertColor("Get-Mailbox", Type);
            shell.AssertPlain(Prose);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeclaredFencesKeepTheirLanguageAndReturnToProse(bool markdownNote) => Sta.Run(() =>
        {
            using var shell = new SyntaxEditor(true, "```ps1", Command, "```", Prose);
            if (markdownNote) shell.Set("_currentFormat", 1);
            shell.Paint();
            shell.AssertColor("$User", Variable);
            shell.AssertColor("Get-Mailbox", Type);
            shell.AssertPlain(Prose);
        });

        [Fact]
        public void EditingThePrecedingLineRevalidatesCachedCommentContext() => Sta.Run(() =>
        {
            using var shell = new SyntaxEditor(false, "Get-Item $value", "    return $value", "#>", Prose);
            shell.Paint();
            shell.AssertColor("return", Color.FromRgb(86, 156, 214));
            var first = (Paragraph)shell.Editor.Document.Blocks.FirstBlock;
            first.Inlines.Clear();
            first.Inlines.Add(new Run("Get-Item $value; <#"));
            shell.Call("QueueSyntaxHighlighting");
            shell.Paint();
            shell.AssertColor("return", Color.FromRgb(106, 153, 85));
            shell.AssertPlain(Prose);
            first.Inlines.Clear();
            first.Inlines.Add(new Run("Get-Item $value"));
            shell.Call("QueueSyntaxHighlighting");
            shell.Paint();
            shell.AssertColor("return", Color.FromRgb(86, 156, 214));
        });

        internal sealed class SyntaxEditor : IDisposable
        {
            private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            public MainWindow Shell { get; } = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            public RichTextBox Editor { get; } = new RichTextBox { FontSize = 14, Foreground = Brushes.White, Background = Brushes.Black };
            private readonly Window _host;

            public SyntaxEditor(bool softBreaks, params string[] lines)
            {
                Set("Editor", Editor);
                foreach (string name in new[] { "_syntaxColored", "_syntaxSeen", "_syntaxFlat", "_syntaxFlatIndex", "_fenceLanguages", "_fenceParts", "_fenceReader", "_syntaxTimer" })
                {
                    var field = typeof(MainWindow).GetField(name, Members)!;
                    field.SetValue(Shell, Activator.CreateInstance(field.FieldType, true));
                }
                Set("_syntaxHighlight", true);
                Set("_syntaxFlatDirty", true);
                Set("_fenceVersion", -1L);
                Set("_docMdVersion", -1L);
                Set("_docLangVersion", -1L);
                var document = new FlowDocument { PagePadding = new Thickness(12) };
                if (softBreaks)
                {
                    var paragraph = new Paragraph();
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) paragraph.Inlines.Add(new LineBreak());
                        // Fragmented formatting exercises real TextPointer boundaries.
                        int middle = lines[i].Length / 2;
                        paragraph.Inlines.Add(new Run(lines[i].Substring(0, middle)));
                        paragraph.Inlines.Add(new Bold(new Run(lines[i].Substring(middle))));
                    }
                    document.Blocks.Add(paragraph);
                }
                else foreach (string line in lines) document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0) });
                Editor.Document = document;
                _host = new Window { Content = Editor, Width = 1100, Height = 600, Left = -32000, Top = -32000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                _host.Show();
                Pump();
            }

            public string Text => new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
            public void Set(string name, object value) => typeof(MainWindow).GetField(name, Members)!.SetValue(Shell, value);
            public void Call(string name) => typeof(MainWindow).GetMethod(name, Members)!.Invoke(Shell, null);
            public void Paint()
            {
                string original = Text;
                Editor.UpdateLayout();
                Call("ApplySyntaxHighlighting");
                Assert.Equal(original, Text);
            }
            public void AssertColor(string token, Color expected)
            {
                var text = Text;
                int start = text.IndexOf(token, StringComparison.Ordinal);
                Assert.True(start >= 0);
                var colors = Colors();
                for (int i = start; i < start + token.Length; i++) Assert.Equal(expected, colors[i]);
            }
            public void AssertPlain(string prose)
            {
                int start = Text.IndexOf(prose, StringComparison.Ordinal);
                var colors = Colors();
                Assert.True(start >= 0);
                for (int i = start; i < start + prose.Length; i++) Assert.Null(colors[i]);
            }
            private List<Color?> Colors()
            {
                var colors = new List<Color?>();
                foreach (Paragraph p in Editor.Document.Blocks)
                {
                    var at = p.ContentStart;
                    while (at != null && at.CompareTo(p.ContentEnd) < 0)
                    {
                        var context = at.GetPointerContext(LogicalDirection.Forward);
                        if (context == TextPointerContext.Text)
                        {
                            string run = at.GetTextInRun(LogicalDirection.Forward);
                            var element = (TextElement)at.Parent;
                            Color? color = element.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush brush ? brush.Color : (Color?)null;
                            colors.AddRange(Enumerable.Repeat(color, run.Length));
                            at = at.GetPositionAtOffset(run.Length, LogicalDirection.Forward);
                            continue;
                        }
                        if (context == TextPointerContext.ElementStart && at.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                            colors.AddRange(new Color?[] { null, null });
                        at = at.GetNextContextPosition(LogicalDirection.Forward);
                    }
                    colors.AddRange(new Color?[] { null, null });
                }
                Assert.Equal(Text.Length, colors.Count);
                return colors;
            }
            public static void Pump()
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.ApplicationIdle);
                Dispatcher.PushFrame(frame);
            }
            public void Dispose()
            {
                ((DispatcherTimer)typeof(MainWindow).GetField("_syntaxTimer", Members)!.GetValue(Shell)!).Stop();
                _host.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }
    }
}
