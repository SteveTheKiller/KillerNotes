using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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
        private Window? _managerCardOwner;

        /// <summary>Owner for the rename, delete and new-group dialogs: the manager while it is
        /// the window asking, so they open over it instead of over the main window.</summary>
        private Window GroupDialogOwner => _managerCardOwner is { IsVisible: true } owner ? owner
            : _groupManager is { IsActive: true } m ? m : this;

        /// <summary>Opens the manager, or brings it forward, with the given group selected.</summary>
        private void OpenGroupManager(string? selectPath = null)
        {
            if (!NoteStore.IsOpen) return;
            if (_groupManager == null)
            {
                _groupManager = new GroupManagerWindow(this);
                if (_groupManager.WindowStartupLocation != WindowStartupLocation.Manual) CenterOnThisWindow(_groupManager);
                void MainClosed(object? sender, EventArgs args) => CloseGroupManager();
                Closed += MainClosed;
                _groupManager.Closed += (_, _) => { Closed -= MainClosed; _groupManager = null; };
                _groupManager.Show();
            }
            else
            {
                if (_groupManager.WindowState == WindowState.Minimized) _groupManager.WindowState = WindowState.Normal;
                _groupManager.Activate();
            }
            _groupManager.Reload(selectPath);
        }

        /// <summary>First open, no saved bounds: centered over the main window. The manager is not
        /// owned (it should not ride on top of the notes or minimize with them), so CenterOwner alone
        /// has nothing to center on and WPF dropped it in the screen corner. Measured from the
        /// window's real screen position, so a maximized window on another monitor works too.</summary>
        private void CenterOnThisWindow(Window w)
        {
            if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return;
            Point topLeft = target.TransformFromDevice.Transform(PointToScreen(new Point(0, 0)));
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = topLeft.X + Math.Max(0, (ActualWidth - w.Width) / 2);
            w.Top = topLeft.Y + Math.Max(0, (ActualHeight - w.Height) / 2);
        }

        /// <summary>A database switch or lock takes the manager down with it: its rows belong to
        /// the notepad that was open.</summary>
        private void CloseGroupManager() => _groupManager?.CloseForStoreChange();

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

        // The manager's right-click menus are the sidebar's own cards, not copies: the group
        // card for a group, the note card for its rows. Same rows, same handlers, same undo.

        void IGroupManagerHost.ShowGroupCard(string path, int count, UIElement target, bool atMouse)
        {
            if (!NoteStore.IsOpen) return;
            var t = NoteStore.ListGroupTree().FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (t.Path == null) return;
            var g = new GroupHeader { Path = t.Path, Name = NoteStore.GroupNameOf(t.Path), NameColor = t.Color ?? "", Count = count };
            _ctxGroup = g;
            FillGroupCard(g);   // GroupCard.cs
            OpenCardOver(GroupCard, target, atMouse, null);
        }

        void IGroupManagerHost.ShowNoteCard(IReadOnlyCollection<long> ids, UIElement target, bool atMouse)
        {
            if (!NoteStore.IsOpen) return;
            var set = ids.ToHashSet();
            // The sidebar's own Note objects where it has them (the search can hide some), so a
            // change the card makes shows on the same instances the sidebar draws.
            var notes = _notes.Where(n => set.Contains(n.Id)).ToList();
            if (notes.Count < set.Count)
            {
                var have = notes.Select(n => n.Id).ToHashSet();
                notes.AddRange(NoteStore.List().Where(n => set.Contains(n.Id) && !have.Contains(n.Id)));
            }
            notes = [.. notes.Where(n => !n.IsDeleted)];
            if (notes.Count == 0) return;
            TagManager.Refresh();
            TagManager.ApplyChips(notes);
            _managerCardNotes = notes;   // NoteCard.cs SidebarSelection
            _noteContextTarget = true;
            OpenCardOver(NoteCard, target, atMouse, () => _managerCardNotes = null);
        }

        /// <summary>Opens one of the sidebar's cards over an element of the manager. Once it
        /// closes and the chosen action has run (Background priority, after the click), the card
        /// goes back to the sidebar's own placement and the manager reloads to show the change.</summary>
        private void OpenCardOver(ContextMenu card, UIElement target, bool atMouse, Action? onDone)
        {
            _managerCardOwner = Window.GetWindow(target);
            card.PlacementTarget = target;
            card.Placement = atMouse ? PlacementMode.MousePoint : PlacementMode.Bottom;
            void Closed(object sender, RoutedEventArgs e)
            {
                card.Closed -= Closed;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    card.ClearValue(ContextMenu.PlacementTargetProperty);
                    card.ClearValue(ContextMenu.PlacementProperty);
                    onDone?.Invoke();
                    _managerCardOwner = null;
                    GroupsChanged?.Invoke();
                }), DispatcherPriority.Background);
            }
            card.Closed += Closed;
            card.IsOpen = true;
        }

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
