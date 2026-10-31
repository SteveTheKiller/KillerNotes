using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A drag that loses mouse capture (a context menu, the SketchPad, alt-tab) and is released
    /// somewhere else never reaches the release handler. The next mouse move with the button up
    /// ends it, so the next ordinary click is not taken for the end of that drag.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class FloatDragTests
    {
        [Fact]
        public void AMoveWithTheButtonUpEndsADragThatLostItsRelease() => Sta.Run(() =>
        {
            var (shell, editor) = EditorShell.Make();
            var image = new Image { Source = BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8) };
            var floater = new Floater(new BlockUIContainer(image));
            var anchor = new Paragraph(floater);
            editor.Document = new FlowDocument(anchor);
            EditorShell.Set(shell, "_dragFloater", floater);
            EditorShell.Set(shell, "_dragObject", image);
            EditorShell.Set(shell, "_dragBlockFloor", 1);
            var move = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent };

            EditorShell.Invoke(shell, "Editor_FloatDragMove", editor, move);

            Assert.Null(EditorShell.Get(shell, "_dragFloater"));
            Assert.Null(EditorShell.Get(shell, "_dragObject"));
        });
    }
}
