using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class EditorCardContextTests
    {
        [Fact]
        public void KeyboardMenuUsesTheCurrentTextSelectionAfterAnImageMenuCloses() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var editor = (RichTextBox)window.FindName("Editor");
            var image = Picture();
            var words = new Paragraph(new Run("Selected words"));
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new InlineUIContainer(image)));
            editor.Document.Blocks.Add(words);
            WithEditor(editor, () =>
            {
                Open(window, editor, image);
                Close(editor);
                editor.Selection.Select(words.ContentStart, words.ContentEnd);
                editor.Focus();
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Apps)
                    { RoutedEvent = Keyboard.KeyUpEvent, Source = editor });
                editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                Assert.True(editor.ContextMenu.IsOpen);
                Assert.True(Has(window, "wrap"));
                Assert.True(Has(window, "list"));
                Assert.False(Has(window, "play"));
            });
        });

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ClosingAnImageCardPreservesTheNotesPreviewAvailability(bool supported) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = (RichTextBox)window.FindName("Editor");
            var image = Picture();
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new Run(supported ? "# Heading" : "Plain text")));
            editor.Document.Blocks.Add(new Paragraph(new InlineUIContainer(image)));
            var detection = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("AboutDetectMarkdown");
            detection.IsChecked = false;
            detection.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            detection.IsChecked = true;
            detection.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var preview = CardTileHelper.Field<MenuItem>(window, "PreviewMenuItem");
            Assert.Equal(supported ? Visibility.Visible : Visibility.Collapsed, preview.Visibility);
            WithEditor(editor, () =>
            {
                Open(window, editor, image);
                // 1.7.1: an image's card carries no Preview tile; the note's own Preview gate is untouched.
                Assert.False(Has(window, "preview"));
                Close(editor);
                Assert.Equal(supported ? Visibility.Visible : Visibility.Collapsed, preview.Visibility);
            });
        });

        [Fact]
        public void RecordingCardSeparatesClipboardFromRecordingWithoutATrailingRule() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var editor = (RichTextBox)window.FindName("Editor");
            var chip = new Border { Child = new Grid(), Width = 160, Height = 32,
                Tag = new Shell.RecordingRef("test-recording", 1000).ToTag() };
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new InlineUIContainer(chip)));
            WithEditor(editor, () =>
            {
                Open(window, editor, chip);
                Assert.True(Has(window, "play"));
                Assert.True(Has(window, "copyrec"));
                Assert.True(Has(window, "copy"));
                Assert.False(Has(window, "wrap"));
            });
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TextAndTableCardsKeepTheirApplicableActions(bool table) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            var words = new Paragraph(new Run("Selected words"));
            if (table)
            {
                var cell = new TableCell(words);
                var row = new TableRow(); row.Cells.Add(cell);
                var group = new TableRowGroup(); group.Rows.Add(row);
                var content = new Table(); content.RowGroups.Add(group);
                editor.Document.Blocks.Add(content);
            }
            else editor.Document.Blocks.Add(words);
            editor.Selection.Select(words.ContentStart, words.ContentEnd);
            WithEditor(editor, () =>
            {
                Open(window, editor, editor);
                Assert.True(Has(window, "link"));
                Assert.True(Has(window, "list"));
                Assert.True(Has(window, "wrap"));
                Assert.Equal(table, Has(window, "rowabove"));
                Assert.Equal(table, Has(window, "deltable"));
            });
        });

        private static Image Picture() => new() { Width = 32, Height = 32,
            Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4) };

        [Fact]
        public void ImageCardContainsOnlyClipboardActionsAndHasNoTrailingRule() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var editor = (RichTextBox)window.FindName("Editor");
            var image = new Image { Width = 32, Height = 32,
                Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4) };
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new InlineUIContainer(image)));
            WithEditor(editor, () =>
            {
                Open(window, editor, image);
                Assert.Equal(["cut", "copy", "paste"], Shell.NoteCards.Tiles(Card(window)).Select(b => b.Uid));
            });
        });

        private static ContextMenu Card(Shell.MainWindow window) => (ContextMenu)window.FindName("EditorCard");

        /// <summary>Whether the open editor card carries the tile with this Id (1.7.1 tile card).</summary>
        private static bool Has(Shell.MainWindow window, string id) => Shell.NoteCards.Tile(Card(window), id) != null;

        private static void WithEditor(RichTextBox editor, Action body)
        {
            var parent = (Decorator)editor.Parent;
            parent.Child = null;
            var target = new Window { Content = editor, Width = 500, Height = 250, ShowInTaskbar = false };
            try
            {
                target.Show();
                editor.Focus();
                editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                body();
            }
            finally
            {
                Close(editor);
                target.Content = null;
                target.Close();
                parent.Child = editor;
            }
        }

        private static void Open(Shell.MainWindow window, RichTextBox editor, UIElement source)
        {
            editor.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = source });
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Apps)
                { RoutedEvent = Keyboard.KeyUpEvent, Source = editor });
            editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(((ContextMenu)window.FindName("EditorCard")).IsOpen, "The real editor card must open before inspecting its context.");
            if (Environment.GetEnvironmentVariable("KN_UI_CAPTURE_DIR") is string directory && directory.Length > 0)
            {
                var card = editor.ContextMenu;
                card.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(card);
                Directory.CreateDirectory(directory);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string context = source is Image ? "image" : source is Border ? "recording" :
                    Has(window, "rowabove") ? "table" : "text";
                using var file = File.Create(Path.Combine(directory, context + ".png"));
                encoder.Save(file);
            }
        }

        private static void Close(RichTextBox editor)
        {
            if (!editor.ContextMenu.IsOpen) return;
            bool closed = false;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            RoutedEventHandler done = (_, _) => { closed = true; frame.Continue = false; };
            editor.ContextMenu.Closed += done;
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            try
            {
                editor.ContextMenu.IsOpen = false;
                if (!closed) Dispatcher.PushFrame(frame);
                Assert.True(closed, "The editor card must finish closing before checking restored state.");
            }
            finally { timer.Stop(); editor.ContextMenu.Closed -= done; }
        }
    }
}
