// ═══════════════════════════════════════════════════════════
//  COMMAND SEARCH  -  find and run an action from the F1 list
// ═══════════════════════════════════════════════════════════
//
// 1.6.0, KN-P09. The F1 list already names every action with its shortcut, so it doubles as the
// command search instead of a new palette on yet another shortcut: typing filters it by name or
// keys under the search rule (TextFold), and Enter runs the chosen row by replaying its shortcut
// (Shell/Shortcuts.cs). Replaying keeps one code path per action: the row runs exactly what
// pressing its keys runs, with the same guards.
//
// TipState is the other half of the onboarding: one-sentence tips in the status line the first
// time a feature is used, each once, all off from the About card.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace KillerNotes.Services
{
    internal static class CommandSearch
    {
        /// <summary>A row matches when every word of the filter is in its name or its keys.</summary>
        public static bool Matches(string label, string keys, string filter)
        {
            var words = filter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => TextFold.FindAll(label, w).Count > 0 || TextFold.FindAll(keys, w).Count > 0);
        }

        // Keys that only mean something where the caret is; a row made of one of these is a
        // description, not an action to replay.
        private static readonly HashSet<Key> ContextKeys =
        [
            Key.Home, Key.End, Key.Left, Key.Right, Key.Up, Key.Down, Key.PageUp, Key.PageDown,
            Key.Tab, Key.Enter, Key.Space, Key.Back, Key.Escape,
        ];

        /// <summary>The first gesture in a list row's keys ("F7 / Ctrl+Shift+D" gives F7), or false
        /// when it is not one replayable gesture.</summary>
        public static bool TryParseGesture(string keys, out ModifierKeys modifiers, out Key key)
        {
            modifiers = ModifierKeys.None;
            key = Key.None;
            string first = keys.Split([" / "], StringSplitOptions.None)[0].Trim();
            if (first.Length == 0) return false;
            var parts = first.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": modifiers |= ModifierKeys.Control; break;
                    case "shift": modifiers |= ModifierKeys.Shift; break;
                    case "alt": modifiers |= ModifierKeys.Alt; break;
                    case "win": modifiers |= ModifierKeys.Windows; break;
                    default: return false;
                }
            }
            string name = parts[parts.Length - 1].Trim();
            if (name.Length == 0) return false;
            if (name.Length == 1 && char.IsDigit(name[0])) key = Key.D0 + (name[0] - '0');
            else if (name.Length == 1 && char.IsLetter(name[0])) key = Key.A + (char.ToUpperInvariant(name[0]) - 'A');
            else
            {
                string normalized = name switch { "Del" => "Delete", "Esc" => "Escape", "Bksp" => "Back", _ => name };
                if (!Enum.TryParse(normalized, ignoreCase: true, out key) || key == Key.None) return false;
            }
            return !(modifiers == ModifierKeys.None && ContextKeys.Contains(key));
        }
    }

    /// <summary>Which one-time tips have been shown, and whether tips are on.</summary>
    internal sealed class TipState(bool showTips, string seen)
    {
        private readonly HashSet<string> _seen = new(seen.Split([','], StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        public string Seen => string.Join(",", _seen.OrderBy(s => s, StringComparer.Ordinal));

        /// <summary>True the first time a tip is asked for while tips are on; it is then seen.</summary>
        public bool TakeTip(string key) => showTips && _seen.Add(key);
    }
}
