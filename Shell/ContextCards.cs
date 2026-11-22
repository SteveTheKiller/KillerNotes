using System.Windows;
using System.Windows.Controls;
using KillerFamily.Controls;

namespace KillerNotes.Shell
{
    // ============================================================
    // The small right-click cards that were plain menu rows before 1.7.1: the sidebar sort button,
    // the search icon, the dictation rail button and the mentions strip. Each is a NoteCards tile
    // card (family card rules: tiles only, a dimmed tile keeps its spot), the way the Trash header's
    // card is. The text boxes' Cut, Copy and Paste card is Controls/TextFieldCard.cs.
    //
    // Each element keeps an empty card from the XAML on, and its ContextMenuOpening fills that same
    // card. WPF decides whether a right-click or Shift+F10 opens anything from the card the element
    // already has, so a card handed over inside ContextMenuOpening came too late and the first
    // right-click opened nothing.
    // ============================================================
    public partial class MainWindow
    {
        /// <summary>The sort card: the three sorts with the active one on, then Reverse order, dimmed for custom
        /// order, which has no direction. Picking the active sort again reverses it, as the buttons do.</summary>
        internal ContextMenu BuildSortCard() => FillSortCard(NoteCards.Menu());

        private ContextMenu FillSortCard(ContextMenu card) => Refill(card, new[]
        {
            SortTile("created", "", "Str_Sort_Created", "Alt+4"),
            SortTile("title", "", "Str_Sort_Title", "Alt+5"),
            SortTile("custom", "", "Str_Sort_Custom", "Alt+6"),
            new CardTile("", Loc("Str_Ctx_ReverseOrder"), "Alt+7", ReverseSort, Enabled: _sortField != "custom") { Id = "reverse" },
        });

        /// <summary>Reverse order (Alt+7): flips the active sort's direction; custom order has none.</summary>
        private void ReverseSort()
        {
            if (_sortField != "custom") SetSort(_sortField, defaultAsc: true);   // same field: SetSort flips the direction
        }

        private CardTile SortTile(string field, string glyph, string caption, string keys) =>
            new(glyph, Loc(caption), keys, () => SetSort(field, defaultAsc: DefaultAscending(field)))
            { Id = field, Checked = _sortField == field };

        private void SortBtn_ContextMenuOpening(object sender, ContextMenuEventArgs e) => FillSortCard(SortBtn.ContextMenu);

        /// <summary>The search icon's card: show or hide the search bar (Alt+F3).</summary>
        internal ContextMenu BuildSearchIconCard() => FillSearchIconCard(NoteCards.Menu());

        private ContextMenu FillSearchIconCard(ContextMenu card)
        {
            Refill(card, new[]
            {
                new CardTile(_titleSearchAutoHide ? "" : "",
                    Loc(_titleSearchAutoHide ? "Str_Search_ShowBar" : "Str_Search_HideBar"), "Alt+F3",
                    () => SearchBarVisibility_Click(this, new RoutedEventArgs())) { Id = "searchbar" },
            });
            card.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            return card;
        }

        private void SearchIcon_ContextMenuOpening(object sender, ContextMenuEventArgs e) => FillSearchIconCard(SearchIconButton.ContextMenu);

        /// <summary>The dictation rail button's card: the speech model chooser (Alt+Q).</summary>
        internal ContextMenu BuildDictationCard() => FillDictationCard(NoteCards.Menu());

        /// <summary>Fills the rail button's card in place; RailMenu_Opening (ThemeFlyout.cs) then places and opens it.</summary>
        internal ContextMenu FillDictationCard(ContextMenu card) => Refill(card, new[]
        {
            new CardTile("", Loc("Str_Whisper_Menu"), "Alt+Q", () => SpeechModel_Click(this, new RoutedEventArgs())) { Id = "speechmodel" },
        });

        /// <summary>The mentions strip's card: hide the strip (Alt+M).</summary>
        internal ContextMenu BuildMentionsCard() => FillMentionsCard(NoteCards.Menu());

        private ContextMenu FillMentionsCard(ContextMenu card) => Refill(card, new[]
        {
            new CardTile("", Loc("Str_Ctx_HideMentions"), "Alt+M", ToggleBacklinkBar) { Id = "hidementions" },
        });

        private void BacklinkBar_ContextMenuOpening(object sender, ContextMenuEventArgs e) => FillMentionsCard(BacklinkBar.ContextMenu);

        /// <summary>Gives a card (a fresh one or the XAML's empty one) the card look and refills its tiles.</summary>
        private static ContextMenu Refill(ContextMenu card, CardTile[] tiles)
        {
            NoteCards.Prepare(card);
            NoteCards.Fill(card, null, tiles);
            return card;
        }
    }
}
