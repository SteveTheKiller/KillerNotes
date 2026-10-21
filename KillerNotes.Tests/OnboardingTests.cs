using System.Linq;
using System.Windows.Input;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Onboarding (1.6.0, KN-P09): the F1 list's search runs what it finds by replaying its
    /// shortcut, and each tip is shown once until tips are turned off.
    /// </summary>
    public sealed class OnboardingTests
    {
        [Theory]
        [InlineData("Ctrl+N", ModifierKeys.Control, Key.N)]
        [InlineData("Ctrl+Shift+B", ModifierKeys.Control | ModifierKeys.Shift, Key.B)]
        [InlineData("Alt+Shift+F", ModifierKeys.Alt | ModifierKeys.Shift, Key.F)]
        [InlineData("F7 / Ctrl+Shift+D", ModifierKeys.None, Key.F7)]       // the first of two ways
        [InlineData("Alt+1 / 2 / 3", ModifierKeys.Alt, Key.D1)]
        [InlineData("Alt+Delete", ModifierKeys.Alt, Key.Delete)]
        [InlineData("Shift+F12", ModifierKeys.Shift, Key.F12)]
        [InlineData("Alt+Left", ModifierKeys.Alt, Key.Left)]
        [InlineData("Ctrl+Enter", ModifierKeys.Control, Key.Enter)]
        public void ShortcutsParseIntoAKeyToReplay(string keys, ModifierKeys modifiers, Key key)
        {
            Assert.True(CommandSearch.TryParseGesture(keys, out var parsedModifiers, out var parsedKey));
            Assert.Equal(modifiers, parsedModifiers);
            Assert.Equal(key, parsedKey);
        }

        [Theory]
        [InlineData("Home / End")]            // context keys: nothing to replay on their own
        [InlineData("Ctrl+Shift+> / <")]
        [InlineData("")]
        [InlineData("Ctrl+")]
        public void KeysThatAreNotOneGestureAreNotReplayed(string keys) =>
            Assert.False(CommandSearch.TryParseGesture(keys, out _, out _));

        [Fact]
        public void TheListSearchMatchesNamesAndKeysTheWaySearchDoes()
        {
            Assert.True(CommandSearch.Matches("Export the open note", "F8", "export"));
            Assert.True(CommandSearch.Matches("Export the open note", "F8", "f8"));
            Assert.True(CommandSearch.Matches("Exportér", "F8", "EXPORTÉR"));
            Assert.True(CommandSearch.Matches("Lock now", "Ctrl+Shift+U", "lock now"));
            Assert.False(CommandSearch.Matches("Lock now", "Ctrl+Shift+U", "export"));
            Assert.True(CommandSearch.Matches("Anything", "F1", "  "));
        }

        [Fact]
        public void EachTipIsShownOnceAndNeverWhenTipsAreOff()
        {
            var tips = new TipState(showTips: true, seen: "lock");
            Assert.False(tips.TakeTip("lock"));
            Assert.True(tips.TakeTip("backup"));
            Assert.False(tips.TakeTip("backup"));
            Assert.Equal(new[] { "backup", "lock" }, tips.Seen.Split(',').OrderBy(s => s));

            var off = new TipState(showTips: false, seen: "");
            Assert.False(off.TakeTip("export"));
            Assert.Equal("", off.Seen);
        }
    }
}
