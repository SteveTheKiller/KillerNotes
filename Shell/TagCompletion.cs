using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private Popup? _tagPopup;
        private ListBox? _tagList;
        private TextBlock? _tagStatus;
        private TextPointer? _tagCaret;
        private int _tagQueryLength;
        private bool _tagUpdateQueued, _tagCompleting, _tagDismissed;

        private void InitTagCompletion()
        {
            _tagList = new ListBox
            {
                Style = (Style)FindResource("NavigationList"), MaxHeight = 220, MinWidth = 220, FontSize = 12
            };
            _tagList.SetResourceReference(ForegroundProperty, "TextBrush");
            ScrollViewer.SetVerticalScrollBarVisibility(_tagList, ScrollBarVisibility.Auto);
            AutomationProperties.SetName(_tagList, Loc("Str_TagComplete_Title"));
            _tagList.PreviewKeyDown += Shortcuts_PreviewKeyDown;
            _tagList.PreviewTextInput += (_, e) =>
            {
                RestoreTagCaret();
                Editor.Selection.Text = e.Text;
                Editor.CaretPosition = Editor.Selection.End;
                e.Handled = true;
            };
            _tagList.PreviewMouseLeftButtonUp += (_, _) => AcceptTagCompletion();
            _tagStatus = new TextBlock { FontSize = 11, Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
            _tagStatus.SetResourceReference(ForegroundProperty, "DimTextBrush");
            AutomationProperties.SetLiveSetting(_tagStatus, AutomationLiveSetting.Polite);
            var panel = new StackPanel();
            panel.Children.Add(_tagStatus);
            panel.Children.Add(_tagList);
            var border = new Border { Child = panel, BorderThickness = new Thickness(1), Padding = new Thickness(2) };
            border.SetResourceReference(Border.BackgroundProperty, "MenuBackgroundBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");
            _tagPopup = new Popup
            {
                Child = border, PlacementTarget = Editor, Placement = PlacementMode.RelativePoint,
                StaysOpen = false, AllowsTransparency = true
            };
            Editor.TextChanged += (_, _) =>
            {
                if (_tagCompleting || _loadingNote) return;
                _tagDismissed = false;
                QueueTagCompletion();
            };
            Editor.SelectionChanged += (_, _) => { if (_tagPopup.IsOpen) QueueTagCompletion(); };
            Editor.LostKeyboardFocus += (_, _) => QueueTagCompletion();
            _tagList.LostKeyboardFocus += (_, _) => QueueTagCompletion();
        }

        private void QueueTagCompletion()
        {
            if (_tagUpdateQueued || _tagCompleting) return;
            _tagUpdateQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _tagUpdateQueued = false;
                UpdateTagCompletion();
            }), DispatcherPriority.Input);
        }

        private void UpdateTagCompletion()
        {
            if (_tagPopup == null || _tagList == null || _tagStatus == null) return;
            if (_tagList.IsKeyboardFocusWithin) return;
            if (_loadingNote || _tagDismissed || !Editor.IsKeyboardFocusWithin || !Editor.Selection.IsEmpty
                || Editor.IsReadOnly || NoteStore.IsReadOnly)
            {
                _tagPopup.IsOpen = false;
                return;
            }
            string text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
            int offset = new TextRange(Editor.Document.ContentStart, Editor.CaretPosition).Text.Length;
            string? query = BodyTags.CompletionQuery(text, offset);
            if (query == null) { _tagPopup.IsOpen = false; return; }
            var hits = BodyTags.Suggestions(TagManager.Order.Select(t => t.Name), query);
            if (hits.Length == 0) { _tagPopup.IsOpen = false; return; }
            _tagCaret = Editor.CaretPosition;
            _tagQueryLength = query.Length;
            _tagList.ItemsSource = hits;
            _tagList.SelectedIndex = 0;
            Rect rect = _tagCaret.GetCharacterRect(LogicalDirection.Forward);
            _tagPopup.HorizontalOffset = rect.Left;
            _tagPopup.VerticalOffset = rect.Bottom + 2;
            bool opening = !_tagPopup.IsOpen;
            _tagPopup.IsOpen = true;
            string status = string.Format(Loc("Str_TagComplete_Count"), hits.Length);
            if (opening || _tagStatus.Text != status)
            {
                _tagStatus.Text = status;
                AutomationProperties.SetName(_tagStatus, status);
                UIElementAutomationPeer.CreatePeerForElement(_tagStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        private bool TagCompletionKey(KeyEventArgs e)
        {
            if (_tagPopup?.IsOpen != true || _tagList == null) return false;
            if (!Editor.IsKeyboardFocusWithin && !_tagList.IsKeyboardFocusWithin) return false;
            if (Keyboard.Modifiers != ModifierKeys.None) return false;
            switch (e.Key)
            {
                case Key.Down when Editor.IsKeyboardFocusWithin:
                    _tagList.UpdateLayout();
                    if (_tagList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item) item.Focus();
                    break;
                case Key.Tab:
                    AcceptTagCompletion();
                    break;
                case Key.Escape:
                    _tagDismissed = true;
                    _tagPopup.IsOpen = false;
                    RestoreTagCaret();
                    break;
                case Key.Back when _tagList.IsKeyboardFocusWithin:
                    RestoreTagCaret();
                    EditingCommands.Backspace.Execute(null, Editor);
                    break;
                default:
                    return false;
            }
            e.Handled = true;
            return true;
        }

        private void RestoreTagCaret()
        {
            Editor.Focus();
            if (_tagCaret != null) Editor.CaretPosition = _tagCaret;
        }

        private void AcceptTagCompletion()
        {
            if (_tagList?.SelectedItem is not string tag || _tagCaret == null) return;
            var start = PointerBack(_tagCaret, _tagQueryLength);
            if (start == null) return;
            _tagCompleting = true;
            try
            {
                _tagPopup!.IsOpen = false;
                Editor.BeginChange();
                try
                {
                    var range = new TextRange(start, _tagCaret);
                    range.Text = tag;
                    Editor.Focus();
                    Editor.CaretPosition = range.End;
                }
                finally { Editor.EndChange(); }
                _tagDismissed = true;
            }
            finally { _tagCompleting = false; }
            MarkDirty();
        }
    }
}
