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
        // Opt in to WPF's non-adorner text selection rendering, the machinery that makes
        // SelectionTextBrush work (98SE's navy selection with WHITE text). The old renderer
        // paints a rectangle OVER the glyphs and ignores SelectionTextBrush entirely; the new
        // one paints behind them and recolors the selected text. .NET Framework 4.8 shipped
        // the new renderer as the default, but a 2019 servicing update reverted the default
        // to the old one, so on every patched machine this switch is the ONLY way to get it.
        // Must be set before the first text control is created - WPF reads it once and caches
        // it - hence the static ctor, which runs before any of that.
        static App()
        {
            AppContext.SetSwitch("Switch.System.Windows.Controls.Text.UseAdornerForTextboxSelectionRendering", false);
        }

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

            // Elevated half of the dual-install repair below: removes the machine-wide copy.
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "/remove-machine-conflict", StringComparison.OrdinalIgnoreCase))
            {
                RemoveMachineInstallConflict();
                Shutdown(0);
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
                KillerNotes.Shell.MainWindow.DemoMode = true;
                Services.NoteStore.DemoDbFile = "demo-notes.db";
                string demoDb = Path.Combine(Services.NoteStore.DbDir, "demo-notes.db");
                try
                {
                    File.Delete(demoDb);
                    File.Delete(demoDb + "-wal");   // SQLite sidecars, if a run crashed
                    File.Delete(demoDb + "-shm");
                }
                catch { /* locked - usually another demo window still open */ }
                // Only generate into a FRESH database. If the delete failed (a second
                // demo instance holds the file), reuse its notes instead of appending
                // a duplicate set - every parallel launch used to add one full copy.
                KillerNotes.Shell.MainWindow.DemoFresh = !File.Exists(demoDb);
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
            if (!KillerNotes.Shell.MainWindow.DemoMode)
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

            // Portable/per-user: HKCU, best-effort, idempotent. The machine-wide copy skips
            // this - the elevated installer already registered HKLM for every account, and an
            // HKCU copy would shadow it for just this user.
            if (!string.Equals(Process.GetCurrentProcess().MainModule?.FileName, MachineExe,
                    StringComparison.OrdinalIgnoreCase))
                RegisterFileAssociations();

            OfferInstallConflictRepair();

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
            new KillerNotes.Shell.MainWindow().Show();
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
            if (MainWindow is not KillerNotes.Shell.MainWindow win) return;

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
        // Registered per hive: HKCU on every portable/per-user launch (no elevation,
        // follows the exe if it moves), HKLM once by the elevated machine-wide install
        // so every account sees the types. NOT .kdb: that belongs to KeePass 1.x.
        // ============================================================

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        /// <summary>Per-user registration for the running exe, HKCU, every launch.</summary>
        private static void RegisterFileAssociations()
        {
            try
            {
                RegisterFileAssociations(Registry.CurrentUser,
                    Process.GetCurrentProcess().MainModule!.FileName,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                 "KillerNotes", "icons"));
            }
            catch { /* best-effort - sharing still works via the in-app import */ }
        }

        /// <summary>Registers .kndb/.knote in one hive, plus the Capabilities and
        /// RegisteredApplications entries that put KillerNotes in Default Apps and Open With
        /// for that scope. Per-user installs pass HKCU and AppData paths; the machine-wide
        /// install passes HKLM and Program Files paths.</summary>
        private static void RegisterFileAssociations(RegistryKey root, string exe, string iconDir)
        {
            try
            {
                // Dedicated per-type icons, extracted where Explorer (and for HKLM, every
                // account) can read them; the exe icon is the fallback if extraction fails.
                string noteIcon = ExtractIcon("kn-note.ico", iconDir) is string np ? $"{np},0" : $"{exe},0";
                string dbIcon   = ExtractIcon("kn-db.ico",   iconDir) is string dp ? $"{dp},0" : $"{exe},0";
                RegisterType(root, ".kndb",  "KillerNotes.Database", "KillerNotes Database",    exe, dbIcon);
                RegisterType(root, ".knote", "KillerNotes.Note",     "KillerNotes Shared Note", exe, noteIcon);

                using (var cap = root.CreateSubKey(@"Software\KillerNotes\Capabilities"))
                {
                    cap.SetValue("ApplicationName", AppName);
                    cap.SetValue("ApplicationDescription", "Notes that live in a file on your own machine.");
                }
                using (var fa = root.CreateSubKey(@"Software\KillerNotes\Capabilities\FileAssociations"))
                {
                    fa.SetValue(".kndb",  "KillerNotes.Database");
                    fa.SetValue(".knote", "KillerNotes.Note");
                }
                using (var ra = root.CreateSubKey(@"Software\RegisteredApplications"))
                    ra.SetValue(AppName, @"Software\KillerNotes\Capabilities");

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* best-effort - sharing still works via the in-app import */ }
        }

        /// <summary>Takes the associations back out of one hive: extension keys, ProgIDs,
        /// Capabilities and the RegisteredApplications value.</summary>
        private static void UnregisterFileAssociations(RegistryKey root)
        {
            try { root.DeleteSubKeyTree(@"Software\Classes\.kndb", false); } catch { }
            try { root.DeleteSubKeyTree(@"Software\Classes\.knote", false); } catch { }
            try { root.DeleteSubKeyTree(@"Software\Classes\KillerNotes.Database", false); } catch { }
            try { root.DeleteSubKeyTree(@"Software\Classes\KillerNotes.Note", false); } catch { }
            try { root.DeleteSubKeyTree(@"Software\KillerNotes\Capabilities", false); } catch { }
            try
            {
                using var ra = root.OpenSubKey(@"Software\RegisteredApplications", writable: true);
                ra?.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch { }
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Copies an embedded .ico to <paramref name="iconDir"/> (DefaultIcon needs
        /// a real file path). Rewrites when the embedded copy changes. Null on failure.</summary>
        private static string? ExtractIcon(string name, string iconDir)
        {
            try
            {
                Directory.CreateDirectory(iconDir);
                string dest = Path.Combine(iconDir, name);

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

        private static void RegisterType(RegistryKey root, string ext, string progId, string display, string exe, string iconSpec)
        {
            using (var k = root.CreateSubKey($@"Software\Classes\{ext}"))
                k.SetValue("", progId);
            using (var k = root.CreateSubKey($@"Software\Classes\{progId}"))
                k.SetValue("", display);
            using (var k = root.CreateSubKey($@"Software\Classes\{progId}\DefaultIcon"))
                k.SetValue("", iconSpec);
            using (var k = root.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
                k.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // ============================================================
        // Install system (ported from KillerScan)
        // Portable badge Install = per-user (%LOCALAPPDATA%\Programs); /silent =
        // machine-wide Program Files for winget/choco/RMM; /uninstall from ARP.
        // ============================================================

        private const string AppName = "KillerNotes";
        private const string ExeName = "KillerNotes.exe";
        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", AppName);
        private static readonly string InstallExe = Path.Combine(InstallDir, ExeName);

        /// <summary>Where /silent puts a machine-wide install. Needed by IsPortable: without it a
        /// Program Files copy does not recognize itself as installed.</summary>
        private static readonly string MachineDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
        private static readonly string MachineExe = Path.Combine(MachineDir, ExeName);

        private static readonly string StartMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        private static readonly string StartMenuLnk = Path.Combine(StartMenuDir, $"{AppName}.lnk");
        private static readonly string DesktopLnk = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

        /// <summary>True when running from outside EITHER installed location (i.e. portable mode).
        ///
        /// Both locations matter. This used to compare against the per-user path only, so a
        /// machine-wide copy in Program Files reported itself as portable: it showed the PORTABLE
        /// badge and offered to install itself, and accepting created a SECOND, per-user copy
        /// alongside it. That is how a machine ends up running one version while Add/Remove
        /// Programs describes another (seen in the field, 2026-08-05).</summary>
        internal static bool IsPortable()
        {
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            return !string.Equals(currentExe, InstallExe,  StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentExe, MachineExe, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Repairs a machine that carries BOTH a per-user and a machine-wide install -
        /// the state where each Add/Remove Programs entry describes the other copy's version and
        /// launching gets whichever exe the shell resolves first. Detected at startup; offers to
        /// remove whichever copy is NOT running. Removing the machine copy needs elevation, so
        /// that path re-runs this exe with /remove-machine-conflict under UAC.</summary>
        private static void OfferInstallConflictRepair()
        {
            if (!File.Exists(InstallExe) || !File.Exists(MachineExe)) return;
            string current = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            bool runningMachine = string.Equals(current, MachineExe, StringComparison.OrdinalIgnoreCase);
            bool runningUser = string.Equals(current, InstallExe, StringComparison.OrdinalIgnoreCase);
            if (!runningMachine && !runningUser) return;

            string other = runningMachine ? "per-user" : "all-users";
            if (MessageBox.Show($"KillerNotes is installed twice. Remove the other {other} copy now?\n\nYour notes and settings will not be removed.",
                $"{AppName} installation conflict", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            if (runningMachine) RemovePerUserInstall();
            else
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(current, "/remove-machine-conflict")
                    { UseShellExecute = true, Verb = "runas" });
                    p?.WaitForExit();
                }
                catch { /* declining UAC leaves both copies in place */ }
            }
        }

        /// <summary>Remove a per-user install: files, shortcuts and the HKCU install markers.
        /// Settings under Software\KillerNotes\Settings and the notes databases are deliberately
        /// left alone. Deletes the marker VALUES, not the key - the Settings subkey lives under
        /// the same key.</summary>
        private static void RemovePerUserInstall()
        {
            try { if (File.Exists(StartMenuLnk)) File.Delete(StartMenuLnk); } catch { }
            try { if (Directory.Exists(StartMenuDir)) Directory.Delete(StartMenuDir, true); } catch { }
            try { if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk); } catch { }
            try { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true); } catch { }
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\KillerNotes", writable: true);
                key?.DeleteValue("Installed", throwOnMissingValue: false);
                key?.DeleteValue("InstallPath", throwOnMissingValue: false);
                key?.DeleteValue("Version", throwOnMissingValue: false);
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerNotes", throwOnMissingSubKey: false); }
            catch { }
            // The machine install's HKLM associations serve every account; drop the per-user
            // registration so it cannot shadow the shared Program Files paths.
            UnregisterFileAssociations(Registry.CurrentUser);
        }

        private static void RemoveMachineInstallConflict()
        {
            string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
            try { Registry.LocalMachine.DeleteSubKeyTree(@"Software\KillerNotes", false); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerNotes", false); } catch { }
            UnregisterFileAssociations(Registry.LocalMachine);
            try { if (Directory.Exists(common)) Directory.Delete(common, true); } catch { }
            try { if (Directory.Exists(MachineDir)) Directory.Delete(MachineDir, true); } catch { }
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

                // HKLM associations for every account, icons in Program Files where all
                // accounts can read them. This pass runs elevated by definition.
                RegisterFileAssociations(Registry.LocalMachine, installExe,
                    Path.Combine(installDir, "icons"));
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
                    BindingFlags.InvokeMethod, null, shell, [lnkPath])!;
                var sc = shortcut.GetType();
                sc.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, shortcut, [targetPath]);
                sc.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                    null, shortcut, [Path.GetDirectoryName(targetPath)!]);
                sc.InvokeMember("Save", BindingFlags.InvokeMethod,
                    null, shortcut, null);
            }
            catch { /* best-effort */ }
        }

        // Uninstall (Add/Remove Programs). Removes the installed exe, shortcuts, file
        // associations, and registry entries. The notes databases in %APPDATA%\KillerNotes
        // are user data and are deliberately KEPT.
        private static bool RelaunchMachineUninstallElevatedIfNeeded(bool machine)
        {
            if (!machine) return false;
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                    return false;

                Process.Start(new ProcessStartInfo(
                    Process.GetCurrentProcess().MainModule!.FileName, "/uninstall")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // UAC was declined. Leave the installation untouched.
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uninstall could not request administrator access:\n{ex.Message}",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        private static void Uninstall()
        {
            bool machine = string.Equals(Process.GetCurrentProcess().MainModule?.FileName,
                                         MachineExe, StringComparison.OrdinalIgnoreCase);
            if (RelaunchMachineUninstallElevatedIfNeeded(machine)) return;

            var res = MessageBox.Show(
                "Uninstall KillerNotes from this computer?\n\nYour notes are kept.",
                $"{AppName} Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            string startMenuDir = machine
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName)
                : StartMenuDir;
            string targetDir = machine ? MachineDir : InstallDir;

            try { File.Delete(Path.Combine(startMenuDir, $"{AppName}.lnk")); } catch { }
            try { Directory.Delete(startMenuDir, recursive: false); } catch { }
            if (!machine) try { File.Delete(DesktopLnk); } catch { }

            // Delete only the scope represented by the executable Add/Remove Programs launched.
            var hive = machine ? Registry.LocalMachine : Registry.CurrentUser;
            try { hive.DeleteSubKeyTree(@"Software\KillerNotes", throwOnMissingSubKey: false); } catch { }
            try { hive.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerNotes"); } catch { }

            // Drop the associations from the same scope that installed them. A machine
            // uninstall also removes the HKCU shadow this account may carry from an older
            // per-user registration.
            if (machine) UnregisterFileAssociations(Registry.LocalMachine);
            UnregisterFileAssociations(Registry.CurrentUser);

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            // Self-delete: deferred via cmd batch so the EXE can exit first
            string bat = Path.Combine(Path.GetTempPath(), "killernotes_uninstall.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                $"rmdir /s /q \"{targetDir}\"\r\n" +
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
