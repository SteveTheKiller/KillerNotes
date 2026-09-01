// ═══════════════════════════════════════════════════════════
//  DAILY NOTES  -  today's note, one key away
// ═══════════════════════════════════════════════════════════
//
// Alt+D opens the note titled with today's date (2026-09-01: sortable, unambiguous, and the
// form every other daily-notes app settled on) and creates it when it does not exist yet. New
// day notes go in the daily-notes group, which is a group the user marks from its header menu
// or, failing that, a "Daily notes" group made on first use. When the templates group holds a
// note titled "Daily", that is what the new day note is copied from, placeholders and all
// (Templates.cs), so a standing agenda shows up already filled in.
//
// The title is looked up across the whole notebook, not just the group: if today's note was
// filed somewhere else by hand, that is still today's note.

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private const string DailyGroupSetting = "DailyGroup";

        private static string TodayTitle() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static bool IsDailyGroup(string path) =>
            string.Equals(DbScopedSetting(DailyGroupSetting), path, StringComparison.OrdinalIgnoreCase);

        // Group header menu toggle (Tag="daily"; GroupHeader_RightDown sets its check).
        private void DailyGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            bool on = IsDailyGroup(g.Path);
            SetDbScopedSetting(DailyGroupSetting, on ? null : g.Path);
            FlashStatus(on ? Loc("Str_St_DailyOff") : string.Format(Loc("Str_St_DailyOn"), g.Name));
        }

        private void TodayNote_Click(object sender, RoutedEventArgs e) => OpenTodayNote();

        /// <summary>Alt+D (Shortcuts.cs) and the sidebar menu: today's note, made if need be.</summary>
        private void OpenTodayNote()
        {
            if (!NoteStore.IsOpen) return;
            string title = TodayTitle();

            long id = NoteStore.ResolveTitle(title);
            if (id >= 0)
            {
                SaveCurrentNote(refreshList: false);
                if (SearchBox.Text.Length > 0) SearchBox.Text = "";   // a filter could be hiding it
                OpenNote(id);
                SelectNoteInList(id);   // WikiLinkNav.cs
                Editor.Focus();
                return;
            }

            if (NoteStore.IsReadOnly)
            {
                FlashStatus(string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner));
                return;
            }

            // The group: the one marked from a header, else a "Daily notes" group made now and
            // remembered. A marked group that has since been renamed or deleted counts as unset.
            string? group = DbScopedSetting(DailyGroupSetting);
            if (group == null || !NoteStore.ListGroupTree().Any(x => string.Equals(x.Path, group, StringComparison.OrdinalIgnoreCase)))
            {
                group = Loc("Str_Grp_Daily");
                NoteStore.AddGroup(group);   // an existing group of that name just gets used
                SetDbScopedSetting(DailyGroupSetting, group);
            }

            // Seeded from the template named "Daily" when there is one, else empty.
            string tplName = Loc("Str_DailyTemplate");
            var tpl = TemplateNotes().FirstOrDefault(t =>
                string.Equals(t.Title, tplName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Title, "Daily", StringComparison.OrdinalIgnoreCase));

            SaveCurrentNote(refreshList: false);
            if (tpl != null)
            {
                id = CreateFromTemplate(tpl.Id, title, group, open: false);   // Templates.cs
            }
            else
            {
                id = NoteStore.Create(title);
                if (id >= 0) NoteStore.SetNoteGroup(id, group);
            }
            if (id < 0) return;

            NoteStore.SetGroupCollapsed(group, false);   // reveal the new row
            SearchBox.Text = "";
            RefreshList();
            OpenNote(id);
            SelectNoteInList(id);
            Editor.Focus();
            FlashStatus(Loc("Str_St_TodayCreated"));
        }
    }
}
