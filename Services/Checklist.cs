// ═══════════════════════════════════════════════════════════
//  CHECKLIST  -  the rules for a checkbox line, with no editor in sight
// ═══════════════════════════════════════════════════════════
//
// A checkbox is text, not a control. A rich-text line opens with a BALLOT BOX glyph (☐ or ☑)
// and a markdown line with the task-list marker ("- [ ] " or "- [x] "), and both are exactly
// what gets stored, searched, exported and printed. That is the same rule wikilinks follow: a
// control inside an InlineUIContainer would not survive the XamlPackage round trip, and a
// paragraph marker style has nothing to click.
//
// This file is the shared vocabulary. The editor behavior (toggle, click, Enter) is in
// Shell/Checklist.cs and the markdown mapping is in MarkdownConvert, and both ask here so
// the two notations can never drift apart.

using System;

namespace KillerNotes.Services
{
    internal static class Checklist
    {
        public const char Empty = '☐';     // BALLOT BOX
        public const char Checked = '☑';   // BALLOT BOX WITH CHECK

        public const string MdEmpty = "- [ ] ";
        public const string MdChecked = "- [x] ";
        private const string MdCheckedUpper = "- [X] ";   // Markdig accepts either case

        public enum State { None, Unchecked, Checked }

        public static bool IsBox(char c) => c == Empty || c == Checked;

        /// <summary>The checkbox state a line carries, in the notation of its note type.</summary>
        public static State Of(string line, bool markdown)
        {
            if (markdown)
            {
                if (line.StartsWith(MdEmpty, StringComparison.Ordinal)) return State.Unchecked;
                if (line.StartsWith(MdChecked, StringComparison.Ordinal) ||
                    line.StartsWith(MdCheckedUpper, StringComparison.Ordinal)) return State.Checked;
                return State.None;
            }
            if (line.Length == 0) return State.None;
            return line[0] == Empty ? State.Unchecked
                 : line[0] == Checked ? State.Checked
                 : State.None;
        }

        /// <summary>What a fresh checkbox line starts with.</summary>
        public static string Prefix(bool markdown, bool isChecked) => markdown
            ? (isChecked ? MdChecked : MdEmpty)
            : (isChecked ? Checked : Empty) + " ";

        /// <summary>How many characters at the head of the line belong to the checkbox: the
        /// whole marker in markdown; the glyph plus the space after it, when there is one, in
        /// rich text. Zero for a line without one.</summary>
        public static int PrefixLength(string line, bool markdown)
        {
            if (Of(line, markdown) == State.None) return 0;
            if (markdown) return MdEmpty.Length;
            return line.Length > 1 && line[1] == ' ' ? 2 : 1;
        }

        /// <summary>The [From, To) character span of the box itself, the part a click flips.</summary>
        public static (int From, int To) BoxSpan(bool markdown) => markdown ? (2, 5) : (0, 1);

        /// <summary>A markdown line with its box flipped. Unchanged when it has none.</summary>
        public static string ToggleMarkdown(string line) => Of(line, true) switch
        {
            State.Unchecked => MdChecked + line.Substring(MdEmpty.Length),
            State.Checked   => MdEmpty + line.Substring(MdChecked.Length),
            _ => line,
        };

        /// <summary>A rich-text checkbox line in task-list syntax, for the markdown writer.
        /// Unchanged when the line has no box.</summary>
        public static string ToMarkdownLine(string richLine)
        {
            var state = Of(richLine, false);
            if (state == State.None) return richLine;
            return (state == State.Checked ? MdChecked : MdEmpty) + richLine.Substring(1).TrimStart();
        }
    }
}
