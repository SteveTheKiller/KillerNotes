using System;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>
    /// The one case- and composition-insensitive form text is compared in for library search and
    /// wikilink resolution: precomposed (NFC), then upper-cased by the invariant culture. So "café",
    /// "CAFÉ" and "cafe" plus a combining accent are one word, Greek and Cyrillic fold like Latin,
    /// and there is no Turkish dotless-i rule. SQLite's own NOCASE folds ASCII only, which is why
    /// "café" used to miss "CAFÉ". Registered on the connection as kn_fold() and COLLATE KNFOLD.
    /// </summary>
    internal static class TextFold
    {
        internal static string Fold(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string composed;
            try { composed = text!.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { composed = text!; }   // unpaired surrogate: fold case only
            return composed.ToUpperInvariant();
        }
    }
}
