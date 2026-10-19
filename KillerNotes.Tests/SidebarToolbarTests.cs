using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The sidebar toolbar (Sidebar.cs): the New note button steps down to a bare "+" first, and
    /// the sort buttons drop to their own row only when even the "+" cannot share the row.
    /// </summary>
    public sealed class SidebarToolbarTests
    {
        private const double Plus = 37, Sorts = 134;

        [Fact]
        public void The_plus_and_the_sorts_share_a_row_whenever_they_fit()
        {
            Assert.False(MainWindow.SidebarToolbarWraps(Plus + Sorts, Plus, Sorts));
            Assert.False(MainWindow.SidebarToolbarWraps(184, Plus, Sorts));   // a 232 right-hand sidebar
        }

        [Fact]
        public void The_sorts_wrap_only_when_the_plus_cannot_fit_beside_them()
        {
            Assert.True(MainWindow.SidebarToolbarWraps(Plus + Sorts - 1, Plus, Sorts));
        }
    }
}
