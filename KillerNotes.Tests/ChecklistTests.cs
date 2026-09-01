using System.Linq;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Checkbox lines (1.3.1): the shared line rules, and the markdown mapping that turns the
    /// editor's box glyph into task-list syntax and back. Converter bodies run on STA like the
    /// rest of the FlowDocument tests.
    /// </summary>
    public sealed class ChecklistTests
    {
        private const double Base = 13;

        // ---- Line rules ----

        [Fact]
        public void RichLinesReadTheirStateFromTheFirstCharacter()
        {
            Assert.Equal(Checklist.State.Unchecked, Checklist.Of("☐ milk", false));
            Assert.Equal(Checklist.State.Checked, Checklist.Of("☑ eggs", false));
            Assert.Equal(Checklist.State.None, Checklist.Of("milk", false));
            Assert.Equal(Checklist.State.None, Checklist.Of("", false));
        }

        [Fact]
        public void MarkdownLinesReadTheirStateFromTheMarker()
        {
            Assert.Equal(Checklist.State.Unchecked, Checklist.Of("- [ ] milk", true));
            Assert.Equal(Checklist.State.Checked, Checklist.Of("- [x] eggs", true));
            Assert.Equal(Checklist.State.Checked, Checklist.Of("- [X] eggs", true));
            Assert.Equal(Checklist.State.None, Checklist.Of("- milk", true));
            Assert.Equal(Checklist.State.None, Checklist.Of("[ ] milk", true));
        }

        [Fact]
        public void PrefixLengthCoversTheGlyphAndItsSpaceOnly()
        {
            Assert.Equal(2, Checklist.PrefixLength("☐ milk", false));
            Assert.Equal(1, Checklist.PrefixLength("☐milk", false));   // the space was typed over
            Assert.Equal(0, Checklist.PrefixLength("milk", false));
            Assert.Equal(6, Checklist.PrefixLength("- [ ] milk", true));
        }

        [Fact]
        public void MarkdownToggleFlipsTheMarkerAndLeavesTheRestAlone()
        {
            Assert.Equal("- [x] milk", Checklist.ToggleMarkdown("- [ ] milk"));
            Assert.Equal("- [ ] milk", Checklist.ToggleMarkdown("- [x] milk"));
            Assert.Equal("- [ ] milk", Checklist.ToggleMarkdown("- [X] milk"));
            Assert.Equal("plain", Checklist.ToggleMarkdown("plain"));
        }

        [Fact]
        public void RichLineBecomesTaskListSyntax()
        {
            Assert.Equal("- [ ] milk", Checklist.ToMarkdownLine("☐ milk"));
            Assert.Equal("- [x] eggs", Checklist.ToMarkdownLine("☑ eggs"));
            Assert.Equal("- [ ] milk", Checklist.ToMarkdownLine("☐milk"));
            Assert.Equal("milk", Checklist.ToMarkdownLine("milk"));
        }

        // ---- Markdown mapping ----

        [Fact]
        public void TaskListImportsAsBoxParagraphsNotBullets() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("- [ ] milk\n- [x] eggs\n", Base);
            var paras = doc.Blocks.OfType<Paragraph>().ToList();
            Assert.Equal(2, paras.Count);
            Assert.Empty(doc.Blocks.OfType<List>());
            Assert.Equal("☐ milk", Text(paras[0]));
            Assert.Equal("☑ eggs", Text(paras[1]));
        });

        [Fact]
        public void OrdinaryListsStillImportAsLists() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("- milk\n- [ ] eggs\n", Base);
            // A mixed list is not a task list, so it keeps its bullets and the marker rides
            // inside the second item as the glyph.
            var list = Assert.Single(doc.Blocks.OfType<List>());
            Assert.Equal(2, list.ListItems.Count);
        });

        [Fact]
        public void BoxParagraphsExportAsTaskListSyntax() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run("☐ milk")));
            doc.Blocks.Add(new Paragraph(new Run("☑ eggs")));
            string md = MarkdownConvert.FromDocument(doc);
            Assert.DoesNotContain("☐", md);
            Assert.DoesNotContain("☑", md);
            Assert.Contains("- [ ] milk", md);
            Assert.Contains("- [x] eggs", md);
        });

        [Fact]
        public void ABulletedBoxLineExportsWithOneMarker() => Sta.Run(() =>
        {
            var list = new List { MarkerStyle = System.Windows.TextMarkerStyle.Disc };
            list.ListItems.Add(new ListItem(new Paragraph(new Run("☐ milk"))));
            var doc = new FlowDocument();
            doc.Blocks.Add(list);
            string md = MarkdownConvert.FromDocument(doc);
            Assert.Contains("- [ ] milk", md);
            Assert.DoesNotContain("- - ", md);
        });

        [Fact]
        public void ChecklistSurvivesARoundTrip() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("- [ ] milk\n- [x] eggs\n", Base);
            string md = MarkdownConvert.FromDocument(doc);
            var again = MarkdownConvert.ToDocument(md, Base);
            var texts = again.Blocks.OfType<Paragraph>().Select(Text).ToList();
            Assert.Equal(["☐ milk", "☑ eggs"], texts);
        });

        private static string Text(Paragraph p) => new TextRange(p.ContentStart, p.ContentEnd).Text;
    }
}
