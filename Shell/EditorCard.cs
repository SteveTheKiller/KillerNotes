using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerFamily.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // The editor card (1.7.0, KillerUI tiles in 1.7.1): the editor's right-click menu as one tile grid
    // (MainWindow.xaml EditorCard, built here each time it opens). Every tile calls the handler its old
    // menu row called, so Ctrl+X, Alt+Shift+A, F4 and the rest still reach the same code, and every tile
    // shows that key on its second line. A tile closes the card BEFORE it acts (the kit's tile Click):
    // Link, Edit recording and Table style open dialogs, and a card left open under a modal dialog keeps
    // its popup on screen.
    public partial class MainWindow
    {
        /// <summary>The Preview gate: visible while the open note is a detected markdown note. Preview.cs owns
        /// it and F4 (Shortcuts.cs) reads it; it is not on the card, the card shows a Preview tile for it.</summary>
        private readonly MenuItem PreviewMenuItem = new() { Visibility = Visibility.Collapsed };

        /// <summary>Kept for InitEditor's call order; the card wires each tile as it builds it.</summary>
        private void InitEditorCard() { }

        /// <summary>The tiles for what was right-clicked: the clipboard always, Link and List on text, the
        /// markdown tools on a markdown note, the recording tiles on a recording, the table tiles in a table
        /// cell, the view tiles on text. Called from Editor_ContextMenuOpening after UpdateTableMenu.</summary>
        private void FillEditorCard(bool onObject, bool hasText, bool onRecording)
        {
            NoteCards.Prepare(EditorCard);
            CardTile Act(string id, string glyph, string caption, string? keys, Action run, bool enabled = true) =>
                new(glyph, Loc(caption), keys, run, false, enabled) { Id = id };
            CardTile Click(string id, string glyph, string caption, string? keys, RoutedEventHandler run, bool enabled = true) =>
                Act(id, glyph, caption, keys, () => run(this, new RoutedEventArgs()), enabled);

            var groups = new List<IEnumerable<CardTile>>
            {
                new[]
                {
                    Act("cut", "", "Str_Ctx_Cut", "Ctrl+X", () => ApplicationCommands.Cut.Execute(null, Editor),
                        ApplicationCommands.Cut.CanExecute(null, Editor)),
                    Click("copy", "", "Str_Ctx_Copy", "Ctrl+C", EditorCopyMenuItem_Click,
                        _ctxObject is Image { Source: System.Windows.Media.Imaging.BitmapSource } || !Editor.Selection.IsEmpty),
                    Act("paste", "", "Str_Ctx_Paste", "Ctrl+V", () => ApplicationCommands.Paste.Execute(null, Editor),
                        ApplicationCommands.Paste.CanExecute(null, Editor)),
                },
            };
            if (!onObject)
                groups.Add(new[]
                {
                    Click("link", "", "Str_Card_Link", "Ctrl+K", LinkMenu_Click),
                    Click("list", "", "Str_Card_List", "Ctrl+Shift+J", ConvertToList_Click, hasText),
                });
            if (!onObject && UsesMarkdownTools) groups.Add(MarkdownTiles());
            if (onRecording)
                groups.Add(new[]
                {
                    Click("play", "", "Str_Card_Play", "Alt+Shift+P", RecPlayPause_Click),
                    Click("louder", "", "Str_Card_Louder", "Alt+Shift+Up", RecVolumeUp_Click),
                    Click("quieter", "", "Str_Card_Quieter", "Alt+Shift+Down", RecVolumeDown_Click),
                    Click("editrec", "", "Str_Card_EditRec", "Ctrl+Shift+F9", RecEdit_Click),
                    Click("saverec", "", "Str_Card_SaveAs", "Ctrl+Shift+F10", RecSaveAs_Click),
                    Click("copyrec", "", "Str_Card_CopyFile", "Ctrl+Shift+F11", RecCopyFile_Click),
                });
            if (_tblMenuCell != null)
                groups.Add(new[]
                {
                    Click("rowabove", "", "Str_Card_RowAbove", "Alt+Shift+A", TableRowAbove_Click),
                    Click("rowbelow", "", "Str_Card_RowBelow", "Alt+Shift+B", TableRowBelow_Click),
                    Click("colleft", "", "Str_Card_ColLeft", "Alt+Shift+L", TableColLeft_Click),
                    Click("colright", "", "Str_Card_ColRight", "Alt+Shift+R", TableColRight_Click),
                    Click("tablestyle", "", "Str_Card_TableStyle", "Alt+Shift+S", TableStyle_Click),
                    Click("delrow", "", "Str_Ctx_TblDelRow", "Alt+Shift+D", TableDelRow_Click),
                    Click("delcol", "", "Str_Ctx_TblDelCol", "Alt+Shift+C", TableDelCol_Click),
                    Click("deltable", "", "Str_Ctx_TblDelete", "Alt+Shift+X", TableDelete_Click),
                });
            if (!onObject)
            {
                var view = new List<CardTile>
                {
                    Click("wrap", "", "Str_Card_Wrap", "Ctrl+Shift+W", WordWrap_Click) with { Checked = _wordWrap },
                    Click("syntax", "", "Str_Card_Syntax", "Ctrl+Shift+E", SyntaxHighlight_Click) with { Checked = _syntaxHighlight },
                    Click("spell", "", "Str_Card_Spelling", "Ctrl+Shift+P", Spell_Click) with { Checked = Editor.SpellCheck.IsEnabled },
                    NoteCards.Flyout("", Loc("Str_Card_Width"), null, PageWidthRows(), id: "width"),
                };
                if (PreviewMenuItem.Visibility == Visibility.Visible)
                    view.Add(NoteCards.Flyout("", Loc("Str_Card_Preview"), "F4", PreviewRows(), id: "preview"));
                groups.Add(view);
            }
            EditorCard.Items.Add(NoteCards.Editable(EditorCard, "Editor", ["cut", "copy", "paste"], [], groups.ToArray()));
        }

        /// <summary>The markdown tools a markdown note's card carries, writable notes only.</summary>
        private CardTile[] MarkdownTiles()
        {
            bool writable = CanEditCurrentNote(explain: false);
            CardTile Md(string id, string glyph, string label, string keys, Action run) =>
                new(glyph, Loc(label), keys, run, false, writable) { Id = id };
            return
            [
                Md("bold", "", "Str_Kb_Bold", "Ctrl+B", () => EditingCommands.ToggleBold.Execute(null, Editor)),
                Md("italic", "", "Str_Kb_Italic", "Ctrl+I", () => EditingCommands.ToggleItalic.Execute(null, Editor)),
                Md("strike", "", "Str_KS_Strike", "Ctrl+Shift+S", () => Strike_Click(this, new RoutedEventArgs())),
                Md("mono", "", "Str_KS_Mono", "Ctrl+Shift+M", () => Mono_Click(this, new RoutedEventArgs())) with { Shown = Loc("Str_Card_Code") },
                Md("heading", "", "Str_Kb_Heading", "Alt+1", () => Heading_Click(this, new RoutedEventArgs())),
                Md("bullets", "", "Str_Kb_Bullets", "Ctrl+Shift+L", () => EditingCommands.ToggleBullets.Execute(null, Editor)),
                Md("numbered", "", "Str_Kb_Numbered", "Ctrl+Shift+N", () => EditingCommands.ToggleNumbering.Execute(null, Editor)),
                Md("checkbox", "", "Str_Kb_Checkbox", "Alt+C", () => Checkbox_Click(this, new RoutedEventArgs())),
                Md("table", "", "Str_KS_Table", "Ctrl+Shift+T", () => TableBtn_Click(this, new RoutedEventArgs())),
                Md("rule", "", "Str_Kb_Rule", "Ctrl+Shift+R", () => InsertRule_Click(this, new RoutedEventArgs())),
            ];
        }

        /// <summary>The four page widths as checkable rows, the current one checked.</summary>
        private List<object> PageWidthRows()
        {
            var rows = new List<object>();
            foreach (var mode in new[] { PageWidthMode.Full, PageWidthMode.Wide, PageWidthMode.Medium, PageWidthMode.Narrow })
            {
                var row = new MenuItem
                {
                    Header = Loc(PageWidthLabel(mode)), InputGestureText = PageWidthGesture(mode),
                    IsCheckable = true, IsChecked = mode == _pageWidth,
                };
                var chosen = mode;
                row.Click += (_, _) => ApplyPageWidth(chosen);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Source, Rendered and Split as checkable rows, the current mode checked.</summary>
        private List<object> PreviewRows()
        {
            var rows = new List<object>();
            foreach (var (mode, label, keys) in new[]
            {
                (PreviewMode.Source, "Str_Preview_Source", "Ctrl+Shift+F6"),
                (PreviewMode.Rendered, "Str_Preview_Rendered", "Ctrl+Shift+F7"),
                (PreviewMode.Split, "Str_Preview_Split", "Ctrl+Shift+F8"),
            })
            {
                var row = new MenuItem { Header = Loc(label), InputGestureText = keys, IsCheckable = true, IsChecked = mode == _previewMode };
                var chosen = mode;
                row.Click += (_, _) => SetPreviewMode(chosen);
                rows.Add(row);
            }
            return rows;
        }
    }
}
