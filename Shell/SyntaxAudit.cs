// ═══════════════════════════════════════════════════════════
//  SYNTAX AUDIT  -  proving where the highlighter actually painted
// ═══════════════════════════════════════════════════════════
//
// Colors landing part way into words has been reported, diagnosed and "fixed" more than once, and
// each fix was a reading of the code rather than a captured failure. The last one - counting a
// LineBreak as one character on both sides of the walk - was real and did not end the reports, so
// something else is still moving offsets and nobody has the case that does it.
//
// This is the missing evidence. With --syntaxcheck the highlighter re-reads every paragraph it has
// just painted, works out which characters actually carry a syntax color, and compares that against
// the tokens the tokenizer asked for. A mismatch is written to syntax-audit.log with both strings
// side by side, so the failing input is a file rather than a description.
//
// WHY IT RE-READS RATHER THAN TRUSTING THE POINTERS. The suspicion IS the pointers: tokens are
// resolved to TextPointers up front and then painted into a document that ApplyPropertyValue is
// splitting underneath them. An audit built on those same pointers would agree with the bug. This
// walks the finished document from scratch instead, the way a reader sees it.
//
// It is OFF unless asked for, and slow when on - a second full walk of every painted paragraph.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        /// <summary>Set from --syntaxcheck on the command line (App.xaml.cs).</summary>
        internal static bool SyntaxAudit;

        private static readonly object _auditGate = new();
        private static int _auditMismatches;

        private static string AuditLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KillerNotes", "syntax-audit.log");

        /// <summary>Compares what got painted against what was asked for. Called at the tail of
        /// HighlightParagraph, after the apply loop, and only when the audit is on.</summary>
        private void AuditParagraph(Paragraph paragraph, string asked,
                                    List<(int Start, int Length, Color Color)> tokens,
                                    CodeLanguage language)
        {
            try
            {
                // The text as it stands NOW. If this differs from what the tokenizer matched, the
                // document changed under the paint and every offset in it is meaningless - which is
                // itself the finding, so it is reported rather than silently skipped.
                string now = ParagraphCodeText(paragraph);
                if (!string.Equals(now, asked, StringComparison.Ordinal))
                {
                    AuditReport("TEXT CHANGED UNDER THE PAINT", language,
                           $"tokenized: {Quote(asked)}",
                           $"on screen: {Quote(now)}");
                    return;
                }

                var painted = PaintedColors(paragraph, now.Length);

                // What the tokenizer intended, flattened the same way: last writer wins, matching
                // the apply loop's back-to-front order.
                var wanted = new Color?[now.Length];
                foreach (var t in tokens.OrderByDescending(t => t.Start))
                    for (int i = t.Start; i < t.Start + t.Length && i < wanted.Length; i++)
                        if (i >= 0) wanted[i] = t.Color;

                for (int i = 0; i < now.Length; i++)
                {
                    if (SameColor(wanted[i], painted[i])) continue;
                    // First divergence only. One slid token usually misprints everything after it,
                    // and a log with one clear entry per paragraph is readable where hundreds is not.
                    var tok = tokens.Where(t => t.Start <= i && i < t.Start + t.Length)
                                    .Select(t => (int?)t.Start).FirstOrDefault();
                    string expected = tok is int ts
                        ? Quote(asked.Substring(ts, Math.Min(tokens.First(t => t.Start == ts).Length,
                                                            asked.Length - ts)))
                        : "(no token here)";
                    AuditReport("TOKEN LANDED ON THE WRONG CHARACTERS", language,
                           $"first wrong offset: {i}",
                           $"character there:    {Quote(now[i].ToString())}",
                           $"token wanted:       {expected}",
                           $"wanted color:       {ColorName(wanted[i])}",
                           $"painted color:      {ColorName(painted[i])}",
                           $"paragraph:          {Quote(now)}",
                           $"painted spans:      {SpanList(now, painted)}");
                    return;
                }
            }
            catch (Exception ex)
            {
                AuditReport("AUDIT ITSELF FAILED", language, ex.ToString());
            }
        }

        /// <summary>The foreground actually in effect for every character, read back off the
        /// finished document by the same walk ParagraphCodeText uses, so the indexes line up.
        /// A null entry means the character carries no syntax color.</summary>
        private static Color?[] PaintedColors(Paragraph paragraph, int length)
        {
            var colors = new Color?[length];
            TextPointer end = paragraph.ContentEnd;
            TextPointer? p = paragraph.ContentStart;
            int seen = 0;
            while (p != null && p.CompareTo(end) < 0 && seen < length)
            {
                var ctx = p.GetPointerContext(LogicalDirection.Forward);
                if (ctx == TextPointerContext.Text)
                {
                    string text = p.GetTextInRun(LogicalDirection.Forward);
                    // The Run this text belongs to carries the applied Foreground. Reading the
                    // parent is what makes this independent of how the runs were split.
                    Color? c = null;
                    if (p.Parent is TextElement te &&
                        te.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush b)
                        c = b.Color;
                    for (int i = 0; i < text.Length && seen + i < length; i++) colors[seen + i] = c;
                    seen += text.Length;
                    p = p.GetPositionAtOffset(text.Length, LogicalDirection.Forward);
                    continue;
                }
                if (ctx == TextPointerContext.ElementStart
                    && p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                    seen++;   // one character, exactly as the other two walks count it
                p = p.GetNextContextPosition(LogicalDirection.Forward);
            }
            return colors;
        }

        /// <summary>A syntax color only counts as a match when it is one of the palette's. Any
        /// other brush is the user's own formatting and is not the highlighter's business.</summary>
        private static bool SameColor(Color? wanted, Color? painted)
        {
            if (wanted is null) return painted is null || !SyntaxPalette().Contains(painted.Value);
            return painted is Color p && p == wanted.Value;
        }

        private static string ColorName(Color? c) => c is Color v ? v.ToString() : "(none)";

        /// <summary>Control characters spelled out, so a log entry cannot hide the difference
        /// between a space, a tab and a line break - which is the whole class of bug here.</summary>
        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s)
                sb.Append(ch switch
                {
                    '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '"' => "\\\"",
                    _ => ch < ' ' ? "\\u" + ((int)ch).ToString("X4", CultureInfo.InvariantCulture)
                                  : ch.ToString(),
                });
            return sb.Append('"').ToString();
        }

        /// <summary>The painted colors as runs, so the log shows where each color starts and stops
        /// rather than a wall of per-character values.</summary>
        private static string SpanList(string text, Color?[] colors)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < colors.Length)
            {
                int j = i;
                while (j < colors.Length && SameNullable(colors[j], colors[i])) j++;
                if (colors[i] is not null)
                    sb.Append(Quote(text[i..j])).Append('=').Append(ColorName(colors[i])).Append("  ");
                i = j;
            }
            return sb.Length == 0 ? "(nothing painted)" : sb.ToString().TrimEnd();
        }

        private static bool SameNullable(Color? a, Color? b) =>
            a is null ? b is null : b is Color v && v == a.Value;

        private void AuditReport(string headline, CodeLanguage language, params string[] lines)
        {
            lock (_auditGate)
            {
                _auditMismatches++;
                try
                {
                    var sb = new StringBuilder();
                    sb.Append("=== ")
                      .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                      .Append("  ").Append(headline).Append(" ===\n");
                    sb.Append("language: ").Append(language).Append('\n');
                    foreach (string l in lines) sb.Append(l).Append('\n');
                    sb.Append('\n');
                    string path = AuditLogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
                catch { /* an audit that cannot write is not worth taking the app down for */ }
            }
            // Said out loud, once. An audit nobody notices produced a log nobody reads.
            if (_auditMismatches == 1)
                FlashStatus("Syntax audit: mismatch found, see syntax-audit.log");
        }
    }
}
