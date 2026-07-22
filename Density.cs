using System.Windows;
using System.Windows.Input;

namespace KillerNotes
{
    // Sidebar row density (1.1.0). Three levels the user cycles from the rail button
    // (Density_Click), the mouse wheel over it (Density_MouseWheel), or F10:
    //   0 = Comfortable - title, snippet, date, tags (the original look)
    //   1 = Compact     - title + tags
    //   2 = Minimal     - title only
    // Every Note carries the level (RefreshList stamps it from _density) and the row template
    // binds the per-element visibilities (Note.SnippetVisibility / DateVisibility / ChipsVisibility).
    // Remembered per app in the "SidebarDensity" setting.
    public partial class MainWindow
    {
        private int _density;

        private static readonly string[] DensityStatusKeys =
            { "Str_St_DensityFull", "Str_St_DensityCompact", "Str_St_DensityMin" };

        private void InitDensity() =>
            _density = int.TryParse(App.GetSetting("SidebarDensity"), out int d) ? ClampDensity(d) : 0;

        private static int ClampDensity(int d) => d < 0 ? 0 : d > 2 ? 2 : d;

        // Click cycles Comfortable -> Compact -> Minimal -> Comfortable.
        private void Density_Click(object sender, RoutedEventArgs e) => ApplyDensity((_density + 1) % 3);

        // Wheel up = roomier (toward Comfortable), wheel down = tighter (toward Minimal).
        private void Density_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ApplyDensity(_density + (e.Delta > 0 ? -1 : 1));
            e.Handled = true;   // don't let the scroll fall through to the notes list
        }

        private void ApplyDensity(int level)
        {
            _density = ClampDensity(level);
            App.SetSetting("SidebarDensity", _density.ToString());
            RefreshList(preserveScroll: true);   // re-stamps Density on every note (Notes.cs)
            FlashStatus(Loc(DensityStatusKeys[_density]));
        }
    }
}
