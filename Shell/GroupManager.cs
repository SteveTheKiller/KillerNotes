using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // The group manager (1.7.0): a separate window for sorting a long sidebar, like a browser's
    // bookmark manager (Controls/GroupManagerWindow.xaml). It owns no store logic. Every change
    // it makes comes back here and runs the sidebar's own code - AssignGroup for notes,
    // MoveGroupWithUndo for groups, the header handlers for rename, delete and new - so the
    // undo stack, the settings that point at groups (templates, daily) and the sidebar refresh
    // are exactly what a drag in the sidebar would have done. GroupsChanged tells it to reload
    // when the sidebar changes something underneath it.
    public partial class MainWindow : IGroupManagerHost
    {
        private GroupManagerWindow? _groupManager;

        /// <summary>Owner for the rename, delete and new-group dialogs: the manager while it is
        /// the window asking, so they open over it instead of over the main window.</summary>
        private Window GroupDialogOwner => _groupManager is { IsActive: true } m ? m : this;

        /// <summary>Opens the manager, or brings it forward, with the given group selected.</summary>
        private void OpenGroupManager(string? selectPath = null)
        {
            if (!NoteStore.IsOpen) return;
            if (_groupManager == null)
            {
                _groupManager = new GroupManagerWindow(this);
                _groupManager.Closed += (_, _) => _groupManager = null;
                _groupManager.Show();
            }
            else
            {
                if (_groupManager.WindowState == WindowState.Minimized) _groupManager.WindowState = WindowState.Normal;
                _groupManager.Activate();
            }
            _groupManager.Reload(selectPath);
        }

        /// <summary>A database switch or lock takes the manager down with it: its rows belong to
        /// the notepad that was open.</summary>
        private void CloseGroupManager() => _groupManager?.Close();

        // ---- IGroupManagerHost ----

        IReadOnlyList<Note> IGroupManagerHost.Notes() =>
            NoteStore.IsOpen ? NoteStore.List().Where(n => !n.IsDeleted).ToList() : [];

        IReadOnlyList<(string Path, string Parent, bool Collapsed, string Color)> IGroupManagerHost.Groups() =>
            NoteStore.IsOpen ? NoteStore.ListGroupTree() : [];

        bool IGroupManagerHost.ReadOnly => !NoteStore.IsOpen || NoteStore.IsReadOnly;

        void IGroupManagerHost.MoveNotes(IReadOnlyCollection<long> ids, string group)
        {
            var set = ids.ToHashSet();
            var notes = NoteStore.List().Where(n => set.Contains(n.Id) && !n.IsDeleted).ToList();
            if (notes.Count > 0) AssignGroup(notes, group);   // Groups.Menu.cs
        }

        string? IGroupManagerHost.MoveGroup(string path, string newParent) =>
            MoveGroupWithUndo(path, newParent, null);   // Groups.Header.cs

        void IGroupManagerHost.RenameGroup(string path) => RunOnGroup(path, RenameGroup_Click);
        void IGroupManagerHost.DeleteGroup(string path) => RunOnGroup(path, DeleteGroup_Click);
        void IGroupManagerHost.SetGroupColor(string path, string hex) =>
            SetGroupColorWithUndo(new GroupHeader { Path = path, Name = NoteStore.GroupNameOf(path),
                NameColor = NoteStore.ListGroupTree().FirstOrDefault(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase)).Color ?? "" }, hex);

        void IGroupManagerHost.NewGroup(string parent)
        {
            if (parent.Length > 0) { RunOnGroup(parent, NewSubgroup_Click); return; }
            if (!NoteStore.IsOpen || RefuseReadOnly()) return;   // Notes.Document.cs
            var dlg = new InputDialog(Loc("Str_Dlg_NewGroupHead"), "", Loc("Str_Btn_Create")) { Owner = GroupDialogOwner };
            dlg.ShowDialog();
            string name = dlg.Value.Trim().Replace(NoteStore.GroupSep, "");
            if (!dlg.Confirmed || name.Length == 0) return;
            NoteStore.AddGroup(name);   // an existing name just resolves to that group
            RefreshList(preserveScroll: true);
            GroupsChanged?.Invoke();
        }

        void IGroupManagerHost.OpenNote(long id)
        {
            if (!SaveCurrentNote(refreshList: false)) return;
            if (!_notes.Any(n => n.Id == id)) SearchBox.Text = "";   // the sidebar search is hiding it
            OpenNote(id);
            SelectNoteInList(id);   // WikiLinkNav.cs
        }

        bool IGroupManagerHost.Undo()
        {
            bool undone = PerformActionUndo();   // ActionUndo.cs
            if (undone) GroupsChanged?.Invoke();
            return undone;
        }

        string IGroupManagerHost.Loc(string key) => Loc(key);

        event Action? IGroupManagerHost.GroupsChanged
        {
            add => GroupsChanged += value;
            remove => GroupsChanged -= value;
        }

        /// <summary>Runs a group-header handler on a group named by path, as if its header had
        /// been right-clicked.</summary>
        private void RunOnGroup(string path, RoutedEventHandler action)
        {
            if (!NoteStore.IsOpen) return;
            _ctxGroup = new GroupHeader { Path = path, Name = NoteStore.GroupNameOf(path) };
            action(this, new RoutedEventArgs());
        }
    }
}
