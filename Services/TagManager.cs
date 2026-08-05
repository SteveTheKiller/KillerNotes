using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using KillerNotes.Models;

namespace KillerNotes.Services
{
    /// <summary>
    /// The per-database tag definitions (name + color) and the sidebar chips built from them.
    ///
    /// Definitions live in the database's tags table, so they travel inside shared .kndb/.knote
    /// files; assignment is the notes.tags CSV, which the FTS triggers already index, so tag
    /// search and the chip-click filter are instant.
    ///
    /// Everything here is UI-free: it reads NoteStore and writes model objects, so a caller can
    /// batch a 50-note toggle and repaint the list once rather than fifty times.
    /// </summary>
    internal static class TagManager
    {
        private static Dictionary<string, string> _defs = new(StringComparer.OrdinalIgnoreCase);
        private static List<(string Name, string Color)> _order = [];

        /// <summary>Defined tags in display order. The Ctrl+1..9 shortcuts index into this.</summary>
        internal static IReadOnlyList<(string Name, string Color)> Order => _order;

        /// <summary>Reloads tag definitions from the open database. Cheap (a handful of rows),
        /// called from RefreshList so database switches stay in sync for free.</summary>
        internal static void Refresh()
        {
            _order = NoteStore.ListTags();
            _defs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _order) _defs[t.Name] = t.Color;
        }

        /// <summary>Rebuilds every note's chip list from its CSV + the definitions.</summary>
        internal static void ApplyChips(IEnumerable<Note> notes)
        {
            foreach (var n in notes) BuildChips(n);
        }

        internal static void BuildChips(Note n)
        {
            n.Chips.Clear();
            foreach (string tag in NoteStore.SplitTags(n.Tags))
            {
                // A tag whose definition was deleted still shows, in neutral gray.
                string hex = _defs.TryGetValue(tag, out string? c) ? c! : "#9A9A9A";
                Color color;
                try { color = (Color)ColorConverter.ConvertFromString(hex); }
                catch { color = Color.FromRgb(0x9A, 0x9A, 0x9A); }
                n.Chips.Add(new TagChip
                {
                    Name = tag,
                    Background = new SolidColorBrush(color),
                    Foreground = Luminance(color) > 0.55 ? Brushes.Black : Brushes.White,
                });
            }
        }

        /// <summary>Perceived brightness, used to pick readable chip text.</summary>
        internal static double Luminance(Color c) =>
            (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        internal static bool HasTag(Note note, string tag) =>
            NoteStore.SplitTags(note.Tags).Contains(tag, StringComparer.OrdinalIgnoreCase);

        /// <summary>Persists one note's tag state without touching the UI (callers batch the
        /// list refresh so a 50-note toggle repaints once, not 50 times).</summary>
        internal static void SetAssigned(Note note, string tag, bool assigned)
        {
            var parts = NoteStore.SplitTags(note.Tags).ToList();
            int idx = parts.FindIndex(p => string.Equals(p, tag, StringComparison.OrdinalIgnoreCase));
            if (assigned) { if (idx < 0) parts.Add(tag); else return; }
            else { if (idx >= 0) parts.RemoveAt(idx); else return; }

            note.Tags = string.Join(", ", parts);
            NoteStore.SetNoteTags(note.Id, note.Tags);
            BuildChips(note);
        }
    }
}
