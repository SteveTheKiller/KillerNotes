using System.Windows;
using KillerNotes.Controls;
using KillerNotes.Features;

namespace KillerNotes.Shell
{
    // ============================================================
    // MainWindow - shell composition root.
    //
    // The window is intentionally thin: it wires the pieces in the constructor.
    // Everything else lives in focused partials (family pattern):
    //
    //   UI shell (KillerUI kit): Chrome.cs, ThemeFlyout.cs, Controls/Anim.cs
    //   Notes:    Notes.cs (list/search/save), Editor.cs (formatting/paste/tables)
    //   Features: each is a Controller + IHost pair - the controller holds the behavior
    //             and no WPF controls, the Shell/*Host.cs partial maps it onto the XAML.
    //             Features/About + Shell/About.cs, Features/Security + Shell/SecurityHost.cs
    //   Data:     Services/NoteStore.cs (SQLite + FTS5 + SQLCipher)
    // ============================================================
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            _about    = new AboutController(this);               // Features/About
            _security = new SecurityController(this);            // Features/Security

            VersionLabel.Text = $"v{Services.AppInfo.Version}";  // Services/AppInfo.cs

            RestoreWindowPlacement();                            // Chrome.cs (size/position from previous run)
            SourceInitialized += MainWindow_SourceInitialized;   // Chrome.cs (taskbar-aware maximize + corners)
            ApplyGrainTexture();                                 // Chrome.cs (paints the shared grain)
            ApplyThemeElevation();                               // ThemeFlyout.cs (98SE is deliberately flat)

            UpdateThemeSwatchSelection();                        // ThemeFlyout.cs
            UpdateAccentStrip(animate: false);
            Services.ThemeManager.ThemeChanged += () =>
            {
                UpdateThemeSwatchSelection();
                UpdateAccentStrip(animate: false);
                ApplyThemeElevation();
                // Syntax runs carry a resolved Color, not a DynamicResource, so they cannot follow
                // a theme switch on their own - and the palette genuinely differs between a dark
                // page and a light one. Re-run the pass so a switch into 98SE or Light recolors
                // the open note instead of leaving Dark+ greens on white. (2026-08-07)
                QueueSyntaxHighlighting();                       // SyntaxHighlighting.cs
            };

            // BEFORE InitEditor and InitDictation, and the order is load-bearing: all three attach
            // to Editor.PreviewMouseLeftButtonDown, WPF runs same-element handlers in registration
            // order, and a handler that marks the event handled stops the rest. Dragging a floated
            // object has to win over "select this image" and "play this recording".
            InitFloat();                                         // Editor.Float.cs (float + drag to place)
            InitEditor();                                        // Editor.cs (paste handler, Ctrl+S)
            InitDictation();                                     // Editor.Dictation.cs (recording-chip clicks)
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
                // and canceling the unlock prompt calls Close() - a reentrant Close()
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
        // KillerScan). Notes need no migration: the database lives in
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
