using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Models;
using KillerNotes.Shell;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A main window that was never constructed, with only the pieces the status line, the undo
    /// stack, the note lists and the navigation history need. Commands run against it exactly as
    /// written; what it lacks (the editor, the sidebar rows) is set by the test that needs it.
    /// Nothing here shows a window.
    /// </summary>
    internal sealed class ShellProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public MainWindow Window { get; } = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
        public TextBlock Status { get; } = new();

        public ShellProbe(long currentId = -1)
        {
            Set("_currentId", currentId);
            Set("StatusText", Status);
            Set("_statusTimer", new DispatcherTimer());
            Set("_saveTimer", new DispatcherTimer());
            Set("_actionUndo", new List<Action>());
            Set("_notes", new List<Note>());
            Set("_trashNotes", new List<Note>());
            Set("_sidebarItems", new ObservableCollection<object>());
            Set("_navBack", new List<long>());
            Set("_navFwd", new List<long>());
            Set("SearchBox", new TextBox());
            Set("NotesList", new ListBox());
        }

        public void Set(string name, object? value) => typeof(MainWindow).GetField(name, Members)!.SetValue(Window, value);
        public T Get<T>(string name) => (T)typeof(MainWindow).GetField(name, Members)!.GetValue(Window)!;

        /// <summary>Runs a command and returns what it threw, unwrapped. The probe has no sidebar to
        /// repaint, so a command that gets that far ends in an exception; the tests look at what it
        /// did before that.</summary>
        public Exception? Run(string method, params object?[] args)
        {
            try
            {
                typeof(MainWindow).GetMethod(method, Members)!.Invoke(Window, args);
                return null;
            }
            catch (TargetInvocationException ex) { return ex.InnerException; }
        }

        public int UndoEntries => Get<List<Action>>("_actionUndo").Count;
    }
}
