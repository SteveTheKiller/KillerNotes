using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A clicked image stays selected only until the user types or moves the caret. Backspace after
    /// that fixes a typo instead of deleting the picture that was clicked a while ago.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ImageSelectionKeyTests
    {
        [Fact]
        public void BackspaceAfterTypingDeletesACharacterNotTheImage() => Sta.Run(() =>
        {
            var (shell, editor, image) = Prepared();

            Press(shell, editor, Key.A);
            bool handled = Press(shell, editor, Key.Back);

            Assert.False(handled);
            Assert.Null(EditorShell.Get(shell, "_selImage"));
            Assert.Single(editor.Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<InlineUIContainer>());
        });

        [Fact]
        public void ArrowKeysDeselectTheImage() => Sta.Run(() =>
        {
            var (shell, editor, _) = Prepared();

            Press(shell, editor, Key.Left);

            Assert.Null(EditorShell.Get(shell, "_selImage"));
        });

        [Fact]
        public void HoldingAModifierKeepsTheImageSelected() => Sta.Run(() =>
        {
            var (shell, editor, image) = Prepared();

            Press(shell, editor, Key.LeftShift);

            Assert.Same(image, EditorShell.Get(shell, "_selImage"));
        });

        [Fact]
        public void BackspaceRightAfterClickingTheImageStillDeletesIt() => Sta.Run(() =>
        {
            var (shell, editor, _) = Prepared();

            bool handled = Press(shell, editor, Key.Back);

            Assert.True(handled);
            Assert.Empty(editor.Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<InlineUIContainer>());
        });

        private static (MainWindow, RichTextBox, Image) Prepared()
        {
            var (shell, editor) = EditorShell.Make();
            var image = new Image { Source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8) };
            editor.Document = new FlowDocument(new Paragraph(new Run("x")) { Inlines = { new InlineUIContainer(image) } });
            EditorShell.Set(shell, "_selImage", image);
            return (shell, editor, image);
        }

        private static bool Press(MainWindow shell, RichTextBox editor, Key key)
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, new NoSource(), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            EditorShell.Invoke(shell, "Editor_ImageDeleteKey", editor, e);
            return e.Handled;
        }

        /// <summary>The key event needs a source; nothing here is on screen.</summary>
        private sealed class NoSource : PresentationSource
        {
            public override bool IsDisposed => false;
            protected override CompositionTarget GetCompositionTargetCore() => null!;
            public override Visual RootVisual { get; set; } = null!;
        }
    }
}
