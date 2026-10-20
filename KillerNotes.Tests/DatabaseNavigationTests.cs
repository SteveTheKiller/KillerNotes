using System.Collections.Generic;
using System.Windows;
using KillerNotes.Features.Security;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public class DatabaseNavigationTests
    {
        [Fact]
        public void FailedEditorSavePreventsDatabaseAndPasswordTransitions()
        {
            using var source = new TempStore();
            var host = new Host { SaveResult = false };
            var controller = new SecurityController(host);
            controller.ShowDatabases();
            Assert.True(NoteStore.IsOpen);
            controller.ToggleLock();
            Assert.True(NoteStore.IsOpen);
            Assert.Equal(2, host.SaveCalls);
        }

        [Fact]
        public void EverySuccessfulDatabaseChangeClearsHistoryButReopeningTheSameFileKeepsIt()
        {
            using var source = new TempStore();
            long firstId = NoteStore.Create("A");
            var host = new Host();
            var controller = new SecurityController(host);
            Assert.True(controller.Open(false));
            host.Back.Add(firstId);
            host.Forward.Add(firstId);
            Assert.True(controller.Open(false));
            Assert.Single(host.Back);
            Assert.Single(host.Forward);
            NoteStore.Close();
            NoteStore.DemoDbFile = "other.db";
            Assert.True(controller.Open(false));
            Assert.Equal(firstId, NoteStore.Create("B"));
            Assert.Empty(host.Back);
            Assert.Empty(host.Forward);
        }

        private sealed class Host : ISecurityHost
        {
            public readonly List<long> Back = new();
            public readonly List<long> Forward = new();
            public bool SaveResult = true;
            public int SaveCalls;
            public Window Window => throw new System.InvalidOperationException("Unexpected dialog");
            public string Loc(string key) => key;
            public void SetStatus(string text) { }
            public void ShowLockState(bool encrypted) { }
            public bool SaveOpenNote() { SaveCalls++; return SaveResult; }
            public void LoadNotes() { }
            public void ClearEditor() { }
            public void ApplyReadOnlyState() { }
            public void ClearNavigation() { Back.Clear(); Forward.Clear(); }
        }
    }
}
