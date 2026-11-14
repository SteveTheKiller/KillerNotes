using System.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class GroupBranchUndoReleaseTests
    {
        [Fact]
        public void UndoDeleteRestoresBranchOrderColorsCollapseAndMemberships()
        {
            using var store = new TempStore();
            string child = NoteStore.GroupPath("Work", "Child");
            NoteStore.AddGroup("Before");
            NoteStore.AddGroup("Work");
            NoteStore.AddGroup(child, "Work");
            NoteStore.AddGroup("After");
            NoteStore.SetGroupOrders(new[] { ("Before", 1), ("Work", 2), (child, 3), ("After", 4) });
            NoteStore.SetGroupColor("Work", "#112233");
            NoteStore.SetGroupColor(child, "#445566");
            NoteStore.SetGroupCollapsed(child, true);
            long parentNote = NoteStore.Create("Parent note"), childNote = NoteStore.Create("Child note");
            NoteStore.SetNoteGroup(parentNote, "Work");
            NoteStore.SetNoteGroup(childNote, child);
            var expected = NoteStore.ListGroupTree();

            var undo = NoteStore.DeleteGroupWithUndo("Work");
            Assert.DoesNotContain(NoteStore.ListGroupTree(), g => g.Path == "Work");
            Assert.All(NoteStore.List(), n => Assert.Equal("", n.Notebook));
            Assert.NotNull(undo);
            undo!();

            Assert.Equal(expected, NoteStore.ListGroupTree());
            Assert.Equal("Work", NoteStore.List().Single(n => n.Id == parentNote).Notebook);
            Assert.Equal(child, NoteStore.List().Single(n => n.Id == childNote).Notebook);
        }

        [Fact]
        public void DeleteUndoRefusesRecreatedPathsWithoutChangingGroupsOrNotes()
        {
            using var store = new TempStore();
            NoteStore.AddGroup("Work");
            long original = NoteStore.Create("Original");
            NoteStore.SetNoteGroup(original, "Work");
            var undo = NoteStore.DeleteGroupWithUndo("Work");
            NoteStore.AddGroup("Work");
            NoteStore.SetGroupColor("Work", "#ABCDEF");
            long recreated = NoteStore.Create("Recreated");
            NoteStore.SetNoteGroup(recreated, "Work");
            var before = NoteStore.ListGroupTree();

            var conflict = Assert.Throws<GroupUndoConflictException>(() => undo!());

            Assert.Equal("Work", conflict.Path);
            Assert.Equal(before, NoteStore.ListGroupTree());
            Assert.Equal("", NoteStore.List().Single(n => n.Id == original).Notebook);
            Assert.Equal("Work", NoteStore.List().Single(n => n.Id == recreated).Notebook);
            NoteStore.DeleteGroup("Work");
            undo!();
            Assert.Equal("Work", NoteStore.List().Single(n => n.Id == original).Notebook);
            Assert.Equal("", NoteStore.List().Single(n => n.Id == recreated).Notebook);
        }
        [Fact]
        public void DeleteUndoRestoresSameNotepadAfterPasswordChange()
        {
            using var store = new TempStore();
            NoteStore.AddGroup("Work");
            long id = NoteStore.Create("Original");
            NoteStore.SetNoteGroup(id, "Work");
            var undo = NoteStore.DeleteGroupWithUndo("Work");
            NoteStore.SetPassword("undo-test-password");

            undo!();

            Assert.Contains(NoteStore.ListGroupTree(), group => group.Path == "Work");
            Assert.Equal("Work", NoteStore.List().Single(note => note.Id == id).Notebook);
        }
        [Fact]
        public void DeleteUndoRefusesClosedStoreAndCanRetry()
        {
            using var store = new TempStore();
            NoteStore.AddGroup("Work");
            var undo = NoteStore.DeleteGroupWithUndo("Work");
            NoteStore.Close();

            Assert.Throws<GroupUndoUnavailableException>(() => undo!());

            NoteStore.Open();
            undo!();
            Assert.Contains(NoteStore.ListGroupTree(), group => group.Path == "Work");
        }
    }
}
