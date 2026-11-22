using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KillerFamily.Controls;
using KillerNotes.Services;
using KillerNotes.Shell;

namespace KillerNotes.Controls
{
    /// <summary>
    /// Every text box's right-click card: Cut, Copy, Paste and Select all as tiles (family card rules, NoteCards),
    /// in place of WPF's stock menu or plain rows. Class handlers reach every TextBox and PasswordBox, the ones
    /// built in code and in dialogs too. A box gets the card when it has no menu of its own or carries the
    /// app's TextInputContextMenu (AppStyles.xaml, tagged here); the editor's own card (EditorCard.cs) is a
    /// RichTextBox and is left alone. The card is filled as it opens, so each tile dims when its command
    /// cannot run, and a tile hands the caret back to its box before it acts.
    /// </summary>
    internal static class TextFieldCard
    {
        /// <summary>The Tag on a menu this class fills (AppStyles.xaml TextInputContextMenu carries it too).</summary>
        internal const string Tag = "TextFieldCard";

        private static bool _registered;

        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            foreach (var type in new[] { typeof(TextBox), typeof(PasswordBox) })
            {
                // Before the stock editor menu: a pointer or keyboard request assigns the card first.
                EventManager.RegisterClassHandler(type, UIElement.PreviewMouseRightButtonDownEvent,
                    new MouseButtonEventHandler((sender, _) => Assign(sender)), handledEventsToo: true);
                EventManager.RegisterClassHandler(type, Keyboard.PreviewKeyDownEvent,
                    new KeyEventHandler((sender, e) =>
                    {
                        if (e.Key == Key.Apps || e.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) Assign(sender);
                    }), handledEventsToo: true);
                EventManager.RegisterClassHandler(type, FrameworkElement.ContextMenuOpeningEvent,
                    new ContextMenuEventHandler((sender, _) => Fill(sender)), handledEventsToo: true);
            }
        }

        private static void Assign(object sender)
        {
            if (sender is Control box && box.ContextMenu == null) box.ContextMenu = new ContextMenu { Tag = Tag };
        }

        private static void Fill(object sender)
        {
            if (sender is Control { ContextMenu: { Tag: Tag } menu } box) Fill(menu, box);
        }

        /// <summary>Fills a text box's card for the box as it is now, for the opening card and for tests.</summary>
        internal static ContextMenu Fill(ContextMenu menu, Control box)
        {
            CardTile Act(string id, string glyph, string caption, string keys, RoutedUICommand command) =>
                new(glyph, LocaleManager.Loc(caption), keys, () => { box.Focus(); command.Execute(null, box); },
                    Enabled: command.CanExecute(null, box)) { Id = id };
            NoteCards.Prepare(menu).Tag = Tag;
            menu.Items.Add(NoteCards.Grid(menu, new[]
            {
                Act("cut", "", "Str_Ctx_Cut", "Ctrl+X", ApplicationCommands.Cut),
                Act("copy", "", "Str_Ctx_Copy", "Ctrl+C", ApplicationCommands.Copy),
                Act("paste", "", "Str_Ctx_Paste", "Ctrl+V", ApplicationCommands.Paste),
                Act("selectall", "", "Str_Ctx_SelectAll", "Ctrl+A", ApplicationCommands.SelectAll),
            }));
            return menu;
        }
    }
}
