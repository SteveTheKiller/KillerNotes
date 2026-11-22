using KillerNotes.Controls;

namespace KillerNotes.Shell
{
    // ============================================================
    // PAGE INK (Trash Polka)
    //
    // How strongly the background image shows behind the note (TrashPolkaInk.PageOpacity,
    // ThemeMotif.Page) and behind its rendered view (TrashPolkaInk.PreviewOpacity,
    // ThemeMotif.Preview). Steve set these by eye with sliders on 2026-11-22: 13% for the note,
    // 19% for the rendered view. The sliders are gone; the values are fixed here.
    // ============================================================
    public partial class MainWindow
    {
        internal const double NotePageInk = 0.13;
        internal const double PreviewPageInk = 0.19;

        /// <summary>Applied before the first paint.</summary>
        internal static void LoadPageInk()
        {
            TrashPolkaInk.PageOpacity = NotePageInk;
            TrashPolkaInk.PreviewOpacity = PreviewPageInk;
        }
    }
}
