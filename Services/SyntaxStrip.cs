// Removes syntax-highlight coloring from a serialized note, so the live editor never has to be
// stripped and repainted just to save.
//
// Syntax colors are a transient VIEW over the user's text, not their formatting. They are applied
// as real Foreground values on the document (Shell/SyntaxHighlighting.cs), which meant the save
// path had to clear every color, serialize, then re-tokenize and repaint the whole note. That runs
// inside the 2 second autosave, so a long script paid a full teardown and rebuild every two
// seconds while being edited, which is what made a highlighted note crawl.
//
// Post-processing the bytes instead lets the editor keep its paint and its incremental cache. A
// XamlPackage is an OPC zip whose entry part is XAML, so the colors can be taken out of the stored
// copy without the live document ever changing.
//
// TextEffects was not an option for the same reason Foreground is a problem: it is a serializable
// property on Run and would bake into the package just as readily. Recapturing and restoring the
// user's own brush is banned outright - it baked the previous theme's color in as a local value
// and gave 98SE white text on a white page (2026-08-08).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace KillerNotes.Services
{
    internal static class SyntaxStrip
    {
        /// <summary>Foreground values equal to one of the palette colors are removed from the
        /// package's XAML. Anything else the user colored deliberately is left alone.</summary>
        public static byte[] Remove(byte[] package, IReadOnlyCollection<Color> palette)
        {
            if (package == null || package.Length == 0 || palette == null || palette.Count == 0)
                return package ?? [];
            if (!IsPackage(package)) return package;   // raw markdown blob, nothing to strip

            var hexes = new HashSet<string>(
                palette.Select(Hex).Concat(palette.Select(c => Hex(c).ToLowerInvariant())),
                StringComparer.OrdinalIgnoreCase);

            try
            {
                // Work on a copy: Package writes in place, and a failure part way through must not
                // be able to hand back a half-rewritten note.
                var buffer = new MemoryStream();
                buffer.Write(package, 0, package.Length);
                buffer.Position = 0;

                using (var pkg = Package.Open(buffer, FileMode.Open, FileAccess.ReadWrite))
                {
                    var part = pkg.GetParts().FirstOrDefault(
                        p => p.Uri.OriginalString.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
                    if (part == null) return package;

                    string xaml;
                    using (var reader = new StreamReader(part.GetStream(FileMode.Open, FileAccess.Read),
                                                         Encoding.UTF8))
                        xaml = reader.ReadToEnd();

                    string cleaned = RemoveForegrounds(xaml, hexes);
                    if (ReferenceEquals(cleaned, xaml)) return package;   // nothing matched

                    // Truncate: the replacement is shorter, and a shorter write into an existing
                    // part leaves the tail of the old XAML behind and corrupts the note.
                    using (var stream = part.GetStream(FileMode.Create, FileAccess.Write))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write(cleaned);
                }

                return buffer.ToArray();
            }
            catch
            {
                // A note that keeps its syntax colors is cosmetically wrong on the next open.
                // A note that fails to save is lost work. Never let this throw into the save path.
                return package;
            }
        }

        /// <summary>Drops Foreground attributes whose value is one of the syntax colors, including
        /// the whitespace in front so no double space is left behind.</summary>
        private static string RemoveForegrounds(string xaml, HashSet<string> hexes)
        {
            string result = Regex.Replace(xaml, @"\s+Foreground=""(#[0-9A-Fa-f]{6,8})""", m =>
                hexes.Contains(m.Groups[1].Value) ? "" : m.Value);
            return result.Length == xaml.Length ? xaml : result;
        }

        /// <summary>WPF serializes a SolidColorBrush as #AARRGGBB.</summary>
        private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        private static bool IsPackage(byte[] blob) =>
            blob.Length >= 4 && blob[0] == 0x50 && blob[1] == 0x4B && blob[2] == 0x03 && blob[3] == 0x04;
    }
}
