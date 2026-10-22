using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    // The note text fades out at the top and bottom of the pane while there is more to scroll that
    // way, the same hint the sidebar's note list gives (Sidebar.cs UpdateNotesFade). The fade is an
    // opacity mask on the editor's viewport, so the grain and pane color behind it show through and
    // the scrollbar never fades. Themes with EdgeFadeOpacity 0 (98SE) get none.
    public partial class MainWindow
    {
        private const double EditorFadeHeight = 28;
        private ScrollViewer? _editorScroll;

        private void InitEditorFade()
        {
            Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateEditorFade()));
            Editor.SizeChanged += (_, _) => UpdateEditorFade();
        }

        private void UpdateEditorFade()
        {
            _editorScroll ??= FindDescendant<ScrollViewer>(Editor);
            if (_editorScroll?.Template?.FindName("PART_ScrollContentPresenter", _editorScroll) is not ScrollContentPresenter viewport) return;
            double height = viewport.ActualHeight;
            bool fadeOff = Application.Current?.TryFindResource("EdgeFadeOpacity") is double op && op <= 0;
            bool overflow = _editorScroll.ScrollableHeight > 0.5;
            bool fadeTop = !fadeOff && overflow && _editorScroll.VerticalOffset > 0.5;
            bool fadeBottom = !fadeOff && overflow && _editorScroll.VerticalOffset < _editorScroll.ScrollableHeight - 0.5;
            if ((!fadeTop && !fadeBottom) || height <= EditorFadeHeight * 2)
            {
                viewport.OpacityMask = null;
                return;
            }
            double edge = EditorFadeHeight / height;
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            mask.GradientStops.Add(new GradientStop(fadeTop ? Colors.Transparent : Colors.Black, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, edge));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - edge));
            mask.GradientStops.Add(new GradientStop(fadeBottom ? Colors.Transparent : Colors.Black, 1));
            mask.Freeze();
            viewport.OpacityMask = mask;
        }
    }
}
