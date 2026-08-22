// ═══════════════════════════════════════════════════════════
//  VAULT EXPORT  -  the whole database as a folder of .md files
// ═══════════════════════════════════════════════════════════
//
// The anti-lock-in half of the markdown work (1.3.0). One-way and on demand: every note is
// written out as a .md file, groups become nested subfolders, tags and timestamps ride in YAML
// front matter. What comes out is a plain folder that git, Obsidian, or any text editor reads.
//
// Deliberately NOT a live two-way sync of a watched folder. Sync means conflict resolution,
// file-watcher races, and partial writes from other editors, and it would put a plaintext copy
// of an encrypted database on disk permanently, which contradicts the reason the database is
// encrypted at all. Export is explicit, so the user chooses the moment the plaintext exists.
//
// Rich-text notes are converted on the way out (MarkdownConvert), so the folder is complete
// rather than only the notes that already happened to be markdown. That conversion is lossy in
// the ways Losses() names, but nothing in the database is modified: this writes files and
// touches no note.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private void ExportVault_Click(object sender, RoutedEventArgs e)
        {
            if (!NoteStore.IsOpen) return;

            // Export what is on screen, not a stale copy - same rule as ExportNoteAs.
            SaveCurrentNote(refreshList: false);

            var notes = NoteStore.List();
            if (notes.Count == 0)
            {
                StatusText.Text = Loc("Str_St_VaultEmpty");
                return;
            }

            string? folder = FolderPicker.Show(this, null, Loc("Str_Dlg_VaultPickFolder"));
            if (string.IsNullOrWhiteSpace(folder)) return;

            // Everything lands in a named subfolder rather than loose in whatever the user
            // picked. Choosing Documents and having sixty .md files appear in it is not a
            // recoverable mistake once they are mixed in with what was already there.
            string root = Path.Combine(folder!, SafeName(VaultFolderName()));
            var confirm = new ConfirmDialog(
                Loc("Str_Dlg_VaultHead"),
                string.Format(Loc("Str_Dlg_VaultBody"), notes.Count, root),
                Loc("Str_Btn_Export")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            int written = 0, failed = 0;
            try
            {
                Directory.CreateDirectory(root);
                // One set of used paths for the whole run: two notes can share a title, and two
                // different titles can sanitize down to the same filename.
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var note in notes)
                {
                    try
                    {
                        WriteVaultFile(root, note, used);
                        written++;
                    }
                    catch (Exception) { failed++; }   // one bad note must not abort the rest
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Loc("Str_St_VaultFailed"), ex.Message);
                return;
            }

            StatusText.Text = failed == 0
                ? string.Format(Loc("Str_St_VaultDone"), written, root)
                : string.Format(Loc("Str_St_VaultDonePartial"), written, failed, root);
        }

        /// <summary>Folder name for the export: the open database without its extension, so
        /// exporting two different .kndb files side by side does not merge them.</summary>
        private static string VaultFolderName()
        {
            string name = Path.GetFileNameWithoutExtension(NoteStore.ActiveDbFile);
            return string.IsNullOrWhiteSpace(name) ? "KillerNotes" : name;
        }

        private void WriteVaultFile(string root, Note note, HashSet<string> used)
        {
            // Group path to nested folders. The separator is a non-printing unit separator, so
            // it is split on the constant and never on a typed character.
            string dir = root;
            if (note.Notebook.Length > 0)
            {
                foreach (string part in note.Notebook.Split(
                             [NoteStore.GroupSep], StringSplitOptions.RemoveEmptyEntries))
                    dir = Path.Combine(dir, SafeName(part));
                Directory.CreateDirectory(dir);
            }

            string baseName = SafeName(note.Title.Length > 0 ? note.Title : Loc("Str_Untitled"));
            string path = Path.Combine(dir, baseName + ".md");
            for (int n = 2; used.Contains(path) || File.Exists(path); n++)
                path = Path.Combine(dir, $"{baseName} ({n}).md");
            used.Add(path);

            byte[]? blob = NoteStore.LoadContent(note.Id);
            string body;
            if (note.IsMarkdown)
            {
                body = blob == null || blob.Length == 0 ? "" : new UTF8Encoding(false).GetString(blob);
            }
            else
            {
                var doc = new FlowDocument();
                if (blob != null)
                {
                    using var ms = new MemoryStream(blob);
                    new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
                }
                body = MarkdownConvert.FromDocument(doc);
            }

            // UTF-8 with no BOM. A BOM shows as stray characters at the top of the first heading
            // in a lot of markdown tooling, and nothing here needs one.
            File.WriteAllText(path, FrontMatter(note) + body, new UTF8Encoding(false));
        }

        /// <summary>YAML front matter carrying the metadata markdown itself has no place for.
        /// Readers that do not understand front matter show it as a small block at the top
        /// rather than mangling the note.</summary>
        private static string FrontMatter(Note note)
        {
            var sb = new StringBuilder();
            sb.Append("---\n");
            sb.Append("title: ").Append(YamlScalar(note.Title)).Append('\n');
            var tags = NoteStore.SplitTags(note.Tags).ToList();
            if (tags.Count > 0)
                sb.Append("tags: [").Append(string.Join(", ", tags.Select(YamlScalar))).Append("]\n");
            sb.Append("created: ").Append(note.Created.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("modified: ").Append(note.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("---\n\n");
            return sb.ToString();
        }

        /// <summary>Quotes a YAML scalar. Always quoted rather than only when it looks risky:
        /// a title of "no" or "1.5" is a boolean or a number to a YAML parser otherwise.</summary>
        private static string YamlScalar(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";

        /// <summary>A note title reduced to something Windows will accept as a file or folder
        /// name. Trailing dots and spaces are stripped too - Windows silently drops them, which
        /// turns two distinct titles into one colliding path.</summary>
        private static string SafeName(string s)
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
