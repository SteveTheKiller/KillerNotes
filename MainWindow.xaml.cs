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

            Loaded += (_, _) =>
            {
                FadeInContent();                                 // Chrome.cs (RootGrid 0 -> 1)
                // Deferred, NOT called inline: Loaded fires synchronously inside Show(),
                // and cancelling the unlock prompt calls Close() - a reentrant Close()
                // during Show() throws (this was a real crash). Dispatching lets Show()
                // finish first; the prompt appears right after first paint.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OpenDatabase();               // Security.cs (unlock prompt if encrypted)
                    HandlePendingOpenFile();      // Sharing.cs (double-clicked .kndb/.knote)
                    if (DemoMode) GenerateDemoNotes();   // DemoMode.cs (--demo: screenshot data)
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
    }
}
