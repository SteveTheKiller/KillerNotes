using System;

namespace KillerNotes.Services
{
    /// <summary>
    /// The one rule for which hyperlink targets stay live: absolute http, https and mailto.
    /// Opening a link, pasting HTML, exporting HTML and copying rich content as HTML all ask
    /// here, so a target the app refuses to open never becomes a live link somewhere else.
    /// Relative targets are refused too: a note has no base address, and a relative href in a
    /// saved page resolves against wherever the file happens to sit.
    /// </summary>
    internal static class LinkPolicy
    {
        /// <summary>The canonical href for an allowed target, or null when the link should
        /// fall back to its plain label.</summary>
        internal static string? AllowedHref(Uri? uri)
        {
            if (uri is null || !uri.IsAbsoluteUri) return null;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto
                ? uri.AbsoluteUri
                : null;
        }

        internal static bool IsAllowed(Uri? uri) => AllowedHref(uri) != null;
    }
}
