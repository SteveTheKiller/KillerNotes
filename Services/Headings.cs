// ═══════════════════════════════════════════════════════════
//  HEADINGS  -  what makes a paragraph a heading, in both note types
// ═══════════════════════════════════════════════════════════
//
// A rich-text heading is a bold paragraph at one of three sizes scaled from the editor's base
// size, the same shape MarkdownConvert has produced for "# Title" since 1.3.0, so an imported
// heading and a heading made with Alt+1 are the same thing. That convention IS the marker:
// nothing else survives the XamlPackage round trip (Paragraph.Tag and Name are not written),
// and a marker the storage forgets is no marker at all. The sizes are specific enough that a
// paragraph somebody made large and bold by hand only counts as a heading when it lands on
// one of them, which is also when it looks like one.
//
// A markdown heading is a line opening with one to six # and a space, as everywhere else.
//
// Shared by the editor (Shell/Headings.cs), the outline pane, and the markdown writer, so the
// three can never disagree about which lines are headings.

using System;
using System.Windows;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    internal static class Headings
    {
        /// <summary>The editor's base font size (MainWindow.xaml Editor FontSize).</summary>
        public const double DefaultBase = 13;

        /// <summary>Heading sizes as multiples of the base, h1 to h6. Past h3 the steps are
        /// small on purpose so a deep outline does not become body text that happens to be bold.</summary>
        public static readonly double[] Scale = [1.85, 1.55, 1.32, 1.16, 1.06, 1.0];

        /// <summary>Levels the editor makes and the outline lists. h4 to h6 are close enough to
        /// body text that recognizing them from size alone would catch ordinary bold lines.</summary>
        public const int MaxLevel = 3;

        private const double Tolerance = 0.6;

        public static double SizeFor(int level, double base_) =>
            base_ * Scale[Math.Max(1, Math.Min(Scale.Length, level)) - 1];

        /// <summary>The heading level a uniform font size and weight amount to, or 0.</summary>
        public static int LevelOf(double fontSize, bool bold, double base_)
        {
            if (!bold) return 0;
            for (int level = 1; level <= MaxLevel; level++)
                if (Math.Abs(fontSize - SizeFor(level, base_)) <= Tolerance) return level;
            return 0;
        }

        /// <summary>The heading level of a rich-text paragraph: its whole text must share one
        /// size and be bold throughout. An empty paragraph is never a heading.</summary>
        public static int LevelOf(Paragraph p, double base_)
        {
            var range = new TextRange(p.ContentStart, p.ContentEnd);
            if (range.Text.Trim().Length == 0) return 0;
            if (range.GetPropertyValue(TextElement.FontSizeProperty) is not double size) return 0;
            bool bold = range.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w == FontWeights.Bold;
            return LevelOf(size, bold, base_);
        }

        // ---- Markdown ----

        /// <summary>1 to 6 for a "# " line, else 0.</summary>
        public static int LevelOfMarkdown(string line)
        {
            int n = 0;
            while (n < line.Length && n < 6 && line[n] == '#') n++;
            return n > 0 && n < line.Length && line[n] == ' ' ? n : 0;
        }

        /// <summary>The line with its heading marker replaced (level 0 removes it).</summary>
        public static string SetMarkdownLevel(string line, int level)
        {
            int current = LevelOfMarkdown(line);
            string body = current > 0 ? line.Substring(current + 1) : line;
            return level <= 0 ? body : new string('#', Math.Min(6, level)) + " " + body;
        }
    }
}
