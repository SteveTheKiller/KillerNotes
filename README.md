<p align="center">
  <a href="https://killernotes.net"><img src="docs/wordmark.png" width="640" alt="KillerNotes - Free Encrypted Notepad"></a>
</p>

Notes that keep up. A searchable, organized replacement for the 80-tab Notepad workflow:
rich notes with inline images and tables, instant full-text search, and optional
password protection for the whole database.

Target: .NET Framework 4.8, x64, WPF. Builds on Windows (MSBuild/Visual Studio).

## Features

- Rich text editor: bold, italic, lists, tables, rules, adjustable font size, a full color picker with a desktop-wide eyedropper, inline images, per-note title color and spell check, word wrap toggle, and an optional line-number column
- Search everywhere: search-as-you-type across titles, text, and tags (substring match, so `02` finds `A/002/45`), plus Ctrl+F find in the open note that never touches the note or its undo stack
- Organize: named, collapsible, colorable groups nested to any depth; color-coded tags with a pill filter; sort by time, title, or drag-and-drop custom order; three row densities; sidebar Ctrl+Z undoes group moves, reorders, and deletes
- Syntax highlighting detected paragraph by paragraph, so one note mixes prose with PowerShell, JSON, SQL, C#, Python and ten more without picking a language - token colors are never baked into the saved note
- Hyperlinks: Ctrl+K links the selection, typed URLs auto-link, links survive pasting from browsers and Word; only web and mail addresses ever open
- Dictation (Ctrl+M): record, transcribe entirely on your machine with a downloadable speech model, then print the text, embed the recording as an inline playable chip, or both - recordings store as lossless FLAC and export to WAV or MP3
- Recording editing: the waveform is the editing surface - slice, copy, delete, and paste segments with undo, then save back over the original
- SketchPad (F7): pen, shapes, fill, text labels and eraser on single keys; print the drawing inline and double-click it later to keep editing. Dragged images and recordings lift out of the text and snap to a grid with the paragraph wrapping around them
- Killculator (F9): a themed calculator under the notes list that prints the result or the whole running equation into the note
- Custom fonts for header, sidebar, and note text independently - any installed font, or drop a .ttf/.otf onto the card
- Autosave on pause, note switch, and close; notes reopen at their saved cursor and scroll position. Split-pane preview for markdown and (sanitized) HTML notes
- Storage: one SQLite database in a configurable location (portable next to the exe if you like), with create/rename/switch/relocate in the Manage databases dialog
- Password protection: optional SQLCipher AES-256 encryption of the whole database, set, changed, or removed at any time - no recovery for a lost password
- Sharing: export a note (.knote) or a whole database (.kndb), optionally password protected; both open with a double-click
- Keyboard-first: every function has a shortcut, F1 opens the visual keyboard map, and the whole app scales for accessibility (Ctrl+Shift +/-)
- Localized in ten languages, falling back to English
- Thirteen themes including a full 98SE recreation; Dark, Light, Black, and 98SE each carry six accent colors for 33 looks in all

## Requirements

- Windows 10 or 11 (x64)
- No runtime install. Everything needed is inside the EXE (targets .NET Framework 4.8, which ships with every supported Windows release).

## Download

WinGet:

```powershell
winget install killernotes
```

- Prebuilt binary: <https://github.com/SteveTheKiller/KillerNotes/releases/latest/download/KillerNotes.exe>
- Source (GPL3 corresponding source for this release): <https://github.com/SteveTheKiller/KillerNotes/releases/latest>

## Screenshots

<table>
<tr>
<td width="50%"><img src="docs/main-window.png" alt="Main window"><br><sub>Nested groups, tag pills and search as you type - beside a note whose syntax highlighting is detected per paragraph, thirteen languages in one note.</sub></td>
<td width="50%"><img src="docs/dictation.png" alt="Dictation"><br><sub>Dictation (Ctrl+M): record, scrub the waveform, transcribe offline, then embed the audio or print the text into the note.</sub></td>
</tr>
<tr>
<td><img src="docs/shortcuts.png" alt="Keyboard shortcuts"><br><sub>Every function has a shortcut - F1 opens the visual keyboard map. Hold Ctrl or Shift to preview that layer. Shown in French.</sub></td>
<td><img src="docs/localization.png" alt="Localization"><br><sub>Ten languages, translated down to the context menus - here Bengali, with a SketchPad drawing printed into the note.</sub></td>
</tr>
</table>

## Dependencies

| Package | Why |
|---------|-----|
| Microsoft.Data.Sqlite.Core | ADO.NET SQLite wrapper (managed) |
| SQLitePCLRaw.provider.e_sqlcipher | Managed P/Invoke shim to the SQLCipher native (static provider - the bundle's dynamic loader breaks under Costura) |
| SQLCipher + LibTomCrypt (vendored) | The encryption native itself, built from upstream source in `third_party/sqlcipher/` after the NuGet lib package line was deprecated |
| Markdig | Markdown to HTML for the preview pane (managed, MIT) |
| PolySharp | net48 polyfills for modern C# syntax (compile-time only) |
| libFLAC | Lossless storage for embedded recordings (native, BSD-3-Clause) |
| libmp3lame | MP3 export only, never storage (native, LGPL-2.1 - source shipped with every release) |
| whisper.cpp (+ ggml) | Offline speech recognition for dictation (native, MIT) |

The three audio natives are cross-compiled from their upstream release tarballs rather than
taken from a mirror; the tarballs, hashes and exact build commands are in
`third_party/audio/README.md`. Speech models are downloaded on demand from whisper.cpp's own
repository, not bundled. All of it is optional at build time - with `third_party/audio/`
empty the app still builds and runs, storing recordings as WAV and transcribing with the
Windows engine.

Run `dotnet list package --vulnerable --include-transitive` as part of every release checklist.
Single-exe packaging: Costura.Fody embeds every managed dependency and a self-extracting
bootstrap carries the native e_sqlcipher.dll, so the release ships as one signed exe.
