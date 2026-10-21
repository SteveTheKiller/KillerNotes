using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KillerNotes.Services
{
    internal enum AttachmentKind { Sketch = 1, Recording = 2 }

    /// <summary>One stored payload: a sketch's editable drawing or a recording's audio.</summary>
    internal sealed class Attachment(AttachmentKind kind, byte[] payload, int durationMs = 0)
    {
        public AttachmentKind Kind { get; } = kind;
        public byte[] Payload { get; } = payload;
        public int DurationMs { get; } = durationMs;
        public string Hash { get; } = Attachments.HashOf(payload);
    }

    /// <summary>
    /// How a note's document points at its sketches and recordings.
    ///
    /// The rich-note format (a XamlPackage) strips Tag, Name, Uid and automation properties from
    /// embedded elements and drops any element that is not an Image, so the one thing that
    /// reliably survives a save, a reload, a history version and a clipboard copy is an Image's
    /// ToolTip string. Each sketch, and each recording (stored as a picture of its chip), is an
    /// Image whose ToolTip is a marker naming its payload by SHA-256: "kn-sk:&lt;hash&gt;" or
    /// "kn-rec:&lt;hash&gt;". Naming by content means a history version, a pasted copy or a shared
    /// note always finds exactly the payload it was saved with, and two copies of one sketch share
    /// a single stored payload.
    ///
    /// NoteStore reads markers straight out of stored packages (MarkersIn) to keep its reference
    /// lists, so payload cleanup never depends on the editor.
    /// </summary>
    internal static class Attachments
    {
        private const string SketchPrefix = "kn-sk:", RecordingPrefix = "kn-rec:";

        internal static string HashOf(byte[] payload)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(payload);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        internal static string Marker(AttachmentKind kind, string hash) =>
            (kind == AttachmentKind.Recording ? RecordingPrefix : SketchPrefix) + hash;

        internal static bool TryParseMarker(object? value, out AttachmentKind kind, out string hash)
        {
            kind = AttachmentKind.Sketch;
            hash = "";
            if (value is not string s) return false;
            if (s.StartsWith(SketchPrefix, StringComparison.Ordinal)) { kind = AttachmentKind.Sketch; hash = s.Substring(SketchPrefix.Length); }
            else if (s.StartsWith(RecordingPrefix, StringComparison.Ordinal)) { kind = AttachmentKind.Recording; hash = s.Substring(RecordingPrefix.Length); }
            else return false;
            return IsHash(hash);
        }

        private static bool IsHash(string s)
        {
            if (s.Length != 64) return false;
            foreach (char c in s)
                if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
            return true;
        }

        private static readonly Regex MarkerAttribute =
            new("ToolTip=\"kn-(?:sk|rec):([0-9a-f]{64})\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        /// <summary>Every payload hash a stored rich note points at. Empty for markdown text,
        /// an empty body or anything that is not a XamlPackage.</summary>
        internal static HashSet<string> MarkersIn(byte[]? content)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            if (content == null || content.Length < 4 || content[0] != 0x50 || content[1] != 0x4B) return found;
            using var ms = new MemoryStream(content, writable: false);
            using var package = Package.Open(ms, FileMode.Open, FileAccess.Read);
            foreach (var part in package.GetParts())
            {
                if (!part.Uri.OriginalString.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) continue;
                using var reader = new StreamReader(part.GetStream(FileMode.Open, FileAccess.Read));
                foreach (Match m in MarkerAttribute.Matches(reader.ReadToEnd())) found.Add(m.Groups[1].Value);
            }
            return found;
        }

        /// <summary>A stored rich note with one marker swapped for another, without loading it
        /// into an editor. Null when the package does not contain the old marker.</summary>
        internal static byte[]? ReplaceMarker(byte[] content, string oldMarker, string newMarker)
        {
            using var ms = new MemoryStream();
            ms.Write(content, 0, content.Length);
            bool replaced = false;
            using (var package = Package.Open(ms, FileMode.Open, FileAccess.ReadWrite))
            {
                foreach (var part in package.GetParts())
                {
                    if (!part.Uri.OriginalString.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) continue;
                    string xaml;
                    using (var reader = new StreamReader(part.GetStream(FileMode.Open, FileAccess.Read)))
                        xaml = reader.ReadToEnd();
                    string attribute = "ToolTip=\"" + oldMarker + "\"";
                    if (xaml.IndexOf(attribute, StringComparison.Ordinal) < 0) continue;
                    using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.Write(xaml.Replace(attribute, "ToolTip=\"" + newMarker + "\""));
                    replaced = true;
                }
            }
            return replaced ? ms.ToArray() : null;
        }

        /// <summary>MarkersIn as a JSON array, for the SQLite reference triggers (json_each).</summary>
        internal static string MarkersJson(byte[]? content)
        {
            var sb = new StringBuilder("[");
            foreach (string hash in MarkersIn(content))
            {
                if (sb.Length > 1) sb.Append(',');
                sb.Append('"').Append(hash).Append('"');
            }
            return sb.Append(']').ToString();
        }
    }
}
