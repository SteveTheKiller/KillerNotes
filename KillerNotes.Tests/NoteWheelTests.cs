using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// One wheel notch in a note used to scroll a fixed 120 px, whatever Windows' "lines to
    /// scroll" setting said, so a long note or a tall screenshot took far too much spinning.
    /// </summary>
    public sealed class NoteWheelTests
    {
        [Fact]
        public void ANotchAtTheWindowsDefaultScrollsTwiceWhatItUsedTo() =>
            Assert.Equal(240, MainWindow.NoteWheelStep(120, 3, 800, 1.0), 3);

        [Fact]
        public void TheWindowsLinesSettingScalesIt() =>
            Assert.Equal(480, MainWindow.NoteWheelStep(120, 6, 800, 1.0), 3);

        [Fact]
        public void OneScreenAtATimeScrollsAViewportLessALine() =>
            Assert.Equal(720, MainWindow.NoteWheelStep(120, -1, 800, 1.0), 3);

        [Fact]
        public void ANotchCoversTheSameDistanceOnScreenAtAnyZoom()
        {
            // The editor zooms with a LayoutTransform, so its offsets are unzoomed units.
            Assert.Equal(240, MainWindow.NoteWheelStep(120, 3, 800, 2.0) * 2.0, 3);
            Assert.Equal(-240, MainWindow.NoteWheelStep(-120, 3, 800, 0.5) * 0.5, 3);
        }

        [Fact]
        public void AFinerWheelScrollsInProportion() =>
            Assert.Equal(60, MainWindow.NoteWheelStep(30, 3, 800, 1.0), 3);
    }
}
