namespace KillerNotes.Shell
{
    // APP-OWNED: KillerNotes' side of the family tray (Shell/CloseToTray.cs).
    public partial class MainWindow
    {
        // Asked before a tray Exit and when Windows ends the session. The open note must save
        // first; when it cannot, the app stays and the window comes back to show why.
        private bool TrayCanExit()
        {
            if (SaveCurrentNote(refreshList: false)) return true;
            _tray.ShowWindow();
            return false;
        }
    }
}
