using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A notepad another writer holds is read-only: the store ignores every write. The commands
    /// that organize notes must say so instead of confirming a change that never happened.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ReadOnlyCommandTests
    {
        /// <summary>Holds the open store read-only for the length of a test.</summary>
        private sealed class ReadOnlyScope : IDisposable
        {
            private static readonly MethodInfo Setter = typeof(NoteStore).GetProperty(nameof(NoteStore.IsReadOnly))!.GetSetMethod(nonPublic: true)!;
            private readonly bool _before = NoteStore.IsReadOnly;
            public ReadOnlyScope() => Setter.Invoke(null, new object[] { true });
            public void Dispose() => Setter.Invoke(null, new object[] { _before });
        }

        private static void AssertSaysReadOnly(string status) =>
            Assert.True(status.Contains("Str_St_ReadOnly") || status.Contains("Read-only"), status);

        [Fact]
        public void FilingNotesIntoAGroupSaysReadOnlyAndMovesNothing() => Sta.Run(() =>
        {
            using var store = new TempStore();
            var note = NoteStore.GetNote(NoteStore.Create("Plan"))!;
            using var _ = new ReadOnlyScope();
            var shell = new ShellProbe();

            shell.Run("AssignGroup", new List<Note> { note }, "Work");

            Assert.Equal("", note.Notebook);
            Assert.Equal(0, shell.UndoEntries);
            AssertSaysReadOnly(shell.Status.Text);
        });

        [Fact]
        public void PinningSaysReadOnlyAndPinsNothing() => Sta.Run(() =>
        {
            using var store = new TempStore();
            var note = NoteStore.GetNote(NoteStore.Create("Plan"))!;
            using var _ = new ReadOnlyScope();
            var shell = new ShellProbe();

            shell.Run("TogglePin", new List<Note> { note });

            Assert.False(note.Pinned);
            Assert.Equal(0, shell.UndoEntries);
            AssertSaysReadOnly(shell.Status.Text);
        });

        [Fact]
        public void RestoringFromTheTrashSaysReadOnlyAndRestoresNothing() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Gone");
            NoteStore.Trash(id);
            var trashed = NoteStore.GetNote(id)!;
            using var _ = new ReadOnlyScope();
            var shell = new ShellProbe();

            shell.Run("RestoreNotes", new List<Note> { trashed });

            Assert.Equal(0, shell.UndoEntries);
            AssertSaysReadOnly(shell.Status.Text);
        });

        [Fact]
        public void ANewNoteOnAReadOnlyNotepadLeavesTheOpenNoteAlone() => Sta.Run(() =>
        {
            using var store = new TempStore();
            using var _ = new ReadOnlyScope();
            var shell = new ShellProbe(currentId: 5);
            shell.Window.SearchBox.Text = "kept";

            shell.Run("CreateNewNote", true, 0);

            Assert.Equal(5L, shell.Get<long>("_currentId"));
            Assert.Equal("kept", shell.Window.SearchBox.Text);
            AssertSaysReadOnly(shell.Status.Text);
        });
    }
}
