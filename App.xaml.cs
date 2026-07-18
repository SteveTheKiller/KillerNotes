using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace KillerNotes
{
    public partial class App : Application
    {
        /// <summary>A .kndb/.knote path passed on the command line (double-clicked file);
        /// MainWindow picks it up after the database opens (Sharing.cs).</summary>
        internal static string? PendingOpenFile;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Double-clicked share file (association registered below).
            if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            {
                string ext = Path.GetExtension(e.Args[0]).ToLowerInvariant();
                if (ext == ".kndb" || ext == ".knote") PendingOpenFile = e.Args[0];
            }

            RegisterFileAssociations();   // best-effort, HKCU only, idempotent

            // GPU rendering, like KillerPDF (no SoftwareOnly here): the format bar and pane
            // drop shadows are recomputed on the CPU under software rendering, which made
            // typing visibly lag. If a remote-capture tool ever shows this window black,
            // revisit with a --software fallback switch rather than forcing CPU for everyone.

            // Wire the kit's pluggable persistence to the registry, then restore the saved
            // theme + accent before the window is built (no first-paint flash).
            Services.ThemeManager.GetSetting = GetSetting;
            Services.ThemeManager.SetSetting = SetSetting;
            Services.ThemeManager.Initialize();

            ShutdownMode = ShutdownMode.OnLastWindowClose;
            new MainWindow().Show();
        }

        // ============================================================
        // File associations (.kndb = database, .knote = single shared note)
        // HKCU only - no elevation. Registered every launch so the association
        // follows the exe if it moves. NOT .kdb: that belongs to KeePass 1.x.
        // ============================================================

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        private static void RegisterFileAssociations()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule!.FileName;
                // Dedicated per-type icons, extracted where Explorer can read them;
                // the exe icon is the fallback if extraction fails.
                string noteIcon = ExtractIcon("kn-note.ico") is string np ? $"{np},0" : $"{exe},0";
                string dbIcon   = ExtractIcon("kn-db.ico")   is string dp ? $"{dp},0" : $"{exe},0";
                RegisterType(".kndb",  "KillerNotes.Database", "KillerNotes Database",    exe, dbIcon);
                RegisterType(".knote", "KillerNotes.Note",     "KillerNotes Shared Note", exe, noteIcon);
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* best-effort - sharing still works via the in-app import */ }
        }

        /// <summary>Copies an embedded .ico to AppData\KillerNotes\icons (DefaultIcon needs
        /// a real file path). Rewrites when the embedded copy changes. Null on failure.</summary>
        private static string? ExtractIcon(string name)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KillerNotes", "icons");
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, name);

                var sri = GetResourceStream(new Uri($"pack://application:,,,/Resources/{name}"));
                if (sri == null) return null;
                using var ms = new MemoryStream();
                using (var src = sri.Stream) src.CopyTo(ms);
                byte[] bytes = ms.ToArray();
                if (!File.Exists(dest) || new FileInfo(dest).Length != bytes.Length)
                    File.WriteAllBytes(dest, bytes);
                return dest;
            }
            catch { return null; }
        }

        private static void RegisterType(string ext, string progId, string display, string exe, string iconSpec)
        {
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}"))
                k.SetValue("", progId);
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
                k.SetValue("", display);
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\DefaultIcon"))
                k.SetValue("", iconSpec);
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
                k.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // ============================================================
        // Preference store  (Software\KillerNotes\Settings)
        // Mirrors KillerPDF/KillerScan: simple per-user string settings, used by
        // ThemeManager (theme + accent) and Chrome.cs (window placement).
        // ============================================================

        internal static string? GetSetting(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\KillerNotes\Settings");
                return key?.GetValue(name) as string;
            }
            catch { return null; }
        }

        internal static void SetSetting(string name, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\KillerNotes\Settings");
                key?.SetValue(name, value);
            }
            catch { /* best-effort */ }
        }

        internal static void RemoveSetting(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\KillerNotes\Settings", writable: true);
                key?.DeleteValue(name, throwOnMissingValue: false);
            }
            catch { /* best-effort */ }
        }
    }
}
