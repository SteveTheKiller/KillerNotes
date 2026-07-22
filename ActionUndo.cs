using System;
using System.Collections.Generic;

namespace KillerNotes
{
    // App-level undo for organizational actions - group moves, group/title color changes,
    // tag toggles, and note-to-group assignment. This is SEPARATE from the RichTextBox's
    // built-in text undo: Ctrl+Z still drives text editing while the editor holds focus and
    // has edits left; otherwise it pops this stack (Shortcuts.cs routes it).
    //
    // Entries revert by id/path through NoteStore + RefreshList, never by holding Note
    // references - RefreshList rebuilds _notes with fresh instances, so a captured reference
    // would be stale. The stack is cleared on every database (re)open (InitNotes) so an id
    // from one database can never be replayed against another.
    public partial class MainWindow
    {
        private readonly List<Action> _actionUndo = new();
        private const int ActionUndoLimit = 100;

        // True when the most recent undoable thing the user did was an organizational action
        // (not a text edit). Ctrl+Z uses this to decide who gets the key: a group drag leaves
        // the editor focused with its own undo history, but the user means "undo that drag".
        // Cleared by MarkDirty (Notes.cs) the moment they type in the editor or title.
        private bool _lastActionWasOrg;

        private void PushUndo(Action revert)
        {
            _actionUndo.Add(revert);
            if (_actionUndo.Count > ActionUndoLimit) _actionUndo.RemoveAt(0);
            _lastActionWasOrg = true;
        }

        /// <summary>Reverts the most recent organizational action. Returns false when the
        /// stack is empty so Ctrl+Z can fall through to whatever else wants the key.</summary>
        private bool PerformActionUndo()
        {
            if (_actionUndo.Count == 0) return false;
            int last = _actionUndo.Count - 1;
            var revert = _actionUndo[last];
            _actionUndo.RemoveAt(last);
            revert();
            FlashStatus(Loc("Str_St_Undo"));
            return true;
        }

        private void ClearActionUndo() => _actionUndo.Clear();
    }
}
