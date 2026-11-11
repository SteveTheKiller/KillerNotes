using System.Linq;
using System.Windows;
using KillerNotes.Models;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class SidebarDensityTests
    {
        // A note with no tags renders no chip row, so each density level has to differ in
        // the rows it shows for an untagged note too, or two levels look identical.
        [Fact]
        public void EveryDensityLevelShowsADifferentRowCountForAnUntaggedNote()
        {
            var counts = Enumerable.Range(0, 3).Select(VisibleLinesWithoutTags).ToArray();

            Assert.True(counts[0] > counts[1] && counts[1] > counts[2],
                "Untagged row lines by density: " + string.Join(", ", counts));
        }

        [Fact]
        public void CompactShowsTagsInPlaceOfTheDateWhenTheNoteHasTags()
        {
            var tagged = new Note { Density = 1 };
            tagged.Chips.Add(new TagChip { Name = "work" });
            var untagged = new Note { Density = 1 };

            Assert.Equal(Visibility.Visible, tagged.ChipsVisibility);
            Assert.Equal(Visibility.Collapsed, tagged.DateVisibility);
            Assert.Equal(Visibility.Visible, untagged.DateVisibility);
            Assert.Equal(Visibility.Collapsed, untagged.SnippetVisibility);
        }

        [Fact]
        public void MinimalShowsTheTitleOnly()
        {
            var note = new Note { Density = 2 };

            Assert.Equal(Visibility.Collapsed, note.SnippetVisibility);
            Assert.Equal(Visibility.Collapsed, note.DateVisibility);
            Assert.Equal(Visibility.Collapsed, note.ChipsVisibility);
        }

        private static int VisibleLinesWithoutTags(int density)
        {
            var note = new Note { Density = density };
            int lines = 1;   // the title row is always shown
            if (note.SnippetVisibility == Visibility.Visible) lines++;
            if (note.DateVisibility == Visibility.Visible) lines++;
            return lines;
        }
    }
}
