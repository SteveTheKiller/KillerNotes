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
    // The note card (1.7.0): the sidebar's right-click menu, rebuilt as a card about the note
    // (MainWindow.xaml NoteCard, styled by the family CardMenu pieces in Controls.xaml).
    //
    // One note: a header with Delete, then the KillerUI tile grid: Pin, Share, Export, History,
    // Duplicate, Color, Tags, Group, Markdown and Save as template, each with its shortcut. Several
    // notes: a "12 notes" header, and the tiles for one note stay in place, dimmed. Trashed notes:
    // Restore and Delete permanently only (FillTrashNoteCard).
    //
    // Every control calls the handler its old menu row called, so the keyboard shortcuts in
    // ActionShortcuts.cs and Shortcuts.cs reach exactly the same code.
    public partial class MainWindow
    {
        private List<Note> _cardNotes = [];

        /// <summary>The notes the group manager opened this card for (GroupManager.cs). Its rows
        /// can sit in a collapsed group or behind the sidebar search, so they are not always in
        /// NotesList and cannot simply be selected there. Null whenever the sidebar owns the card.</summary>
        private List<Note>? _managerCardNotes;

        /// <summary>What the card and its handlers act on: the manager's rows while it has the
        /// card open, otherwise the sidebar selection.</summary>
        private List<Note> SidebarSelection() => _managerCardNotes ?? [.. NotesList.SelectedItems.OfType<Note>()];

        private Note? SidebarSelectedNote() => SingleLiveNote(SidebarSelection());

        internal static Note? SingleLiveNote(IEnumerable<Note> selected)
        {
            var live = selected.Where(n => !n.IsDeleted).Take(2).ToArray();
            return live.Length == 1 ? live[0] : null;
        }

        /// <summary>The Tags and Group lists the card's flyout tiles open (Tags.cs, Groups.Menu.cs build their
        /// rows). They are not on the card themselves; a flyout tile takes their rows when it opens.</summary>
        private readonly MenuItem TagsMenu = new(), GroupMenu = new();

        /// <summary>Kept for the constructor's call order; the swatch row is built with each card now.</summary>
        private void InitNoteCard() { }

        /// <summary>Called from NotesContextMenu_Opened (Tags.cs) for live notes, after the Tags and Group
        /// lists are built. Header with Delete, then one customizable tile grid.</summary>
        private void FillNoteCard(List<Note> selected)
        {
            _cardNotes = [.. selected.Where(n => !n.IsDeleted)];
            bool one = _cardNotes.Count == 1;
            var first = _cardNotes.FirstOrDefault();
            bool allPinned = _cardNotes.Count > 0 && _cardNotes.All(n => n.Pinned);
            string? color = SharedValue(_cardNotes.Select(n => n.TitleColor));
            Brush? titleBrush = color is { Length: > 0 } ? TryBrush(color) : null;

            // Header: the note's name, when it changed and what it is; a selection counts itself.
            string title = one ? DisplayTitle(first!) : string.Format(Loc("Str_Card_NotesCount"), _cardNotes.Count);
            string? group = SharedValue(_cardNotes.Select(n => n.Notebook));
            string where = group == null ? Loc("Str_Card_SeveralGroups")
                : group.Length == 0 ? Loc("Str_Card_NoGroup")
                : group.Replace(NoteStore.GroupSep, " / ");
            string meta = one
                ? string.Format(Loc("Str_Card_Edited"), first!.ModifiedDisplay) + "  ·  " +
                  Loc(first.IsMarkdown ? "Str_Card_Markdown" : "Str_Card_Rich")
                : where;

            NoteCards.Prepare(NoteCard);
            NoteCard.Items.Add(NoteCards.Header(NoteCard, title, meta, Loc("Str_Ctx_DeleteNote"), "Delete",
                () => DeleteNote_Click(this, new RoutedEventArgs()), _cardNotes.Count > 0, titleBrush));

            // The color list: the family swatch row in one flyout row, so a swatch is one click away.
            var swatches = new KillerFamily.Controls.SwatchRow
            {
                NoneName = Loc("Str_Card_NoColor"),
                PickerName = Loc("Str_Card_MoreColors") + " (Ctrl+Shift+C)",
                Margin = new Thickness(4),
            };
            swatches.Show(ColorPickerDialog.UserSwatches(), color);
            var notes = _cardNotes;
            swatches.ColorChosen += hex => { CardBuilder.Close(swatches); NoteCard.IsOpen = false; ApplyTitleColorToNotes(notes, hex); };
            swatches.PickRequested += () => { CardBuilder.Close(swatches); NoteCard.IsOpen = false; PickTitleColorForNotes(notes); };

            CardTile Act(string id, string glyph, string caption, string keys, RoutedEventHandler run, bool enabled = true) =>
                new(glyph, Loc(caption), keys, () => run(this, new RoutedEventArgs()), false, enabled) { Id = id };

            var tiles = new List<CardTile>
            {
                Act("pin", "", allPinned ? "Str_Card_Unpin" : "Str_Card_Pin", "Alt+P", PinNote_Click) with { Checked = allPinned },
                Act("share", "", "Str_Card_Share", "Alt+S", ShareNote_Click, one),
                Act("export", "", "Str_Card_Export", "F8", ExportNoteAs_Click, one),
                Act("history", "", "Str_Card_History", "Alt+H", History_Click, one),
                Act("duplicate", "", "Str_Card_Duplicate", "Alt+Shift+U", DuplicateNote_Click, one),
                NoteCards.Flyout("", Loc("Str_Card_Color"), null, new object[] { CardBuilder.Slot(swatches, new Thickness(0)) },
                    _cardNotes.Count > 0, "color"),
                NoteCards.Flyout("", Loc("Str_Card_Tags"), "Alt+K", TagsMenu.Items.Cast<object>().ToList(), _cardNotes.Count > 0, "tags"),
                NoteCards.Flyout("", Loc("Str_Card_Group"), "Alt+G", GroupMenu.Items.Cast<object>().ToList(), _cardNotes.Count > 0, "group"),
                // Format: one note only. A mixed selection has no single direction, and converting a
                // batch silently both ways is not something to do behind one click.
                Act("markdown", "", "Str_Card_Markdown", "Alt+F", ConvertFormat_Click, one) with { Checked = one && first!.IsMarkdown },
                Act("template", "", "Str_Ctx_SaveAsTemplate", "Alt+Shift+T", SaveAsTemplate_Click, one),
            };
            NoteCard.Items.Add(NoteCards.Editable(NoteCard, "Note", ["pin"], ["template"], tiles));
        }

        /// <summary>The trash rows' card (Trash.cs): Restore and Delete permanently, nothing else.</summary>
        private void FillTrashNoteCard(List<Note> selected)
        {
            NoteCards.Prepare(NoteCard);
            string title = selected.Count == 1 ? DisplayTitle(selected[0]) : string.Format(Loc("Str_Card_NotesCount"), selected.Count);
            NoteCard.Items.Add(NoteCards.Header(NoteCard, title));
            NoteCard.Items.Add(NoteCards.Grid(NoteCard, new[]
            {
                new CardTile("", Loc("Str_Ctx_Restore"), "Ctrl+F8", () => RestoreNote_Click(this, new RoutedEventArgs())) { Id = "restore" },
                new CardTile("", Loc("Str_Ctx_DeleteForever"), "Ctrl+F9", () => DeleteForever_Click(this, new RoutedEventArgs())) { Id = "deleteforever" },
            }));
        }

        private string DisplayTitle(Note n) => n.Title.Length > 0 ? n.Title : Loc("Str_Untitled");

        /// <summary>The one value every item shares, or null when they differ.</summary>
        private static string? SharedValue(IEnumerable<string> values)
        {
            string? seen = null;
            foreach (var v in values)
            {
                if (seen == null) seen = v;
                else if (!string.Equals(seen, v, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return seen ?? "";
        }

        private static Brush? TryBrush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch (FormatException) { return null; }
        }

        // ---- Title color for one note or many ----

        private void ApplyTitleColorToNotes(List<Note> notes, string hex)
        {
            if (notes.Count == 0 || RefuseReadOnly()) return;   // Notes.Document.cs
            var snap = notes.Select(n => (n.Id, n.TitleColor)).ToList();
            foreach (var n in notes) SetNoteTitleColor(n, hex);   // Notes.Crud.cs
            PushUndo(() =>
            {
                foreach (var (id, was) in snap) NoteStore.SetTitleColor(id, was);
                RefreshList(preserveScroll: true);
                if (_notes.FirstOrDefault(x => x.Id == _currentId) is Note open) ApplyTitleColor(open);
                GroupsChanged?.Invoke();
            });
            GroupsChanged?.Invoke();
        }

        /// <summary>The picker box: one note keeps its live preview (TitleColorPick_Click);
        /// a selection picks once and applies the result to all of them.</summary>
        private void PickTitleColorForNotes(List<Note> notes)
        {
            if (notes.Count == 1) { PickTitleColor(notes[0]); return; }
            if (notes.Count == 0 || !NoteStore.IsOpen || RefuseReadOnly()) return;
            var initial = SharedValue(notes.Select(n => n.TitleColor)) is { Length: > 0 } hex && TryBrush(hex) is SolidColorBrush sb
                ? sb.Color
                : ((TryFindResource("TextBrush") as SolidColorBrush)?.Color).GetValueOrDefault();
            var dlg = new ColorPickerDialog(GroupDialogOwner, initial);
            dlg.ShowDialog();
            if (dlg.Confirmed) ApplyTitleColorToNotes(notes, KillerFamily.Controls.SwatchRow.Hex(dlg.SelectedColor));
        }

        // ---- Duplicate and Save as template (new in 1.7.0) ----

        private Note? SingleCardNote()
        {
            var notes = SidebarSelection();
            return notes.Count == 0 ? _notes.FirstOrDefault(n => n.Id == _currentId && !n.IsDeleted) : SingleLiveNote(notes);
        }

        /// <summary>An exact copy beside the original, opened. Ctrl+Z removes the copy.</summary>
        private void DuplicateNote_Click(object sender, RoutedEventArgs e)
        {
            if (SingleCardNote() is not Note n || RefuseReadOnly()) return;
            long sourceId = n.Id;
            string title = string.Format(Loc("Str_Card_CopyTitle"), DisplayTitle(n));
            long id = CreateFromTemplate(sourceId, title, n.Notebook.Length > 0 ? n.Notebook : null, open: true, expand: false);
            if (id < 0) return;
            var original = NoteStore.CaptureRow(id)!;
            FlashStatus(Loc("Str_St_Duplicated"));
            PushUndo(() => RemoveCreatedNote(id, sourceId, original));
        }

        /// <summary>Copies the note into the templates group, making that group first when the
        /// database has none. Ctrl+Z removes the copy and, if it made one, the group setting.</summary>
        private void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (SingleCardNote() is not Note n || RefuseReadOnly()) return;
            string? group = TemplatesGroupPath();   // Templates.cs
            bool madeGroup = group == null;
            if (group == null) group = Loc("Str_Card_TemplatesGroupName");
            bool createdGroup = !NoteStore.ListGroups().Any(g => string.Equals(g.Name, group, StringComparison.OrdinalIgnoreCase));
            long id = CreateFromTemplate(n.Id, n.Title, group, open: false, expand: false);
            if (id < 0) return;
            var original = NoteStore.CaptureRow(id)!;
            if (madeGroup) SetDbScopedSetting(TemplatesGroupSetting, group);
            long roleRevision = GroupSettingRevision(TemplatesGroupSetting);
            RefreshList(preserveScroll: true);
            FlashStatus(string.Format(Loc("Str_St_SavedAsTemplate"), group.Replace(NoteStore.GroupSep, " / ")));
            PushUndo(() =>
            {
                if (!RemoveCreatedNote(id, -1, original)) return;
                if (madeGroup && GroupSettingRevision(TemplatesGroupSetting) == roleRevision)
                    SetDbScopedSetting(TemplatesGroupSetting, null);
                if (createdGroup && !IsTemplatesGroup(group) && !IsDailyGroup(group) &&
                    !NoteStore.List().Concat(NoteStore.ListTrash()).Any(n => string.Equals(n.Notebook, group, StringComparison.OrdinalIgnoreCase)) &&
                    !NoteStore.ListGroups().Any(g => g.Name.StartsWith(group + NoteStore.GroupSep, StringComparison.OrdinalIgnoreCase)))
                    NoteStore.DeleteGroup(group);
                RefreshList(preserveScroll: true);
                GroupsChanged?.Invoke();
            });
        }

        /// <summary>Removes an untouched copy or trashes an edited copy after saving its editor.
        /// The editor leaves the copy before removal so it cannot save to a missing note.</summary>
        private bool RemoveCreatedNote(long id, long reopen, NoteStore.NoteRow original)
        {
            bool wasOpen = _currentId == id;
            if (wasOpen)
            {
                if (!SaveCurrentNote(refreshList: false)) throw new ActionUndoRefusedException();
                _saveTimer.Stop();
                CloseCurrentNote();
                ShowEditor(false);
            }
            NoteStore.RemoveCreatedCopy(id, original);
            RefreshList(preserveScroll: true);
            if (wasOpen && reopen >= 0 && _notes.Any(x => x.Id == reopen))
            {
                OpenNote(reopen);
                SelectNoteInList(reopen);   // WikiLinkNav.cs
            }
            else if (wasOpen) OpenStartupNote();
            GroupsChanged?.Invoke();
            return true;
        }
    }
}
