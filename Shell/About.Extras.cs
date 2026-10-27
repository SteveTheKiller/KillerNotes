using System.Windows;
using KillerNotes.Features.About;

namespace KillerNotes.Shell
{
    /// <summary>
    /// APP-OWNED: the rows KillerNotes' About card adds to the shared core (Shell/About.cs).
    /// </summary>
    public partial class MainWindow : IAboutStartup, IAboutTray
    {
        bool IAboutStartup.StartWithWindows { set => AboutStartWithWindows.IsChecked = value; }
        bool IAboutTray.CloseToTray         { set => AboutCloseToTray.IsChecked = value; }

        internal bool SaveOpenNoteForUpdate() => SaveCurrentNote(refreshList: false);

        private void AboutStartWithWindows_Click(object sender, RoutedEventArgs e)
            => _about.SetStartWithWindows(AboutStartWithWindows.IsChecked == true);

        private void AboutCloseToTray_Click(object sender, RoutedEventArgs e)
            => _about.SetCloseToTray(AboutCloseToTray.IsChecked == true);
    }
}
