// ═══════════════════════════════════════════════════════════
//  TEMPLATES  -  a group whose notes seed new ones
// ═══════════════════════════════════════════════════════════
//
// No template editor and no template file format. A template is a note, and the templates
// group is an ordinary group the user marks from its header menu; from then on every note in
// it is offered under "New note from template" on the sidebar's right-click menu. Moving a
// note into the group makes it a template, moving it out un-makes it, and editing it edits
// the template, all with tools that already exist.
//
// Making a note from one copies the content, tags, title color, sketches and the two per-note
// toggles, and fills the {date}-style placeholders (Services/TemplateText.cs) in the title and
// body on the way. The copy is a plain new note: nothing ties it back to the template.
//
// The chosen group is remembered per database ("file|path", the LastNote pattern), because a
// group name only means something inside the database that holds it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private const string TemplatesGroupSetting = "TemplatesGroup";

        // ---- Per-database settings ----
        // Stored as "file|value" so a value from one database never applies to another. Demo
        // sessions keep theirs in memory: they must never write real settings (OpenNote rule).

        private static readonly Dictionary<string, string> DemoScopedSettings = new(StringComparer.OrdinalIgnoreCase);

        private static string? DbScopedSetting(string name)
        {
            string? raw = NoteStore.DemoDbFile != null
                ? (DemoScopedSettings.TryGetValue(name, out var v) ? v : null)
                : App.GetSetting(name);
            if (raw == null) return null;
            // A Windows file name cannot contain '|', so the first one is the seam whatever
            // the value holds.
            int sep = raw.IndexOf('|');
            if (sep <= 0) return null;
            return string.Equals(raw[..sep], NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase)
                ? raw[(sep + 1)..]
                : null;
        }

        private static void SetDbScopedSetting(string name, string? value)
        {
            string? raw = value == null ? null : $"{NoteStore.ActiveDbFile}|{value}";
            if (NoteStore.DemoDbFile != null)
            {
                if (raw == null) DemoScopedSettings.Remove(name);
                else DemoScopedSettings[name] = raw;
                return;
            }
            if (raw == null) App.RemoveSetting(name);
            else App.SetSetting(name, raw);
        }

        // ---- The templates group ----

        private static string? TemplatesGroupPath() => DbScopedSetting(TemplatesGroupSetting);

        private static bool IsTemplatesGroup(string path) =>
            string.Equals(TemplatesGroupPath(), path, StringComparison.OrdinalIgnoreCase);

        // Group header menu toggle (Tag="templates"; GroupHeader_RightDown sets its check).
        private void TemplatesGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            bool on = IsTemplatesGroup(g.Path);
            SetDbScopedSetting(TemplatesGroupSetting, on ? null : g.Path);
            FlashStatus(on ? Loc("Str_St_TemplatesOff") : string.Format(Loc("Str_St_TemplatesOn"), g.Name));
        }

        /// <summary>The notes in the templates group, by title. Read from the store rather than
        /// the sidebar, so a search filter never hides a template from the menu.</summary>
        private static List<Note> TemplateNotes()
        {
            string? path = TemplatesGroupPath();
            if (path == null) return [];
            return NoteStore.List(null, "title-asc")
                .Where(n => string.Equals(n.Notebook, path, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // ---- The submenu (NotesContextMenu_Opened, Tags.cs) and the Alt+T flyout ----

        private void BuildTemplateMenu() => FillTemplateMenu(TemplateMenu);

        /// <summary>Alt+T (Shortcuts.cs): the same rows as the submenu, as a flyout anchored to
        /// the New note button - the control that makes notes, so the menu opens where the
        /// action lives rather than at a fixed spot.</summary>
        private void TemplateShortcut()
        {
            if (!NoteStore.IsOpen) return;
            var menu = new ContextMenu { PlacementTarget = NewNoteBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            FillTemplateMenu(menu);
            menu.IsOpen = true;
        }

        private void FillTemplateMenu(ItemsControl menu)
        {
            menu.Items.Clear();
            var templates = TemplateNotes();
            if (templates.Count == 0)
            {
                // One disabled row saying why, so an empty submenu never reads as broken.
                menu.Items.Add(new MenuItem
                {
                    Header = BuildMenuRow(null, null,
                        Loc(TemplatesGroupPath() == null ? "Str_Ctx_NoTemplatesGroup" : "Str_Ctx_NoTemplates"), null),
                    IsEnabled = false,
                    Padding = new Thickness(6, 5, 14, 5),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                });
                return;
            }
            foreach (var t in templates)
            {
                var item = new MenuItem
                {
                    Header = BuildMenuRow(null, null, t.Title, null),   // Tags.cs (shared row layout)
                    Padding = new Thickness(6, 5, 14, 5),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                long id = t.Id;
                string title = t.Title;
                item.Click += (_, _) =>
                {
                    if (CreateFromTemplate(id) < 0) return;
                    FlashStatus(string.Format(Loc("Str_St_FromTemplate"), title));
                    TitleBox.Focus();   // the copied title is a starting point; typing replaces it
                    TitleBox.SelectAll();
                };
                menu.Items.Add(item);
            }
        }

        // ---- Making the note ----

        /// <summary>Creates a new note from a template. The title is the template's own with
        /// placeholders filled, unless one is given; the group is the caller's (null = loose).
        /// Returns the new id, or -1 when nothing was made. With open, the note is shown.</summary>
        private long CreateFromTemplate(long templateId, string? title = null, string? group = null, bool open = true)
        {
            if (!NoteStore.IsOpen) return -1;
            if (NoteStore.IsReadOnly)
            {
                FlashStatus(string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner));
                return -1;
            }
            var tpl = NoteStore.List().FirstOrDefault(n => n.Id == templateId);
            if (tpl == null) return -1;

            SaveCurrentNote(refreshList: false);   // the template itself may be the open note
            var now = DateTime.Now;
            string newTitle = title ?? TemplateText.Expand(tpl.Title, now);
            long id = NoteStore.Create(newTitle, tpl.Format);
            if (id < 0) return -1;

            byte[]? blob = NoteStore.LoadContent(templateId);
            byte[] content;
            string plain;
            if (tpl.IsMarkdown)
            {
                plain = TemplateText.Expand(MarkdownBlob.Decode(blob), now);
                content = MarkdownBlob.Encode(plain);
            }
            else
            {
                var doc = new FlowDocument();
                if (blob != null)
                {
                    using var ms = new MemoryStream(blob);
                    try { new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage); }
                    catch { doc.Blocks.Clear(); }   // an unreadable template still yields an empty note
                }
                TemplateText.ExpandDocument(doc, now);
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                using var outMs = new MemoryStream();
                range.Save(outMs, DataFormats.XamlPackage);
                content = outMs.ToArray();
                plain = range.Text;
            }
            NoteStore.Save(id, newTitle, content, plain);
            NoteStore.SetLinks(id, WikiLinks.Parse(plain));   // a template's [[links]] are links in the copy too
            if (tpl.Tags.Length > 0) NoteStore.SetNoteTags(id, tpl.Tags);
            if (tpl.TitleColor.Length > 0) NoteStore.SetTitleColor(id, tpl.TitleColor);
            if (tpl.SyntaxHighlight) NoteStore.SetSyntaxHighlight(id, true);
            if (tpl.SpellCheck) NoteStore.SetSpellCheck(id, true);
            var sketches = NoteStore.LoadSketches(templateId);
            if (sketches.Count > 0) NoteStore.SaveSketches(id, sketches);
            if (group != null) NoteStore.SetNoteGroup(id, group);

            if (open)
            {
                SearchBox.Text = "";   // a filtered list would hide the new note
                RefreshList();
                OpenNote(id);
                SelectNoteInList(id);   // WikiLinkNav.cs
            }
            return id;
        }
    }
}
