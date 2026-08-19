using System;
using System.Collections.Generic;
using KillerNotes.Models;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Issue #13 rule: every Note property bound in the sidebar row template must notify,
    /// because SaveCurrentNote edits the displayed row IN PLACE and the RowSig reconcile
    /// then sees no diff - without a notification the row shows stale data until restart.
    /// </summary>
    public sealed class NoteModelTests
    {
        private static List<string> Track(Note n)
        {
            var fired = new List<string>();
            n.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");
            return fired;
        }

        [Fact]
        public void TitleNotifies()
        {
            var n = new Note();
            var fired = Track(n);
            n.Title = "renamed";
            Assert.Contains("Title", fired);
        }

        [Fact]
        public void SnippetNotifies()
        {
            var n = new Note();
            var fired = Track(n);
            n.Snippet = "first line";
            Assert.Contains("Snippet", fired);
        }

        [Fact]
        public void ModifiedNotifiesItselfAndItsDisplayForm()
        {
            var n = new Note();
            var fired = Track(n);
            n.Modified = new DateTime(2026, 8, 19, 9, 30, 0);
            Assert.Contains("Modified", fired);
            Assert.Contains("ModifiedDisplay", fired);   // the template binds the formatted string
            Assert.Equal("2026-08-19 09:30", n.ModifiedDisplay);
        }

        [Fact]
        public void TitleColorNotifiesEveryDerivedBinding()
        {
            var n = new Note();
            var fired = Track(n);
            n.TitleColor = "#B829FF";
            Assert.Contains("TitleColor", fired);
            Assert.Contains("HasTitleColor", fired);
            Assert.Contains("TitleBrush", fired);
            Assert.True(n.HasTitleColor);
        }

        [Fact]
        public void UnchangedValuesDoNotNotify()
        {
            var n = new Note { Title = "same" };
            var fired = Track(n);
            n.Title = "same";
            n.Snippet = "";
            n.TitleColor = "";
            Assert.Empty(fired);
        }
    }
}
