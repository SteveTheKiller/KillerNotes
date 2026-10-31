using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The sidebar can be filtered by a search, and its rows can be older copies of the notes
    /// the list holds. Commands that reach a note by id or by tag must work from the store and
    /// from the row on screen, not from whichever copy happens to be in the filtered list.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class NoteListScopeTests
    {
        [Fact]
        public void ATagShortcutBuildsOnTheTagsTheRowShowsNotAnOlderCopy() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.AddTag("alpha", "");
            NoteStore.AddTag("beta", "");
            TagManager.Refresh();
            int beta = TagManager.Order.Select(t => t.Name).ToList().IndexOf("beta");
            long id = NoteStore.Create("Plan");
            var older = NoteStore.GetNote(id)!;          // what the list kept from before the tag was added
            NoteStore.SetNoteTags(id, "alpha");
            var shown = NoteStore.GetNote(id)!;          // the row on screen, edited through the context menu
            var shell = new ShellProbe(currentId: id);
            shell.Set("_notes", new List<Note> { older });
            shell.Set("_sidebarItems", new ObservableCollection<object> { shown });

            shell.Window.ToggleTagByIndex(beta);

            Assert.Equal(new[] { "alpha", "beta" }, NoteStore.SplitTags(NoteStore.GetNote(id)!.Tags).OrderBy(t => t));
            Assert.Contains(shown.Chips, c => c.Name == "beta");
        });

        [Fact]
        public void FollowingALinkToANoteOutsideTheSearchClearsTheSearch() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.Create("Elsewhere");
            var shell = new ShellProbe();
            shell.Window.SearchBox.Text = "something else";

            shell.Run("FollowWikiLink", "Elsewhere");

            Assert.Equal("", shell.Window.SearchBox.Text);
        });

        [Fact]
        public void BackToANoteTheSearchHidesStillGoesBack() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Earlier");
            var shell = new ShellProbe();
            shell.Get<List<long>>("_navBack").Add(id);
            shell.Window.SearchBox.Text = "something else";

            shell.Run("NavBack");

            Assert.Equal("", shell.Status.Text);   // no "nothing to go back to"
            Assert.Equal("", shell.Window.SearchBox.Text);
            Assert.Empty(shell.Get<List<long>>("_navBack"));
        });

        [Fact]
        public void BackSkipsANoteThatWasDeleted() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Doomed");
            NoteStore.Trash(id);
            var shell = new ShellProbe();
            shell.Get<List<long>>("_navBack").Add(id);

            shell.Run("NavBack");

            Assert.NotEqual("", shell.Status.Text);   // "nothing to go back to"
            Assert.Empty(shell.Get<List<long>>("_navBack"));
        });
    }
}
