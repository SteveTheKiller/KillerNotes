using System.IO;
using System.Windows;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Clicking through notes fast started drags nobody made. Each press remembered the row under
    /// it as a drag candidate and nothing forgot it on the release, so when the next fast click
    /// arrived WPF moved the pointer there with the button already down, the stale candidate was
    /// past the threshold, and a drag of the PREVIOUS note began. Windows then sat in its drag
    /// loop: right-click did nothing until another click dropped the note somewhere, reordering
    /// the list (reproduced on SN01, 2026-11-12, 15 ms clicks down a list of picture notes).
    /// </summary>
    public sealed class DragGateTests
    {
        private static readonly Point A = new(10, 10), B = new(10, 80);

        [Fact]
        public void APressThatWasReleasedNeverBecomesADrag()
        {
            var gate = new DragGate<string>();
            gate.Press("Note A", A);
            gate.Release();
            Assert.Null(gate.TryTake(B, pressed: true));
        }

        [Fact]
        public void AHeldPressPastTheThresholdIsADrag()
        {
            var gate = new DragGate<string>();
            gate.Press("Note A", A);
            Assert.Equal("Note A", gate.TryTake(B, pressed: true));
            Assert.Null(gate.TryTake(B, pressed: true));   // taken once, not twice
        }

        [Fact]
        public void ASmallMoveOrAnUnpressedMoveIsNotADrag()
        {
            var gate = new DragGate<string>();
            gate.Press("Note A", A);
            Assert.Null(gate.TryTake(new Point(11, 11), pressed: true));
            Assert.Null(gate.TryTake(B, pressed: false));
            Assert.Null(gate.TryTake(B, pressed: true));   // observing the release disarms it
        }

        [Fact]
        public void TheFactorWidensTheThreshold()
        {
            var gate = new DragGate<string>();
            gate.Press("Group", A);
            var justPast = new Point(A.X, A.Y + SystemParameters.MinimumVerticalDragDistance + 1);
            Assert.Null(gate.TryTake(justPast, pressed: true, factor: 2.5));
        }

        /// <summary>Every surface that drags from a press disarms on ANY release in its window,
        /// including releases a row or header handled first, so a release outside the row also
        /// counts.</summary>
        [Fact]
        public void TheSidebarAndTheGroupsWindowDisarmOnEveryRelease()
        {
            string root = Root();
            string main = File.ReadAllText(Path.Combine(root, "Shell", "Sharing.cs"));
            string manager = File.ReadAllText(Path.Combine(root, "Controls", "GroupManagerWindow.xaml.cs"));
            foreach (string src in new[] { main, manager })
            {
                Assert.Contains("DragGate<", src);
                Assert.Contains("MouseLeftButtonUpEvent", src);
                Assert.Contains("handledEventsToo: true", src);
            }
        }

        private static string Root()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) dir = dir.Parent;
            return dir!.FullName;
        }
    }
}
