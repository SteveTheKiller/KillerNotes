// ═══════════════════════════════════════════════════════════
//  QUICK CAPTURE  -  a thought into the notepad without opening it
// ═══════════════════════════════════════════════════════════
//
// The store side of the quick-note window (1.6.0, KN-P04; Shell/QuickNote.cs). A quick note is
// a new Markdown note in a group (Inbox by default), titled by its first line, or text appended
// to an existing note such as today's daily note. Both write straight to the database and never
// go through the editor, so whatever note is open stays exactly as it was - the shell routes an
// append to the OPEN note through the editor instead, so the two never race.
//
// The routes in: the tray icon's menu, the taskbar jump list and --quick-note (a second launch
// forwards PipeMessage to the running copy), and an optional global shortcut (QuickNoteHotkey).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    internal static class QuickCapture
    {
        public const int MaxTitle = 120;
        public const int MaxBytes = 1024 * 1024;

        /// <summary>What a second launch sends the running copy for --quick-note. Not a path any
        /// file could have, so it can never be mistaken for a double-clicked file.</summary>
        public const string PipeMessage = "kn:quick-note";

        public static bool IsQuickNoteLaunch(IEnumerable<string> args) =>
            args.Any(a => string.Equals(a, "--quick-note", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "/quick-note", StringComparison.OrdinalIgnoreCase));

        public static bool TooLarge(string text) => Encoding.UTF8.GetByteCount(text) > MaxBytes;

        /// <summary>The first non-blank line, trimmed and cut at a word to MaxTitle.</summary>
        public static string TitleOf(string text, string untitled)
        {
            string line = Normalize(text).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            if (line.Length == 0) return untitled;
            if (line.Length <= MaxTitle) return line;
            int cut = line.LastIndexOf(' ', MaxTitle);
            return line.Substring(0, cut > MaxTitle / 2 ? cut : MaxTitle).TrimEnd();
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary>A new Markdown note holding the whole text, in the group (created if missing,
        /// with its parents). Returns its id.</summary>
        public static long CreateNote(string group, string text, string untitled)
        {
            text = Normalize(text);
            string title = TitleOf(text, untitled);
            if (group.Length > 0)
            {
                string[] parts = group.Split([NoteStore.GroupSep], StringSplitOptions.None);
                for (int depth = 1; depth <= parts.Length; depth++)
                    NoteStore.AddGroup(string.Join(NoteStore.GroupSep, parts.Take(depth)),
                        string.Join(NoteStore.GroupSep, parts.Take(depth - 1)), atTop: false);
            }
            long id = NoteStore.Create(title, 1);
            if (id < 0) throw new InvalidOperationException("The database is not writable.");
            NoteStore.SaveEditorState(id, title, MarkdownBlob.Encode(text), text, WikiLinks.Parse(text), []);
            if (group.Length > 0) NoteStore.SetNoteGroup(id, group);
            return id;
        }

        /// <summary>Adds the text to the end of a note that is NOT open in the editor: after a blank
        /// line in a Markdown note, as new paragraphs in a rich one. Needs an STA thread for rich
        /// notes. Pictures, sketches and recordings already in the note are kept.</summary>
        public static void Append(long id, string text)
        {
            var note = NoteStore.GetNote(id) ?? throw new InvalidOperationException("The note no longer exists.");
            text = Normalize(text).Trim('\n');
            if (note.IsMarkdown)
            {
                string old = MarkdownBlob.Decode(NoteStore.LoadContent(id)).Replace("\r\n", "\n").TrimEnd('\n');
                string merged = old.Length == 0 ? text : old + "\n\n" + text;
                NoteStore.SaveEditorState(id, note.Title, MarkdownBlob.Encode(merged), merged, WikiLinks.Parse(merged), []);
                return;
            }
            var doc = new FlowDocument();
            if (NoteStore.LoadContent(id) is byte[] blob)
            {
                using var input = new MemoryStream(blob);
                new TextRange(doc.ContentStart, doc.ContentEnd).Load(input, DataFormats.XamlPackage);
            }
            foreach (string line in text.Split('\n'))
                doc.Blocks.Add(new Paragraph(new Run(line)));
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var output = new MemoryStream();
            range.Save(output, DataFormats.XamlPackage);
            // The stored plain text plus the new lines: it keeps the sketch labels search reads.
            string plain = NoteStore.LoadPlain(id).TrimEnd() + "\n" + text;
            NoteStore.SaveEditorState(id, note.Title, output.ToArray(), plain, WikiLinks.Parse(plain), []);
        }
    }

    /// <summary>Explorer can hand the same file over twice in a moment (a double double-click, two
    /// selected files opened together, a retry). The second request for the same unchanged file
    /// within a minute is the same request.</summary>
    internal sealed class RecentOpens
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
        private readonly Dictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True the first time a path and content hash arrive inside the window.</summary>
        public bool First(string path, string hash, DateTime now)
        {
            foreach (var stale in _seen.Where(p => now - p.Value > Window).Select(p => p.Key).ToList()) _seen.Remove(stale);
            string key = path + "|" + hash;
            if (_seen.ContainsKey(key)) return false;
            _seen[key] = now;
            return true;
        }
    }

    /// <summary>The global quick-note shortcut choices. Off by default; only Win+Alt combinations
    /// are offered, because Ctrl+Alt is AltGr on international keyboards and would steal a
    /// character from typing everywhere.</summary>
    internal static class QuickNoteHotkey
    {
        public const string Setting = "QuickNoteHotkey";
        public static readonly string[] Choices = ["Win+Alt+N", "Win+Alt+J"];

        private const uint ModAlt = 0x0001, ModWin = 0x0008, ModNoRepeat = 0x4000;

        public static bool TryParse(string? stored, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;
            if (stored == null || Array.IndexOf(Choices, stored) < 0) return false;
            modifiers = ModWin | ModAlt | ModNoRepeat;
            key = stored[stored.Length - 1];   // 'N' and 'J' are their own virtual-key codes
            return true;
        }
    }
}
