using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // The group card (1.7.0): the group header's right-click menu, in the same card shape as
    // the note card (NoteCard.cs): a header with Delete, then the KillerUI tile grid. One instance,
    // parked on GroupCardHost in MainWindow.xaml and handed to whichever header was right-clicked
    // (Groups.Header.cs UpdateGroupHeaderMenu). _ctxGroup is the group it is about; every tile acts
    // on it through the handler its old menu row used, so Ctrl+F2, Ctrl+Shift+G and the rest reach
    // the same code.
    public partial class MainWindow
    {
        /// <summary>The Parent list the card's flyout tile opens (BuildParentMenu builds its rows).</summary>
        private readonly MenuItem GroupParentMenu = new();

        /// <summary>Builds the card for this group: a header with Delete, then one customizable tile grid.</summary>
        private void FillGroupCard(GroupHeader g)
        {
            string parent = NoteStore.GroupParentOf(g.Path);
            string count = g.Count == 1 ? Loc("Str_Card_OneNote") : string.Format(Loc("Str_Card_NotesCount"), g.Count);
            string meta = parent.Length > 0 ? count + "  ·  " + parent.Replace(NoteStore.GroupSep, " / ") : count;
            NoteCards.Prepare(GroupCard);
            GroupCard.Items.Add(NoteCards.Header(GroupCard, g.Name, meta, Loc("Str_Ctx_DeleteGroup"), "Ctrl+F7",
                () => DeleteGroup_Click(this, new RoutedEventArgs()), titleBrush: g.NameBrush));

            var swatches = new SwatchRow
            {
                NoneName = Loc("Str_Card_NoColor"),
                PickerName = Loc("Str_Card_MoreColors") + " (Ctrl+Shift+K)",
                Margin = new Thickness(4),
            };
            swatches.Show(ColorPickerDialog.UserSwatches(), g.NameColor);
            swatches.ColorChosen += hex => { CardBuilder.Close(swatches); GroupCard.IsOpen = false; SetGroupColorWithUndo(_ctxGroup, hex); };
            swatches.PickRequested += () => { CardBuilder.Close(swatches); GroupCard.IsOpen = false; GroupColorPick_Click(this, new RoutedEventArgs()); };
            BuildParentMenu(g.Path);

            CardTile Act(string id, string glyph, string caption, string keys, RoutedEventHandler run) =>
                new(glyph, Loc(caption), keys, () => run(this, new RoutedEventArgs())) { Id = id };
            var tiles = new List<CardTile>
            {
                Act("newnote", "", "Str_Card_NewNote", "Alt+Shift+H", GroupCardNewNote_Click),
                Act("subgroup", "", "Str_Card_Subgroup", "Ctrl+Shift+G", NewSubgroup_Click),
                Act("rename", "", "Str_Card_Rename", "Ctrl+F2", RenameGroup_Click),
                NoteCards.Flyout("", Loc("Str_Card_Color"), null, new object[] { CardBuilder.Slot(swatches, new Thickness(0)) }, id: "color"),
                NoteCards.Flyout("", Loc("Str_Card_Parent"), null, GroupParentMenu.Items.Cast<object>().ToList(), id: "parent"),
                Act("organize", "", "Str_Card_Organize", "Alt+Shift+O", GroupCardManager_Click),
                Act("templates", "", "Str_Card_Templates", "Ctrl+F4", TemplatesGroup_Click) with { Checked = IsTemplatesGroup(g.Path) },   // Templates.cs
                Act("daily", "", "Str_Card_Daily", "Ctrl+F5", DailyGroup_Click) with { Checked = IsDailyGroup(g.Path) },   // DailyNote.cs
                Act("collapse", "", "Str_Card_CollapseAll", "Alt+Shift+Z", CollapseAllGroups_Click),
                Act("graph", "", "Str_Card_Graph", "Ctrl+Shift+B", GroupGraph_Click),
                Act("subtop", "", "Str_Ctx_SubTop", "Ctrl+F3", SubgroupsOnTop_Click) with { Checked = App.GetSetting("SubgroupsOnTop") != "0" },
            };
            GroupCard.Items.Add(NoteCards.Editable(GroupCard, "Group", ["newnote"], ["graph", "subtop"], tiles));
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

        private void GroupCardNewNote_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is GroupHeader g) NewNoteInGroup(g.Path);
        }

        private void GroupCardManager_Click(object sender, RoutedEventArgs e) => OpenGroupManager(_ctxGroup?.Path);   // GroupManager.cs

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
