using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Shell;

namespace KillerNotes.Services
{
    /// <summary>
    /// FlowDocument to Markdown, for Export note as... (F8).
    ///
    /// Small, purposeful, no dep. Handles what notes actually carry: paragraphs, bold /
    /// italic / strike, monospace (inline backticks and fenced blocks for all-mono
    /// paragraphs), ordered and unordered lists (nested), simple tables (one row group,
    /// no merged cells - a merged-cell table falls back to a cell-per-line block),
    /// horizontal rules (the FontSize-2 border paragraph the editor uses), hyperlinks,
    /// and inline images.
    ///
    /// Images are extracted to a sibling &lt;name&gt;.assets/ folder as PNG (matches
    /// Obsidian). If the export target is a share where the sibling cannot be created,
    /// each image emits a plain-text placeholder and ImagesSkipped is set so the caller
    /// can flag it in the status bar. A vault export owns its folder and writes numbered
    /// files; a single note goes through MarkdownExport, which keeps the previous export
    /// intact until the new one is complete.
    ///
    /// Markdown has no portable underline spelling, so underline emits &lt;u&gt;...&lt;/u&gt;.
    /// Most renderers pass raw HTML through, and the source stays readable.
    ///
    /// Callers that already know the note is markdown-format should skip this walker and
    /// write the editor's plain text directly - a markdown note is stored as one
    /// Paragraph per line, and the walker's blank-line-between-blocks pass would
    /// double-space that source.
    /// </summary>
    internal sealed class MarkdownWriter
    {
        private readonly StringBuilder _sb = new();
        private readonly string _assetsDirRelName;
        private readonly string? _assetsDirFullPath;
        private int _imgIndex, _audioIndex;
        private bool _assetsCreated;
        private readonly Func<string, Attachment?>? _attachments;
        private readonly MarkdownAssets? _assets;
        internal bool ImagesSkipped { get; private set; }

        /// <summary>How many embedded items (pictures, recordings) could not be written out.</summary>
        internal int Lost { get; private set; }

        private MarkdownWriter(string assetsDirRelName, string? assetsDirFullPath, Func<string, Attachment?>? attachments, MarkdownAssets? assets)
        {
            _assetsDirRelName  = assetsDirRelName;
            _assetsDirFullPath = assetsDirFullPath;
            _attachments       = attachments;
            _assets            = assets;
        }

        /// <summary>Walks the document and returns the markdown text. Images are written
        /// to the sibling assets folder as a side effect, through assets when given. With stored
        /// payloads available, a recording (a marked picture of its chip, Services/Attachments.cs)
        /// is written as its audio file and linked by name and length. Returns how many embedded
        /// items could not be written.</summary>
        internal static (string Markdown, int Lost) FromDocument(FlowDocument doc, string targetFilePath,
            Func<string, Attachment?>? attachments, MarkdownAssets? assets = null)
        {
            string dir       = Path.GetDirectoryName(targetFilePath) ?? "";
            string name      = Path.GetFileNameWithoutExtension(targetFilePath);
            string assetsRel = name + ".assets";
            string? assetsFull = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, assetsRel);
            var w = new MarkdownWriter(assetsRel, assetsFull, attachments, assets);
            foreach (var b in doc.Blocks) w.AppendBlock(b);
            return (w._sb.ToString().TrimEnd() + "\r\n", w.Lost);
        }

        // ---- Block dispatch ----

        private void AppendBlock(Block block)
        {
            switch (block)
            {
                case Paragraph p when p.FontSize <= 2 && p.BorderThickness.Bottom > 0:
                    _sb.Append("---\r\n\r\n");
                    break;
                case Paragraph p:
                    AppendParagraph(p);
                    break;
                case List list:
                    AppendList(list, indent: 0);
                    _sb.Append("\r\n");
                    break;
                case Table t:
                    AppendTable(t);
                    break;
                case Section s:
                    foreach (var b in s.Blocks) AppendBlock(b);
                    break;
                case BlockUIContainer bc when bc.Child is Image img:
                    string? embedded = Embedded(img);
                    if (embedded != null) _sb.Append(embedded).Append("\r\n\r\n");
                    break;
                case BlockUIContainer bc when RecordingRef.From((bc.Child as Border)?.Tag) is { } chip:
                    string? recording = EmbeddedRecording(chip.Hash);
                    if (recording != null) _sb.Append(recording).Append("\r\n\r\n");
                    break;
            }
        }

        private void AppendParagraph(Paragraph p)
        {
            // Fenced-code shortcut when every meaningful inline is Consolas.
            if (IsAllMonospace(p.Inlines))
            {
                _sb.Append("```\r\n");
                foreach (var i in p.Inlines) AppendPlain(i);
                _sb.Append("\r\n```\r\n\r\n");
                return;
            }
            foreach (var i in p.Inlines) AppendInline(i);
            _sb.Append("\r\n\r\n");
        }

        private static bool IsAllMonospace(InlineCollection inlines)
        {
            bool sawText = false;
            foreach (var i in inlines)
            {
                switch (i)
                {
                    case Run r:
                        sawText = true;
                        string? src = r.FontFamily?.Source;
                        if (src == null || src.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) < 0)
                            return false;
                        break;
                    case LineBreak: break;
                    default: return false;
                }
            }
            return sawText;
        }

        // ---- Inlines ----

        private void AppendPlain(Inline inline)
        {
            switch (inline)
            {
                case Run r:     _sb.Append(r.Text); break;
                case LineBreak: _sb.Append("\r\n"); break;
                case Span sp:   foreach (var i in sp.Inlines) AppendPlain(i); break;
            }
        }

        private void AppendInline(Inline inline)
        {
            switch (inline)
            {
                case Run r: AppendRun(r); break;
                case LineBreak: _sb.Append("  \r\n"); break;   // two-space soft break
                case InlineUIContainer iu when iu.Child is Image img:
                    string? embedded = Embedded(img);
                    if (embedded != null) _sb.Append(embedded);
                    break;
                case InlineUIContainer iu when RecordingRef.From((iu.Child as Border)?.Tag) is { } chip:
                    string? recording = EmbeddedRecording(chip.Hash);
                    if (recording != null) _sb.Append(recording);
                    break;
                case AnchoredBlock anchored:   // floated pictures and chips: written where they are anchored
                    foreach (var b in anchored.Blocks)
                    {
                        _sb.Append("\r\n\r\n");
                        AppendBlock(b);
                    }
                    break;
                case Hyperlink h when h.NavigateUri != null:
                    _sb.Append('[');
                    foreach (var i in h.Inlines) AppendInline(i);
                    _sb.Append("](")
                       .Append(h.NavigateUri.IsAbsoluteUri ? h.NavigateUri.AbsoluteUri : h.NavigateUri.OriginalString)
                       .Append(')');
                    break;
                case Span sp:
                    foreach (var i in sp.Inlines) AppendInline(i);
                    break;
            }
        }

        private void AppendRun(Run r)
        {
            if (string.IsNullOrEmpty(r.Text)) return;

            bool bold   = r.FontWeight.ToOpenTypeWeight() >= 600;
            bool italic = r.FontStyle == FontStyles.Italic;
            bool mono   = r.FontFamily?.Source?.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0;
            bool under  = false, strike = false;
            if (r.TextDecorations != null)
            {
                foreach (var d in r.TextDecorations)
                {
                    if (d.Location == TextDecorationLocation.Underline)     under  = true;
                    if (d.Location == TextDecorationLocation.Strikethrough) strike = true;
                }
            }

            // Emit-order sandwich. Bold + italic combine as ***. Mono is the outermost
            // fence because backticks disable other inline formatting anyway.
            if (mono)   _sb.Append('`');
            if (strike) _sb.Append("~~");
            if (bold && italic) _sb.Append("***");
            else if (bold)      _sb.Append("**");
            else if (italic)    _sb.Append('*');
            if (under)  _sb.Append("<u>");

            _sb.Append(r.Text);

            if (under)  _sb.Append("</u>");
            if (bold && italic) _sb.Append("***");
            else if (bold)      _sb.Append("**");
            else if (italic)    _sb.Append('*');
            if (strike) _sb.Append("~~");
            if (mono)   _sb.Append('`');
        }

        // ---- Lists ----

        private void AppendList(List list, int indent)
        {
            bool ordered = list.MarkerStyle == TextMarkerStyle.Decimal;
            int n = list.StartIndex < 1 ? 1 : list.StartIndex;
            foreach (var li in list.ListItems)
            {
                _sb.Append(' ', indent);
                _sb.Append(ordered ? (n++ + ". ") : "- ");
                bool firstBlock = true;
                foreach (var b in li.Blocks)
                {
                    if (b is Paragraph pp)
                    {
                        if (!firstBlock)
                        {
                            _sb.Append("\r\n");
                            _sb.Append(' ', indent + 2);
                        }
                        foreach (var i in pp.Inlines) AppendInline(i);
                        _sb.Append("\r\n");
                    }
                    else if (b is List nested)
                    {
                        AppendList(nested, indent + 2);
                    }
                    firstBlock = false;
                }
            }
        }

        // ---- Tables ----

        private void AppendTable(Table t)
        {
            var rows = new List<string[]>();
            foreach (var g in t.RowGroups)
            {
                foreach (var row in g.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.ColumnSpan > 1 || cell.RowSpan > 1)
                        {
                            AppendTableFallback(t);
                            return;
                        }
                    }
                    rows.Add([.. row.Cells.Select(CellToPlain)]);
                }
            }
            if (rows.Count == 0) return;
            int cols = rows.Max(r => r.Length);

            // Header (row 0)
            _sb.Append('|');
            for (int c = 0; c < cols; c++)
                _sb.Append(' ').Append(c < rows[0].Length ? EscapePipe(rows[0][c]) : "").Append(" |");
            _sb.Append("\r\n|");
            for (int c = 0; c < cols; c++) _sb.Append(" --- |");
            _sb.Append("\r\n");
            // Body
            for (int r = 1; r < rows.Count; r++)
            {
                _sb.Append('|');
                for (int c = 0; c < cols; c++)
                    _sb.Append(' ').Append(c < rows[r].Length ? EscapePipe(rows[r][c]) : "").Append(" |");
                _sb.Append("\r\n");
            }
            _sb.Append("\r\n");
        }

        private void AppendTableFallback(Table t)
        {
            // Merged cells break the pipe grammar. Emit each cell as its own line so no
            // content is lost, separate rows by a blank line.
            foreach (var g in t.RowGroups)
            {
                foreach (var row in g.Rows)
                {
                    foreach (var cell in row.Cells)
                        _sb.Append(CellToPlain(cell)).Append("\r\n");
                    _sb.Append("\r\n");
                }
            }
        }

        private static string CellToPlain(TableCell cell)
        {
            var s = new StringBuilder();
            foreach (var b in cell.Blocks)
            {
                if (b is Paragraph pp)
                {
                    foreach (var i in pp.Inlines) AppendInlinePlain(s, i);
                }
            }
            return s.ToString().Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static void AppendInlinePlain(StringBuilder s, Inline inline)
        {
            switch (inline)
            {
                case Run r:     s.Append(r.Text); break;
                case LineBreak: s.Append(' '); break;
                case Span sp:   foreach (var i in sp.Inlines) AppendInlinePlain(s, i); break;
            }
        }

        private static string EscapePipe(string s) => s.Replace("|", "\\|");

        // ---- Image extraction to sibling assets folder ----

        /// <summary>The markdown for one embedded picture: an image link, or for a recording an
        /// ordinary link to its audio file. Null, counted as lost, when it could not be written.</summary>
        private string? Embedded(Image img)
        {
            if (Attachments.TryParseMarker(img.ToolTip, out var kind, out string hash) && kind == AttachmentKind.Recording)
                return EmbeddedRecording(hash);
            string? link = TrySaveImage(img);
            if (link == null) { Lost++; return null; }
            return "![](" + Destination(link) + ")";
        }

        /// <summary>A recording, from its marked picture in a stored note or its playback chip in
        /// the open editor.</summary>
        private string? EmbeddedRecording(string hash)
        {
            var stored = _attachments?.Invoke(hash);
            string? audio = stored != null ? TrySaveAudio(stored) : null;
            if (audio == null) { Lost++; return null; }
            var length = TimeSpan.FromMilliseconds(stored!.DurationMs);
            return "[Recording " + length.ToString(@"m\:ss") + "](" + Destination(audio) + ")";
        }

        /// <summary>A link destination with a space in it ("My note.assets/...") is not one in
        /// CommonMark unless it is wrapped in angle brackets, and vault apps then show the raw text
        /// instead of the picture.</summary>
        private static string Destination(string path) => path.IndexOf(' ') >= 0 ? "<" + path + ">" : path;

        // Stored bytes as they are (FLAC, or WAV for older takes): no re-encode.
        private string? TrySaveAudio(Attachment recording) =>
            SaveAsset("recording", ++_audioIndex, AudioCodec.ExtensionFor(recording.Payload), recording.Payload);

        private string? TrySaveImage(Image img)
        {
            if (img.Source is not BitmapSource src) { ImagesSkipped = true; return null; }
            string? link = null;
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using var png = new MemoryStream();
                enc.Save(png);
                link = SaveAsset("img", ++_imgIndex, ".png", png.ToArray());
            }
            catch { /* an image that will not encode is one more that could not be written */ }
            if (link == null) ImagesSkipped = true;
            return link;
        }

        /// <summary>Writes one file and returns its relative link, forward slashes so it stays
        /// portable across editors. Null when it could not be written.</summary>
        private string? SaveAsset(string prefix, int index, string extension, byte[] bytes)
        {
            if (_assetsDirFullPath == null) return null;
            try
            {
                string fileName;
                if (_assets != null) fileName = _assets.Add(prefix, extension, bytes);
                else
                {
                    if (!_assetsCreated)
                    {
                        Directory.CreateDirectory(_assetsDirFullPath);
                        _assetsCreated = true;
                    }
                    fileName = $"{prefix}-{index}{extension}";
                    File.WriteAllBytes(Path.Combine(_assetsDirFullPath, fileName), bytes);
                }
                return _assetsDirRelName + "/" + fileName;
            }
            catch (Exception ex)
            {
                _assets?.Fail(ex);
                return null;
            }
        }
    }
}
