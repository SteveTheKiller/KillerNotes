namespace KillerNotes.Shell
{
    // APP-OWNED: KillerNotes' side of the family system menu (Shell/SystemMenu.cs).
    public partial class MainWindow
    {
        // Show and handle Windows' access letters (R, M, S, N, X, C) while the menu is open.
        partial void UseSystemMenuAccessKeys(ref bool use) => use = true;
    }
}
