using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // The group card (1.7.0): the group header's right-click menu, in the same card shape as
    // the note card (NoteCard.cs). One instance, parked on GroupCardHost in MainWindow.xaml and
    // handed to whichever header was right-clicked (Groups.Header.cs UpdateGroupHeaderMenu).
    // _ctxGroup is the group it is about; every control acts on it through the handler its
    // old menu row used, so Ctrl+F2, Ctrl+Shift+G and the rest still reach the same code.
    public partial class MainWindow
    {
        private bool _groupCardWired;

        private void FillGroupCard(GroupHeader g)
        {
            GroupCardSwatches.NoneName = Loc("Str_Card_NoColor");
            GroupCardSwatches.PickerName = Loc("Str_Card_MoreColors") + " (Ctrl+Shift+K)";
            if (!_groupCardWired)
            {
                _groupCardWired = true;
                GroupCardSwatches.ColorChosen += hex => { GroupCard.IsOpen = false; SetGroupColorWithUndo(_ctxGroup, hex); };
                GroupCardSwatches.PickRequested += () => { GroupCard.IsOpen = false; GroupColorPick_Click(this, new RoutedEventArgs()); };
            }

            GroupCardTitle.Text = g.Name;
            string parent = NoteStore.GroupParentOf(g.Path);
            string count = g.Count == 1 ? Loc("Str_Card_OneNote") : string.Format(Loc("Str_Card_NotesCount"), g.Count);
            GroupCardMeta.Text = parent.Length > 0 ? count + "  \u00B7  " + parent.Replace(NoteStore.GroupSep, " / ") : count;
            GroupCardDot.Fill = g.NameBrush ?? (Brush)Brushes.Transparent;
            if (g.NameBrush is Brush titleBrush) GroupCardTitle.Foreground = titleBrush;
            else GroupCardTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            GroupCardSwatches.Show(ColorPickerDialog.UserSwatches(), g.NameColor);
            GroupCardParentText.Text = parent.Length == 0 ? Loc("Str_Card_TopLevel") : parent.Replace(NoteStore.GroupSep, " / ");
            BuildParentMenu(g.Path);
            GroupCardTemplatesToggle.IsChecked = IsTemplatesGroup(g.Path);   // Templates.cs
            GroupCardDailyToggle.IsChecked = IsDailyGroup(g.Path);           // DailyNote.cs
            GroupCardSubTopItem.IsChecked = App.GetSetting("SubgroupsOnTop") != "0";
        }

        /// <summary>Every place this group may move: the top level, then each group that is not
        /// itself or inside it. The current parent carries the check.</summary>
        private void BuildParentMenu(string path)
        {
            GroupParentMenu.Items.Clear();
            string current = NoteStore.GroupParentOf(path);
            void Add(string target, string label, int depth)
            {
                bool here = string.Equals(target, current, StringComparison.OrdinalIgnoreCase);
                var check = new TextBlock { Text = here ? "\u2713" : "", VerticalAlignment = VerticalAlignment.Center };
                check.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
                var item = new MenuItem
                {
                    Header = BuildMenuRow(check, null, new string(' ', depth * 3) + label, null),
                    Padding = new Thickness(6, 5, 14, 5),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = !here,
                };
                item.Click += (_, _) => { GroupCard.IsOpen = false; MoveGroupWithUndo(path, target, null); };
                GroupParentMenu.Items.Add(item);
            }
            Add("", Loc("Str_Card_TopLevel"), 0);
            foreach (var g in NoteStore.ListGroupTree())
            {
                if (string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (g.Path.StartsWith(path + NoteStore.GroupSep, StringComparison.OrdinalIgnoreCase)) continue;
                int depth = g.Path.Count(c => c.ToString() == NoteStore.GroupSep);
                Add(g.Path, NoteStore.GroupNameOf(g.Path), depth);
            }
        }

        private void SetGroupColorWithUndo(GroupHeader? g, string hex)
        {
            if (g == null || RefuseReadOnly()) return;   // Notes.Document.cs
            string path = g.Path, original = g.NameColor;
            NoteStore.SetGroupColor(path, hex);
            PushUndo(() => RestoreGroupColor(path, original));   // Groups.Color.cs
            RefreshList(preserveScroll: true);
            GroupsChanged?.Invoke();   // GroupManager.cs
        }

        private void RunFromGroupCard(RoutedEventHandler action)
        {
            GroupCard.IsOpen = false;
            action(this, new RoutedEventArgs());
        }

        private void GroupCardNewNote_Click(object sender, RoutedEventArgs e)
        {
            GroupCard.IsOpen = false;
            if (_ctxGroup is GroupHeader g) NewNoteInGroup(g.Path);
        }

        private void GroupCardSubgroup_Click(object sender, RoutedEventArgs e) => RunFromGroupCard(NewSubgroup_Click);
        private void GroupCardRename_Click(object sender, RoutedEventArgs e) => RunFromGroupCard(RenameGroup_Click);
        private void GroupCardGraph_Click(object sender, RoutedEventArgs e) => RunFromGroupCard(GroupGraph_Click);
        private void GroupCardDelete_Click(object sender, RoutedEventArgs e) => RunFromGroupCard(DeleteGroup_Click);
        private void GroupCardCollapseAll_Click(object sender, RoutedEventArgs e) => RunFromGroupCard(CollapseAllGroups_Click);

        private void GroupCardManager_Click(object sender, RoutedEventArgs e)
        {
            GroupCard.IsOpen = false;
            OpenGroupManager(_ctxGroup?.Path);   // GroupManager.cs
        }

        private void GroupCardTemplates_Click(object sender, RoutedEventArgs e) => RunFromGroupCard(TemplatesGroup_Click);
        private void GroupCardDaily_Click(object sender, RoutedEventArgs e) => RunFromGroupCard(DailyGroup_Click);

        /// <summary>A new untitled note filed straight into the group, which opens to show it.
        /// Alt+Shift+H runs it on the group the keyboard is in (RunGroupAction).</summary>
        private void NewNoteInGroup(string path)
        {
            if (!NoteStore.IsOpen || RefuseReadOnly()) return;   // Notes.Document.cs
            if (!SaveCurrentNote(refreshList: false)) return;
            long id = NoteStore.Create(Loc("Str_Untitled"));
            if (id < 0) return;
            NoteStore.SetNoteGroup(id, path);
            NoteStore.SetGroupCollapsed(path, false);
            SearchBox.Text = "";   // a filtered list would hide the new note
            RefreshList(preserveScroll: true);
            OpenNote(id);
            SelectNoteInList(id);   // WikiLinkNav.cs
            GroupsChanged?.Invoke();
        }

        private void NewNoteInGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is GroupHeader g) NewNoteInGroup(g.Path);
        }

        /// <summary>Folds every group in the sidebar. Ctrl+Z opens back up the ones that were open.</summary>
        private void CollapseAllGroups_Click(object sender, RoutedEventArgs e)
        {
            if (!NoteStore.IsOpen || NoteStore.IsReadOnly) return;
            var open = NoteStore.ListGroupTree().Where(g => !g.Collapsed).Select(g => g.Path).ToList();
            if (open.Count == 0) return;
            foreach (var path in open) NoteStore.SetGroupCollapsed(path, true);
            PushUndo(() =>
            {
                foreach (var path in open) NoteStore.SetGroupCollapsed(path, false);
                RefreshList(preserveScroll: true);
            });
            RefreshList(preserveScroll: true);
        }
    }
}
