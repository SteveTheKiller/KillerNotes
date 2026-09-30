using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerNotes.Services
{
    /// <summary>
    /// FlowDocument to standalone HTML, for Export note as... (F8).
    ///
    /// Deliberately small: paragraphs (the FontSize-2 bottom-border rule becomes an hr),
    /// lists, tables, bold/italic/underline/strike, monospace, text color/highlight, and inline
    /// images (base64 PNG). Enough for notes, not a Word clone. The caller passes the live theme
    /// colors so the export looks like the app (and the theme text color the XamlPackage bakes
    /// into runs stays readable).
    /// </summary>
    internal static class HtmlExport
    {
        /// <summary>Renders the document as a complete HTML page styled with the supplied theme
        /// colors.</summary>
        internal static string FromDocument(FlowDocument doc, string title,
                                            string bg, string fg, string accent, string border)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>")
              .Append(Html(title)).Append("</title><style>")
              .Append($"body{{background:{bg};color:{fg};font-family:'Segoe UI',sans-serif;")
              .Append("font-size:13px;max-width:900px;margin:24px auto;padding:0 16px}")
              .Append($"a{{color:{accent}}}")
              .Append($"table{{border-collapse:collapse}}td,th{{border:1px solid {border};padding:3px 8px}}")
              .Append("code{font-family:Consolas,monospace}img{max-width:100%}")
              .Append($"hr{{border:none;border-top:1px solid {border}}}")
              .Append("p{margin:0 0 2px 0}")
              .Append("</style></head><body>");
            foreach (var block in doc.Blocks) AppendBlock(sb, block, true);
            sb.Append("</body></html>");
            return sb.ToString();
        }

        /// <summary>Renders the document as an HTML fragment for the clipboard, so a copy pasted
        /// into a browser, mail client or Office keeps real links, lists, tables and emphasis.
        /// Run colors are left out: the editor bakes theme text colors into runs, and light
        /// theme text pasted onto a white page would be unreadable.</summary>
        internal static string FragmentFromDocument(FlowDocument doc)
        {
            var sb = new StringBuilder();
            foreach (var block in doc.Blocks) AppendBlock(sb, block, false);
            return sb.ToString();
        }

        /// <summary>Wraps an HTML fragment in the Windows CF_HTML clipboard envelope. The header
        /// offsets are UTF-8 byte positions, which is how WPF writes the Html format.</summary>
        internal static string ClipboardEnvelope(string fragment)
        {
            const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string prefix = "<html><head><meta charset=\"utf-8\"/></head><body><!--StartFragment-->";
            const string suffix = "<!--EndFragment--></body></html>";
            int headerLength = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
            int startFragment = headerLength + Encoding.UTF8.GetByteCount(prefix);
            int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
            int endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
            return string.Format(header, headerLength, endHtml, startFragment, endFragment) + prefix + fragment + suffix;
        }

        private static void AppendBlock(StringBuilder sb, Block block, bool colors)
        {
            switch (block)
            {
                case Paragraph p when p.FontSize <= 2 && p.BorderThickness.Bottom > 0:
                    sb.Append("<hr/>");
                    break;
                case Paragraph p:
                    sb.Append("<p>");
                    AppendInlines(sb, p.Inlines, colors);
                    sb.Append("</p>");
                    break;
                case List list:
                    string tag = list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul";
                    sb.Append('<').Append(tag).Append('>');
                    foreach (var li in list.ListItems)
                    {
                        sb.Append("<li>");
                        foreach (var b in li.Blocks)
                        {
                            if (b is Paragraph pp) AppendInlines(sb, pp.Inlines, colors);
                            else AppendBlock(sb, b, colors);
                        }
                        sb.Append("</li>");
                    }
                    sb.Append("</").Append(tag).Append('>');
                    break;
                case Table t:
                    sb.Append("<table>");
                    foreach (var g in t.RowGroups)
                    {
                        foreach (var row in g.Rows)
                        {
                            sb.Append("<tr>");
                            foreach (var cell in row.Cells)
                            {
                                sb.Append("<td>");
                                foreach (var b in cell.Blocks)
                                {
                                    if (b is Paragraph pp) AppendInlines(sb, pp.Inlines, colors);
                                    else AppendBlock(sb, b, colors);
                                }
                                sb.Append("</td>");
                            }
                            sb.Append("</tr>");
                        }
                    }
                    sb.Append("</table>");
                    break;
                case Section s:
                    foreach (var b in s.Blocks) AppendBlock(sb, b, colors);
                    break;
                case BlockUIContainer bc when bc.Child is Image img:
                    AppendImage(sb, img);
                    break;
            }
        }

        private static void AppendInlines(StringBuilder sb, InlineCollection inlines, bool colors)
        {
            foreach (var inline in inlines) AppendInline(sb, inline, colors);
        }

        private static void AppendInline(StringBuilder sb, Inline inline, bool colors)
        {
            switch (inline)
            {
                case Run r:
                    AppendRun(sb, r, colors);
                    break;
                case LineBreak:
                    sb.Append("<br/>");
                    break;
                case InlineUIContainer iu when iu.Child is Image img:
                    AppendImage(sb, img);
                    break;
                case Hyperlink h when h.NavigateUri != null:   // real anchors (Links.cs, 1.1.3)
                    sb.Append("<a href=\"").Append(Html(h.NavigateUri.IsAbsoluteUri ? h.NavigateUri.AbsoluteUri : h.NavigateUri.OriginalString))
                      .Append("\" target=\"_blank\" rel=\"noopener\">");
                    AppendInlines(sb, h.Inlines, colors);
                    sb.Append("</a>");
                    break;
                case Span sp:   // includes Bold/Italic/Underline containers
                    AppendInlines(sb, sp.Inlines, colors);
                    break;
            }
        }

        private static void AppendRun(StringBuilder sb, Run r, bool colors)
        {
            bool bold   = r.FontWeight.ToOpenTypeWeight() >= 600;
            bool italic = r.FontStyle == FontStyles.Italic;
            bool mono   = r.FontFamily?.Source?.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0;
            bool under  = false, strike = false;
            if (r.TextDecorations != null)
            {
                foreach (var d in r.TextDecorations)
                {
                    if (d.Location == TextDecorationLocation.Underline) under = true;
                    if (d.Location == TextDecorationLocation.Strikethrough) strike = true;
                }
            }

            // Local values only: after NormalizeThemeColors, a color that is still set
            // was chosen on purpose. Inherited defaults stay unstyled so the page's
            // theme-shell CSS colors them.
            string style = "";
            if (colors && r.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush f)
                style += $"color:#{f.Color.R:X2}{f.Color.G:X2}{f.Color.B:X2};";
            if (colors && r.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush b && b.Color.A > 0)
                style += $"background:#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2};";

            if (style.Length > 0) sb.Append("<span style=\"").Append(style).Append("\">");
            if (bold)   sb.Append("<b>");
            if (italic) sb.Append("<i>");
            if (under)  sb.Append("<u>");
            if (strike) sb.Append("<s>");
            if (mono)   sb.Append("<code>");

            sb.Append(Html(r.Text));

            if (mono)   sb.Append("</code>");
            if (strike) sb.Append("</s>");
            if (under)  sb.Append("</u>");
            if (italic) sb.Append("</i>");
            if (bold)   sb.Append("</b>");
            if (style.Length > 0) sb.Append("</span>");
        }

        private static void AppendImage(StringBuilder sb, Image img)
        {
            if (img.Source is not BitmapSource src) return;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            sb.Append("<img src=\"data:image/png;base64,")
              .Append(Convert.ToBase64String(ms.ToArray()))
              .Append("\"/>");
        }

        private static string Html(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
