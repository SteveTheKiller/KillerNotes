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
    }
}
