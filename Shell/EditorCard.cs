using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KillerNotes.Shell
{
    // The editor card (1.7.0): the editor's right-click menu in the note card's shape
    // (MainWindow.xaml EditorCard). Every control calls the handler its old menu row called, so
    // Ctrl+X, Alt+Shift+A, F4 and the rest still reach the same code. Wired here rather than in
    // XAML because a control has to close the card BEFORE it acts: Link, Edit recording and Table
    // style open dialogs, and a card left open under a modal dialog keeps its popup on screen.
    public partial class MainWindow
    {
        /// <summary>Called once from InitEditor (Editor.Float.cs), beside the menu's opening hooks.</summary>
        private void InitEditorCard()
        {
            Act(EditorCutBtn, () => ApplicationCommands.Cut.Execute(null, Editor));
            Act(EditorCopyBtn, () => EditorCopyMenuItem_Click(this, new RoutedEventArgs()));
            Act(EditorPasteBtn, () => ApplicationCommands.Paste.Execute(null, Editor));
            Act(EditorLinkBtn, () => LinkMenu_Click(this, new RoutedEventArgs()));
            Act(EditorListBtn, () => ConvertToList_Click(this, new RoutedEventArgs()));

            Act(RecPlayPauseBtn, () => RecPlayPause_Click(this, new RoutedEventArgs()));
            // Volume steps keep the card open, as the old rows did, so a few clicks walk the level.
            RecVolumeUpBtn.Click += (_, _) => RecVolumeUp_Click(this, new RoutedEventArgs());
            RecVolumeDownBtn.Click += (_, _) => RecVolumeDown_Click(this, new RoutedEventArgs());
            Act(RecEditBtn, () => RecEdit_Click(this, new RoutedEventArgs()));
            Act(RecSaveAsBtn, () => RecSaveAs_Click(this, new RoutedEventArgs()));
            Act(RecCopyFileBtn, () => RecCopyFile_Click(this, new RoutedEventArgs()));

            Act(TblRowAboveBtn, () => TableRowAbove_Click(this, new RoutedEventArgs()));
            Act(TblRowBelowBtn, () => TableRowBelow_Click(this, new RoutedEventArgs()));
            Act(TblColLeftBtn, () => TableColLeft_Click(this, new RoutedEventArgs()));
            Act(TblColRightBtn, () => TableColRight_Click(this, new RoutedEventArgs()));
            Act(TableStyleBtn, () => TableStyle_Click(this, new RoutedEventArgs()));

            // The chips flip themselves on click; each handler flips the real state and writes the
            // chip back from it, so a refused toggle (spell check on a huge note) shows as off.
            Act(WordWrapToggle, () => WordWrap_Click(this, new RoutedEventArgs()));
            Act(SyntaxToggle, () => SyntaxHighlight_Click(this, new RoutedEventArgs()));
            Act(SpellToggle, () => { Spell_Click(this, new RoutedEventArgs()); SpellToggle.IsChecked = Editor.SpellCheck.IsEnabled; });

            Act(PageWidthFullSeg, () => PageWidthFull_Click(this, new RoutedEventArgs()));
            Act(PageWidthWideSeg, () => PageWidthWide_Click(this, new RoutedEventArgs()));
            Act(PageWidthMediumSeg, () => PageWidthMedium_Click(this, new RoutedEventArgs()));
            Act(PageWidthNarrowSeg, () => PageWidthNarrow_Click(this, new RoutedEventArgs()));

            Act(PreviewSourceSeg, () => PreviewSource_Click(this, new RoutedEventArgs()));
            Act(PreviewRenderedSeg, () => PreviewRendered_Click(this, new RoutedEventArgs()));
            Act(PreviewSplitSeg, () => PreviewSplit_Click(this, new RoutedEventArgs()));
        }

        /// <summary>Closes the card, then runs the action.</summary>
        private void Act(ButtonBase control, Action action) =>
            control.Click += (_, _) => { EditorCard.IsOpen = false; action(); };

        /// <summary>The parts of the card for what was right-clicked: the clipboard strip always,
        /// Link and List on text, the recording strip on a recording, the table strip and rows in
        /// a table cell, the view switches on text. Called from Editor_ContextMenuOpening.</summary>
        private void FillEditorCard(bool onObject, bool hasText, bool onRecording)
        {
            Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
            EditorCutBtn.IsEnabled = ApplicationCommands.Cut.CanExecute(null, Editor);
            EditorCopyBtn.IsEnabled = _ctxObject is System.Windows.Controls.Image { Source: System.Windows.Media.Imaging.BitmapSource } || !Editor.Selection.IsEmpty;
            EditorPasteBtn.IsEnabled = ApplicationCommands.Paste.CanExecute(null, Editor);
            EditorLinkBtn.Visibility = Show(!onObject);
            EditorListBtn.Visibility = Show(!onObject && hasText);
            RecSlot.Visibility = Show(onRecording);
            DocToggleSlot.Visibility = Show(!onObject);
            PageWidthSlot.Visibility = Show(!onObject);
            if (onObject) PreviewMenuItem.Visibility = Visibility.Collapsed;
            WordWrapToggle.IsChecked = _wordWrap;
            SyntaxToggle.IsChecked = _syntaxHighlight;
            SpellToggle.IsChecked = Editor.SpellCheck.IsEnabled;
        }
    }
}
