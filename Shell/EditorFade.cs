using System.Windows;
using System.Windows.Controls;

namespace KillerNotes.Shell
{
    // The note text fades out at the top and bottom of the pane while there is more to scroll that
    // way, the same hint the sidebar's note list gives. The fades are overlays painted as the pane
    // itself (MainWindow.xaml EditorTopFade / EditorBottomFade), so the scrollbar never fades.
    // Themes with EdgeFadeOpacity 0 (98SE) show none.
    public partial class MainWindow
    {
        private void InitEditorFade()
        {
            Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateEditorFade()), true);
            Editor.SizeChanged += (_, _) => UpdateEditorFade();
        }

        private void UpdateEditorFade()
        {
            double scrollable = Editor.ExtentHeight - Editor.ViewportHeight;
            bool overflow = scrollable > 0.5 && Editor.ViewportHeight > 56;
            EditorTopFade.Visibility = overflow && Editor.VerticalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;
            EditorBottomFade.Visibility = overflow && Editor.VerticalOffset < scrollable - 0.5 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
