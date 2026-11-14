using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // The note card (1.7.0): the sidebar's right-click menu, rebuilt as a card about the note
    // (MainWindow.xaml NoteCard, styled by the family CardMenu pieces in Controls.xaml).
    //
    // One note: header, the five quick buttons, color, tags, group, format, Save as template
    // and Delete. Several notes: a "12 notes" header and only what applies to all of them -
    // Pin, color, tags, group and Delete. Trashed notes: Restore and Delete forever only
    // (Trash.cs ApplyTrashMenuMode runs first and hides everything else).
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

        /// <summary>Wires the swatch row once; called from the constructor.</summary>
        private void InitNoteCard()
        {
            CardSwatches.ColorChosen += hex =>
            {
                NoteCard.IsOpen = false;
                ApplyTitleColorToNotes(_cardNotes, hex);
            };
            CardSwatches.PickRequested += () =>
            {
                NoteCard.IsOpen = false;
                PickTitleColorForNotes(_cardNotes);
            };
        }

        /// <summary>Called from NotesContextMenu_Opened (Tags.cs) for live notes.</summary>
        private void FillNoteCard(List<Note> selected)
        {
            CardSwatches.NoneName = Loc("Str_Card_NoColor");
            CardSwatches.PickerName = Loc("Str_Card_MoreColors") + " (Ctrl+Shift+C)";
            _cardNotes = [.. selected.Where(n => !n.IsDeleted)];
            bool one = _cardNotes.Count == 1;
            var first = _cardNotes.FirstOrDefault();

            // Header: the note's name, when it changed and what it is; a selection counts itself.
            CardTitle.Text = one ? DisplayTitle(first!) : string.Format(Loc("Str_Card_NotesCount"), _cardNotes.Count);
            CardMeta.Text = one
                ? string.Format(Loc("Str_Card_Edited"), first!.ModifiedDisplay) + "  \u00B7  " +
                  Loc(first.IsMarkdown ? "Str_Card_Markdown" : "Str_Card_Rich")
                : "";
            CardMeta.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
            bool allPinned = _cardNotes.Count > 0 && _cardNotes.All(n => n.Pinned);
            CardPinMark.Visibility = allPinned ? Visibility.Visible : Visibility.Collapsed;

            string? color = SharedValue(_cardNotes.Select(n => n.TitleColor));
            CardColorDot.Fill = color is { Length: > 0 } && TryBrush(color) is Brush b ? b : Brushes.Transparent;
            CardSwatches.Show(ColorPickerDialog.UserSwatches(), color);

            // Quick buttons: Pin names what it will do; the rest only make sense for one note.
            CardPinBtn.Tag = Loc(allPinned ? "Str_Card_Unpin" : "Str_Card_Pin");
            DescribeSurface(CardPinBtn, allPinned ? "Str_Card_Unpin" : "Str_Card_Pin", "Alt+P");
            CardShareBtn.Visibility = CardExportBtn.Visibility = CardHistoryBtn.Visibility =
                CardDuplicateBtn.Visibility = one ? Visibility.Visible : Visibility.Collapsed;

            // Tags row: the tags every selected note shares, or a quiet "None".
            var shared = first == null ? [] : first.Chips
                .Where(c => _cardNotes.All(n => TagManager.HasTag(n, c.Name))).ToList();
            CardTagChips.ItemsSource = shared.Count > 0 ? shared : [NoneChip()];

            // Group row: where the notes live, or "Several groups".
            string? group = SharedValue(_cardNotes.Select(n => n.Notebook));
            CardGroupText.Text = group == null ? Loc("Str_Card_SeveralGroups")
                : group.Length == 0 ? Loc("Str_Card_NoGroup")
                : group.Replace(NoteStore.GroupSep, " / ");

            // Format: one note only. A mixed selection has no single direction, and converting a
            // batch silently both ways is not something to do behind one click.
            CardFormatSlot.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
            if (one)
            {
                CardRichSeg.IsChecked = !first!.IsMarkdown;
                CardMarkdownSeg.IsChecked = first.IsMarkdown;
            }
            CardSaveTemplateItem.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
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

        private TagChip NoneChip()
        {
            var chip = new TagChip { Name = Loc("Str_Card_NoTags"), Background = Brushes.Transparent };
            if (TryFindResource("DimTextBrush") is Brush dim) chip.Foreground = dim;
            return chip;
        }

        // ---- Quick buttons: close the card, then run the row handler they replace ----

        private void RunFromCard(RoutedEventHandler action)
        {
            NoteCard.IsOpen = false;
            action(this, new RoutedEventArgs());
        }

        private void CardPin_Click(object sender, RoutedEventArgs e) => RunFromCard(PinNote_Click);
        private void CardShare_Click(object sender, RoutedEventArgs e) => RunFromCard(ShareNote_Click);
        private void CardExport_Click(object sender, RoutedEventArgs e) => RunFromCard(ExportNoteAs_Click);
        private void CardHistory_Click(object sender, RoutedEventArgs e) => RunFromCard(History_Click);
        private void CardDuplicate_Click(object sender, RoutedEventArgs e) => RunFromCard(DuplicateNote_Click);

        /// <summary>The Rich text / Markdown toggle. Picking the side the note is already on
        /// does nothing; the other side runs the same conversion as Alt+F.</summary>
        private void CardFormat_Click(object sender, RoutedEventArgs e)
        {
            if (_cardNotes.Count != 1) return;
            bool wantMarkdown = ReferenceEquals(sender, CardMarkdownSeg);
            if (wantMarkdown == _cardNotes[0].IsMarkdown) return;
            RunFromCard(ConvertFormat_Click);
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
            RefreshList(preserveScroll: true);
            FlashStatus(string.Format(Loc("Str_St_SavedAsTemplate"), group.Replace(NoteStore.GroupSep, " / ")));
            PushUndo(() =>
            {
                if (!RemoveCreatedNote(id, -1, original)) return;
                if (madeGroup) SetDbScopedSetting(TemplatesGroupSetting, null);
                if (createdGroup &&
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
