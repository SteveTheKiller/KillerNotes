using System;
using System.Threading.Tasks;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Features.About
{
    /// <summary>
    /// The About card's content: version and release date, the code-signing publisher and
    /// thumbprint, the running exe's SHA-256, and a quiet update check with optional one-click
    /// self-update.
    /// </summary>
    internal sealed class AboutController
    {
        private readonly IAboutHost _host;

        /// <summary>Tag of the newer release found by the check, null when there is nothing to
        /// install.</summary>
        private string? _updateTag;

        /// <summary>Setting that turns the startup check off. Anything but "0" leaves it on.</summary>
        internal const string StartupCheckSetting = "CheckForUpdatesOnStartup";

        private bool _startupCheckStarted;
        private bool _updating;

        internal AboutController(IAboutHost host) => _host = host;

        internal static bool StartupCheckEnabled => ThemeManager.GetSetting(StartupCheckSetting) != "0";

        /// <summary>Saves the startup check choice from either the About checkbox or the prompt.</summary>
        internal void SetStartupCheck(bool enabled)
        {
            ThemeManager.SetSetting(StartupCheckSetting, enabled ? "1" : "0");
            _host.StartupUpdateCheck = enabled;
        }

        /// <summary>Adds or removes the app's Run value from the About checkbox, then shows what the
        /// registry actually holds, so a refused write unchecks the box again.</summary>
        internal void SetStartWithWindows(bool enabled)
        {
            KillerFamily.Deployment.StartupRegistration.Set(AppInfo.DisplayName, enabled);
            _host.StartWithWindows = KillerFamily.Deployment.StartupRegistration.IsEnabled(AppInfo.DisplayName);
        }

        /// <summary>Whether closing the main window hides it to the notification area. Read by
        /// CloseToTray each time the window closes, so the checkbox takes effect at once.</summary>
        internal static bool CloseToTrayEnabled => ThemeManager.GetSetting(KillerFamily.Shell.CloseToTray.SettingName) == "1";

        /// <summary>Saves the Close to system tray choice from the About checkbox.</summary>
        internal void SetCloseToTray(bool enabled)
        {
            ThemeManager.SetSetting(KillerFamily.Shell.CloseToTray.SettingName, enabled ? "1" : "0");
            _host.CloseToTray = enabled;
        }

        /// <summary>Once per run, after the notebook is open: when a newer release exists, offers it
        /// with an "Always check on startup" box so the check can be turned off right there. The
        /// About card's checkbox turns it back on. Skipped under --demo so captures stay clean.</summary>
        internal async void CheckOnStartup()
        {
            if (_startupCheckStarted || _host.DemoPreview || !StartupCheckEnabled) return;
            _startupCheckStarted = true;
            var tag = await UpdateService.CheckAsync(AppInfo.AssemblyVersion);
            if (tag is null || _updating) return;

            _updateTag          = tag;
            _host.UpdateText    = string.Format(_host.Loc("Str_About_UpdateAvailable"), tag);
            _host.UpdateVisible = true;

            // Never stack the prompt on a dialog the user already has open.
            if (!_host.Window.IsVisible || _host.Window.OwnedWindows.Count > 0) return;
            var dlg = new ConfirmDialog(
                string.Format(_host.Loc("Str_About_StartupUpdateHeading"), AppInfo.DisplayName, tag),
                _host.Loc("Str_About_StartupUpdateDetail"),
                _host.Loc("Str_About_UpdateNow"), _host.Loc("Str_About_NotNow"),
                _host.Loc("Str_About_AlwaysCheckOnStartup"), StartupCheckEnabled) { Owner = _host.Window };
            dlg.ShowDialog();
            SetStartupCheck(dlg.Check1Checked);
            if (dlg.Confirmed) await InstallAsync(tag);
        }

        /// <summary>Fills the card in and shows it, then finishes the two slow parts in the
        /// background.</summary>
        internal void Show()
        {
            _host.Version = $"v{AppInfo.Version}";
            // Its own block so it can sit muted, italic and right-aligned opposite the version.
            _host.ReleaseDate = AppInfo.ReleaseDate;

            var (sigValid, subject, thumb) = CodeSignature.GetSignerInfo();
            // --demo previews the signed card (DemoMode.cs), using the real cert values.
            if (_host.DemoPreview)
            {
                sigValid = true;
                subject  = AppInfo.DemoSubject;
                thumb    = AppInfo.DemoThumbprint;
            }

            _host.Publisher = sigValid ? subject : "(not signed or chain failed)";

            // Shown only when the exe is signed by Steve AND the signature actually verifies -
            // reading a cert out of the file does not prove the file was not tampered with.
            bool signedByMe = sigValid &&
                subject.IndexOf(AppInfo.SignerName, StringComparison.OrdinalIgnoreCase) >= 0;

            // Only the quoted alias goes in the run - the "AKA " prefix and the thekiller.net
            // hyperlink around it live in the XAML. 0x201C / 0x201D are the curly quotes, built
            // from codepoints so this line adds no non-ASCII bytes to the source.
            _host.Alias        = (char)0x201C + AppInfo.AkaName + (char)0x201D;
            _host.AliasVisible = signedByMe;
            _host.Thumbprint   = thumb;
            _host.Sha256       = _host.Loc("Str_About_Computing");
            _host.UpdateVisible = false;
            _host.StartupUpdateCheck = StartupCheckEnabled;
            _host.StartWithWindows = KillerFamily.Deployment.StartupRegistration.IsEnabled(AppInfo.DisplayName);
            _host.CloseToTray = CloseToTrayEnabled;

            _host.ShowCard();

            // SHA-256 is slow on a large EXE; compute off the UI thread.
            Task.Run(() =>
            {
                var hash = CodeSignature.ExeSha256();
                _host.Window.Dispatcher.BeginInvoke((Action)(() => _host.Sha256 = hash));
            });

            CheckForUpdate();
        }

        /// <summary>Opens the release page for the running version.</summary>
        internal void OpenReleaseNotes() => WebLink.Open(UpdateService.ReleaseUrl(AppInfo.Version));

        /// <summary>Shows the update button if a newer release exists. Silent when offline.</summary>
        private async void CheckForUpdate()
        {
            var tag = await UpdateService.CheckAsync(AppInfo.AssemblyVersion);
            if (tag is null || _updating) return;

            _updateTag          = tag;
            _host.UpdateText    = string.Format(_host.Loc("Str_About_UpdateAvailable"), tag);
            _host.UpdateVisible = true;
        }

        /// <summary>Downloads the newer release, verifies it, and hands off to the helper that swaps
        /// the exe and relaunches. Every failure path falls back to the releases page, so a user who
        /// cannot be updated automatically is never left thinking nothing happened.</summary>
        internal async void Update()
        {
            var tag = _updateTag;
            if (string.IsNullOrEmpty(tag) || _updating) return;

            var dlg = new ConfirmDialog(
                string.Format(_host.Loc("Str_About_UpdateHeading"), AppInfo.DisplayName, tag),
                _host.Loc("Str_About_UpdateDetail"),
                _host.Loc("Str_About_UpdateNow"),
                _host.Loc("Str_Btn_Cancel")) { Owner = _host.Window };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;
            await InstallAsync(tag!);
        }

        private async Task InstallAsync(string tag)
        {
            if (_updating) return;
            _updating           = true;
            _host.UpdateEnabled = false;
            _host.UpdateText    = _host.Loc("Str_About_Downloading");

            string? newExe;
            try
            {
                newExe = await UpdateService.DownloadAsync(tag!);
            }
            catch
            {
                _updating           = false;
                _host.UpdateEnabled = true;
                _host.UpdateText    = string.Format(_host.Loc("Str_About_UpdateAvailable"), tag);
                WebLink.Open(UpdateService.ReleasesUrl);
                return;
            }

            try
            {
                // Declining UAC throws Win32Exception 1223, so the shutdown only happens once the
                // helper is actually running - otherwise the app would close without updating.
                if (!_host.SaveOpenNote())
                {
                    _updating = false;
                    _host.UpdateEnabled = true;
                    _host.UpdateText = string.Format(_host.Loc("Str_About_UpdateAvailable"), tag);
                    return;
                }
                UpdateService.StartSwap(newExe, tag!.TrimStart('v', 'V'));
                System.Windows.Application.Current.Shutdown();
            }
            catch
            {
                // A failed helper launch retains the verified download for recovery.
                _updating           = false;
                _host.UpdateEnabled = true;
                _host.UpdateText    = string.Format(_host.Loc("Str_About_UpdateAvailable"), tag);
            }
        }
    }
}
