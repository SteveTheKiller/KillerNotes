using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml.Linq;

namespace KillerNotes.Services
{
    /// <summary>
    /// Cleans a rich note that came from someone else's file (.knote, .kndb) before it is stored.
    ///
    /// A XamlPackage is a zip whose XAML names its pictures by part ("./Image1.png"). Nothing in
    /// WPF stops a hand-built package from naming "\\host\share\x.png" or a web address instead,
    /// and opening the note would then fetch it: over SMB that hands the host the user's NTLM
    /// hash. So every source-like attribute that does not point inside the package is removed.
    /// The picture is lost; the note, its text and everything the package carries itself stay.
    ///
    /// Pure zip and XML work, no WPF, so it runs on any thread the import runs on.
    /// </summary>
    internal static class PackageSanitizer
    {
        private static readonly string[] SourceAttributes = ["Source", "UriSource", "ImageSource", "BaseUri", "NavigateUri"];

        /// <summary>The blob with every outside reference removed, or the same array when it had
        /// none or is not a package this can read.</summary>
        public static byte[] WithoutOutsideReferences(byte[] blob)
        {
            if (!MarkdownBlob.IsPackage(blob)) return blob;
            try
            {
                var ms = new MemoryStream();
                ms.Write(blob, 0, blob.Length);
                bool changed = false;
                using (var pkg = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
                {
                    foreach (var part in pkg.GetParts().Where(p => p.Uri.OriginalString.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        XDocument doc;
                        using (var read = part.GetStream(FileMode.Open, FileAccess.Read)) doc = XDocument.Load(read, LoadOptions.PreserveWhitespace);
                        var outside = doc.Descendants().Attributes()
                            .Where(a => SourceAttributes.Contains(a.Name.LocalName) && !IsInside(a, a.Value)).ToList();
                        if (outside.Count == 0) continue;
                        // A bitmap left without its source will not load, and a package that will not
                        // load is the startup crash KnoteCompatibilityTests guards against. So the whole
                        // picture goes: its UI container when it has one, else the Image itself.
                        foreach (var a in outside)
                        {
                            var owner = a.Parent!;
                            var holder = owner.AncestorsAndSelf().FirstOrDefault(e => e.Name.LocalName is "BlockUIContainer" or "InlineUIContainer")
                                      ?? owner.AncestorsAndSelf().FirstOrDefault(e => e.Name.LocalName is "Image" or "MediaElement");
                            if (holder != null) { if (holder.Parent != null) holder.Remove(); }
                            else if (a.Parent != null) a.Remove();
                        }
                        using var write = part.GetStream(FileMode.Create, FileAccess.Write);
                        doc.Save(write, SaveOptions.DisableFormatting);
                        changed = true;
                    }
                }
                return changed ? ms.ToArray() : blob;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or FileFormatException or InvalidOperationException or ArgumentException)
            {
                return blob;   // unreadable here means unreadable to the editor too, which already copes
            }
        }

        // A link the user clicks is not fetched on open, so NavigateUri only loses web-shaped
        // values that WPF would resolve on its own: a file or share path. Everything else must
        // be a relative part name.
        private static bool IsInside(XAttribute attribute, string value)
        {
            string v = value.Trim();
            if (attribute.Name.LocalName == "NavigateUri")
                return !(v.StartsWith(@"\\", StringComparison.Ordinal) || v.StartsWith("//", StringComparison.Ordinal)
                         || v.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
            if (v.Length == 0 || v.StartsWith("{", StringComparison.Ordinal)) return true;   // empty, or a markup extension
            if (v.StartsWith(@"\\", StringComparison.Ordinal) || v.StartsWith("//", StringComparison.Ordinal)) return false;
            if (v.IndexOf(':') >= 0) return false;   // any scheme or drive letter
            return !v.Contains("..");
        }
    }
}
