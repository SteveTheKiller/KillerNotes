# KillerNotes

Notes that keep up. A searchable, organized replacement for the 80-tab Notepad workflow:
rich notes with inline images and tables, instant full-text search, and optional
password protection for the whole database.

Target: .NET Framework 4.8, x64, WPF. Builds on Windows (MSBuild/Visual Studio).

## Features

- Sidebar library: search-as-you-type across titles, note text, and tags (SQLite FTS5),
  sort by creation time or title (either direction) or a drag-and-drop custom order,
  new note, delete with themed confirm.
- Groups & custom order: drag notes up and down the sidebar to arrange them by hand (an
  accent line shows where they land; dragging in another sort switches to custom order
  automatically), and file notes into named collapsible groups - from the right-click
  Group submenu or by dropping a note onto a header. Deleting a group keeps its notes,
  and groups travel inside shared .kndb files.
- Tags: color-coded, per-database tag definitions (they travel inside shared .knote/.kndb
  files). Assign from the right-click Tags submenu or Ctrl+1-9, filter the list by clicking
  a pill on a card, and add / rename / recolor / delete in the Manage tags editor (F7).
  Renames and deletes ripple through every note; the notes.tags CSV is FTS-indexed, so tag
  search and the pill filter are free.
- Editor: WPF RichTextBox (FlowDocument). Bold / italic / underline / strikethrough /
  monospace / lists / horizontal rules, adjustable font size, and text + highlight color
  from a full picker (saturation-value square, hex fields, desktop-wide eyedropper, saved
  swatches). Insert real tables and paste images inline (screenshots land as part of the
  note). Per-note title color (right-click > Title color) and per-note spell check (format-bar
  toggle, Windows spell-checking engine, remembered per note) round it out.
- Autosave: 2 seconds after the last change, on note switch, and on close. Ctrl+S saves now.
- Markdown/HTML preview: when a note's text looks like markdown or HTML, a preview toggle
  appears in the format bar and opens a split pane (Markdig for markdown; HTML is rendered
  with scripts, event handlers, frames, and javascript: URLs stripped first).
- Storage: single database at `%APPDATA%\KillerNotes\notes.db` by default; the data folder
  is configurable (Manage databases > Change data folder), including next to the exe for a
  fully portable setup. Note bodies are stored as XamlPackage blobs (text + images + tables
  in one), plus extracted plain text for search.
- Password protection (lock button in the title bar): SQLCipher AES-256 encryption of the
  entire database file. No password = plain SQLite. Set / change / remove at any time
  (the file is rewritten through sqlcipher_export). There is no recovery for a lost
  password; the unlock screen's "New database" archives the locked file and starts fresh.
- Multiple databases: the Manage databases dialog (title-bar button) lists every .db in
  the data folder, creates/renames/deletes them, switches the active one, and moves the
  data folder itself (offering to bring the files along).
- Sharing: .knote (one note, optionally password protected) via right-click > Share note;
  .kndb (whole database, encryption included) via Manage databases > Export. Both are
  double-clickable on any machine with KillerNotes (HKCU associations register on launch).

## Dependencies

| Package | Why |
|---------|-----|
| Microsoft.Data.Sqlite.Core | ADO.NET SQLite wrapper (managed) |
| SQLitePCLRaw.bundle_e_sqlcipher | SQLCipher native build: SQLite + FTS5 + AES-256 |
| Markdig | Markdown to HTML for the preview pane (managed, MIT) |
| PolySharp | net48 polyfills for modern C# syntax (compile-time only) |

Run `dotnet list package --vulnerable --include-transitive` as part of every release checklist.
Single-exe packaging: Costura.Fody embeds every managed dependency and a self-extracting
bootstrap carries the native e_sqlcipher.dll, so the release ships as one signed exe.

## Layout

- `MainWindow.xaml` + partials: `Notes.cs` (list/search/save), `Editor.cs` (paste/tables),
  `Groups.cs` (custom order + note groups), `Security.cs` (password flow), plus the KillerUI
  kit files (`Chrome.cs`, `ThemeFlyout.cs`, `About.cs`, `Anim.cs`, `ConfirmDialog`,
  `PasswordDialog`, `InputDialog`).
- `Services/NoteStore.cs` - all SQL. `Services/ThemeManager.cs` - kit theme engine.
- `Themes/` + `Themes/Accents/` - the family palettes, copied from KillerScan.
