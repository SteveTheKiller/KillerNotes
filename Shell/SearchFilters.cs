// ═══════════════════════════════════════════════════════════
//  SEARCH FILTERS  -  the funnel beside the title-bar search box
// ═══════════════════════════════════════════════════════════
//
// 1.6.0, KN-P06. One button (Alt+Shift+F) for everything the search box understands beyond
// words, so the syntax never has to be remembered: it inserts tag:, group:, title: and date
// filters and toggles in:trash (Services/SearchQuery.cs), and it saves and recalls searches,
// which are kept in the notepad (NoteStore saved_searches). The search box stays the one place a
// search lives: every choice here only edits its text.

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private void SearchFilter_Click(object sender, RoutedEventArgs e)
        {
            if (!NoteStore.IsOpen) return;
            SetTitleSearchExpanded(true);
            TitleNoteSearch.UpdateLayout();
            var menu = new ContextMenu { PlacementTarget = SearchFilterBtn, Placement = PlacementMode.Bottom };
            System.Windows.Automation.AutomationProperties.SetName(menu, Loc("Str_TT_SearchFilters"));

            var tags = new MenuItem { Header = Loc("Str_Search_FilterTag") };
            foreach (var (name, _) in NoteStore.ListTags())
                tags.Items.Add(Item(name, () => AppendSearchToken("tag:" + QuoteToken(name))));
            tags.IsEnabled = tags.Items.Count > 0;
            menu.Items.Add(tags);

            var groups = new MenuItem { Header = Loc("Str_Search_FilterGroup") };
            foreach (var (path, _, _, _) in NoteStore.ListGroupTree())
            {
                string shown = path.Replace(NoteStore.GroupSep, " / ");
                groups.Items.Add(Item(shown, () => AppendSearchToken("group:" + QuoteToken(path.Replace(NoteStore.GroupSep, "/")))));
            }
            groups.IsEnabled = groups.Items.Count > 0;
            menu.Items.Add(groups);

            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            menu.Items.Add(Item(Loc("Str_Search_FilterTitle"), () => AppendSearchToken("title:", select: 0)));
            menu.Items.Add(Item(Loc("Str_Search_FilterAfter"), () => AppendSearchToken("after:" + today, select: today.Length)));
            menu.Items.Add(Item(Loc("Str_Search_FilterBefore"), () => AppendSearchToken("before:" + today, select: today.Length)));
            string month = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            menu.Items.Add(Item(Loc("Str_Search_FilterCreated"), () => AppendSearchToken("created:" + month, select: month.Length)));
            var trash = new MenuItem { Header = Loc("Str_Search_FilterTrash"), IsCheckable = true, IsChecked = SearchQuery.Parse(SearchBox.Text).InTrash };
            trash.Click += (_, _) => ToggleTrashFilter();
            menu.Items.Add(trash);

            menu.Items.Add(new Separator());
            var saved = NoteStore.ListSavedSearches();
            foreach (var (name, query) in saved)
            {
                var row = Item(name, () => { SearchBox.Text = query; FocusSearchEnd(); });
                row.ToolTip = query;
                menu.Items.Add(row);
            }
            var save = Item(Loc("Str_Search_Save"), SaveCurrentSearch);
            save.IsEnabled = !SearchQuery.Parse(SearchBox.Text).IsEmpty && !NoteStore.IsReadOnly;
            menu.Items.Add(save);
            var remove = new MenuItem { Header = Loc("Str_Search_Remove") };
            foreach (var (name, _) in saved)
                remove.Items.Add(Item(name, () =>
                {
                    NoteStore.DeleteSavedSearch(name);
                    FlashStatus(string.Format(Loc("Str_St_SearchRemoved"), name));
                }));
            remove.IsEnabled = remove.Items.Count > 0 && !NoteStore.IsReadOnly;
            menu.Items.Add(remove);

            menu.Opened += (_, _) => menu.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (menu.IsOpen) menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled)?.Focus();
            }), DispatcherPriority.Input);
            menu.IsOpen = true;
        }

        private static MenuItem Item(string header, Action run)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => run();
            return item;
        }

        private static string QuoteToken(string value) => value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;

        /// <summary>Adds a filter at the end of the search, then puts the caret there, selecting the
        /// last <paramref name="select"/> characters (a date to overwrite) when asked.</summary>
        private void AppendSearchToken(string token, int select = -1)
        {
            string text = SearchBox.Text.TrimEnd();
            SearchBox.Text = (text.Length > 0 ? text + " " : "") + token;
            FocusSearchEnd();
            if (select > 0)
            {
                SearchBox.SelectionStart = SearchBox.Text.Length - select;
                SearchBox.SelectionLength = select;
            }
        }

        private void ToggleTrashFilter()
        {
            string text = SearchBox.Text;
            var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bool on = tokens.Any(t => t.Equals("in:trash", StringComparison.OrdinalIgnoreCase));
            SearchBox.Text = on
                ? string.Join(" ", tokens.Where(t => !t.Equals("in:trash", StringComparison.OrdinalIgnoreCase)))
                : (text.TrimEnd() + " in:trash").TrimStart();
            FocusSearchEnd();
        }

        private void FocusSearchEnd()
        {
            SetTitleSearchExpanded(true);
            TitleNoteSearch.UpdateLayout();
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }

        private void SaveCurrentSearch()
        {
            string query = SearchBox.Text.Trim();
            if (SearchQuery.Parse(query).IsEmpty) return;
            var dlg = new InputDialog(Loc("Str_Search_SaveHead"), query.Length > 60 ? query.Substring(0, 60) : query, Loc("Str_Btn_Save")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed || string.IsNullOrWhiteSpace(dlg.Value)) return;
            NoteStore.SaveSearch(dlg.Value, query);
            FlashStatus(string.Format(Loc("Str_St_SearchSaved"), dlg.Value.Trim()));
        }
    }
}
