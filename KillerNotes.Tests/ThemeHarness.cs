using System;
using System.Reflection;
using System.Windows;
using KillerNotes.Services;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Builds a finished theme dictionary the way the app does, with KillerNotes' hooks configured,
    /// on an Application with no window. Call from inside <see cref="Sta.RunApplication"/>.
    /// </summary>
    internal static class ThemeHarness
    {
        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static ResourceDictionary Create(Theme theme, Accent accent)
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppTheme.Configure();
            var create = typeof(ThemeManager).GetMethod("CreateDictionary", Any)!;
            return (ResourceDictionary)create.Invoke(null, new object[] { theme, accent })!;
        }

        /// <summary>The accent a theme opens with on first run, for themes without accents too.</summary>
        internal static Accent FirstRunAccent(Theme theme) => ThemeManager.AccentChoiceFor(theme);
    }
}
