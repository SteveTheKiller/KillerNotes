using System;
using System.Windows;

namespace KillerNotes.Services
{
    internal enum PageWidthMode { Full, Wide, Medium, Narrow }

    /// <summary>The page width presets (#31). KillerNotes has no fixed page, the editor is as
    /// wide as its pane, so a preset caps the text column instead and the slack becomes equal
    /// side padding. The column therefore stays centered and simply shrinks with the window when
    /// the pane is narrower than the cap. Stored as a global setting, like word wrap.</summary>
    internal static class PageWidth
    {
        internal const string Setting = "PageWidth";

        /// <summary>The widest text column a preset allows, or null for the whole pane.</summary>
        internal static double? Cap(PageWidthMode mode) => mode switch
        {
            PageWidthMode.Wide => 1100,
            PageWidthMode.Medium => 900,
            PageWidthMode.Narrow => 700,
            _ => null,
        };

        /// <summary>The document padding for a preset: the editor's own padding when the pane is
        /// no wider than the cap, otherwise the slack split evenly between the two sides.</summary>
        internal static Thickness Padding(PageWidthMode mode, double viewportWidth, Thickness basePadding)
        {
            double? cap = Cap(mode);
            if (cap == null || double.IsNaN(viewportWidth) || viewportWidth <= cap.Value) return basePadding;
            // Just past the cap the slack is a pixel or two; the editor's own padding stays the floor
            // so the column never jumps wider than it was a few pixels narrower.
            double side = Math.Floor((viewportWidth - cap.Value) / 2);
            return new Thickness(Math.Max(basePadding.Left, side), basePadding.Top,
                Math.Max(basePadding.Right, side), basePadding.Bottom);
        }

        internal static PageWidthMode Next(PageWidthMode mode) =>
            (PageWidthMode)(((int)mode + 1) % 4);

        internal static string Serialize(PageWidthMode mode) => mode.ToString().ToLowerInvariant();

        internal static PageWidthMode Parse(string? value) =>
            Enum.TryParse(value, ignoreCase: true, out PageWidthMode mode) ? mode : PageWidthMode.Full;
    }
}
