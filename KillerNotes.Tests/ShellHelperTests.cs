using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Services;
using KillerNotes.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Small rules the main window leans on: where a remembered group goes when the group moves,
    /// which sort direction a mode starts in, what a shared note's file may be called, and which
    /// failure counts as a wrong password.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ShellHelperTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static string? Setting(string name) =>
            (string?)typeof(MainWindow).GetMethod("DbScopedSetting", Static)!.Invoke(null, new object[] { name });

        private static void Remember(string name, string? value) =>
            typeof(MainWindow).GetMethod("SetDbScopedSetting", Static)!.Invoke(null, new object?[] { name, value });

        [Fact]
        public void RenamingOrMovingTheTemplatesAndDailyGroupsCarriesTheSettingsAlong() => Sta.Run(() =>
        {
            using var store = new TempStore();
            Remember("TemplatesGroup", "Work" + NoteStore.GroupSep + "Templates");
            Remember("DailyGroup", "Daily notes");
            try
            {
                // The ancestor was renamed: the group under it moves with it.
                MainWindow.RepointGroupSettings("Work", "Job");
                Assert.Equal("Job" + NoteStore.GroupSep + "Templates", Setting("TemplatesGroup"));
                Assert.Equal("Daily notes", Setting("DailyGroup"));

                // Dragged under another group.
                MainWindow.RepointGroupSettings("Daily notes", "Archive" + NoteStore.GroupSep + "Daily notes");
                Assert.Equal("Archive" + NoteStore.GroupSep + "Daily notes", Setting("DailyGroup"));

                // A group whose name only starts the same way is not part of the branch.
                MainWindow.RepointGroupSettings("Job" + NoteStore.GroupSep + "Temp", "Elsewhere");
                Assert.Equal("Job" + NoteStore.GroupSep + "Templates", Setting("TemplatesGroup"));

                // Deleted: the setting is cleared rather than left naming nothing.
                MainWindow.RepointGroupSettings("Job", null);
                Assert.Null(Setting("TemplatesGroup"));
                Assert.Equal("Archive" + NoteStore.GroupSep + "Daily notes", Setting("DailyGroup"));
            }
            finally { Remember("TemplatesGroup", null); Remember("DailyGroup", null); }
        });

        [Fact]
        public void DroppingAGroupOnTheTopEdgeOfItsOwnHeaderKeepsItsPlace()
        {
            using var store = new TempStore();
            foreach (string name in new[] { "C", "B", "A" }) NoteStore.AddGroup(name);   // each lands on top: A, B, C
            Assert.Equal(new[] { "A", "B", "C" }, NoteStore.ListGroupTree().Select(g => g.Path));

            // The header the pointer is over is the group being dragged: "before itself".
            bool moved = NoteStore.MoveGroup("B", "", "B");

            Assert.False(moved);
            Assert.Equal(new[] { "A", "B", "C" }, NoteStore.ListGroupTree().Select(g => g.Path));
        }

        [Theory]
        [InlineData("created", false)]   // newest first, like the time button and Alt+4
        [InlineData("title", true)]
        [InlineData("custom", true)]
        public void EachSortModeStartsInTheDirectionItsButtonUses(string field, bool ascending) =>
            Assert.Equal(ascending, MainWindow.DefaultAscending(field));

        [Theory]
        [InlineData("Con", "Con_")]
        [InlineData("nul", "nul_")]
        [InlineData("AUX", "AUX_")]
        [InlineData("Prn", "Prn_")]
        [InlineData("COM1", "COM1_")]
        [InlineData("lpt9", "lpt9_")]
        [InlineData("Con.report", "Con_.report")]
        [InlineData("Console", "Console")]
        [InlineData("COM10", "COM10")]
        [InlineData("My Con", "My Con")]
        [InlineData("a/b", "a-b")]
        [InlineData("", "note")]
        public void AFileNameNeverResolvesToADevice(string title, string expected) =>
            Assert.Equal(expected, MainWindow.SafeFileName(title));

        [Fact]
        public void OnlyTheNotADatabaseFailureReadsAsAWrongPassword() => Sta.Run(() =>
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (new TempStore())
                {
                    NoteStore.Create("Shared");
                    NoteStore.ExportNote(NoteStore.List().Single().Id, knote, "right");
                }
                using var store = new TempStore();

                var wrong = Assert.Throws<SqliteException>(() => NoteStore.ImportNotes(knote, "wrong"));

                Assert.True(MainWindow.IsWrongPassword(wrong));
                Assert.False(MainWindow.IsWrongPassword(new SqliteException("database disk image is malformed", 11)));
                Assert.False(MainWindow.IsWrongPassword(new SqliteException("disk I/O error", 10)));
            }
            finally { try { File.Delete(knote); } catch { } }
        });

        [Fact]
        public void ALockRemovesTheFilesDraggedOutOfTheNotepad()
        {
            string temp = Path.Combine(Path.GetTempPath(), "kn-dragtest-" + Guid.NewGuid().ToString("N"));
            try
            {
                var drag = new NoteDragData(1, "Private", temp, path => File.WriteAllText(path, "x"));
                string dragged = ((string[])drag.GetData(System.Windows.DataFormats.FileDrop)!)[0];
                string other = Path.Combine(temp, "notes.txt");
                File.WriteAllText(other, "x");

                NoteDragData.RemoveDragFolders(temp);

                Assert.False(File.Exists(dragged));
                Assert.False(Directory.Exists(Path.GetDirectoryName(dragged)));
                Assert.True(File.Exists(other));
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        [Fact]
        public void ALinkIsFoundInTheParagraphUnderThePointer() => Sta.Run(() =>
        {
            var first = new Run("intro with [[Not Here]] text");
            var second = new Run("see [[Target]] and [[Other]] here");
            var third = new Run("tail");
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(first));
            doc.Blocks.Add(new Paragraph(second));
            doc.Blocks.Add(new Paragraph(third));

            Assert.Equal("Target", MainWindow.TargetAtPosition(second.ContentStart.GetPositionAtOffset(8)!));
            Assert.Equal("Other", MainWindow.TargetAtPosition(second.ContentStart.GetPositionAtOffset(22)!));
            Assert.Equal("Not Here", MainWindow.TargetAtPosition(first.ContentStart.GetPositionAtOffset(15)!));
            Assert.Null(MainWindow.TargetAtPosition(second.ContentStart.GetPositionAtOffset(1)!));
            Assert.Null(MainWindow.TargetAtPosition(third.ContentStart.GetPositionAtOffset(2)!));
        });

        private static string Painted(string code, string language, Color expected)
        {
            var type = typeof(MainWindow).GetNestedType("CodeLanguage", BindingFlags.NonPublic)!;
            var tokens = (List<(int Start, int Length, Color Color)>)typeof(MainWindow)
                .GetMethod("CollectSyntaxTokens", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { code, Enum.Parse(type, language), false })!;
            return string.Join("|", tokens.Where(t => t.Color == expected).Select(t => code.Substring(t.Start, t.Length)));
        }

        private static Color Syn(string name) =>
            (Color)typeof(MainWindow).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        [Theory]
        [InlineData("CSharp", "var u = \"http://example.com\"; // real", "\"http://example.com\"", "// real")]
        [InlineData("JavaScript", "const u = 'http://example.com'; /* c */", "'http://example.com'", "/* c */")]
        [InlineData("PowerShell", "$m = \"Issue #5\" # real", "\"Issue #5\"", "# real")]
        [InlineData("Python", "m = 'Issue #5'  # real", "'Issue #5'", "# real")]
        [InlineData("Bash", "echo \"Issue #5\" # real", "\"Issue #5\"", "# real")]
        public void ACommentMarkerInsideAStringStaysPartOfTheString(string language, string code, string text, string comment)
        {
            Assert.Equal(text, Painted(code, language, Syn("SynString")));
            Assert.Equal(comment, Painted(code, language, Syn("SynComment")));
        }

        [Fact]
        public void AQuoteInsideACommentStaysPartOfTheComment()
        {
            Assert.Equal("// don't stop", Painted("run(); // don't stop", "CSharp", Syn("SynComment")));
            Assert.Equal("", Painted("run(); // don't stop", "CSharp", Syn("SynString")));
        }
    }
}
