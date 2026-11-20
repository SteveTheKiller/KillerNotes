using System.Linq;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class MarkdownAuthoringTests
    {
        [Fact]
        public void MarkdownRejectsUnderlineAndReadOnlyEditorsRejectSourceEdits() => Sta.Run(() =>
        {
            var editor = new RichTextBox();
            MarkdownEditing.Attach(editor,()=>true);
            editor.Document.Blocks.Clear(); MarkdownBlob.Fill(editor.Document,"words"); editor.SelectAll();
            Assert.False(EditingCommands.ToggleUnderline.CanExecute(null,editor));
            EditingCommands.ToggleUnderline.Execute(null,editor);
            var decorations = editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            Assert.True(decorations == null || decorations.Count == 0);
            editor.IsReadOnly = true;
            Assert.False(EditingCommands.ToggleBold.CanExecute(null,editor));
            MarkdownEditing.ToggleInline(editor,"**"); MarkdownEditing.ToggleList(editor,false);
            Assert.Equal("words",MarkdownBlob.TextOf(editor.Document));
        });

        [Fact]
        public void DetectedMarkdownUsesSourceToolsWithoutConvertingItsStorageFormat() => CardColorBehaviorTests.WithShell((window,_) =>
        {
            Ready(window);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor=(RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear(); MarkdownBlob.Fill(editor.Document,"# Title\nwords");
            var detection=(System.Windows.Controls.Primitives.ToggleButton)window.FindName("AboutDetectMarkdown");
            detection.IsChecked=false; detection.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            detection.IsChecked=true; detection.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Collapsed,((Button)window.FindName("FmtUnderlineBtn")).Visibility);
            editor.CaretPosition=editor.Document.Blocks.LastBlock.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            ((Button)window.FindName("FmtHeadingBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("# Title\n# words",MarkdownBlob.TextOf(editor.Document));
            Assert.Equal(0,((KillerNotes.Models.Note)((ListBox)window.FindName("NotesList")).SelectedItem).Format);
        });

        [Fact]
        public void InlineCodePreservesBackticksInsideTheCode() => Sta.Run(() =>
        {
            var editor = new RichTextBox();
            editor.Document.Blocks.Clear(); MarkdownBlob.Fill(editor.Document,"a `tick` word"); editor.SelectAll();
            MarkdownEditing.ToggleInline(editor,"`");
            string source = MarkdownBlob.TextOf(editor.Document);
            Assert.Equal("``a `tick` word``",source);
            var rendered = MarkdownConvert.ToDocument(source,13);
            Assert.Equal("a `tick` word",new TextRange(rendered.ContentStart,rendered.ContentEnd).Text.TrimEnd('\r','\n'));
        });

        [Fact]
        public void BoldWritesMarkdownAndUndoRestoresTheSource() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            MarkdownBlob.Fill(editor.Document, "selected words");
            editor.SelectAll();
            editor.IsUndoEnabled = false;
            editor.IsUndoEnabled = true;
            EditingCommands.ToggleBold.Execute(null, editor);
            Assert.True(editor.CanUndo);
            Assert.Equal("**selected words**", MarkdownBlob.TextOf(editor.Document));
            Assert.Equal("**selected words**", MarkdownBlob.Decode(MarkdownBlob.Encode(MarkdownBlob.TextOf(editor.Document))));
            editor.Undo();
            Assert.Equal("selected words", MarkdownBlob.TextOf(editor.Document));
        });

        [Fact]
        public void MarkdownToolbarInsertsAPipeTableAsOneUndoableTextEdit() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            Assert.Equal(Visibility.Visible, ((Border)window.FindName("FormatBar")).Visibility);
            foreach (string name in new[] { "FmtUnderlineBtn", "FontSizeBtn", "TextColorBtn", "FmtImageBtn" })
                Assert.True(((UIElement)window.FindName(name)).Visibility == Visibility.Collapsed,
                    name + ": " + ((UIElement)window.FindName(name)).Visibility + "; format: "
                    + (((ListBox)window.FindName("NotesList")).SelectedItem as KillerNotes.Models.Note)?.Format
                    + "; status: " + ((TextBlock)window.FindName("StatusText")).Text);
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            MarkdownBlob.Fill(editor.Document, "Existing text");
            editor.SelectAll();
            editor.IsUndoEnabled = false;
            editor.IsUndoEnabled = true;
            ((Button)window.FindName("TableBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            ((TextBox)window.FindName("TblColsBox")).Text = "2";
            ((TextBox)window.FindName("TblRowsBox")).Text = "2";
            var popup = (System.Windows.Controls.Primitives.Popup)window.FindName("TableSizePopup");
            var panel = (StackPanel)((Border)popup.Child).Child;
            panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string source = MarkdownBlob.TextOf(editor.Document);
            Assert.Contains("Existing text\n\n|", source);
            Assert.Contains("| --- | --- |", source);
            Assert.Empty(editor.Document.Blocks.OfType<Table>());
            Assert.Equal(source, MarkdownBlob.Decode(MarkdownBlob.Encode(source)));
            editor.Undo();
            Assert.Equal("Existing text", MarkdownBlob.TextOf(editor.Document));
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MarkdownLinkToolWritesTheLabelAndEscapedDestinationIntoTheSource(bool afterLineBreak) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            string prefix = afterLineBreak ? "prefix " + char.ConvertFromUtf32(0x1F600) + "\n" : "";
            if (afterLineBreak)
            {
                var words = new Run("selected [words]");
                editor.Document.Blocks.Add(new Paragraph { Inlines = { new Run(prefix.TrimEnd('\n')),new LineBreak(),words } });
                editor.Selection.Select(words.ContentStart,words.ContentEnd);
            }
            else { MarkdownBlob.Fill(editor.Document,"selected [words]"); editor.SelectAll(); }
            var existing = Application.Current.Windows.Cast<Window>().ToArray();
            bool opened = false;
            string destination = "https://example.test/a(b)";
            string? initial = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<KillerNotes.Controls.InputDialog>()
                    .FirstOrDefault(d => d.IsVisible && !existing.Contains(d));
                if (dialog == null)
                {
                    if (DateTime.UtcNow <= deadline) return;
                    timer.Stop();
                    foreach (var pending in Application.Current.Windows.Cast<Window>().Where(w => !existing.Contains(w)).ToArray()) pending.Close();
                    return;
                }
                timer.Stop();
                opened = dialog.Owner == window;
                initial = ((TextBox)dialog.FindName("ValueBox")).Text;
                ((TextBox)dialog.FindName("ValueBox")).Text = destination;
                ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            try
            {
                timer.Start();
                ((Button)window.FindName("FmtLinkBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(opened);
                string source = MarkdownBlob.TextOf(editor.Document);
                Assert.Equal(prefix+"[selected \\[words\\]](https://example.test/a%28b%29)",source);
                Assert.Equal(source,MarkdownBlob.Decode(MarkdownBlob.Encode(source)));
                Assert.Empty(((Paragraph)editor.Document.Blocks.FirstBlock).Inlines.OfType<Hyperlink>());
                editor.CaretPosition = editor.Document.Blocks.FirstBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
                    .GetPositionAtOffset(-10,LogicalDirection.Backward);
                destination = "https://example.test/changed";
                timer.Start();
                ((Button)window.FindName("FmtLinkBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("https://example.test/a%28b%29",initial);
                Assert.Equal(prefix+"[selected \\[words\\]](https://example.test/changed)",MarkdownBlob.TextOf(editor.Document));
            }
            finally { timer.Stop(); }
        });

        [Theory]
        [InlineData("FmtStrikeBtn", "~~words~~")]
        [InlineData("FmtMonoBtn", "`words`")]
        [InlineData("FmtRuleBtn", "words\n\n---\n\n")]
        public void TextToolsWriteMarkdownSyntax(string name, string expected) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            MarkdownBlob.Fill(editor.Document,"words");
            if (name == "FmtRuleBtn") editor.CaretPosition = editor.Document.ContentEnd;
            else editor.SelectAll();
            editor.IsUndoEnabled = false; editor.IsUndoEnabled = true;
            ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(expected,MarkdownBlob.TextOf(editor.Document));
            editor.Undo();
            Assert.Equal("words",MarkdownBlob.TextOf(editor.Document));
        });

        [Fact]
        public void MarkdownContextToolsFormatSourceAndStayOutOfRichTextCards() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear(); MarkdownBlob.Fill(editor.Document,"words"); editor.SelectAll();
            Open();
            // 1.7.1: the markdown tools are tiles on the editor card, on a markdown note only.
            var card = (ContextMenu)editor.ContextMenu;
            var bold = CardTileHelper.Tile(card, "bold");
            Assert.Equal("Ctrl+B", System.Windows.Automation.AutomationProperties.GetAcceleratorKey(bold));
            CardTileHelper.Click(bold);
            Assert.False(editor.ContextMenu.IsOpen);
            Assert.Equal("**words**",MarkdownBlob.TextOf(editor.Document));
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Open();
            Assert.Null(Shell.NoteCards.Tile(card, "bold"));
            editor.ContextMenu.IsOpen=false;
            Assert.Equal(Visibility.Visible,((Button)window.FindName("FmtUnderlineBtn")).Visibility);

            void Open()
            {
                editor.Focus();
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(editor),0,Key.Apps)
                    {RoutedEvent=Keyboard.KeyUpEvent,Source=editor});
                window.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ContextIdle);
                Assert.True(editor.ContextMenu.IsOpen);
            }
        });

        [Fact]
        public void ViewDropdownKeepsKeyboardFocusWhileChoosingAMode() => CardColorBehaviorTests.WithShell((window,_) =>
        {
            Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            var picker=(ComboBox)window.FindName("PreviewModeBtn");
            picker.Focus(); picker.IsDropDownOpen=true;
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(picker),0,Key.Down)
                {RoutedEvent=Keyboard.KeyDownEvent,Source=picker});
            window.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.True(picker.IsDropDownOpen);
            Assert.True(picker.IsKeyboardFocusWithin);
            picker.IsDropDownOpen=false;
        });

        internal static void Ready(Window window)
        {
            bool demoFresh = KillerNotes.Shell.MainWindow.DemoFresh;
            KillerNotes.Shell.MainWindow.DemoFresh = false;
            window.Show();
            var frame = new System.Windows.Threading.DispatcherFrame();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            bool ready = false;
            timer.Tick += (_, _) =>
            {
                ready = window.IsLoaded && NoteStore.IsOpen
                    && !((Panel)window.FindName("FrameHost")).Children.OfType<Border>().Any(b => Panel.GetZIndex(b) == 10050);
                if (ready || elapsed.Elapsed.TotalSeconds > 5) { timer.Stop(); frame.Continue = false; }
            };
            timer.Start();
            try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); KillerNotes.Shell.MainWindow.DemoFresh = demoFresh; }
            Assert.True(ready, "The shell must finish opening its database before the test creates a note.");
        }
    }
}
