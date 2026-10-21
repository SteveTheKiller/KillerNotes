// ═══════════════════════════════════════════════════════════
//  FOLDER IMPORTER  -  a folder of Markdown files as notes
// ═══════════════════════════════════════════════════════════
//
// The way in (1.6.0, KN-P03), the counterpart of VaultExporter: an Obsidian vault, a KillerNotes
// export, or the Markdown export of another app. Three stages, so nothing changes until the
// user has seen what will happen and said yes:
//
//   Plan     (any thread)  reads the folder: each .md file's front matter, its group from its
//                          folders, the pictures and audio it links to, and what cannot come in -
//                          other file types, ![[embeds]], files over the size limit, missing files.
//                          Files imported before with the same content are counted, not planned,
//                          so importing a folder twice never makes duplicates.
//   Prepare  (STA thread)  turns each planned file into a note: Markdown as written when it has no
//                          pictures or audio, otherwise rich text with the pictures embedded and
//                          WAV/FLAC audio as recording chips. Links are rewritten to point at the
//                          imported notes by title. Cancelable; writes nothing.
//   Apply    (UI thread)   NoteStore.ImportBatch: every note, group, tag, payload and link in one
//                          transaction, so a failure or a closed app leaves the notepad as it was.
//
// Web pictures are never downloaded: they stay links. #tags written in the text stay text; only
// front matter tags become KillerNotes tags.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerNotes.Services
{
    /// <summary>What the open notepad already holds, for the preview: sources imported before
    /// ("path|hash"), folded titles, and group paths.</summary>
    internal sealed class ImportState(HashSet<string> sources, HashSet<string> titles, HashSet<string> groups)
    {
        public HashSet<string> Sources { get; } = sources;
        public HashSet<string> Titles { get; } = titles;
        public HashSet<string> Groups { get; } = groups;
    }

    internal sealed class ImportCandidate
    {
        public string FullPath = "";
        public string File = "";                 // relative to the imported folder, '/' separated
        public string Title = "";
        public List<string> Group = [];
        public List<string> Tags = [];
        public DateTime? Created, Modified;
        public bool Pinned;
        public string Color = "";
        public string Body = "";
        public string Hash = "";
        public string SourceKey = "";
    }

    /// <summary>A file, or part of one, that will not come in. Reason: type, large, embed,
    /// missing, audio or unlinked.</summary>
    internal sealed record ImportSkip(string File, string Reason);

    internal sealed class ImportPlan
    {
        public string Folder = "";
        public List<ImportCandidate> Notes = [];
        public List<ImportSkip> Skipped = [];
        public List<string> NewGroups = [];
        public List<string> Clashes = [];
        public int AlreadyImported;
    }

    /// <summary>One note ready for NoteStore.ImportBatch.</summary>
    internal sealed class PreparedImport
    {
        public string Title = "";
        public string Group = "";                // a group path, NoteStore.GroupSep between levels
        public List<string> Tags = [];
        public DateTime Created, Modified;
        public bool Pinned;
        public string Color = "";
        public int Format;
        public byte[] Content = [];
        public string Plain = "";
        public List<string> Links = [];
        public List<Attachment> Attachments = [];
        public string SourceKey = "";
        public string SourceHash = "";
    }

    internal static class FolderImporter
    {
        public const long DefaultMaxBytes = 50L * 1024 * 1024;

        private static readonly HashSet<string> NoteExts = new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };
        private static readonly HashSet<string> PictureExts = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff" };
        private static readonly HashSet<string> RecordingExts = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".flac" };
        private static readonly HashSet<string> OtherAudioExts = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".ogg", ".opus", ".aac", ".webm" };

        // [text](destination) and ![alt](destination); the destination bracketed or bare.
        private static readonly Regex LinkRx = new(@"(!?)\[([^\]\r\n]*)\]\((?:<([^>\r\n]+)>|([^)\s]+))\)", RegexOptions.Compiled);
        private static readonly Regex EmbedRx = new(@"!\[\[[^\]\r\n]+\]\]", RegexOptions.Compiled);
        private static readonly Regex WikiRx = new(@"\[\[([^\[\]\r\n|\\]{1,200})(?:(\\?\|)([^\[\]\r\n]{0,200}))?\]\]", RegexOptions.Compiled);

        // ---- Plan ----

        public static ImportPlan Plan(string folder, ImportState state, CancellationToken token, long maxBytes = DefaultMaxBytes)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\', '/');
            var plan = new ImportPlan { Folder = root };
            string Rel(string full) => full.Substring(root.Length + 1).Replace('\\', '/');
            // A KillerNotes export carries its own report and manifest; neither is a note.
            bool ownExport = File.Exists(Path.Combine(root, VaultExporter.ManifestName));
            var others = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in Files(root))
            {
                token.ThrowIfCancellationRequested();
                string rel = Rel(path);
                if (ownExport && (rel == VaultExporter.ReportName || rel == VaultExporter.ManifestName)) continue;
                if (!NoteExts.Contains(Path.GetExtension(path))) { others.Add(path); continue; }
                if (new FileInfo(path).Length > maxBytes) { plan.Skipped.Add(new ImportSkip(rel, "large")); continue; }

                byte[] bytes = File.ReadAllBytes(path);
                string text = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Replace("\r\n", "\n");
                string hash = Hex(bytes);
                string key = path.ToLowerInvariant();
                var (front, body) = SplitFrontMatter(text);
                bool already = state.Sources.Contains(key + "|" + hash);
                // An already-imported note's files still count as used, so they are not listed as
                // unlinked; only a note that is coming in reports what it cannot bring.
                foreach (string media in MediaIn(body, Path.GetDirectoryName(path)!, rel, plan, maxBytes, report: !already))
                    used.Add(media);
                if (already) { plan.AlreadyImported++; continue; }

                var note = new ImportCandidate
                {
                    FullPath = path, File = rel, Body = body, Hash = hash, SourceKey = key,
                    Title = Path.GetFileNameWithoutExtension(path),
                    Group = rel.Split('/').Take(rel.Count(c => c == '/')).ToList(),
                };
                ApplyFrontMatter(note, front);
                if (EmbedRx.IsMatch(body)) plan.Skipped.Add(new ImportSkip(rel, "embed"));
                plan.Notes.Add(note);
            }

            foreach (string path in others)
            {
                if (used.Contains(path)) continue;
                string ext = Path.GetExtension(path);
                bool media = PictureExts.Contains(ext) || RecordingExts.Contains(ext) || OtherAudioExts.Contains(ext);
                plan.Skipped.Add(new ImportSkip(Rel(path), media ? "unlinked" : "type"));
            }

            plan.Notes = [.. plan.Notes.OrderBy(n => n.File, StringComparer.OrdinalIgnoreCase)];
            plan.Clashes = [.. plan.Notes.Where(n => state.Titles.Contains(TextFold.Fold(n.Title.Trim()))).Select(n => n.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase)];
            var groups = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in plan.Notes)
                for (int depth = 1; depth <= note.Group.Count; depth++)
                {
                    string path = string.Join(NoteStore.GroupSep, note.Group.Take(depth));
                    if (!state.Groups.Contains(path)) groups.Add(string.Join(" / ", note.Group.Take(depth)));
                }
            plan.NewGroups = [.. groups];
            plan.Skipped = [.. plan.Skipped.Distinct().OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase)];
            return plan;
        }

        /// <summary>Every file under root, skipping dot folders (.obsidian, .git, .trash): app
        /// settings and version control, not notes.</summary>
        private static IEnumerable<string> Files(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                foreach (string file in Directory.GetFiles(dir)) yield return file;
                foreach (string sub in Directory.GetDirectories(dir))
                    if (!Path.GetFileName(sub).StartsWith(".", StringComparison.Ordinal)) pending.Push(sub);
            }
        }

        /// <summary>The local pictures and WAV/FLAC files a note links to, as full paths. With
        /// report set, what cannot come in is added to the plan's skipped list.</summary>
        private static IEnumerable<string> MediaIn(string body, string dir, string rel, ImportPlan plan, long maxBytes, bool report)
        {
            foreach (var link in Links(body))
            {
                string? local = LocalPath(link.Destination, dir);
                if (local == null) continue;
                string ext = Path.GetExtension(local);
                if (NoteExts.Contains(ext)) continue;
                bool picture = link.Image && PictureExts.Contains(ext);
                bool recording = RecordingExts.Contains(ext);
                if (!picture && !recording)
                {
                    if (report && OtherAudioExts.Contains(ext)) plan.Skipped.Add(new ImportSkip(rel + ": " + link.Destination, "audio"));
                    continue;
                }
                if (!File.Exists(local)) { if (report) plan.Skipped.Add(new ImportSkip(rel + ": " + link.Destination, "missing")); continue; }
                if (new FileInfo(local).Length > maxBytes) { if (report) plan.Skipped.Add(new ImportSkip(rel + ": " + link.Destination, "large")); continue; }
                yield return local;
            }
        }

        private sealed record MdLink(Match Match, bool Image, string Text, string Destination);

        private static IEnumerable<MdLink> Links(string body)
        {
            bool fenced = false;
            foreach (string line in body.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) fenced = !fenced;
                else if (!fenced)
                    foreach (Match m in LinkRx.Matches(line))
                        yield return new MdLink(m, m.Groups[1].Value == "!", m.Groups[2].Value,
                            m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);
            }
        }

        /// <summary>A link destination as a local file path, or null for a web or mail address.</summary>
        private static string? LocalPath(string destination, string dir)
        {
            if (destination.Contains("://") || destination.StartsWith("#", StringComparison.Ordinal) ||
                destination.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return null;
            int hash = destination.IndexOf('#');
            if (hash > 0) destination = destination.Substring(0, hash);
            try { return Path.GetFullPath(Path.Combine(dir, Uri.UnescapeDataString(destination).Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        }

        // ---- Front matter ----

        private static (Dictionary<string, object> Front, string Body) SplitFrontMatter(string text)
        {
            var front = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (front, text);
            int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0) return (front, text);
            int bodyStart = text.IndexOf('\n', end + 4);
            string body = bodyStart < 0 ? "" : text.Substring(bodyStart + 1);
            string? listKey = null;
            foreach (string raw in text.Substring(4, end - 3).Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (listKey != null && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    ((List<string>)front[listKey]).Add(Unquote(line.TrimStart().Substring(2)));
                    continue;
                }
                listKey = null;
                int colon = line.IndexOf(':');
                if (colon <= 0 || char.IsWhiteSpace(line[0])) continue;
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (value.Length == 0) { front[key] = new List<string>(); listKey = key; }
                else if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                    front[key] = SplitInline(value.Substring(1, value.Length - 2)).Select(Unquote).Where(v => v.Length > 0).ToList();
                else front[key] = Unquote(value);
            }
            return (front, body.TrimStart('\n'));
        }

        /// <summary>Splits a YAML flow list on commas outside quotes.</summary>
        private static IEnumerable<string> SplitInline(string items)
        {
            var sb = new StringBuilder();
            char quote = '\0';
            for (int i = 0; i < items.Length; i++)
            {
                char c = items[i];
                if (quote != '\0')
                {
                    if (c == '\\' && quote == '"' && i + 1 < items.Length) { sb.Append(c).Append(items[++i]); continue; }
                    if (c == quote) quote = '\0';
                    sb.Append(c);
                }
                else if (c is '"' or '\'') { quote = c; sb.Append(c); }
                else if (c == ',') { yield return sb.ToString(); sb.Clear(); }
                else sb.Append(c);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        private static string Unquote(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            return value;
        }

        private static void ApplyFrontMatter(ImportCandidate note, Dictionary<string, object> front)
        {
            string? Text(params string[] keys) => keys.Select(k => front.TryGetValue(k, out var v) ? v as string : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (Text("title") is string title) note.Title = title.Trim();
            if (front.TryGetValue("tags", out var tags))
                note.Tags = (tags as List<string> ?? SplitInline((string)tags).Select(Unquote).ToList())
                    .Select(t => t.Trim().TrimStart('#').Replace(",", "")).Where(t => t.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            note.Created = Date(Text("created", "date"));
            note.Modified = Date(Text("modified", "updated"));
            note.Pinned = string.Equals(Text("pinned"), "true", StringComparison.OrdinalIgnoreCase);
            if (Text("color") is string color && Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")) note.Color = color.ToUpperInvariant();
        }

        /// <summary>A front matter date. One with a zone (2025-01-02T03:04:00Z) comes back as local
        /// time; one without is taken as already local, which is how KillerNotes writes them.</summary>
        private static DateTime? Date(string? text) =>
            text != null && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var when)
                ? when : null;

        private static string Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        // ---- Prepare ----

        /// <summary>Builds every planned note's body. Needs an STA thread for the rich ones; writes
        /// nothing, so canceling here leaves the notepad untouched.</summary>
        public static List<PreparedImport> Prepare(ImportPlan plan, IProgress<int>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Link targets: a note's path without .md, and its file name when that is unique.
            var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in plan.Notes) byPath[StripExt(note.File)] = note.Title;
            var byName = plan.Notes.GroupBy(n => Path.GetFileNameWithoutExtension(n.File), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Title, StringComparer.OrdinalIgnoreCase);
            string? TitleFor(string target)
            {
                target = target.Trim().Replace('\\', '/');
                if (byPath.TryGetValue(StripExt(target), out var t)) return t;
                return byName.TryGetValue(Path.GetFileNameWithoutExtension(target), out t) ? t : null;
            }

            var prepared = new List<PreparedImport>();
            int done = 0;
            foreach (var note in plan.Notes)
            {
                token.ThrowIfCancellationRequested();
                string dir = Path.GetDirectoryName(note.FullPath)!;
                string body = RewriteLinks(note.Body, dir, plan.Folder, TitleFor);
                var media = new Dictionary<string, string>();   // token -> full path
                body = TokenizeMedia(body, dir, media);
                var item = new PreparedImport
                {
                    Title = note.Title,
                    Group = string.Join(NoteStore.GroupSep, note.Group),
                    Tags = note.Tags,
                    Created = note.Created ?? File.GetCreationTime(note.FullPath),
                    Modified = note.Modified ?? File.GetLastWriteTime(note.FullPath),
                    Pinned = note.Pinned,
                    Color = note.Color,
                    SourceKey = note.SourceKey,
                    SourceHash = note.Hash,
                };
                if (media.Count == 0)
                {
                    item.Format = 1;
                    item.Content = MarkdownBlob.Encode(body);
                    item.Plain = body;
                }
                else
                {
                    var doc = MarkdownConvert.ToDocument(body, 13, tables: true);
                    PlaceMedia(doc.Blocks, media, item.Attachments);
                    doc.FontFamily = new FontFamily("Segoe UI");
                    doc.FontSize = 13;
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    using var ms = new MemoryStream();
                    range.Save(ms, DataFormats.XamlPackage);
                    item.Content = ms.ToArray();
                    item.Plain = range.Text;
                }
                item.Links = WikiLinks.Parse(item.Plain);
                prepared.Add(item);
                progress?.Report(++done);
            }
            return prepared;
        }

        private static string StripExt(string path) =>
            NoteExts.Contains(Path.GetExtension(path)) ? path.Substring(0, path.Length - Path.GetExtension(path).Length) : path;

        /// <summary>[[path|label]] and [text](other.md) become [[Title|label]] links to the imported
        /// notes; anything that does not name one of them is left as written. Code fences are left
        /// alone, and a table row keeps its pipe escaped.</summary>
        internal static string RewriteLinks(string body, string dir, string root, Func<string, string?> titleFor)
        {
            var lines = body.Split('\n');
            bool fenced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) { fenced = !fenced; continue; }
                if (fenced) continue;
                string pipe = trimmed.StartsWith("|", StringComparison.Ordinal) ? "\\|" : "|";
                string Link(string title, string label) => label == title ? "[[" + title + "]]" : "[[" + title + pipe + label + "]]";
                lines[i] = WikiRx.Replace(lines[i], m =>
                {
                    if (titleFor(m.Groups[1].Value) is not string title) return m.Value;
                    return Link(title, m.Groups[3].Success ? m.Groups[3].Value : m.Groups[1].Value.Trim());
                });
                lines[i] = LinkRx.Replace(lines[i], m =>
                {
                    if (m.Groups[1].Value == "!") return m.Value;
                    string dest = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
                    if (LocalPath(dest, dir) is not string local || !NoteExts.Contains(Path.GetExtension(local))) return m.Value;
                    if (!local.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return m.Value;
                    if (titleFor(local.Substring(root.Length + 1)) is not string title) return m.Value;
                    string text = m.Groups[2].Value;
                    return Link(title, text.Length == 0 ? title : text);
                });
            }
            return string.Join("\n", lines);
        }

        /// <summary>Swaps each local picture and recording link for a private-use token the
        /// Markdown converter carries through as text, so PlaceMedia can put the real thing there.</summary>
        private static string TokenizeMedia(string body, string dir, Dictionary<string, string> media)
        {
            var lines = body.Split('\n');
            bool fenced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) { fenced = !fenced; continue; }
                if (fenced) continue;
                lines[i] = LinkRx.Replace(lines[i], m =>
                {
                    string dest = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
                    if (LocalPath(dest, dir) is not string local || !File.Exists(local)) return m.Value;
                    string ext = Path.GetExtension(local);
                    bool picture = m.Groups[1].Value == "!" && PictureExts.Contains(ext);
                    if (!picture && !RecordingExts.Contains(ext)) return m.Value;
                    string token = "" + media.Count.ToString(CultureInfo.InvariantCulture) + "";
                    media[token] = local;
                    return token;
                });
            }
            return string.Join("\n", lines);
        }

        private static readonly Regex TokenRx = new("(\\d+)", RegexOptions.Compiled);

        /// <summary>Replaces media tokens in the converted document with pictures and recording
        /// markers (the chip comes back from the marker when the note opens).</summary>
        private static void PlaceMedia(BlockCollection blocks, Dictionary<string, string> media, List<Attachment> attachments)
        {
            foreach (var block in blocks.ToList())
            {
                switch (block)
                {
                    case Paragraph p: PlaceMedia(p.Inlines, media, attachments); break;
                    case Section s: PlaceMedia(s.Blocks, media, attachments); break;
                    case List l: foreach (var item in l.ListItems) PlaceMedia(item.Blocks, media, attachments); break;
                    case Table t:
                        foreach (var group in t.RowGroups)
                            foreach (var row in group.Rows)
                                foreach (var cell in row.Cells) PlaceMedia(cell.Blocks, media, attachments);
                        break;
                }
            }
        }

        private static void PlaceMedia(InlineCollection inlines, Dictionary<string, string> media, List<Attachment> attachments)
        {
            foreach (var inline in inlines.ToList())
            {
                if (inline is Span span) { PlaceMedia(span.Inlines, media, attachments); continue; }
                if (inline is not Run run || run.Text.IndexOf('') < 0) continue;
                string text = run.Text;
                Inline after = run;
                int last = 0;
                var pieces = new List<Inline>();
                foreach (Match m in TokenRx.Matches(text))
                {
                    if (m.Index > last) pieces.Add(new Run(text.Substring(last, m.Index - last)));
                    if (media.TryGetValue(m.Value, out var path))
                        // A file named like a picture that does not decode keeps its name in the text.
                        pieces.Add(Embed(path, attachments) is Image image
                            ? new InlineUIContainer(image)
                            : new Run("[" + Path.GetFileName(path) + "]"));
                    last = m.Index + m.Length;
                }
                if (last < text.Length) pieces.Add(new Run(text.Substring(last)));
                foreach (var piece in pieces)
                {
                    inlines.InsertAfter(after, piece);
                    after = piece;
                }
                inlines.Remove(run);
            }
        }

        private static Image? Embed(string path, List<Attachment> attachments)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (RecordingExts.Contains(Path.GetExtension(path)))
            {
                var recording = new Attachment(AttachmentKind.Recording, bytes, AudioCodec.DurationMs(bytes));
                if (!attachments.Any(a => a.Hash == recording.Hash)) attachments.Add(recording);
                var marker = new Image
                {
                    Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4),
                    Stretch = Stretch.None,
                };
                Sketch.MarkImage(marker, Attachments.Marker(AttachmentKind.Recording, recording.Hash));
                return marker;
            }
            try
            {
                // Decoded, frozen, re-encoded as PNG: the shape the XamlPackage writer keeps, as
                // ImportImage does for a dropped picture.
                var raw = new BitmapImage();
                raw.BeginInit();
                raw.CacheOption = BitmapCacheOption.OnLoad;
                raw.StreamSource = new MemoryStream(bytes);
                raw.EndInit();
                raw.Freeze();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(raw));
                using var png = new MemoryStream();
                encoder.Save(png);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(png.ToArray());
                bitmap.EndInit();
                bitmap.Freeze();
                return new Image { Source = bitmap, MaxWidth = 640, Stretch = Stretch.Uniform };
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
            {
                return null;   // not a picture after all
            }
        }
    }
}
