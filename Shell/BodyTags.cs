using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private bool OpenBodyTagMenu()
        {
            if (_currentId < 0) return false;
            string text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
            int offset = new TextRange(Editor.Document.ContentStart, Editor.CaretPosition).Text.Length;
            string? tag = BodyTags.At(text, offset);
            if (tag == null) return false;

            SaveCurrentNote(refreshList: false);
            var notes = BodyTags.RecentNotes(NoteStore.List(), tag, _currentId);
            var caret = Editor.CaretPosition;
            bool chosen = false;
            var menu = new ContextMenu { PlacementTarget = Editor, Placement = PlacementMode.Relative };
            Rect rect = caret.GetCharacterRect(LogicalDirection.Forward);
            menu.HorizontalOffset = rect.Left;
            menu.VerticalOffset = rect.Bottom;
            AutomationProperties.SetName(menu, string.Format(Loc("Str_BodyTag_Menu"), tag));
            foreach (var note in notes)
            {
                var item = new MenuItem { Header = note.Title };
                AutomationProperties.SetName(item, note.Title);
                item.Click += (_, _) =>
                {
                    chosen = true;
                    OpenNote(note.Id);
                    SelectNoteInList(note.Id);
                    FocusNoteBody();
                };
                menu.Items.Add(item);
            }
            if (notes.Count == 0)
            {
                string label = string.Format(Loc("Str_BodyTag_Empty"), tag);
                var item = new MenuItem { Header = label };
                AutomationProperties.SetName(item, label);
                menu.Items.Add(item);
            }
            menu.Opened += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (menu.IsOpen) ((MenuItem)menu.Items[0]).Focus();
            }), DispatcherPriority.Input);
            menu.Closed += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (chosen) return;
                Editor.Focus();
                Editor.CaretPosition = caret;
            }), DispatcherPriority.Input);
            menu.IsOpen = true;
            return true;
        }
    }
}
