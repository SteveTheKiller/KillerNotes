// ═══════════════════════════════════════════════════════════
//  VAULT EXPORTER  -  the whole notepad as a folder of .md files
// ═══════════════════════════════════════════════════════════
//
// The anti-lock-in half of the markdown work (1.3.0), made a dependable exit path in 1.6.0
// (KN-P02). One-way and on demand: every note becomes a .md file, groups become nested folders,
// tags, dates, the note's id, pin and title color ride in YAML front matter, and pictures,
// sketches and recordings go to a <name>.assets folder beside the note. What comes out is a
// plain folder that git, Obsidian, or any text editor reads.
//
// Deliberately NOT a live two-way sync of a watched folder. Sync means conflict resolution,
// file-watcher races, and partial writes from other editors, and it would put a plaintext copy
// of an encrypted database on disk permanently, which contradicts the reason the database is
// encrypted at all. Export is explicit, so the user chooses the moment the plaintext exists.
//
// What makes it an exit path rather than a dump:
//   - Every export gets a fresh dated folder, so a second export never mixes "(2)" copies into
//     the first one.
//   - Links survive renamed files. A [[Title]] is left alone when a vault app would find the
//     note by that name, and becomes [[Folder/File name|Title]] when the file name differs (an
//     unsafe character, a "(2)" for a repeated title) or the same name sits in two folders.
//     Duplicate titles resolve to the lowest id, as they do in the app (NoteStore.TitleIndex).
//   - killernotes-export.json lists every note with its id, file, group, tags, dates, format,
//     files and losses, under a format version, so an importer can bring it back exactly.
//   - export-report.md is always written: a one-line summary, what each note lost, what is never
//     exported (trash, history), and the result of reading the written folder back (Check).
//   - Cancel removes the folder; an interrupted export is never left looking finished.
//
// It runs on its own STA thread (FlowDocument needs one) and reads through its own read-only
// connection (NoteStore.OpenVaultSource), so the app stays usable while it works. Strings are
// resolved on the UI thread beforehand (VaultExportText): resources are not read from here.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Models;

namespace KillerNotes.Services
{
    /// <summary>Where the exporter reads note bodies and attachments from.</summary>
    internal interface IVaultSource
    {
        byte[]? LoadContent(long id);
        Attachment? GetAttachment(string hash);
    }

    /// <summary>The report's words, looked up on the UI thread before the export starts.</summary>
    internal sealed class VaultExportText
    {
        private readonly Dictionary<string, string> _text = new();

        private static readonly string[] Keys =
        [
            "Str_Untitled", "Str_Vault_ReportTitle", "Str_Vault_NothingLost", "Str_Vault_SomeLost",
            "Str_Vault_NotIncluded", "Str_Vault_LostMedia", "Str_Vault_NoteFailed", "Str_Vault_CheckHead",
            "Str_Vault_CheckClean", "Str_Vault_Unresolved", "Str_MdLoss_Tables", "Str_MdLoss_Colors",
        ];

        internal static VaultExportText From(Func<string, string> loc)
        {
            var text = new VaultExportText();
            foreach (string key in Keys) text._text[key] = loc(key);
            return text;
        }

        internal string this[string key] => _text.TryGetValue(key, out var value) ? value : key;
    }

    internal sealed class VaultNoteResult
    {
        public long Id;
        public string Title = "";
        public string File = "";          // relative to the export folder, '/' separated
        public int MediaLost;
        public List<string> Losses = [];   // Str_MdLoss_* keys
        public List<string> Assets = [];   // relative to the export folder
        public string? Error;
        public bool Lossy => MediaLost > 0 || Losses.Count > 0 || Error != null;
    }

    internal sealed class VaultExportResult
    {
        public string Root = "";
        public List<VaultNoteResult> Notes = [];
        public List<string> Problems = [];
        public int UnresolvedLinks;
        public bool Canceled;
        public int Written => Notes.Count(n => n.Error == null);
        public int Failed => Notes.Count(n => n.Error != null);
        public int Lossy => Notes.Count(n => n.Lossy);
    }

    [DataContract]
    internal sealed class VaultManifest
    {
        [DataMember(Name = "formatVersion", Order = 0)] public int FormatVersion;
        [DataMember(Name = "app", Order = 1)] public string App = "";
        [DataMember(Name = "exported", Order = 2)] public string Exported = "";
        [DataMember(Name = "notepad", Order = 3)] public string Notepad = "";
        [DataMember(Name = "notes", Order = 4)] public List<VaultManifestNote> Notes = [];
    }

    [DataContract]
    internal sealed class VaultManifestNote
    {
        [DataMember(Name = "id", Order = 0)] public long Id;
        [DataMember(Name = "file", Order = 1)] public string File = "";
        [DataMember(Name = "title", Order = 2)] public string Title = "";
        [DataMember(Name = "group", Order = 3)] public List<string> Group = [];
        [DataMember(Name = "tags", Order = 4)] public List<string> Tags = [];
        [DataMember(Name = "created", Order = 5)] public string Created = "";
        [DataMember(Name = "modified", Order = 6)] public string Modified = "";
        [DataMember(Name = "format", Order = 7)] public string Format = "";
        [DataMember(Name = "pinned", Order = 8)] public bool Pinned;
        [DataMember(Name = "color", Order = 9)] public string Color = "";
        [DataMember(Name = "assets", Order = 10)] public List<string> Assets = [];
        [DataMember(Name = "mediaLost", Order = 11)] public int MediaLost;
        [DataMember(Name = "losses", Order = 12)] public List<string> Losses = [];
    }

    internal static class VaultExporter
    {
        public const int FormatVersion = 1;
        public const string ManifestName = "killernotes-export.json";
        public const string ReportName = "export-report.md";

        /// <summary>A new folder under the one picked: "Notepad 2026-10-21 1430", then " (2)" and so
        /// on, never one that already exists.</summary>
        public static string NewExportFolder(string parent, string notepad, DateTime now)
        {
            string stem = SafeName(notepad) + " " + now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
            string path = Path.Combine(parent, stem);
            for (int n = 2; Directory.Exists(path) || File.Exists(path); n++)
                path = Path.Combine(parent, $"{stem} ({n})");
            return path;
        }

        /// <summary>Writes every note into root (created here). Progress reports the number of
        /// notes done. Canceling deletes root and returns with Canceled set.</summary>
        public static VaultExportResult Export(string root, IReadOnlyList<Note> notes, IVaultSource source,
            VaultExportText text, IProgress<int>? progress, CancellationToken token)
        {
            var result = new VaultExportResult { Root = root };
            try
            {
                Directory.CreateDirectory(root);
                var ordered = notes.OrderBy(n => n.Id).ToList();
                var files = PlanFiles(ordered, text);
                var linkFor = LinkResolver(ordered, files, result);
                var manifest = new VaultManifest
                {
                    FormatVersion = FormatVersion,
                    App = "KillerNotes " + AppInfo.Version,
                    Exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Notepad = Path.GetFileName(root),
                };

                int done = 0;
                foreach (var note in ordered)
                {
                    token.ThrowIfCancellationRequested();
                    var outcome = new VaultNoteResult { Id = note.Id, Title = note.Title, File = files[note.Id] + ".md" };
                    try { WriteNote(root, note, outcome, source, linkFor); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { outcome.Error = ex.Message; }   // one bad note must not abort the rest
                    result.Notes.Add(outcome);
                    if (outcome.Error == null) manifest.Notes.Add(ManifestEntry(note, outcome));
                    progress?.Report(++done);
                }
                token.ThrowIfCancellationRequested();

                WriteManifest(Path.Combine(root, ManifestName), manifest);
                result.Problems.AddRange(Check(root));
                File.WriteAllText(Path.Combine(root, ReportName), Report(result, text), new UTF8Encoding(false));
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Canceled = true;
                try { Directory.Delete(root, recursive: true); } catch { /* a locked file; the folder name says it is partial */ }
                return result;
            }
        }

        // ---- Paths ----

        /// <summary>Each note's file, relative to the export folder and without ".md": its group
        /// path as folders, then a name made safe for Windows, unique case-insensitively.</summary>
        private static Dictionary<long, string> PlanFiles(List<Note> notes, VaultExportText text)
        {
            var files = new Dictionary<long, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "export-report", "killernotes-export" };
            foreach (var note in notes)
            {
                var folders = note.Notebook.Split([NoteStore.GroupSep], StringSplitOptions.RemoveEmptyEntries).Select(SafeName);
                string dir = string.Join("/", folders);
                string stem = SafeName(note.Title.Length > 0 ? note.Title : text["Str_Untitled"]);
                string Rel(string name) => dir.Length == 0 ? name : dir + "/" + name;
                string rel = Rel(stem);
                for (int n = 2; !used.Add(rel); n++) rel = Rel($"{stem} ({n})");
                files[note.Id] = rel;
            }
            return files;
        }

        /// <summary>What a [[target]] should become: null to leave it as typed (unresolved, or a
        /// vault app would already find the note by that name), else the file path to link to.</summary>
        private static Func<string, string?> LinkResolver(List<Note> notes, Dictionary<long, string> files, VaultExportResult result)
        {
            var byTitle = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var note in notes)
            {
                string key = TextFold.Fold(note.Title.Trim());
                if (!byTitle.ContainsKey(key)) byTitle[key] = note.Id;
            }
            var baseNames = files.Values.Select(f => f.Substring(f.LastIndexOf('/') + 1))
                .GroupBy(b => b, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            return target =>
            {
                if (!byTitle.TryGetValue(TextFold.Fold(target.Trim()), out long id))
                {
                    result.UnresolvedLinks++;
                    return null;
                }
                string file = files[id];
                string baseName = file.Substring(file.LastIndexOf('/') + 1);
                bool findable = string.Equals(baseName, target.Trim(), StringComparison.OrdinalIgnoreCase) && baseNames[baseName] == 1;
                return findable ? null : file;
            };
        }

        // ---- One note ----

        private static void WriteNote(string root, Note note, VaultNoteResult outcome, IVaultSource source, Func<string, string?> linkFor)
        {
            string path = Path.Combine(root, outcome.File.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[]? blob = source.LoadContent(note.Id);
            string body;
            if (note.IsMarkdown)
            {
                body = MarkdownBlob.Decode(blob);
            }
            else
            {
                var doc = new FlowDocument();
                if (blob != null)
                {
                    using var ms = new MemoryStream(blob);
                    new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
                }
                // Pictures and sketches go to a sibling <name>.assets folder, recordings as their
                // audio files; what could not be written is counted, never silently dropped.
                (body, outcome.MediaLost) = MarkdownWriter.FromDocument(doc, path, source.GetAttachment);
                // Pictures are carried as files, so only what markdown has no way to say is a loss.
                outcome.Losses = MarkdownConvert.Losses(doc).Where(k => k != "Str_MdLoss_Images").ToList();
            }
            body = RewriteLinks(body, linkFor);
            // UTF-8 with no BOM. A BOM shows as stray characters at the top of the first heading
            // in a lot of markdown tooling, and nothing here needs one.
            File.WriteAllText(path, FrontMatter(note) + body, new UTF8Encoding(false));

            string assets = Path.ChangeExtension(path, null) + ".assets";
            if (Directory.Exists(assets))
                outcome.Assets = Directory.GetFiles(assets)
                    .Select(f => outcome.File.Substring(0, outcome.File.Length - 3) + ".assets/" + Path.GetFileName(f))
                    .OrderBy(f => f, StringComparer.Ordinal).ToList();
        }

        /// <summary>YAML front matter carrying the metadata markdown itself has no place for.
        /// Readers that do not understand front matter show it as a small block at the top
        /// rather than mangling the note.</summary>
        private static string FrontMatter(Note note)
        {
            var sb = new StringBuilder();
            sb.Append("---\n");
            sb.Append("title: ").Append(YamlScalar(note.Title)).Append('\n');
            sb.Append("killernotes-id: ").Append(note.Id.ToString(CultureInfo.InvariantCulture)).Append('\n');
            var tags = NoteStore.SplitTags(note.Tags).ToList();
            if (tags.Count > 0)
                sb.Append("tags: [").Append(string.Join(", ", tags.Select(YamlScalar))).Append("]\n");
            sb.Append("created: ").Append(note.Created.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("modified: ").Append(note.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            if (note.Pinned) sb.Append("pinned: true\n");
            if (note.TitleColor.Length > 0) sb.Append("color: ").Append(YamlScalar(note.TitleColor)).Append('\n');
            sb.Append("---\n\n");
            return sb.ToString();
        }

        /// <summary>Quotes a YAML scalar. Always quoted rather than only when it looks risky:
        /// a title of "no" or "1.5" is a boolean or a number to a YAML parser otherwise.</summary>
        private static string YamlScalar(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";

        // ---- Links ----

        // The same shape WikiLinks indexes: [[target]] or [[target|label]], no newlines inside.
        private static readonly Regex LinkRx = new(
            @"\[\[([^\[\]\r\n|]{1,200})(?:(\\?\|)([^\[\]\r\n]{0,200}))?\]\]", RegexOptions.Compiled);

        /// <summary>Rewrites [[target]] and [[target|label]] to [[file|label]] wherever linkFor names
        /// a file. Code fences are left exactly as written, and inside a table row the pipe is
        /// escaped so the link does not split the cell.</summary>
        public static string RewriteLinks(string markdown, Func<string, string?> linkFor)
        {
            var lines = markdown.Split('\n');
            bool fenced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    fenced = !fenced;
                    continue;
                }
                if (fenced || lines[i].IndexOf("[[", StringComparison.Ordinal) < 0) continue;
                bool tableRow = trimmed.StartsWith("|", StringComparison.Ordinal);
                lines[i] = LinkRx.Replace(lines[i], m =>
                {
                    string target = m.Groups[1].Value;
                    if (linkFor(target) is not string file) return m.Value;
                    string label = m.Groups[3].Success ? m.Groups[3].Value : target;
                    return "[[" + file + (tableRow ? "\\|" : "|") + label + "]]";
                });
            }
            return string.Join("\n", lines);
        }

        // ---- Manifest and report ----

        private static VaultManifestNote ManifestEntry(Note note, VaultNoteResult outcome) => new()
        {
            Id = note.Id,
            File = outcome.File,
            Title = note.Title,
            Group = note.Notebook.Split([NoteStore.GroupSep], StringSplitOptions.RemoveEmptyEntries).ToList(),
            Tags = NoteStore.SplitTags(note.Tags).ToList(),
            Created = note.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Modified = note.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Format = note.IsMarkdown ? "markdown" : "rich",
            Pinned = note.Pinned,
            Color = note.TitleColor,
            Assets = outcome.Assets,
            MediaLost = outcome.MediaLost,
            Losses = outcome.Losses.Select(k => k.Replace("Str_MdLoss_", "").ToLowerInvariant()).ToList(),
        };

        private static void WriteManifest(string path, VaultManifest manifest)
        {
            var serializer = new DataContractJsonSerializer(typeof(VaultManifest));
            using var file = File.Create(path);
            serializer.WriteObject(file, manifest);
        }

        public static VaultManifest ReadManifest(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(VaultManifest));
            using var file = File.OpenRead(path);
            return (VaultManifest)serializer.ReadObject(file)!;
        }

        private static string Report(VaultExportResult result, VaultExportText text)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(text["Str_Vault_ReportTitle"]).Append("\n\n");
            sb.Append(result.Lossy == 0
                ? string.Format(text["Str_Vault_NothingLost"], result.Written)
                : string.Format(text["Str_Vault_SomeLost"], result.Lossy, result.Notes.Count)).Append("\n\n");
            sb.Append(text["Str_Vault_NotIncluded"]).Append("\n\n");
            foreach (var note in result.Notes.Where(n => n.Lossy))
            {
                var parts = new List<string>();
                if (note.Error != null) parts.Add(string.Format(text["Str_Vault_NoteFailed"], note.Error));
                if (note.MediaLost > 0) parts.Add(string.Format(text["Str_Vault_LostMedia"], note.MediaLost));
                parts.AddRange(note.Losses.Select(k => text[k]));
                sb.Append("- ").Append(note.Title.Length > 0 ? note.Title : text["Str_Untitled"])
                  .Append(" (").Append(note.File).Append("): ").Append(string.Join("; ", parts)).Append('\n');
            }
            sb.Append("\n## ").Append(text["Str_Vault_CheckHead"]).Append("\n\n");
            if (result.Problems.Count == 0) sb.Append(text["Str_Vault_CheckClean"]).Append('\n');
            foreach (string problem in result.Problems) sb.Append("- ").Append(problem).Append('\n');
            if (result.UnresolvedLinks > 0)
                sb.Append('\n').Append(string.Format(text["Str_Vault_Unresolved"], result.UnresolvedLinks)).Append('\n');
            return sb.ToString();
        }

        // ---- Reading the folder back ----

        // A markdown link or image destination: <with spaces> or without.
        private static readonly Regex DestinationRx = new(@"\]\((?:<([^>\r\n]+)>|([^)\s]+))\)", RegexOptions.Compiled);

        /// <summary>Reads a written export back the way another app would: every note's front
        /// matter parses, every attached file it links to exists, every link rewritten to a path
        /// lands on a file, and every file the manifest lists is there. Returns one line per problem.</summary>
        public static List<string> Check(string root)
        {
            var problems = new List<string>();
            string Rel(string full) => full.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            foreach (string path in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories))
            {
                string rel = Rel(path);
                if (rel == ReportName) continue;
                string text = File.ReadAllText(path);
                int end = text.StartsWith("---\n", StringComparison.Ordinal) ? text.IndexOf("\n---\n", 3, StringComparison.Ordinal) : -1;
                if (end < 0 || text.IndexOf("\ntitle: ", 0, end + 1, StringComparison.Ordinal) < 0 ||
                    text.IndexOf("\nkillernotes-id: ", 0, end + 1, StringComparison.Ordinal) < 0)
                    problems.Add(rel + ": front matter");
                string dir = Path.GetDirectoryName(path)!;
                foreach (Match m in DestinationRx.Matches(text))
                {
                    string dest = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (dest.Contains("://") || dest.StartsWith("#", StringComparison.Ordinal) ||
                        dest.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                    string local;
                    try { local = Path.Combine(dir, Uri.UnescapeDataString(dest).Replace('/', Path.DirectorySeparatorChar)); }
                    catch (ArgumentException) { problems.Add(rel + ": " + dest); continue; }
                    if (!File.Exists(local)) problems.Add(rel + ": " + dest);
                }
                // A labeled link into a folder is the form RewriteLinks writes; it has to land on a
                // file. Plain [[Title]] links are left as typed, including ones to notes that do
                // not exist, which the report counts instead.
                foreach (Match m in LinkRx.Matches(text))
                {
                    string target = m.Groups[1].Value;
                    if (!m.Groups[2].Success || target.IndexOf('/') < 0) continue;
                    if (!File.Exists(Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar) + ".md")))
                        problems.Add(rel + ": [[" + target + "]]");
                }
            }
            string manifest = Path.Combine(root, ManifestName);
            if (File.Exists(manifest))
            {
                foreach (var note in ReadManifest(manifest).Notes)
                    foreach (string file in note.Assets.Prepend(note.File))
                        if (!File.Exists(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))))
                            problems.Add(ManifestName + ": " + file);
            }
            return problems.Distinct().ToList();
        }

        // ---- Names ----

        /// <summary>A title reduced to something Windows will accept as a file or folder name.
        /// Trailing dots and spaces are stripped too - Windows silently drops them, which turns
        /// two distinct titles into one colliding path.</summary>
        internal static string SafeName(string s)
        {
            var sb = new StringBuilder(s.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (char c in s)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) ? '-' : c);
            string name = sb.ToString().Trim().TrimEnd('.', ' ');
            if (name.Length == 0) name = "note";
            // Reserved device names are rejected whatever the extension.
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (ReservedNames.Contains(stem)) name = "_" + name;
            // Leave headroom under MAX_PATH for the directory, the extension and a dedupe suffix.
            return name.Length > 80 ? name[..80].TrimEnd('.', ' ') : name;
        }

        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };
    }
}
