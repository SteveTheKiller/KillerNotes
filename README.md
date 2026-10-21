<p align="center">
  <a href="https://killernotes.net"><img src="docs/wordmark.png" height="180" alt="KillerNotes - Free Encrypted Notepad"></a>
</p>

Notes that keep up. A searchable, organized replacement for the 80-tab Notepad workflow:
rich notes with inline images and tables, instant full-text search, scheduled backups
with safe restore, and optional password protection for the whole database.

Target: .NET Framework 4.8, x64, WPF. Builds on Windows (MSBuild/Visual Studio).

See the [help page](https://killernotes.net/help.html) for workflows and shortcuts, and the [technical page](https://killernotes.net/technical.html) for storage, rendering, and security details.

## Features

- Rich text with images, tables, lists, checkboxes, spell check, custom fonts, title colors, and optional line numbers
- Search titles, text, and tags as you type; find and replace within a note or across notes
- Nested groups, colored tags, pinned notes, drag-and-drop ordering, templates, and daily notes
- Incoming and outgoing wikilinks, including links to notes that do not exist yet, plus a graph and heading outline
- Paragraph-level code highlighting and language-aware fenced Markdown code blocks
- Native Markdown and HTML preview with Source, Rendered, and adjustable Split views, following the active theme without a browser engine
- Autosave, saved cursor positions, version history, a 30-day trash, and scheduled backups that restore into a new database
- Multiple SQLite databases with optional SQLCipher AES-256 encryption; password-protected note (.knote) and database (.kndb) sharing
- Offline dictation (Ctrl+M) with downloadable speech models, waveform editing, inline audio, and WAV or MP3 export
- Editable inline drawings with SketchPad (F7), floating images and recordings, and the Killculator (F9)
- Keyboard access throughout, contextual menu hints, an F1 keyboard map, and whole-app accessibility scaling
- CLI support for search, editing, sharing, export, templates, groups, tags, recovery, and backups; run `KillerNotes --cli --help` for commands
- Twenty languages and thirteen themes; eight accents on Dark, Light, Black, and 98SE give 41 looks

## Requirements

- Windows 10 or 11 (x64)
- No runtime install. Everything needed is inside the EXE (targets .NET Framework 4.8, which ships with every supported Windows release).

## Download

WinGet:

```powershell
winget install killernotes
```

Chocolatey:

```powershell
choco install killernotes
```

- Prebuilt binary: <https://github.com/SteveTheKiller/KillerNotes/releases/latest/download/KillerNotes.exe>
- Source (GPL3 corresponding source for this release): <https://github.com/SteveTheKiller/KillerNotes/releases/latest>

## Screenshots

<table>
<tr>
<td width="50%"><img src="notes-landing/screenshots/01.png" alt="Ectoplasm theme with a quarterly review note and its heading outline open"><br><sub>A full client review with tables and headings, and the outline pane listing every section.</sub></td>
<td width="50%"><img src="notes-landing/screenshots/02.png" alt="Black theme with the note graph window over a networking index note"><br><sub>Linked notes and the graph of them, each note colored by its group.</sub></td>
</tr>
<tr>
<td><img src="notes-landing/screenshots/05.png" alt="Decay theme with SketchPad open beside a Wi-Fi survey report, sketching access point coverage"><br><sub>SketchPad open beside a Wi-Fi survey report, marking up access point coverage.</sub></td>
<td><img src="notes-landing/screenshots/06.png" alt="Malaise theme in Japanese with the Dictation window over weekly meeting notes"><br><sub>Dictation open over meeting notes, with the app running in Japanese.</sub></td>
</tr>
</table>

## Dependencies

| Package | Why |
|---------|-----|
| Microsoft.Data.Sqlite.Core | ADO.NET SQLite wrapper (managed) |
| SQLitePCLRaw.provider.e_sqlcipher | Managed P/Invoke shim to the SQLCipher native (static provider - the bundle's dynamic loader breaks under Costura) |
| SQLCipher + LibTomCrypt (vendored) | The encryption native itself, built from upstream source in `third_party/sqlcipher/` after the NuGet lib package line was deprecated |
| Markdig | Parses Markdown for native preview and rich text conversion (managed, MIT) |
| PolySharp | net48 polyfills for modern C# syntax (compile-time only) |
| libFLAC | Lossless storage for embedded recordings (native, BSD-3-Clause) |
| libmp3lame | MP3 export only, never storage (native, LGPL-2.1 - source shipped with every release) |
| whisper.cpp (+ ggml) | Offline speech recognition for dictation (native, MIT) |

The three audio natives are cross-compiled from their upstream release tarballs rather than
taken from a mirror; the tarballs, hashes and exact build commands are in
`third_party/audio/README.md`. Speech models are downloaded on demand from whisper.cpp's model
repository on Hugging Face, not bundled. All of it is optional at build time - with `third_party/audio/`
empty the app still builds and runs, storing recordings as WAV and transcribing with the
Windows engine.

Run `dotnet list package --vulnerable --include-transitive` as part of every release checklist.
Single-exe packaging: Costura.Fody embeds every managed dependency and a self-extracting
bootstrap carries the native e_sqlcipher.dll, so the release ships as one signed exe.
