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
            var preview = (MenuItem)window.FindName("PreviewMenuItem");
            Assert.Equal(supported ? Visibility.Visible : Visibility.Collapsed, preview.Visibility);
            WithEditor(editor, () =>
            {
                Open(window, editor, image);
                Assert.Equal(Visibility.Collapsed, preview.Visibility);
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
                Assert.Equal(Visibility.Visible, ((MenuItem)window.FindName("RecSlot")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((MenuItem)window.FindName("DocToggleSlot")).Visibility);
                Assert.Equal(1, ((Border)((MenuItem)window.FindName("EditorQuickSlot")).Header).BorderThickness.Bottom);
                Assert.Equal(0, ((Border)((MenuItem)window.FindName("RecSlot")).Header).BorderThickness.Bottom);
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
                Assert.Equal(Visibility.Visible, ((Button)window.FindName("EditorLinkBtn")).Visibility);
                Assert.Equal(Visibility.Visible, ((Button)window.FindName("EditorListBtn")).Visibility);
                Assert.Equal(Visibility.Visible, ((MenuItem)window.FindName("DocToggleSlot")).Visibility);
                Assert.Equal(table ? Visibility.Visible : Visibility.Collapsed, ((MenuItem)window.FindName("TableSlot")).Visibility);
                Assert.Equal(table ? Visibility.Visible : Visibility.Collapsed, ((MenuItem)window.FindName("TblDeleteMenuItem")).Visibility);
                Assert.Equal(1, ((Border)((MenuItem)window.FindName("EditorQuickSlot")).Header).BorderThickness.Bottom);
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
                Assert.Equal(Visibility.Collapsed, ((MenuItem)window.FindName("DocToggleSlot")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((MenuItem)window.FindName("RecSlot")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((MenuItem)window.FindName("TableSlot")).Visibility);
                var strip = (Border)((MenuItem)window.FindName("EditorQuickSlot")).Header;
                Assert.Equal(0, strip.BorderThickness.Bottom);
            });
        });

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
                    ((MenuItem)window.FindName("TableSlot")).Visibility == Visibility.Visible ? "table" : "text";
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
