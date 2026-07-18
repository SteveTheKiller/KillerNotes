# KillerNotes

Notes that keep up. A searchable, organized replacement for the 80-tab Notepad workflow:
rich notes with inline images and tables, instant full-text search, and optional
password protection for the whole database.

Built on the KillerUI "Grunge" shell (film grain, typewriter wordmark, 6 themes + accents).
Default look: **Black theme, Purple accent**.

Target: .NET Framework 4.8, x64, WPF. Builds on Windows (MSBuild/Visual Studio).

## Features (scaffold)

- Sidebar library: search-as-you-type across titles, note text, and tags (SQLite FTS5),
  sort by modified / created / title, new note, delete with themed confirm.
- Editor: WPF RichTextBox (FlowDocument). Bold / italic / underline / lists, insert
  real tables, paste images inline (screenshots land as part of the note).
- Autosave: 2 seconds after the last change, on note switch, and on close. Ctrl+S saves now.
- Markdown/HTML preview: when a note's text looks like markdown or HTML, a preview toggle
  appears in the format bar and opens a split pane (Markdig for markdown; HTML is rendered
  with scripts, event handlers, frames, and javascript: URLs stripped first).
- Storage: single database at `%APPDATA%\KillerNotes\notes.db`. Note bodies are stored
  as XamlPackage blobs (text + images + tables in one), plus extracted plain text for search.
- Password protection (lock button in the title bar): SQLCipher AES-256 encryption of the
  entire database file. No password = plain SQLite. Set / change / remove at any time
  (the file is rewritten through sqlcipher_export). There is no recovery for a lost
  password; the unlock screen's "New database" archives the locked file and starts fresh.
- Multiple databases: the Manage databases dialog (title-bar button) lists every .db in
  the data folder, creates/renames/deletes them, and switches the active one.
- Sharing: .knote (one note, optionally password protected) via right-click > Share note;
  .kndb (whole database, encryption included) via Manage databases > Export. Both are
  double-clickable on any machine with KillerNotes (HKCU associations register on launch).
- Family shell: custom chrome, theme + accent flyout, About overlay with update check,
  window placement persistence, 24px footer standard with resize grip.

## Dependencies

| Package | Why |
|---------|-----|
| Microsoft.Data.Sqlite.Core | ADO.NET SQLite wrapper (managed) |
| SQLitePCLRaw.bundle_e_sqlcipher | SQLCipher native build: SQLite + FTS5 + AES-256 |
| Markdig | Markdown to HTML for the preview pane (managed, MIT) |
| PolySharp | net48 polyfills for modern C# syntax (compile-time only) |

Run `dotnet list package --vulnerable --include-transitive` as part of every release checklist.
Single-exe packaging (Costura or ILRepack plus the native e_sqlcipher.dll) is a release-time
task, deliberately not wired into the scaffold.

## Layout

- `MainWindow.xaml` + partials: `Notes.cs` (list/search/save), `Editor.cs` (paste/tables),
  `Security.cs` (password flow), plus the KillerUI kit files (`Chrome.cs`, `ThemeFlyout.cs`,
  `About.cs`, `Anim.cs`, `ConfirmDialog`, `PasswordDialog`).
- `Services/NoteStore.cs` - all SQL. `Services/ThemeManager.cs` - kit theme engine.
- `Themes/` + `Themes/Accents/` - the family palettes, copied from KillerScan.
