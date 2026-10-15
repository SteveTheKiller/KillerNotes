using System.Windows;

namespace KillerNotes.Services
{
    /// <summary>
    /// The theme menu's Film Grain switch, on by default. Turning it off sets GrainOpacity to 0
    /// in the application resources, which outrank every theme dictionary, so each grain layer
    /// bound to it disappears whatever theme or accent is active. A layer drawn at a fixed
    /// strength (the content pane's own grain) follows FilmGrainVisibility instead. The theme is then
    /// applied again so surfaces that take their grain when the theme changes redraw too.
    /// </summary>
    internal static class FilmGrain
    {
        private const string SettingKey = "FilmGrain";

        /// <summary>True while the grain texture is shown.</summary>
        internal static bool Enabled { get; private set; } = true;

        /// <summary>Restores the saved choice. Called once at startup, after the theme loads.</summary>
        internal static void Initialize()
        {
            Enabled = ThemeManager.GetSetting(SettingKey) != "0";
            Publish();
        }

        /// <summary>Shows or hides the grain on every surface and saves the choice.</summary>
        internal static void Set(bool enabled)
        {
            if (enabled == Enabled) return;
            Enabled = enabled;
            ThemeManager.SetSetting(SettingKey, enabled ? "1" : "0");
            Publish();
            ThemeManager.Apply(ThemeManager.Current);
        }

        private static void Publish()
        {
            var resources = Application.Current?.Resources;
            if (resources == null) return;
            if (Enabled) resources.Remove("GrainOpacity");
            else resources["GrainOpacity"] = 0.0;
            resources["FilmGrainVisibility"] = Enabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
