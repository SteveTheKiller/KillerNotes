using System.IO.Packaging;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    /// <summary>net48 has no ModuleInitializerAttribute of its own; the compiler only needs the name.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace KillerNotes.Tests
{
    /// <summary>
    /// ThemeManager builds pack:// URIs in its static initializer, and new Uri only understands
    /// that scheme once WPF has registered it. The app always has by then. In this process it
    /// depended on some earlier test having touched WPF, so the first test to reach ThemeManager
    /// could fail the type initializer for every test after it, depending on the order they ran.
    /// </summary>
    internal static class TestStartup
    {
        [ModuleInitializer]
        internal static void RegisterPackScheme() => _ = PackUriHelper.UriSchemePack;
    }
}
