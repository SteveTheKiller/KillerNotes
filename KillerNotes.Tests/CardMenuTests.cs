using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using KillerFamily.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    // 1.7.0 card menus: the note card is about the note, and what left it has one new home.
    public sealed class CardMenuTests
    {
        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate the application sources");
        }

        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static XElement Named(XDocument doc, string name) =>
            doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

        private static IEnumerable<string> Clicks(XElement scope) =>
            scope.Descendants().Select(e => (string?)e.Attribute("Click")).OfType<string>();

        [Theory]
        [InlineData("NavBack_Click")]
        [InlineData("NavForward_Click")]
        [InlineData("ImportFolder_Click")]
        [InlineData("ExportVault_Click")]
        [InlineData("NewMarkdownNote_Click")]
        [InlineData("TodayNote_Click")]
        [InlineData("PreviewDetectGlobal_Click")]
        public void TheNoteCardCarriesOnlyNoteActions(string handler)
        {
            // 1.7.1: the note card is a KillerUI tile card built in NoteCard.cs, and the XAML element is empty.
            var doc = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            Assert.Empty(Clicks(Named(doc, "NoteCard")));
            Assert.DoesNotContain(handler, File.ReadAllText(Path.Combine(Root(), "Shell", "NoteCard.cs")));
        }

        [Theory]
        [InlineData("LibraryRailBtn", "ExportVault_Click")]
        [InlineData("AboutDetectMarkdown", "AboutDetectMarkdown_Click")]
        public void EvictedActionsHaveANewHome(string home, string handler)
        {
            var doc = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var scope = Named(doc, home);
            Assert.Contains(handler, Clicks(scope).Concat([(string?)scope.Attribute("Click") ?? ""]));
        }

        /// <summary>The new-note card (a tile card built in NewMenu.cs) is the one home of Markdown note,
        /// Today's note and Import.</summary>
        [Theory]
        [InlineData("NewMarkdownNote_Click")]
        [InlineData("TodayNote_Click")]
        [InlineData("ImportFolder_Click")]
        public void TheNewMenuCardIsTheHomeOf(string handler)
        {
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "NewMenu.cs"));
            int open = src.IndexOf("private void NewMenu_Opened", StringComparison.Ordinal);
            Assert.Contains(handler, src.Substring(open));
        }

        [Fact]
        public void TheNewMenuOffersANoteInAnyGroup()
        {
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "NewMenu.cs"));
            Assert.Contains("BuildNewInGroupMenu", src);
            Assert.Contains("id: \"ingroup\"", src);
        }

        /// <summary>The note card's Group list ends with Manage groups, the way the Tags list ends
        /// with Manage tags, and the row opens the group manager.</summary>
        [Fact]
        public void TheGroupListLinksToTheGroupManager()
        {
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "Groups.Menu.cs"));
            int build = src.IndexOf("private void BuildGroupMenu", StringComparison.Ordinal);
            int end = src.IndexOf("private List<Note> SelectedOrSame", StringComparison.Ordinal);
            string body = src.Substring(build, end - build);
            Assert.Contains("Str_Ctx_ManageGroups", body);
            Assert.Contains("OpenGroupManager(", body);
            foreach (string strings in Directory.GetFiles(Path.Combine(Root(), "Strings"), "*.xaml"))
                Assert.Contains("x:Key=\"Str_Ctx_ManageGroups\"", File.ReadAllText(strings));
        }

        [Fact]
        public void ImportHasOneHome()
        {
            var doc = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            Assert.DoesNotContain("ImportFolder_Click", Clicks(Named(doc, "LibraryRailBtn")));
        }

        [Fact]
        public void TheTemplateListMovedToTheNewMenu()
        {
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "NewMenu.cs"));
            Assert.Contains("TemplateMenu.Items", src);
            Assert.Contains("id: \"template\"", src);
        }

        [Fact]
        public void GroupHeadersShareOneGroupCard()
        {
            var doc = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var headerTemplate = doc.Descendants().Single(e => e.Name.LocalName == "DataTemplate"
                && ((string?)e.Attribute("DataType"))?.Contains("GroupHeader") == true);
            Assert.DoesNotContain(headerTemplate.Descendants(), e => e.Name.LocalName == "ContextMenu");
            Assert.Equal("ContextMenu", Named(doc, "GroupCard").Name.LocalName);
        }

        [Fact]
        public void CardStringsExistInEnglish()
        {
            var doc = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var keys = XDocument.Load(Path.Combine(Root(), "Strings", "en-US.xaml")).Root!.Elements()
                .Select(e => (string?)e.Attribute(X + "Key")).OfType<string>().ToHashSet();
            // The cards are built in code (1.7.1): every Str_ key their builders name must exist.
            var used = new[] { "NoteCard.cs", "GroupCard.cs", "EditorCard.cs", "NewMenu.cs" }
                .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(Root(), "Shell", f)), "\"(Str_[A-Za-z0-9_]+)\"")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value)).Where(k => !k.EndsWith("_", StringComparison.Ordinal)).Distinct().ToList();
            Assert.NotEmpty(used);
            Assert.All(used, key => Assert.Contains(key, keys));
        }

        [Fact]
        public void SwatchRowRingsTheCurrentColorAndReportsTheChosenOne() => Sta.Run(() =>
        {
            var row = new SwatchRow();
            var colors = new[] { Color.FromRgb(0xFF, 0, 0), Color.FromRgb(0, 0, 0xFF) };
            row.Show(colors, "#0000ff");
            var buttons = row.Children.OfType<Button>().ToList();
            Assert.Equal(4, buttons.Count);   // none, two colors, picker
            Assert.Null(buttons[1].Tag);
            Assert.Equal("on", buttons[2].Tag);

            string? chosen = null;
            row.ColorChosen += hex => chosen = hex;
            buttons[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("#FF0000", chosen);
            Assert.Equal("on", buttons[1].Tag);
            Assert.Null(buttons[2].Tag);

            buttons[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("", chosen);

            bool picked = false;
            row.PickRequested += () => picked = true;
            buttons[3].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(picked);
        });

        [Fact]
        public void SwatchRowRingsNothingForAMixedSelection() => Sta.Run(() =>
        {
            var row = new SwatchRow();
            row.Show([Color.FromRgb(0xFF, 0, 0)], null);
            Assert.All(row.Children.OfType<Button>(), b => Assert.Null(b.Tag));
        });
    }
}
