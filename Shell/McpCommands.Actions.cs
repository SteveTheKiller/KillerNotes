using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    [DataContract]
    internal sealed class McpActionResult
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "format")] public string Format { get; set; } = "";
        [DataMember(Name = "content")] public string Content { get; set; } = "";
        [DataMember(Name = "output")] public string Output { get; set; } = "";
        [DataMember(Name = "count")] public int Count { get; set; }
    }

    public partial class MainWindow
    {
        private bool TryExecuteMcpAction(string command, List<string> values, out string json)
        {
            json = "";
            if (TryExecuteMcpManagement(command, values, out json)) return true;
            if (command is not ("share" or "export-all" or "detect-markdown" or "convert" or "templates" or "create-from-template" or "today" or "trash-list" or "trash" or "restore" or "pin" or "unpin" or "version-get" or "version-restore")) return false;
            if (NoteStore.IsReadOnly && command is not ("share" or "export-all" or "templates" or "trash-list" or "version-get" or "detect-markdown"))
                throw new InvalidOperationException("The database is read-only");
            SaveCurrentNote(refreshList: false);
            var result = new McpActionResult();
            switch (command)
            {
                case "templates":
                    RequireEmpty(values);
                    json = KillerNotes.Cli.Program.SerializeJson(TemplateNotes().Select(n => new KillerNotes.Cli.NoteSummary { Id = n.Id, Title = n.Title, Group = n.Notebook, Tags = n.Tags, Format = n.IsMarkdown ? "markdown" : "rich" }).ToList());
                    return true;
                case "trash-list":
                    RequireEmpty(values);
                    json = KillerNotes.Cli.Program.SerializeJson(NoteStore.ListTrash().Select(n => new KillerNotes.Cli.NoteSummary { Id = n.Id, Title = n.Title, Group = n.Notebook, Tags = n.Tags, Format = n.IsMarkdown ? "markdown" : "rich" }).ToList());
                    return true;
                case "detect-markdown":
                    string? value = Take(values, "--value"); RequireEmpty(values);
                    if (value != null)
                    {
                        if (value is not ("on" or "off")) throw new ArgumentException("Value must be on or off");
                        App.SetSetting("DetectMarkdown", value);
                        UpdatePreviewState();
                    }
                    result.Content = DetectMarkdownGlobally ? "on" : "off";
                    break;
                case "share":
                    result.Id = TakeId(values); RequireNote(result.Id);
                    result.Output = ValidateOutputPath(Take(values, "--output", true)!);
                    string? password = Take(values, "--password"); RequireEmpty(values);
                    if (!string.Equals(Path.GetExtension(result.Output), ".knote", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Share output must end in .knote");
                    NoteStore.ExportNote(result.Id, result.Output, password);
                    break;
                case "export-all":
                    result.Output = ValidateOutputPath(Take(values, "--output", true)!); RequireEmpty(values);
                    Directory.CreateDirectory(result.Output);
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var note in NoteStore.List()) { WriteVaultFile(result.Output, note, used); result.Count++; }
                    break;
                case "create-from-template":
                    long templateId = TakeNamedId(values, "--template-id"); RequireNote(templateId);
                    string? title = Take(values, "--title"); string? group = Take(values, "--group"); RequireEmpty(values);
                    if (title != null) ValidateText(title, "title", 240);
                    if (group != null) ValidateOptionalText(group, "group", 240);
                    result.Id = CreateFromTemplate(templateId, title, group, open: false);
                    if (result.Id < 0) throw new InvalidOperationException("The template could not be copied");
                    RefreshList(); result.Title = RequireNote(result.Id).Title;
                    break;
                case "today":
                    RequireEmpty(values); OpenTodayNote();
                    result.Id = NoteStore.ResolveTitle(TodayTitle());
                    if (result.Id < 0) throw new InvalidOperationException("Today's note could not be created");
                    result.Title = TodayTitle();
                    break;
                case "version-get":
                case "version-restore":
                    result.Id = TakeId(values); RequireNote(result.Id);
                    long versionId = TakeNamedId(values, "--version-id"); RequireEmpty(values);
                    if (!NoteStore.ListHistory(result.Id).Any(v => v.Id == versionId)) throw new InvalidOperationException("The version was not found for this note");
                    if (command == "version-get")
                    {
                        var version = NoteStore.LoadVersion(versionId) ?? throw new InvalidOperationException("The version was not found");
                        result.Title = version.Title; result.Content = version.Plain; result.Format = version.Format == Note.FormatMarkdown ? "markdown" : "rich";
                    }
                    else
                    {
                        string restoredPlain = NoteStore.LoadVersion(versionId)?.Plain ?? "";
                        if (!NoteStore.RestoreVersion(result.Id, versionId)) throw new InvalidOperationException("The version could not be restored");
                        NoteStore.SetLinks(result.Id, WikiLinks.Parse(restoredPlain));
                        RefreshList(); if (_currentId == result.Id) OpenNote(result.Id);
                    }
                    break;
                case "convert":
                    result.Id = TakeId(values); var source = RequireNote(result.Id);
                    string format = Take(values, "--format", true)!;
                    bool allowLoss = values.Remove("--allow-loss"); RequireEmpty(values);
                    if (format is not ("markdown" or "rich")) throw new ArgumentException("Format must be markdown or rich");
                    int target = format == "markdown" ? Note.FormatMarkdown : Note.FormatRich;
                    if (source.Format != target)
                    {
                        var document = source.IsMarkdown ? MarkdownConvert.ToDocument(MarkdownBlob.Decode(NoteStore.LoadContent(result.Id)), 14) : LoadNoteDocument(result.Id);
                        var losses = target == Note.FormatMarkdown ? MarkdownConvert.Losses(document) : [];
                        if (losses.Count > 0 && !allowLoss) throw new InvalidOperationException("Conversion loses formatting. Review these features and pass --allow-loss: " + string.Join(", ", losses));
                        NoteStore.Snapshot(result.Id, force: true);
                        string plain; byte[] blob;
                        if (target == Note.FormatMarkdown) { plain = MarkdownConvert.FromDocument(document); blob = MarkdownBlob.Encode(plain); }
                        else { var range = new TextRange(document.ContentStart, document.ContentEnd); plain = range.Text; using var stream = new MemoryStream(); range.Save(stream, DataFormats.XamlPackage); blob = stream.ToArray(); }
                        NoteStore.SetFormat(result.Id, target, blob, plain); NoteStore.SetLinks(result.Id, WikiLinks.Parse(plain));
                        RefreshList(); if (_currentId == result.Id) OpenNote(result.Id);
                    }
                    result.Format = format;
                    break;
                default:
                    result.Id = TakeId(values); RequireEmpty(values);
                    if (command == "restore")
                    {
                        if (!NoteStore.ListTrash().Any(n => n.Id == result.Id)) throw new InvalidOperationException("The trashed note was not found");
                        NoteStore.Restore(result.Id);
                    }
                    else
                    {
                        RequireNote(result.Id);
                        if (command == "trash") { NoteStore.Trash(result.Id); if (_currentId == result.Id) CloseCurrentNote(); }
                        else NoteStore.SetPinned(result.Id, command == "pin");
                    }
                    RefreshList();
                    break;
            }
            json = KillerNotes.Cli.Program.SerializeJson(result);
            return true;
        }

        private bool TryExecuteMcpManagement(string command, List<string> values, out string json)
        {
            json = "";
            if (command is not ("rename-group" or "delete-group" or "tag-definitions" or "create-tag" or "rename-tag" or "delete-tag" or "set-tag-color" or "template-group" or "daily-group" or "backup-now" or "permanent-delete" or "empty-trash")) return false;
            if (NoteStore.IsReadOnly && command is not ("tag-definitions" or "template-group" or "daily-group" or "backup-now")) throw new InvalidOperationException("The database is read-only");
            if (command is "delete-group" or "delete-tag" or "permanent-delete" or "empty-trash")
            {
                if (!values.Remove("--confirm")) throw new ArgumentException("This command requires --confirm");
            }
            SaveCurrentNote(refreshList: false);
            var result = new McpActionResult();
            bool refresh = false;
            switch (command)
            {
                case "tag-definitions":
                    RequireEmpty(values);
                    json = KillerNotes.Cli.Program.SerializeJson(NoteStore.ListTags().Select(t => new KillerNotes.Cli.NamedItem { Name = t.Name, Color = t.Color }).ToList());
                    return true;
                case "template-group":
                case "daily-group":
                    string setting = command == "template-group" ? TemplatesGroupSetting : DailyGroupSetting;
                    string? selection = Take(values, "--name"); RequireEmpty(values);
                    if (selection != null) { if (selection.Length > 0) RequireGroup(selection); SetDbScopedSetting(setting, selection.Length == 0 ? null : selection); }
                    result.Content = DbScopedSetting(setting) ?? "";
                    break;
                case "backup-now":
                    result.Output = ValidateOutputPath(Take(values, "--output", true)!); RequireEmpty(values);
                    if (!string.Equals(Path.GetExtension(result.Output), ".kndb", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Backup output must end in .kndb");
                    NoteStore.BackupTo(result.Output);
                    break;
                case "rename-group":
                case "delete-group":
                    string path = RequireGroup(Take(values, "--name", true)!);
                    if (command == "rename-group")
                    {
                        string leaf = ValidateText(Take(values, "--new-name", true)!, "group", 240); RequireEmpty(values);
                        if (leaf.Contains(NoteStore.GroupSep)) throw new ArgumentException("A new group name must be a leaf name");
                        string renamed = NoteStore.GroupPath(NoteStore.GroupParentOf(path), leaf);
                        if (!string.Equals(path, renamed, StringComparison.OrdinalIgnoreCase) && NoteStore.ListGroupTree().Any(g => string.Equals(g.Path, renamed, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A sibling group already has that name");
                        NoteStore.RenameGroup(path, leaf);
                        foreach (string key in new[] { TemplatesGroupSetting, DailyGroupSetting })
                        {
                            string? chosen = DbScopedSetting(key);
                            if (chosen != null && (string.Equals(chosen, path, StringComparison.OrdinalIgnoreCase) || chosen.StartsWith(path + NoteStore.GroupSep, StringComparison.OrdinalIgnoreCase))) SetDbScopedSetting(key, renamed + chosen.Substring(path.Length));
                        }
                        result.Content = renamed;
                    }
                    else
                    {
                        RequireEmpty(values); NoteStore.DeleteGroup(path);
                        foreach (string key in new[] { TemplatesGroupSetting, DailyGroupSetting })
                        {
                            string? chosen = DbScopedSetting(key);
                            if (chosen != null && (string.Equals(chosen, path, StringComparison.OrdinalIgnoreCase) || chosen.StartsWith(path + NoteStore.GroupSep, StringComparison.OrdinalIgnoreCase))) SetDbScopedSetting(key, null);
                        }
                    }
                    refresh = true;
                    break;
                case "create-tag":
                case "rename-tag":
                case "delete-tag":
                case "set-tag-color":
                    string tag = ValidateTagName(Take(values, "--name", true)!);
                    if (command != "create-tag") tag = NoteStore.ListTags().FirstOrDefault(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase)).Name ?? throw new InvalidOperationException("The tag was not found");
                    if (command == "rename-tag")
                    {
                        string renamed = ValidateTagName(Take(values, "--new-name", true)!); RequireEmpty(values);
                        if (!string.Equals(tag, renamed, StringComparison.OrdinalIgnoreCase) && NoteStore.ListTags().Any(t => string.Equals(t.Name, renamed, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A tag already has that name");
                        NoteStore.RenameTag(tag, renamed); result.Content = renamed;
                    }
                    else if (command == "delete-tag") { RequireEmpty(values); NoteStore.DeleteTag(tag); }
                    else
                    {
                        string color = Take(values, "--color", command == "set-tag-color") ?? ""; RequireEmpty(values);
                        if (color.Length > 0) ValidateColor(color);
                        if (command == "create-tag") NoteStore.AddTag(tag, color); else NoteStore.SetTagColor(tag, color);
                        result.Content = tag;
                    }
                    refresh = true;
                    break;
                case "permanent-delete":
                    result.Id = TakeId(values); RequireEmpty(values);
                    if (!NoteStore.ListTrash().Any(n => n.Id == result.Id)) throw new InvalidOperationException("Only a trashed note can be permanently deleted");
                    if (_currentId == result.Id) CloseCurrentNote();
                    NoteStore.DeleteForever(result.Id); refresh = true;
                    break;
                case "empty-trash":
                    RequireEmpty(values);
                    if (NoteStore.ListTrash().Any(n => n.Id == _currentId)) CloseCurrentNote();
                    result.Count = NoteStore.EmptyTrash(); refresh = true;
                    break;
            }
            if (refresh) RefreshList();
            json = KillerNotes.Cli.Program.SerializeJson(result);
            return true;
        }

        private static string RequireGroup(string path) => NoteStore.ListGroupTree().FirstOrDefault(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase)).Path ?? throw new InvalidOperationException("The group was not found");
        private static string ValidateTagName(string name) { ValidateText(name, "tag", 240); if (name.Contains(',') || name.Trim() != name) throw new ArgumentException("Tag names cannot contain commas or surrounding spaces"); return name; }
        private static long TakeNamedId(List<string> values, string name) => long.TryParse(Take(values, name, true), NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0 ? id : throw new ArgumentException("Invalid " + name);
    }
}
