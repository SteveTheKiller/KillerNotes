namespace KillerPDF
{
    // The shared picker intentionally retains its source namespace. This tiny bridge maps
    // its persisted layout settings onto KillerNotes while both compile into this assembly.
    internal static class App
    {
        internal static string? GetSetting(string name) => KillerNotes.App.GetSetting(name);
        internal static void SetSetting(string name, string value) => KillerNotes.App.SetSetting(name, value);
    }
}

namespace KillerPDF.Controls
{
    internal static class KillerDialog
    {
        internal static System.Windows.MessageBoxResult Show(System.Windows.Window owner,
            string message, string title, System.Windows.MessageBoxButton buttons,
            System.Windows.MessageBoxImage image) =>
            System.Windows.MessageBox.Show(owner, message, title, buttons, image);
    }
}

namespace KillerShell.Services
{
    internal static class ThemeManager
    {
        internal static string? GetSetting(string name) => KillerNotes.App.GetSetting("Folder" + name);
        internal static void SetSetting(string name, string value) => KillerNotes.App.SetSetting("Folder" + name, value);
    }

    internal static class ShellIcons
    {
        internal static System.Windows.Media.ImageSource? Small(string path, bool isFolder) =>
            KillerPDF.Services.ShellIcons.Small(path, isFolder);
        internal static System.Windows.Media.ImageSource? Large(string path, bool isFolder) =>
            KillerPDF.Services.ShellIcons.Large(path, isFolder);
    }
}

namespace KillerShell
{
    internal static class MainWindow
    {
        internal static void ApplyThemeBorder(System.Windows.Window _) { }
    }
}

namespace KillerShell.Shell
{
    // Namespace marker required by the shared folder-picker source.
    internal static class PickerShellBridge { }
}
