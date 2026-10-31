using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using KillerNotes.Features.Security;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    // The Databases button works on the locked screen: another notepad can be opened without the
    // locked one's password, and anything short of a successful open leaves the lock in place.
    [Collection(NoteStoreCollection.Name)]
    public class LockedDatabaseSwitchTests
    {
        [Fact]
        public void PickingAnotherDatabaseOpensItAndLeavesTheLockedScreen()
        {
            using var store = new TempStore("secret");
            var (controller, host) = Locked();

            Assert.True(controller.ApplyLockedSelection("other.db"));

            Assert.False(controller.IsLocked);
            Assert.True(NoteStore.IsOpen);
            Assert.False(NoteStore.HasPassword);
            Assert.Equal("other.db", NoteStore.ActiveDbFile);
            Assert.Equal(new[] { "enter:False", "load", "leave" }, host.Events);
        }

        [Fact]
        public void ClosingTheDialogWithoutAPickStaysLocked()
        {
            using var store = new TempStore("secret");
            var (controller, host) = Locked();

            Assert.False(controller.ApplyLockedSelection(null));

            Assert.True(controller.IsLocked);
            Assert.False(NoteStore.IsOpen);
            Assert.Equal("test.db", NoteStore.ActiveDbFile);
            Assert.Equal(new[] { "enter:False" }, host.Events);
        }

        [Fact]
        public void ADatabaseThatWillNotOpenLeavesTheLockOnTheOldOne()
        {
            using var store = new TempStore("secret");
            var (controller, host) = Locked();
            Directory.CreateDirectory(Path.Combine(NoteStore.DbDir, "broken.db"));

            Assert.False(controller.ApplyLockedSelection("broken.db"));

            Assert.True(controller.IsLocked);
            Assert.False(NoteStore.IsOpen);
            Assert.Equal("test.db", NoteStore.ActiveDbFile);
            Assert.StartsWith("Str_St_OpenDbFailed", host.Status);
        }

        [Fact]
        public void AnOpenThatFailsPutsTheLockedScreenUpInsteadOfLeavingNothingOpen()
        {
            using var store = new TempStore();
            NoteStore.Close();
            Directory.CreateDirectory(Path.Combine(NoteStore.DbDir, "broken.db"));
            NoteStore.DemoDbFile = "broken.db";
            var host = new Host();
            var controller = new SecurityController(host);

            Assert.False(controller.Open(exitOnCancel: false));

            // The notes on screen belonged to a database that is closed now; Unlock and Manage
            // databases are what the locked screen offers from here.
            Assert.True(controller.IsLocked);
            Assert.False(NoteStore.IsOpen);
            Assert.Equal(new[] { "enter:False" }, host.Events);
            Assert.StartsWith("Str_St_OpenDbFailed", host.Status);
        }

        [Fact]
        public void ASwitchThatFailsKeepsTheScreenForTheFallbackOpen()
        {
            using var store = new TempStore();
            NoteStore.Close();
            Directory.CreateDirectory(Path.Combine(NoteStore.DbDir, "broken.db"));
            NoteStore.DemoDbFile = "broken.db";
            var host = new Host();
            var controller = new SecurityController(host);

            Assert.False(controller.Open(exitOnCancel: false, lockOnFail: false));

            // Nothing on screen has been closed or cleared yet: the caller is about to reopen the
            // notepad it came from, and the locked screen would only be a flash and a lost session.
            Assert.False(controller.IsLocked);
            Assert.Empty(host.Events);

            NoteStore.DemoDbFile = "test.db";
            Assert.True(controller.Open(exitOnCancel: false));
            Assert.False(controller.IsLocked);
            Assert.Equal(new[] { "load" }, host.Events);
        }

        [Fact]
        public void ManageDatabasesWithNothingOpenDoesNotReadTheClosedStore()
        {
            using var store = new TempStore();
            NoteStore.Close();
            var host = new Host();
            var controller = new SecurityController(host);

            controller.ShowDatabases();

            Assert.True(controller.IsLocked);
            Assert.Equal(new[] { "enter:False" }, host.Events);
        }

        /// <summary>test.db open with a password and locked, other.db beside it with none.</summary>
        private static (SecurityController, Host) Locked()
        {
            NoteStore.Close();
            NoteStore.DemoDbFile = "other.db";
            NoteStore.Open();
            NoteStore.Close();
            NoteStore.DemoDbFile = "test.db";
            NoteStore.Open("secret");

            var host = new Host();
            var controller = new SecurityController(host) { SetActiveDatabase = name => NoteStore.DemoDbFile = name };
            Assert.True(controller.LockNow(automatic: false));
            return (controller, host);
        }

        private sealed class Host : ISecurityHost
        {
            public readonly List<string> Events = new();
            public string? Status;
            public Window Window => throw new InvalidOperationException("Unexpected dialog");
            public string Loc(string key) => key;
            public void SetStatus(string text) => Status = text;
            public void ShowLockState(bool encrypted) { }
            public bool SaveOpenNote() => true;
            public void LoadNotes() => Events.Add("load");
            public void ClearEditor() { }
            public void ApplyReadOnlyState() { }
            public void ClearNavigation() { }
            public void EnterLocked(bool keepUnsavedEdits) => Events.Add("enter:" + keepUnsavedEdits);
            public void LeaveLocked() => Events.Add("leave");
        }
    }
}
