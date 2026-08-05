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
            foreach (var block in doc.Blocks) AppendBlock(sb, block);
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void AppendBlock(StringBuilder sb, Block block)
        {
            switch (block)
            {
                case Paragraph p when p.FontSize <= 2 && p.BorderThickness.Bottom > 0:
                    sb.Append("<hr/>");
                    break;
                case Paragraph p:
                    sb.Append("<p>");
                    AppendInlines(sb, p.Inlines);
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
                            if (b is Paragraph pp) AppendInlines(sb, pp.Inlines);
                            else AppendBlock(sb, b);
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
                                    if (b is Paragraph pp) AppendInlines(sb, pp.Inlines);
                                    else AppendBlock(sb, b);
                                }
                                sb.Append("</td>");
                            }
                            sb.Append("</tr>");
                        }
                    }
                    sb.Append("</table>");
                    break;
                case Section s:
                    foreach (var b in s.Blocks) AppendBlock(sb, b);
                    break;
                case BlockUIContainer bc when bc.Child is Image img:
                    AppendImage(sb, img);
                    break;
            }
        }

        private static void AppendInlines(StringBuilder sb, InlineCollection inlines)
        {
            foreach (var inline in inlines) AppendInline(sb, inline);
        }

        private static void AppendInline(StringBuilder sb, Inline inline)
        {
            switch (inline)
            {
                case Run r:
                    AppendRun(sb, r);
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
                    AppendInlines(sb, h.Inlines);
                    sb.Append("</a>");
                    break;
                case Span sp:   // includes Bold/Italic/Underline containers
                    AppendInlines(sb, sp.Inlines);
                    break;
            }
        }

        private static void AppendRun(StringBuilder sb, Run r)
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
            if (r.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush f)
                style += $"color:#{f.Color.R:X2}{f.Color.G:X2}{f.Color.B:X2};";
            if (r.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush b && b.Color.A > 0)
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
