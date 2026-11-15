using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    // Alt+B docks or floats the format bar, as it does in KillerPDF. Backup now lives on Shift+F6.
    [Collection("Application resources")]
    public sealed class ToolMenuShortcutTests
    {
        private static object[] Rows(string keys)
        {
            var table = (Array)typeof(MainWindow).GetField("KsTable", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            return table.Cast<object>().Where(r => (string)r.GetType().GetProperty("Keys")!.GetValue(r)! == keys).ToArray();
        }

        private static string Label(object row) => (string)row.GetType().GetProperty("Label")!.GetValue(row)!;

        [Fact]
        public void AltBDocksOrFloatsTheFormatBarAndShiftF6BacksUp()
        {
            Assert.DoesNotContain(Rows("Alt+B"), r => Label(r) == "Str_KS_BackupNow");
            var alt = Rows("Alt+B").Single(r => Label(r) == "Str_KS_ToolMenu");
            var caps = (Array)alt.GetType().GetProperty("Caps")!.GetValue(alt)!;
            var cap = caps.Cast<object>().Single();
            Assert.Equal("Alt", cap.GetType().GetField("Item1")!.GetValue(cap)!.ToString());
            Assert.Equal("B", cap.GetType().GetField("Item2")!.GetValue(cap));
            Assert.Equal("Str_KS_BackupNow", Label(Rows("Shift+F6").Single()));
        }

        [Fact]
        public void ThePlacementRowAdvertisesAltB() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var item = (MenuItem)typeof(MainWindow).GetMethod("FormatBarPlacementItem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            Assert.Equal("Alt+B", item.InputGestureText);
        });
    }
}
