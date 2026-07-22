using System.Windows;

namespace KillerNotes
{
    // ============================================================
    // MainWindow - shell composition root.
    //
    // The window is intentionally thin: it wires the pieces in the constructor.
    // Everything else lives in focused partials (family pattern):
    //
    //   UI shell (KillerUI kit): Chrome.cs, ThemeFlyout.cs, About.cs, Anim.cs
    //   Notes:   Notes.cs (list/search/save), Editor.cs (formatting/paste/tables),
    //            Security.cs (password + unlock flow)
    //   Data:    Services/NoteStore.cs (SQLite + FTS5 + SQLCipher)
    // ============================================================
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            VersionLabel.Text = $"v{CurrentVersion}";           // About.cs

            RestoreWindowPlacement();                            // Chrome.cs (size/position from previous run)
            SourceInitialized += MainWindow_SourceInitialized;   // Chrome.cs (taskbar-aware maximize + corners)
            ApplyGrainTexture();                                 // Chrome.cs (paints the shared grain)

            UpdateThemeSwatchSelection();                        // ThemeFlyout.cs
            UpdateAccentSwatches();
            Services.ThemeManager.ThemeChanged += () =>
            {
                UpdateThemeSwatchSelection();
                UpdateAccentSwatches();
            };

            InitEditor();                                        // Editor.cs (paste handler, Ctrl+S)
            InitSidebar();                                       // Sidebar.cs (restore collapsed state)
            InitShortcuts();                                     // Shortcuts.cs (hotkeys + F1 overlay)
            InitAppScale();                                      // AppScale.cs (restore app-wide size)
            InitLineNumbers();                                   // LineNumbers.cs (optional gutter)
            InitDensity();                                       // Density.cs (restore sidebar row density)
            InitFonts();                                         // Fonts.cs (restore header/content fonts)

            Loaded += (_, _) =>
            {
                FadeInContent();                                 // Chrome.cs (RootGrid 0 -> 1)
                // Portable badge (family install system): hidden when running from the
                // installed location, and in demo mode so screenshots stay clean.
                if (App.IsPortable() && !DemoMode) PortableBadge.Visibility = Visibility.Visible;
                // Deferred, NOT called inline: Loaded fires synchronously inside Show(),
                // and cancelling the unlock prompt calls Close() - a reentrant Close()
                // during Show() throws (this was a real crash). Dispatching lets Show()
                // finish first; the prompt appears right after first paint.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OpenDatabase();               // Security.cs (unlock prompt if encrypted)
                    HandlePendingOpenFile();      // Sharing.cs (double-clicked .kndb/.knote)
                    if (DemoMode && DemoFresh) GenerateDemoNotes();   // DemoMode.cs (--demo, fresh db only)
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        // Footer version number -> About overlay (About.cs). Overlays are mutually
        // exclusive, so an open shortcuts overlay goes away first (Shortcuts.cs).
        private void VersionLabel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            HideShortcutsOverlay();
            ShowAboutOverlay();
        }

        // PORTABLE badge Install button (family install system, ported from
        // KillerScan/KillerFind). Notes need no migration: the database lives in
        // %APPDATA%\KillerNotes regardless of where the exe runs from.
        private void Install_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ConfirmDialog(
                Loc("Str_Dlg_InstallMsg"), Loc("Str_Dlg_InstallBullets"), Loc("Str_Btn_DoInstall"),
                check1Label: Loc("Str_Chk_Desktop"), check1Initial: true) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            PortableBadge.Visibility = Visibility.Collapsed;
            SaveCurrentNote(refreshList: false);   // Notes.cs - flush the open note before the relaunch
            App.InstallAndRelaunch(wantDesktop: dlg.Check1Checked);
        }
    }
}
