using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
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

            // Silent install: KillerNotes.exe /silent
            // Installs machine-wide to Program Files, no UI. Used by winget/choco/RMM.
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "/silent", StringComparison.OrdinalIgnoreCase))
            {
                DoSilentInstall();
                Shutdown(0);
                return;
            }

            // Uninstall flag (called by Add/Remove Programs)
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
            {
                Uninstall();
                Shutdown();
                return;
            }

            // Screenshot / demo mode: --demo (or /demo) fills a scratch database with
            // fabricated notes (DemoMode.cs). The real notes.db is never touched.
            foreach (string a in e.Args)
            {
                if (!a.Equals("--demo", StringComparison.OrdinalIgnoreCase) &&
                    !a.Equals("/demo", StringComparison.OrdinalIgnoreCase)) continue;
                // Fully qualified: inside App, bare "MainWindow" is the
                // Application.MainWindow property (a Window), not our class.
                KillerNotes.MainWindow.DemoMode = true;
                Services.NoteStore.DemoDbFile = "demo-notes.db";
                try { File.Delete(Path.Combine(Services.NoteStore.DbDir, "demo-notes.db")); }
                catch { /* fresh roll is best-effort */ }
            }

            // Double-clicked share file (association registered below).
            if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            {
                string ext = Path.GetExtension(e.Args[0]).ToLowerInvariant();
                if (ext == ".kndb" || ext == ".knote") PendingOpenFile = e.Args[0];
            }

            // Single instance (per desktop session). Two instances sharing the same
            // notes.db is how the password-change file swap fails with "in use by
            // another process" (#3) - SQLite happily lets both open the file, so the
            // user never notices the double launch. A second launch forwards its
            // command line (a double-clicked .knote/.kndb, or nothing) to the running
            // window through a named pipe and exits; the running window activates and
            // imports the file exactly as a first-launch double-click would.
            // --demo is exempt: it only ever touches the scratch demo database.
            if (!KillerNotes.MainWindow.DemoMode)
            {
                _instanceMutex = new Mutex(true, @"Local\KillerNotes-SingleInstance", out bool firstInstance);
                if (!firstInstance)
                {
                    ForwardToRunningInstance(PendingOpenFile);
                    Shutdown(0);
                    return;
                }
                StartPipeServer();
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
            Services.LocaleManager.Initialize();   // layers Strings/en-US.xaml (+ saved locale)

            ShutdownMode = ShutdownMode.OnLastWindowClose;
            new MainWindow().Show();
        }

        // ============================================================
        // Single instance (see OnStartup): "Local\" mutex + named pipe, both scoped
        // to the desktop session, so RDS/multi-user boxes still get one per user.
        // ============================================================

        // Held for the process lifetime; the OS releases it on exit or crash.
        private static Mutex? _instanceMutex;

        private static string PipeName =>
            $"KillerNotes-{Process.GetCurrentProcess().SessionId}";

        /// <summary>Second launch: hands the double-clicked file path (or an empty line,
        /// meaning "just come to the front") to the running instance, then this process
        /// exits. Best-effort: if the pipe is unreachable the launch simply ends.</summary>
        private static void ForwardToRunningInstance(string? path)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(2000);
                using var w = new StreamWriter(pipe) { AutoFlush = true };
                w.WriteLine(path ?? "");
            }
            catch { /* running instance not listening (mid-shutdown) - nothing to do */ }
        }

        /// <summary>First instance: listens for forwarded launches for the process
        /// lifetime on a background thread; each message is dispatched to the UI thread.</summary>
        private void StartPipeServer()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                        server.WaitForConnection();
                        using var r = new StreamReader(server);
                        string? path = r.ReadLine();
                        Dispatcher.BeginInvoke(new Action(() => OnForwardedLaunch(path)));
                    }
                    catch (IOException) { /* client vanished mid-handshake - keep listening */ }
                    catch (ObjectDisposedException) { return; }
                }
            })
            { IsBackground = true, Name = "KillerNotes single-instance pipe" };
            thread.Start();
        }

        /// <summary>UI thread: brings the window to the front and routes a forwarded
        /// .knote/.kndb through the same import path as a first-launch double-click.</summary>
        private void OnForwardedLaunch(string? path)
        {
            if (MainWindow is not KillerNotes.MainWindow win) return;

            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
            win.Topmost = true; win.Topmost = false;   // foreground nudge past focus rules

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".kndb" && ext != ".knote") return;

            PendingOpenFile = path;
            win.HandlePendingOpenFile();
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
        // Install system (ported from KillerScan/KillerFind)
        // Portable badge Install = per-user (%LOCALAPPDATA%\Programs); /silent =
        // machine-wide Program Files for winget/choco/RMM; /uninstall from ARP.
        // ============================================================

        private const string AppName = "KillerNotes";
        private const string ExeName = "KillerNotes.exe";
        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", AppName);
        private static readonly string InstallExe = Path.Combine(InstallDir, ExeName);

        private static readonly string StartMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        private static readonly string StartMenuLnk = Path.Combine(StartMenuDir, $"{AppName}.lnk");
        private static readonly string DesktopLnk = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

        /// <summary>True when running from outside the installed location (i.e. portable mode).</summary>
        internal static bool IsPortable()
        {
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            return !string.Equals(currentExe, InstallExe, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Installs KillerNotes, then relaunches from the installed location.</summary>
        internal static void InstallAndRelaunch(bool wantDesktop)
        {
            DoInstall(wantDesktop);

            Process.Start(new ProcessStartInfo(InstallExe));
            Application.Current.Shutdown();
        }

        // Silent (machine-wide) install -- used by winget / choco / RMM
        private static void DoSilentInstall()
        {
            try
            {
                string installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
                string installExe = Path.Combine(installDir, ExeName);
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
                string startMenuLnk = Path.Combine(startMenuDir, $"{AppName}.lnk");

                Directory.CreateDirectory(installDir);
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                File.Copy(src, installExe, overwrite: true);

                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(startMenuLnk, installExe);

                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

                using (var key = Registry.LocalMachine.CreateSubKey(@"Software\KillerNotes"))
                {
                    key.SetValue("Installed",   1);
                    key.SetValue("InstallPath", installExe);
                    key.SetValue("Version",     version);
                }

                using (var key = Registry.LocalMachine.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerNotes"))
                {
                    key.SetValue("DisplayName",          AppName);
                    key.SetValue("DisplayVersion",       version);
                    key.SetValue("Publisher",            "Steve / thekiller.net");
                    key.SetValue("InstallLocation",      installDir);
                    key.SetValue("DisplayIcon",          $"{installExe},0");
                    key.SetValue("UninstallString",      $"\"{installExe}\" /uninstall");
                    key.SetValue("QuietUninstallString", $"\"{installExe}\" /uninstall");
                    key.SetValue("NoModify",             1);
                    key.SetValue("NoRepair",             1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Silent install failed: {ex.Message}");
                Environment.Exit(1);
            }
        }

        // Per-user install (the PORTABLE badge's Install button)
        private static void DoInstall(bool wantDesktop)
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                File.Copy(src, InstallExe, overwrite: true);

                Directory.CreateDirectory(StartMenuDir);
                CreateShortcut(StartMenuLnk, InstallExe);
                if (wantDesktop)
                    CreateShortcut(DesktopLnk, InstallExe);

                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\KillerNotes"))
                {
                    key.SetValue("Installed",   1);
                    key.SetValue("InstallPath", InstallExe);
                    key.SetValue("Version",     version);
                }

                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerNotes"))
                {
                    key.SetValue("DisplayName",          AppName);
                    key.SetValue("DisplayVersion",       version);
                    key.SetValue("Publisher",            "Steve / thekiller.net");
                    key.SetValue("InstallLocation",      InstallDir);
                    key.SetValue("DisplayIcon",          $"{InstallExe},0");
                    key.SetValue("UninstallString",      $"\"{InstallExe}\" /uninstall");
                    key.SetValue("QuietUninstallString", $"\"{InstallExe}\" /uninstall");
                    key.SetValue("NoModify",             1);
                    key.SetValue("NoRepair",             1);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation failed:\n{ex.Message}", AppName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void CreateShortcut(string lnkPath, string targetPath)
        {
            // Reflection over IDispatch instead of `dynamic` - avoids needing the
            // Microsoft.CSharp runtime binder reference this project doesn't carry.
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null) return;
                object shell = Activator.CreateInstance(shellType)!;
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath })!;
                var sc = shortcut.GetType();
                sc.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, shortcut, new object[] { targetPath });
                sc.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                    null, shortcut, new object[] { Path.GetDirectoryName(targetPath)! });
                sc.InvokeMember("Save", BindingFlags.InvokeMethod,
                    null, shortcut, null);
            }
            catch { /* best-effort */ }
        }

        // Uninstall (Add/Remove Programs). Removes the installed exe, shortcuts, file
        // associations, and registry entries. The notes databases in %APPDATA%\KillerNotes
        // are user data and are deliberately KEPT.
        private static void Uninstall()
        {
            var res = MessageBox.Show(
                "Uninstall KillerNotes from this computer?\n\nYour notes are kept.",
                $"{AppName} Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            try { File.Delete(StartMenuLnk); } catch { }
            try { Directory.Delete(StartMenuDir, recursive: false); } catch { }
            try { File.Delete(DesktopLnk); } catch { }

            // Settings + install info (Software\KillerNotes covers the Settings subkey too)
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\KillerNotes"); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerNotes"); } catch { }

            // File associations registered at launch (RegisterFileAssociations)
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.kndb"); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.knote"); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\KillerNotes.Database"); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\KillerNotes.Note"); } catch { }

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            // Self-delete: deferred via cmd batch so the EXE can exit first
            string bat = Path.Combine(Path.GetTempPath(), "killernotes_uninstall.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                $"rmdir /s /q \"{InstallDir}\"\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                WindowStyle     = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });

            MessageBox.Show("KillerNotes has been uninstalled. Your notes were kept.", AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
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
