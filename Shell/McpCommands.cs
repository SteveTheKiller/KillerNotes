using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Windows.Documents;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    [DataContract]
    internal sealed class McpCommandResult
    {
        [DataMember(Name = "id", EmitDefaultValue = false)] public long Id { get; set; }
        [DataMember(Name = "title", EmitDefaultValue = false)] public string? Title { get; set; }
        [DataMember(Name = "name", EmitDefaultValue = false)] public string? Name { get; set; }
        [DataMember(Name = "color", EmitDefaultValue = false)] public string? Color { get; set; }
        [DataMember(Name = "output", EmitDefaultValue = false)] public string? Output { get; set; }
    }

    public partial class MainWindow
    {
        internal string ExecuteMcpCommand(string[] args)
        {
            if (!NoteStore.IsOpen) throw new InvalidOperationException("Open and unlock KillerNotes first");
            if (args.Length == 0) throw new ArgumentException("A command is required");
            string command = args[0].ToLowerInvariant();
            var values = args.Skip(1).ToList();
            McpCommandResult result;
            switch (command)
            {
                case "create":
                    {
                        string title = Take(values, "--title", required: true)!;
                        string content = Take(values, "--content", required: true)!;
                        string group = Take(values, "--group") ?? "";
                        string tags = Take(values, "--tags") ?? "";
                        string color = Take(values, "--title-color") ?? "";
                        RequireEmpty(values);
                        long id = NoteStore.Create(ValidateText(title, "title", 240), Note.FormatMarkdown);
                        SaveMarkdown(id, title, ValidateText(content, "content", 500000));
                        if (group.Length > 0) NoteStore.SetNoteGroup(id, ValidateText(group, "group", 240));
                        if (tags.Length > 0) NoteStore.SetNoteTags(id, ValidateText(tags, "tags", 1000));
                        if (color.Length > 0) NoteStore.SetTitleColor(id, ValidateColor(color));
                        RefreshList();
                        result = new McpCommandResult { Id = id, Title = title };
                        break;
                    }
                case "update":
                    {
                        long id = TakeId(values);
                        var note = RequireNote(id);
                        string title = Take(values, "--title") ?? note.Title;
                        string? content = Take(values, "--content");
                        string? group = Take(values, "--group");
                        string? tags = Take(values, "--tags");
                        string? color = Take(values, "--title-color");
                        RequireEmpty(values);
                        if (content != null) SaveMarkdown(id, ValidateText(title, "title", 240), ValidateText(content, "content", 500000));
                        else if (!string.Equals(title, note.Title, StringComparison.Ordinal))
                        {
                            byte[] blob = NoteStore.LoadContent(id) ?? [];
                            string plain = note.Format == Note.FormatMarkdown ? MarkdownBlob.Decode(blob) : DocumentText(LoadNoteDocument(id));
                            NoteStore.Save(id, ValidateText(title, "title", 240), blob, plain);
                        }
                        if (group != null) NoteStore.SetNoteGroup(id, ValidateText(group, "group", 240));
                        if (tags != null) NoteStore.SetNoteTags(id, ValidateText(tags, "tags", 1000));
                        if (color != null) NoteStore.SetTitleColor(id, color.Length == 0 ? "" : ValidateColor(color));
                        RefreshList();
                        result = new McpCommandResult { Id = id, Title = title };
                        break;
                    }
                case "create-group":
                    {
                        string name = ValidateText(Take(values, "--name", true)!, "group", 240);
                        string parent = Take(values, "--parent") ?? "";
                        string? color = Take(values, "--color");
                        RequireEmpty(values);
                        NoteStore.AddGroup(name, parent);
                        string path = NoteStore.GroupPath(parent, name);
                        if (!string.IsNullOrEmpty(color)) NoteStore.SetGroupColor(path, ValidateColor(color!));
                        RefreshList(); result = new McpCommandResult { Name = path }; break;
                    }
                case "set-group-color":
                    {
                        string name = ValidateText(Take(values, "--name", true)!, "group", 240);
                        string color = Take(values, "--color", true)!;
                        RequireEmpty(values); NoteStore.SetGroupColor(name, color.Length == 0 ? "" : ValidateColor(color));
                        RefreshList(); result = new McpCommandResult { Name = name, Color = color }; break;
                    }
                case "set-title-color":
                    {
                        long id = TakeId(values); RequireNote(id);
                        string color = Take(values, "--color", true)!; RequireEmpty(values);
                        NoteStore.SetTitleColor(id, color.Length == 0 ? "" : ValidateColor(color));
                        RefreshList(); result = new McpCommandResult { Id = id, Color = color }; break;
                    }
                case "import-image":
                    {
                        string path = ValidateInputFile(Take(values, "--path", true)!, [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"], 32 * 1024 * 1024);
                        string? group = Take(values, "--group"); RequireEmpty(values);
                        long id = ImportImage(path); if (!string.IsNullOrEmpty(group)) NoteStore.SetNoteGroup(id, group!);
                        RefreshList(); result = new McpCommandResult { Id = id, Title = Path.GetFileNameWithoutExtension(path) }; break;
                    }
                case "export":
                    {
                        long id = TakeId(values); Note note = RequireNote(id);
                        string output = ValidateOutputPath(Take(values, "--output", true)!); RequireEmpty(values);
                        ExportForMcp(note, output); result = new McpCommandResult { Id = id, Output = output }; break;
                    }
                default: throw new ArgumentException("Unsupported write command");
            }
            return KillerNotes.Cli.Program.SerializeJson(result);
        }

        private static void SaveMarkdown(long id, string title, string content)
        {
            byte[] blob = MarkdownBlob.Encode(content);
            NoteStore.Save(id, title, blob, content);
            NoteStore.SetLinks(id, WikiLinks.Parse(content));
        }

        private void ExportForMcp(Note note, string output)
        {
            if (_currentId == note.Id) SaveCurrentNote(false);
            byte[] blob = NoteStore.LoadContent(note.Id) ?? [];
            var doc = note.Format == Note.FormatMarkdown ? MarkdownConvert.ToDocument(MarkdownBlob.Decode(blob), 14) : LoadNoteDocument(note.Id);
            switch (Path.GetExtension(output).ToLowerInvariant())
            {
                case ".html": File.WriteAllText(output, DocumentToHtml(doc, note.Title), Encoding.UTF8); break;
                case ".md": File.WriteAllText(output, note.Format == Note.FormatMarkdown ? MarkdownBlob.Decode(blob) : MarkdownWriter.FromDocument(doc, output).Markdown, Encoding.UTF8); break;
                case ".knote": NoteStore.ExportNote(note.Id, output, null); break;
                case ".txt": File.WriteAllText(output, new TextRange(doc.ContentStart, doc.ContentEnd).Text, Encoding.UTF8); break;
                default: throw new ArgumentException("Output must end in .txt, .md, .html, or .knote");
            }
        }

        private static Note RequireNote(long id) => NoteStore.List().FirstOrDefault(n => n.Id == id) ?? throw new InvalidOperationException("The note was not found");
        private static string DocumentText(FlowDocument document) => new TextRange(document.ContentStart, document.ContentEnd).Text;
        private static long TakeId(List<string> values) => long.TryParse(Take(values, "--id", true), NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0 ? id : throw new ArgumentException("Invalid note id");
        private static string? Take(List<string> values, string name, bool required = false) { int i = values.IndexOf(name); if (i < 0) { if (required) throw new ArgumentException("Missing " + name); return null; } if (i + 1 >= values.Count) throw new ArgumentException("Missing value for " + name); string value = values[i + 1]; values.RemoveRange(i, 2); return value; }
        private static void RequireEmpty(List<string> values) { if (values.Count != 0) throw new ArgumentException("Invalid command arguments"); }
        private static string ValidateText(string value, string name, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum ? value : throw new ArgumentException("Invalid " + name);
        private static string ValidateColor(string color) { try { _ = System.Windows.Media.ColorConverter.ConvertFromString(color); return color; } catch { throw new ArgumentException("Color must be a named color or #RRGGBB value"); } }
        private static string ValidateInputFile(string path, string[] extensions, long maximum) { string full = Path.GetFullPath(path); var info = new FileInfo(full); if (!Path.IsPathRooted(path) || !info.Exists || info.Length > maximum || !extensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Invalid input file"); return full; }
        private static string ValidateOutputPath(string path) { string full = Path.GetFullPath(path); if (!Path.IsPathRooted(path) || File.Exists(full) || Directory.Exists(full) || !Directory.Exists(Path.GetDirectoryName(full))) throw new ArgumentException("Output must be a new file in an existing folder"); return full; }
    }
}
