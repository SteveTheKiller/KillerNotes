using System;
using System.ComponentModel;
using KillerNotes.Controls;
using KillerNotes.Services;
using KillerNotes.Shell;

namespace KillerNotes.Features.About
{
    // APP-OWNED: KillerNotes' side of the family About controller (AboutController.cs).
    internal sealed partial class AboutController
    {
        // --demo previews the signed card (DemoMode.cs), using the real certificate values.
        partial void AdjustSignature(ref bool valid, ref string subject, ref string thumbprint)
        {
            if (!MainWindow.DemoMode) return;
            valid      = true;
            subject    = AppInfo.DemoSubject;
            thumbprint = AppInfo.DemoThumbprint;
        }

        // No startup update prompt under --demo, so captures stay clean.
        partial void SkipStartupCheck(ref bool skip) => skip = MainWindow.DemoMode;

        // The open note saves before the swap helper starts; when it cannot, the update waits.
        partial void ConfirmUpdateShutdown(ref bool proceed)
            => proceed = _host.Window is not MainWindow main || main.SaveOpenNoteForUpdate();

        // UAC declined or the helper failed: the app stays, and so does anything it was told to drop.
        partial void UpdateShutdownCanceled() => (_host.Window as MainWindow)?.QuitCalledOff();

        partial void UpdateNotVerified(Exception failure)
        {
            // The download arrived but was refused, so the releases page is no remedy: say so.
            new ConfirmDialog(AppInfo.DisplayName, _host.Loc("Str_About_UpdateNotVerified"), _host.Loc("Str_Btn_OK"), notice: true) { Owner = _host.Window }.ShowDialog();
        }

        partial void UpdateHandoffFailed(Exception failure)
        {
            if (HandoffFailureText(failure, _host.Loc) is not string text) return;
            new ConfirmDialog(AppInfo.DisplayName, text, _host.Loc("Str_Btn_OK"), notice: true) { Owner = _host.Window }.ShowDialog();
        }

        private const int UacDeclined = 1223;

        /// <summary>What to tell the user, or null when the user said no to the UAC prompt.</summary>
        internal static string? HandoffFailureText(Exception failure, Func<string, string> loc) =>
            failure is Win32Exception { NativeErrorCode: UacDeclined } ? null : string.Format(loc("Str_About_UpdateFailed"), failure.Message);
    }
}
