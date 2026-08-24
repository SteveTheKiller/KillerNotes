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

        internal AboutController(IAboutHost host) => _host = host;

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
            if (tag is null) return;

            _updateTag          = tag;
            _host.UpdateText    = string.Format(_host.Loc("Str_About_UpdateAvail"), tag);
            _host.UpdateVisible = true;
        }

        /// <summary>Downloads the newer release, verifies it, and hands off to the helper that swaps
        /// the exe and relaunches. Every failure path falls back to the releases page, so a user who
        /// cannot be updated automatically is never left thinking nothing happened.</summary>
        internal async void Update()
        {
            var tag = _updateTag;
            if (string.IsNullOrEmpty(tag)) return;

            var dlg = new ConfirmDialog(
                string.Format(_host.Loc("Str_Dlg_UpdateHead"), AppInfo.DisplayName, tag),
                _host.Loc("Str_Dlg_UpdateBody"),
                _host.Loc("Str_Btn_Update")) { Owner = _host.Window };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            _host.UpdateEnabled = false;
            _host.UpdateText    = _host.Loc("Str_About_Downloading");

            string? newExe;
            try
            {
                newExe = await UpdateService.DownloadAsync(tag!);
            }
            catch
            {
                _host.UpdateEnabled = true;
                _host.UpdateText    = string.Format(_host.Loc("Str_About_UpdateAvail"), tag);
                WebLink.Open(UpdateService.ReleasesUrl);
                return;
            }

            try
            {
                // Declining UAC throws Win32Exception 1223, so the shutdown only happens once the
                // helper is actually running - otherwise the app would close without updating.
                UpdateService.StartSwap(newExe, tag!.TrimStart('v', 'V'));
                System.Windows.Application.Current.Shutdown();
            }
            catch
            {
                UpdateService.DiscardDownload(newExe);
                _host.UpdateEnabled = true;
                _host.UpdateText    = string.Format(_host.Loc("Str_About_UpdateAvail"), tag);
            }
        }
    }
}
