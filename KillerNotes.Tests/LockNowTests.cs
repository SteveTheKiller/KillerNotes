using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Features.Security;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public class LockNowTests
    {
        [Fact]
        public void LockClosesThePasswordDatabaseAndForgetsThePassword()
        {
            using var store = new TempStore("secret");
            var host = new Host();
            var controller = new SecurityController(host);
            Assert.True(controller.Open(false));
            host.Events.Clear();

            Assert.True(controller.LockNow(automatic: false));

            Assert.True(controller.IsLocked);
            Assert.False(NoteStore.IsOpen);
            Assert.Equal(new[] { "enter:False" }, host.Events);
            // A remembered password would reopen silently here; the forgotten one has to ask,
            // and this host has no window to ask with.
            Assert.False(controller.Open(false));
            Assert.False(NoteStore.IsOpen);
        }

        [Fact]
        public void LockNowNeedsAPassword()
        {
            using var store = new TempStore();
            var host = new Host();
            var controller = new SecurityController(host);

            Assert.False(controller.CanLockNow);
            Assert.False(controller.LockNow(automatic: true));
            Assert.True(NoteStore.IsOpen);
            Assert.Empty(host.Events);
        }

        [Fact]
        public void FailedSaveStopsAManualLock()
        {
            using var store = new TempStore("secret");
            var host = new Host { SaveResult = false };
            var controller = new SecurityController(host);

            Assert.False(controller.LockNow(automatic: false));

            Assert.False(controller.IsLocked);
            Assert.True(NoteStore.IsOpen);
            Assert.Equal("Str_St_LockSaveFailed", host.Status);
            Assert.Empty(host.Events);
        }

        [Fact]
        public void AutomaticLockKeepsUnsavedEditsWhenTheSaveFails()
        {
            using var store = new TempStore("secret");
            var host = new Host { SaveResult = false };
            var controller = new SecurityController(host);

            Assert.True(controller.LockNow(automatic: true));

            Assert.True(controller.IsLocked);
            Assert.False(NoteStore.IsOpen);
            Assert.Equal(new[] { "enter:True" }, host.Events);
        }

        [Fact]
        public void WrongPasswordStaysLockedAndTheRightOneReopens()
        {
            using var store = new TempStore("secret");
            var host = new Host();
            var controller = new SecurityController(host);
            controller.LockNow(automatic: false);

            Assert.False(controller.Unlock("wrong"));
            Assert.True(controller.IsLocked);
            Assert.False(NoteStore.IsOpen);

            Assert.True(controller.Unlock("secret"));
            Assert.False(controller.IsLocked);
            Assert.True(NoteStore.IsOpen);
            Assert.Equal(new[] { "enter:False", "load", "leave" }, host.Events);
            // The password is the session's again, so a database round trip stays silent.
            NoteStore.Close();
            Assert.True(controller.Open(false));
        }

        [Fact]
        public void McpRefusesWhileLocked() => Sta.Run(() =>
        {
            using var store = new TempStore("secret");
            var controller = new SecurityController(new Host());
            controller.LockNow(automatic: false);
            var shell = (KillerNotes.Shell.MainWindow)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(KillerNotes.Shell.MainWindow));
            var execute = typeof(KillerNotes.Shell.MainWindow).GetMethod("ExecuteMcpCommand",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(
                () => execute.Invoke(shell, new object[] { new[] { "list" } }));
            Assert.Contains("unlock", error.InnerException!.Message);
        });

        [Fact]
        public void HeldEditsComeBackOnlyToTheirOwnNoteAndDatabase() => Sta.Run(() =>
        {
            var editor = new FlowDocument(new Paragraph(new Run("unsaved words")));
            var held = LockedEdits.Capture(editor, 7, "Title typed", "a.db", 0);

            Assert.Empty(editor.Blocks);
            var other = new FlowDocument(new Paragraph(new Run("loaded from disk")));
            Assert.False(held.TryRestore(other, 8, "a.db"));
            Assert.False(held.TryRestore(other, 7, "b.db"));
            Assert.Equal("loaded from disk\r\n", new TextRange(other.ContentStart, other.ContentEnd).Text);

            Assert.True(held.TryRestore(other, 7, "A.DB"));
            Assert.Equal("unsaved words\r\n", new TextRange(other.ContentStart, other.ContentEnd).Text);
            Assert.Equal("Title typed", held.Title);
        });

        [Theory]
        [InlineData(0, 120, false)]
        [InlineData(5, 4, false)]
        [InlineData(5, 5, true)]
        [InlineData(15, 30, true)]
        public void IdleLockFiresOnlyWhenTurnedOnAndTheTimeIsUp(int minutes, int idleMinutes, bool expected)
            => Assert.Equal(expected, AutoLock.IdleDue(minutes, TimeSpan.FromMinutes(idleMinutes)));

        [Theory]
        [InlineData(null, 0)]
        [InlineData("15", 15)]
        [InlineData("7", 0)]
        [InlineData("junk", 0)]
        public void IdleSettingAcceptsOnlyTheOfferedChoices(string? stored, int expected)
            => Assert.Equal(expected, AutoLock.ParseIdleMinutes(stored));

        private sealed class Host : ISecurityHost
        {
            public readonly List<string> Events = new();
            public bool SaveResult = true;
            public string? Status;
            public Window Window => throw new InvalidOperationException("Unexpected dialog");
            public string Loc(string key) => key;
            public void SetStatus(string text) => Status = text;
            public void ShowLockState(bool encrypted) { }
            public bool SaveOpenNote() => SaveResult;
            public void LoadNotes() => Events.Add("load");
            public void ClearEditor() { }
            public void ApplyReadOnlyState() { }
            public void ClearNavigation() { }
            public void EnterLocked(bool keepUnsavedEdits) => Events.Add("enter:" + keepUnsavedEdits);
            public void LeaveLocked() => Events.Add("leave");
        }
    }
}
