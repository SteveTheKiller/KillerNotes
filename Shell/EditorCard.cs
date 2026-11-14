using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using KillerFamily.Controls;

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
            bool markdown = !onObject && UsesMarkdownTools;
            MarkdownFormatSlot.Visibility = MarkdownInsertSlot.Visibility = Show(markdown);
            if (markdown) FillMarkdownCard();
            RecSlot.Visibility = Show(onRecording);
            DocToggleSlot.Visibility = Show(!onObject);
            PageWidthSlot.Visibility = Show(!onObject);
            ((Border)EditorQuickSlot.Header).BorderThickness = new Thickness(0, 0, 0, !onObject || onRecording ? 1 : 0);
            ((Border)RecSlot.Header).BorderThickness = new Thickness(0, 0, 0, !onObject ? 1 : 0);
            if (onObject) PreviewMenuItem.Visibility = Visibility.Collapsed;
            WordWrapToggle.IsChecked = _wordWrap;
            SyntaxToggle.IsChecked = _syntaxHighlight;
            SpellToggle.IsChecked = Editor.SpellCheck.IsEnabled;
        }

        private void FillMarkdownCard()
        {
            bool writable = CanEditCurrentNote(explain:false);
            CardAction Action(string glyph, string caption, string label, string gesture, Action run) =>
                new(glyph,caption,Loc(label),gesture,run,writable);
            Put(MarkdownFormatSlot,false,
                Action("\uE8DD","**","Str_Kb_Bold","Ctrl+B",() => EditingCommands.ToggleBold.Execute(null,Editor)),
                Action("\uE8DB","*","Str_Kb_Italic","Ctrl+I",() => EditingCommands.ToggleItalic.Execute(null,Editor)),
                Action("\uEDE0","~~","Str_KS_Strike","Ctrl+Shift+S",() => Strike_Click(this,new RoutedEventArgs())),
                Action("\uE943","`","Str_KS_Mono","Ctrl+Shift+M",() => Mono_Click(this,new RoutedEventArgs())),
                Action("\uE8A5","#","Str_Kb_Heading","Alt+1 / Alt+2 / Alt+3",() => Heading_Click(this,new RoutedEventArgs())));
            Put(MarkdownInsertSlot,true,
                Action("\uE8FD","-","Str_Kb_Bullets","Ctrl+Shift+L",() => EditingCommands.ToggleBullets.Execute(null,Editor)),
                Action("\uEA37","1.","Str_Kb_Numbered","Ctrl+Shift+N",() => EditingCommands.ToggleNumbering.Execute(null,Editor)),
                Action("\uE73A","[ ]","Str_Kb_Checkbox","Alt+C",() => Checkbox_Click(this,new RoutedEventArgs())),
                Action("\uE8A9","| |","Str_KS_Table","Ctrl+Shift+T",() => TableBtn_Click(this,new RoutedEventArgs())),
                Action("\uE738","---","Str_KS_Rule","Ctrl+Shift+R",() => InsertRule_Click(this,new RoutedEventArgs())));

            void Put(MenuItem slot, bool rule, params CardAction[] actions)
            {
                var row = CardBuilder.QuickStrip(Loc("Str_Card_Markdown"),actions);
                var header = (Border)row.Header;
                row.Header = null;
                header.BorderThickness = new Thickness(0,0,0,rule ? 1 : 0);
                if (slot == MarkdownFormatSlot)
                {
                    var button = (Button)((UniformGrid)header.Child).Children[4];
                    button.Content = "H";
                    button.ApplyTemplate();
                    if (button.Template.FindName("Glyph",button) is TextBlock heading)
                    {
                        heading.SetResourceReference(TextBlock.FontFamilyProperty,"UiFont");
                        heading.FontWeight = FontWeights.Bold;
                    }
                }
                slot.Header = header;
            }
        }
    }
}
