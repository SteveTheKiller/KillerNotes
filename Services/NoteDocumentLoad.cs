using System;
using System.Windows.Controls;

namespace KillerNotes.Services
{
    /// <summary>Owns the undo boundary for a complete note load, including normalization and
    /// embedded content restoration. Loading another note must never become an editor undo.</summary>
    internal sealed class NoteDocumentLoad : IDisposable
    {
        private readonly RichTextBox editor;
        private readonly bool undoEnabled;

        public NoteDocumentLoad(RichTextBox editor)
        {
            this.editor = editor;
            undoEnabled = editor.IsUndoEnabled;
            editor.IsUndoEnabled = false;
        }

        public void Dispose() => editor.IsUndoEnabled = undoEnabled;
    }
}
