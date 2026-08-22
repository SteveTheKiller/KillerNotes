# Changelog

All notable changes to KillerNotes are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - Unreleased

### Added
- Hungarian and Polish localization for the complete app interface and killernotes.net, the eleventh and twelfth languages. Every one of the twelve now carries all 587 interface keys, so search and replace, the network-lock warnings, markdown notes and the markdown export read in your own language rather than falling back to English.
- A network data folder now warns once, and a lock file beside the database guards concurrent use: a database in use on another computer opens read-only instead of competing for SQLite's single writer, with the owning machine named. A clean close clears the lock.
- Text labels inside sketches now feed the search index, so a label like "IDF-2, port 14" makes its note findable.
- Search and replace (#14): Ctrl+H adds a replace row to the find bar with match-case, whole-word and regex options; Replace All in a note is a single undo step. The sidebar gains replace-across-all-notes behind a confirmation naming each note and its match count, committed in one transaction and undone with one Ctrl+Z.
- Notes can now be markdown instead of rich text. A markdown note stores its source text, edits as plain text with markdown syntax highlighting, and converts to and from rich text from the note menu; converting to markdown lists what will not survive before it runs. Images, tables, sketches and embedded recordings are refused in a markdown note rather than being dropped at the next save.
- Export all notes as markdown writes the whole database to a folder of .md files, groups as subfolders and tags in YAML front matter. Rich-text notes convert on the way out and nothing in the database changes.

### Changed
- The SQLCipher encryption native is now built from upstream source (SQLCipher 4.18.0, SQLite 3.53.4) and vendored in the repo, replacing the deprecated SQLitePCLRaw.lib.e_sqlcipher package.
- Internal cancellation resource keys now use the same American spelling as their displayed text.
- Markdown notes now store their source inside a XamlPackage rather than as raw bytes, so a database or a shared note carrying one can still be opened by releases older than 1.3.0. Notes written by earlier 1.3.0 builds are converted the first time the database is opened, so a database does not stay a hazard to older builds just because nobody edited the note.

### Fixed
- A note whose stored content will not load no longer takes the app down at startup. It is left unopened with the reason in the status bar, and saving is blocked so the content on disk is not overwritten.
- The Killculator's Print Equation keeps the whole running calculation when you continue from a result with an operator, instead of restarting the tape at that result.
- Dragging a note while sorted by time or alphabet now really does keep what is on screen (#4). The drop was landing against the stored order rather than the visible one, reshuffling the sidebar. The sort button still leaves a saved arrangement untouched.
- Theme names in the picker are now translated in all twelve languages instead of always showing their English names. KillerNotes was the only app in the family missing the shared theme-name strings.
- A failed unlock no longer leaves the database reporting itself as open; a wrong password now cleanly returns to the prompt.
- Find-in-note now has localized match counts, button tooltips and shortcut labels in Bengali, Czech, German, Spanish, French, Japanese, Turkish, Simplified Chinese and Traditional Chinese; the technical page now documents how Ctrl+F, F3, Shift+F3 and Esc work without altering the note.
- Cut no longer sometimes leaves the text in place: the selection is only deleted after the clipboard write has succeeded, and the write retries while another app briefly holds the clipboard (#16, thanks MrPapaya-JRR).
- Machine-wide uninstall now requests administrator access and removes the Program Files copy, Common Start Menu shortcut and HKLM registration instead of silently reporting success after permission failures.
- KillerNotes now detects when both a per-user and an all-users installation exist and offers to remove the copy that is not running, and self-update keeps the Add/Remove Programs version current instead of leaving it describing the replaced build.
- All-users installs now register the .kndb and .knote associations in HKLM for every account, with Default Apps entries, instead of only registering per-user at launch; each uninstall removes its own scope's registrations, and a per-user copy replaced by an all-users install takes its HKCU registrations with it.
- Keyboard Shortcuts is now bounded by the app window and scrolls internally. Its generated list rows receive wheel input through the overlay's own scroll host, and the two list columns flex with the available width instead of forcing the card beyond a narrow window.
- About and Keyboard Shortcuts now share KillerScan's card padding and compact overlay close control. The inset X stays transparent and only its glyph turns red on hover, replacing the filled rounded caption button that had drifted into the About card.
- Themes are now complete, app-owned resources with no private template overlay or external build dependency.
- The About card now takes its outer edge from the app-frame color and its information panel directly from the context-menu surface, with the pane-border color around that panel. The old About-only color override is gone, so 98SE and the material themes no longer substitute a different panel color.
- Ectoplasm now uses its signature yellow with near-black text for selected rows instead of a muted, muddy selection fill.

## [1.2.1] - 2026-08-10

1.2.1 adds find within the open note, on Ctrl+F, and fixes the themes around it: legible text on filled accent buttons, dark card borders and a real drop shadow on the Black theme, and context menus that cast a shadow again.

### Added
- Find in the open note, on Ctrl+F. A small card fades in over the note with the match count, next and previous, and wrap at either end. Drag it by the grip on its left to put it wherever it suits you and it opens there next time, staying inside the note whatever the window is doing; F3 and Shift+F3 step while it is open, Enter and Shift+Enter do the same from the box itself, and Esc closes it. Selected text seeds the box, so Ctrl+F on a word searches for it straight away. Matches are drawn over the text rather than into it, so nothing about finding touches the note, the undo stack or autosave. The sidebar box is unchanged and still searches across every note; F3 still goes there when the find bar is closed.

### Fixed
- Context menus cast a drop shadow again. The shadow layer was still in place but its blur and depth had drifted to about a quarter of the size every other flyout in the app uses, so on a light background there was nothing to see and the menu sat flat on the page.
- The Black theme draws its card borders dark instead of ringing every card and dialog in a mid-gray box. Black is built so that things read by their background rather than by bright edges, and a mid-gray hairline on a near-black surface was the one edge in the app fighting that.
- Selected text is highlighted in the accent color you actually chose. Every theme carries a default accent, and the selection color was being copied from it before your choice was applied on top - so on Black the selection stayed the theme's terminal green whether you had picked orange, purple, red or blue, and the same in every other theme. It looked random; it was each theme's own default, frozen a moment too early. The Windows 98 theme keeps its period navy selection, which it states outright rather than inheriting.
- Various UI and theme consistency tweaks, including legible accent buttons and matching selection treatments across the theme families.
- Line numbers use a monospace font, so the gutter no longer shifts sideways as you scroll between narrow and wide numbers.

## [1.2.0] - 2026-08-08

1.2.0 adds dictation with offline speech recognition, code-aware notes with syntax highlighting, freely placed images and recordings, and seven new themes, alongside big-note performance fixes (#16), a markdown detection fix, interface refinements, and an installer fix.

### Added
- Dictation (Ctrl+M, or the microphone on the sidebar rail): record, transcribe, then print the text at the caret or embed the recording as a playable inline chip. Everything runs locally; nothing is uploaded.
- Offline speech recognition via whisper.cpp, with three downloadable model sizes. Right-click the rail microphone to change models; without one, transcription falls back to the Windows engine.
- Recording editing: right-click an embedded recording > Edit to slice the waveform and copy, delete, or paste segments, with undo. Saving replaces the original in place.
- Recording sharing: save an embedded recording as a file (FLAC, WAV, or MP3) or copy it as a file to paste elsewhere.
- Recordings are stored as FLAC, roughly half the size, losslessly. Existing WAV recordings keep working.
- Images and recordings can be placed anywhere in a note: drag one out of the text and the paragraph wraps around it. Dropping onto a table cell places it in that cell.
- Optional per-note syntax highlighting (note right-click menu). Language is detected per paragraph, so one note can mix prose with PowerShell, HTML, XAML, XML, Vue, JSON, YAML, Markdown, SQL, C#, Python, JavaScript, TypeScript, CSS, and Bash.
- Seven new themes: 98SE, Ectoplasm, Decay, Mourning, Sepulchre, Delirium and Malaise.
- The file picker shows image thumbnails, and inserting a picture opens with a preview pane.

### Changed
- Line numbers count logical lines: a wrapped paragraph carries one number, and a table numbers as one line.
- The SketchPad and dictation pads are independent windows: the main window can be brought in front of them, and their rail icons light while open and close them when clicked again. The Killculator icon lights the same way.
- Warmer accents for Cyanotic, Greed, Blood and Decay: buttons, icons, selection washes, and highlights use each theme's tan accent instead of white.
- Interface brought in line with the KillerUI family standard: 24px icon rail, shared resize grabber, overflow fades, pane-anchored theme and locale flyouts, normalized menus, themed radio buttons, draggable table walls, and a font-size slider. Editor options (syntax highlighting, word wrap, spell check, preview) moved to the note right-click menu.
- Every window draws its title bar, caption close button, dialog card, and edit fields from one shared definition, themed for all thirteen themes. The separate folder picker dialog is gone; folders use the same picker as files.
- Spell check declines notes over 50,000 characters instead of hanging the app.
- The app-size readout clears after five seconds, matching KillerScan and KillerPDF.
- Internal: the codebase follows the family `Shell/` / `Features/` / `Services/` / `Models/` / `Controls/` layout, oversized files were split, and the F1 shortcut list and keyboard map generate from one shared table. No behavior changes.

### Fixed
- Large notes no longer freeze the app when line numbers are on; the gutter only measures the lines actually on screen (#16, thanks MrPapaya-JRR).
- Position memory now restores correctly on large notes instead of stopping about a quarter of the way in (#16, thanks MrPapaya-JRR).
- Pasting a very large script into a highlighted note no longer crashes or stalls the app; highlighting paints the visible area first and keeps up with scrolling, as do the line numbers.
- Code lines without language tells of their own (a bare `}`, `base.OnStartup(e);`) now color with the block above them; PowerShell detection covers far more cmdlets, C# detection recognizes method signatures, and Markdown highlights whole bold/italic/strikethrough spans and list markers.
- Switching themes no longer bakes the previous theme's text color into a highlighted note (white-on-white on 98SE), and the waveform recolors on a live theme change, in two shades of the accent.
- The color picker's OK now applies the picked color everywhere it is used (tags, groups, note titles, text, pen color).
- The main window no longer drops behind other applications after closing the SketchPad's color picker.
- The SketchPad can no longer print a drawing into the wrong note; a pad session belongs to the note it was opened from.
- Resizing an image can no longer trap it small: the no-upscale cap reads the bitmap's pixel size, not its DPI-scaled size.
- Switching notes no longer carries a ghost selection into the newly opened note.
- The editor's right-click menu matches what was right-clicked, and linking an image reports it cannot be linked instead of crashing.
- Text and images dropped onto a table no longer disappear; they anchor just above it.
- Plain-text notes with dividers are no longer mistaken for markdown, and the markdown toggle is one checkable item on the note menu (#14, thanks MrPapaya-JRR).
- An image that fails to load reports the problem instead of showing an internal key name.
- A machine-wide install no longer shows the PORTABLE badge or offers a second per-user install. KillerPDF, KillerScan, KillerShell and Killendar already had this fix.

## [1.1.6] - 2026-07-24

### Fixed
- A new note's title now appears in the sidebar right away instead of staying "Untitled" until the app is restarted (#13, thanks Dantex). The sidebar row's title, snippet, and date are notifying properties now, so the in-place update on save repaints the row immediately. Previously the 2s autosave's in-place update also matched the freshly rebuilt list, so the reconcile saw no change and never regenerated the row, and the non-notifying title binding held the stale "Untitled" until a full reload.

## [1.1.5] - 2026-07-24

### Added
- SketchPad, a built-in drawing pad for notes (F7, Ctrl+Shift+D, or the pencil on the sidebar rail). It opens as a modeless window that stays up beside the note while you switch back and forth, with a pen, straight line, arrow, rectangle, ellipse, polygon, paint bucket, text labels, and an eraser, plus a color palette with a custom-color picker, stroke width, fill, and fill opacity, and full undo/redo. Every tool has a single-key shortcut (V select, P pen, L line, A arrow, R rectangle, O ellipse, G polygon, B bucket, T text, E eraser, I add image), and the eraser draws a ring the size of the area it will clear so you can see its reach. Print to note (Ctrl+Enter) flattens the drawing to an image stamped inline at the caret without closing the pad, and Copy to clipboard puts that same image on the clipboard for any other app. A printed sketch stays editable: double-click it to reopen the exact drawing, and Print replaces it where it sits. The canvas is the window, so dragging the corner grip grows the drawing area 1:1, and a reopened sketch returns at the size it was made. The whole pad tracks the app theme live, including a theme change made while it is open.
- Getting a picture into a note is easier: a picture button on the format bar opens a file picker and inserts the image at the caret, next to the paste and drag-and-drop that already worked. And double-clicking any image in a note - not just a printed sketch - now opens it in the SketchPad to draw on, with Print stamping the marked-up version back over the original in place.
- Czech (cs-CZ) is now a full interface language, bringing the count to ten and matching KillerPDF's set. Pick it from the language menu in the title bar; the whole UI - menus, dialogs, tooltips, status messages, and the shortcuts overlay - is translated, with English as the automatic fallback for any key a locale leaves out.
- Right-click any of the sidebar sort buttons for a menu that names each sort next to its glyph - by creation time, alphabetical, and custom order - so you can pick one without decoding the icon strip. The active sort is accent-colored and shows its direction arrow, and a Reverse order item flips the current direction (hidden for custom order, which has none). Picking a sort mode does exactly what clicking its button does, including reversing direction when it is already active.
- The Killculator can print the whole equation, not just the total. The single Print button is now two: Sum (Ctrl+Enter) drops the readout as before, and Equation (Ctrl+Shift+Enter) drops the running equation - "12 + 5 = 17 × 3 = 51" - which spells out each step's intermediate result so the pad's strictly left-to-right math (no operator precedence) reads without ambiguity. Suggested on #8.

### Changed
- Right-click menus now carry an icon in the left gutter beside each action - cut/copy/paste, share, export, tags, group, colors, rename, delete, and the rest - the same treatment as the KillerPDF context menus. The toggle rows (tag and group assignments) keep their existing check-and-swatch layout.

### Fixed
- Right-clicking the sidebar search box popped the stock Windows cut/copy/paste menu, bright and unthemed against the dark UI. A text box's built-in editing menu ignores the app's implicit menu styles, so it always renders unthemed; the search box now carries its own themed cut/copy/paste menu with icons, matching the note title box.
- With word wrap off, the editor's horizontal scrollbar ran backwards - scrolled fully to the left, the thumb sat on the right. The themed scrollbar template shares one Track between both orientations with `IsDirectionReversed` set for the vertical bar (value 0 = top), and the horizontal bar inherited it, so offset 0 put the thumb at the far end. The horizontal bar now turns that off.
- The little square where the horizontal and vertical scrollbars meet showed as a bright white block on the dark theme. It was the stock WPF ScrollViewer corner, which fills with the system control color; scoped to the editor, that corner is now transparent so the note pane shows through instead.
- The editor's scrollbars sat inset from the note pane, floating in from the edge. The editor's 8px inset moved from its outer margin to inner padding, so the bars now hug the rounded pane's edge - the same "scrollbar flush inside the pane" treatment the website uses - while the text keeps its breathing room.

## [1.1.4] - 2026-07-22

### Added
- Click the note title to jump back to the top of the note (thanks Dantex). The click still edits the title exactly as before; only the view moves.
- Collapsed groups now lay their line flat: the colored spine that runs down an open group's notes turns into a short horizontal dash when the group is closed - open runs down, closed lies flat, so a tree of collapsed subgroups reads like a dashed outline. Expanded subgroup spines also got a trim: the line now starts just above its own group's name instead of stretching up toward the parent's.

### Fixed
- The sidebar could keep showing a note's old title and snippet after an edit if you switched notes or apps within a couple of seconds of typing. The note itself was saved correctly; only the list row went stale. Latent since the 1.1.0 sidebar rework - the scroll-preserving list keeps its existing row objects, and the quick-save path was updating a different copy of the row than the one on screen.
- Undoing a note delete now restores the reading position too. The row snapshot Ctrl+Z restores carried everything except the remembered caret and scroll, so an undone note reopened at the top instead of where you left off.

## [1.1.3] - 2026-07-22

### Added
- Hyperlinks. Notes can finally hold real links: Ctrl+Click opens them in the browser, Ctrl+K (or the right-click menu) links the selected text or edits the link under the caret - clearing the address removes it. Links pasted from CherryTree, browsers, and Word keep working instead of arriving as dead text, typing a URL followed by a space links it automatically, and the HTML export writes real anchors. Links are colored by the theme accent and only http, https, and mailto ever open - a shared note can't hand your shell anything else.
- Tilt-wheel horizontal scrolling (#9). WPF never delivers the horizontal wheel on its own, so the window now catches WM_MOUSEHWHEEL directly and scrolls whatever is under the mouse - with word wrap off, wide tables and images pan without touching the scrollbar. Shift+wheel does the same for mice without a tilt wheel; Ctrl+wheel stays zoom.

### Fixed
- The sidebar toolbar squeezes gracefully instead of jumping straight to two rows. The New note button now steps its label down as space runs out - "+ New note", "+ New", then a bare "+" (translated at every step) - and the sort buttons only drop underneath when even the "+" cannot share the row, snapping back the moment there is room. In 1.1.2 the sorts ducked below immediately while the top row sat half empty.
- The sidebar no longer jumps back to an old width when switching languages (or changing the app zoom) after a splitter resize. Splitter drags were never recorded, so the next internal width refresh re-asserted the stale remembered value.

## [1.1.2] - 2026-07-22

### Added
- Custom fonts. A Fonts dialog (theme flyout, "Fonts...") swaps three slots independently: Headers (group titles and the Killculator title), Sidebar (note titles in the list), and Note text (the editor default). Pick from any installed font or drop a .ttf/.otf file straight onto the card - it is copied into your data folder so it survives moves. A readability guard keeps symbol fonts like Wingdings out, changes apply live, and one click resets all three slots to the killer defaults.
- Note text now defaults to Bahnschrift, the DIN engineering face that ships with Windows 10/11 - more character next to the typewriter headers, with Segoe UI as the automatic fallback. The sidebar keeps Segoe UI for maximum legibility at its small row sizes; both are just defaults for the Fonts dialog and swap like anything else.

### Fixed
- The icon rail no longer clips its buttons at larger app sizes. The rail column now widens with the app zoom so its icons scale up with everything else - bigger zoom, bigger click targets - instead of overflowing a fixed-width strip and getting cut off. The strip is also trimmed to hug its icons, so high zoom grows the buttons instead of blank space next to them.
- The sidebar toolbar no longer clips at high app zoom. The sidebar keeps its on-screen width while the UI scales, which squeezes its usable width - the sort buttons now drop to a second row under the New note button when one row stops fitting, and snap back when there is room again.
- "Subgroups on top" in the group right-click menu now shows a check mark when it is on. The themed menu template never rendered a check glyph for checkable items, so the toggle gave no visual feedback.
- The F1 keyboard map now covers every shortcut that works in the editor, including the built-ins that were never listed: Ctrl+Home/End (top / end of note), Ctrl+Left/Right (word jump), Ctrl+Backspace/Delete (delete word), Ctrl+L/E/R/J (paragraph alignment), Ctrl+]/[ (text size), and the Menu key. The shortcut list view gained the same entries, translated into all nine languages.
- Editing-category keys on the keyboard map (Ctrl+X/C/V/Z/Y/A and the new built-ins) now light up in their own color. The map referenced a KnCatEdit brush that no theme defined, so those keys drew without a category color.
- Demo mode no longer duplicates its notes when two demo windows run at once. A second instance could not delete the locked scratch database, then seeded a full extra copy into it on every launch; a stale database is now reused as-is, and the fresh-roll cleanup also removes SQLite's -wal/-shm sidecars.

## [1.1.1] - 2026-07-22

### Added
- Notes remember where you left off: switching back to a note restores the cursor position and scroll instead of starting at the top, so a long running note (a log, a journal) reopens right at the spot you were working. The position survives restarts - it is stored per note in the database.

### Changed
- Subgroups now sit at the top of their parent group, above the group's own notes, so nested structure stays visible without scrolling past the notes. A "Subgroups on top" toggle on the group right-click menu restores the old notes-first order, and the group's colored line stays continuous in either arrangement.
- The collapsed group's short colored pill now centers itself on the header text instead of stretching the row, so it lines up with the larger group title font at every density.

### Fixed
- Creating a subgroup no longer snaps the sidebar back to the top - the list holds its scroll position, since the new child appears right where you right-clicked. The same fix covers renaming, coloring, and deleting a group.
- The Killculator no longer swallows the keyboard for the whole app while open: focus decides who gets the keys. Opening the pad (or clicking anywhere on it) points the number and operator keys at the calculator; clicking into the note hands typing back to the editor with the pad still open. This also stops Backspace and digits from vanishing into the calculator while writing a note with the pad up.

## [1.1.0] - 2026-07-22

### Added
- Word wrap toggle (#9): a new button on the format bar, and Ctrl+Shift+W, turns word wrap on or off for the editor. With wrap on (the default) text flows to the pane as before. With wrap off, long lines and anything wider than the pane - a large pasted image, a wide table - stop wrapping and can be reached with the editor's new horizontal scrollbar. The choice is remembered across launches.
- App-wide size control for accessibility: scroll the mouse wheel over the KillerNotes logo in the title bar to scale the app content - sidebar, toolbar, and editor - up or down in fine steps, from 70% to 250% (or with Ctrl+Shift and the +/- keys, and Ctrl+Shift+0 to reset); the size is remembered across launches. It uses a layout scale so text stays sharp rather than blurring, and it is separate from the per-note Ctrl+wheel editor zoom, which still scales only the note body. The title bar and footer stay a fixed size, so the logo you scroll over never moves, and the sidebar holds its on-screen width while its text grows.
- Optional line-number column: a toggle in the icon rail (or F11) shows line numbers down the left edge of the editor, like a code editor, so you can count lines at a glance. The choice is remembered across launches.
- Subgroups: groups now nest to any depth. Right-click a group header and choose New subgroup (or Ctrl+Shift+G) to add a child inside it. The sidebar indents each level, and a parent's colored line runs down the left of its child subgroups so the nesting reads as one contained tree. Collapsing a group hides its whole subtree. Renaming a group carries its subgroups and their notes along, and deleting a group removes its subgroups too (the notes are kept and just leave the group). Notes can sit at any level, and the right-click Group submenu lists every group by its full path (Parent / Child).
- Keyboard shortcuts for group actions on the selected note's group: Ctrl+Shift+G adds a subgroup and Ctrl+Shift+K opens the group color picker. Both are listed in the F1 shortcut overlay and shown, right-aligned, on the group header's right-click menu.
- Drag groups to reorder or re-nest them: press and drag a group header to move it. Drop it on the top or bottom edge of another group to reorder it among that group's siblings, drop it on the middle of a group (or onto one of that group's notes) to nest it inside, and drop it in the empty space below the list to lift it back to the top level. A plain click still toggles collapse.
- Killculator: a themed calculator that slides up from the sidebar footer - press F9, or click the calculator icon in the icon rail. It sits in a row below the notes list so the list stays visible and scrollable above it. Basic four-function math with percent, sign flip, and backspace; while it is open the number and operator keys drive it, so you can type an equation. Its Print key (or Ctrl+Enter) drops the current result into the open note at the cursor, for jotting a total without copy and paste. Opening it while the sidebar is collapsed pops the sidebar out to show it and tucks it back when you close it.
- Sidebar row density: a control in the icon rail (or Ctrl+D) cycles the note list through three densities - full (title, snippet, date, tags), compact (title and tags), and minimal (title only) - so you can fit more notes on screen. The compact modes tighten the group headers too. Scroll the wheel over the icon to step through them, and the choice is remembered across launches.
- Keyboard shortcuts for everything: new group (Ctrl+G), cycle the sidebar sort (F10), title color (Ctrl+Shift+C), spell check (Ctrl+Shift+P), insert table (Ctrl+Shift+T), the theme flyout (Ctrl+T), sidebar density (Ctrl+D), and word wrap (Ctrl+Shift+W). Every function now has a key, and all of them are listed in the F1 overlay and on the visual keyboard map.
- Undo (Ctrl+Z) now reaches sidebar actions, not just editor text: moving a group, changing a group or note color, adding or removing a tag, filing a note into or out of a group, reordering a note, and deleting a note (one or a whole multi-selection). Ctrl+Z reverts the most recent action; a sidebar action like a group drag is undone even while the editor holds focus, and the moment you type in the editor Ctrl+Z goes back to undoing text. The delete-note confirmation now says it can be undone.

### Changed
- Convert to list now also handles a single sentence: highlight a run of words on one line and Convert to list (Ctrl+Shift+J) splits it on spaces, commas, and semicolons into PC1,PC2,PC3, instead of leaving the line as one item. Selections spanning multiple lines still split by line as before.
- Picking a group's color now previews live: right-click a group header, choose Group color, and the group's name and the connector line down its notes recolor as you drag in the color picker, so you see the result before committing. Cancel restores the previous color.
- The sidebar now slides open and closed when you collapse or expand it with the chevron, instead of snapping instantly.
- Group titles now use the KillerNotes typewriter wordmark font at a larger size, and sit closer to the notes beneath them.
- The right-click color option on a nested group now reads "Subgroup color..." rather than "Group color...", so it is clear which level you are coloring.
- Choosing a note's title color now previews live in the sidebar as you drag in the color picker, the same way group color already did, and a selected note keeps its title color instead of being forced to white.
- Each note in the sidebar shows its full title as a tooltip on hover, so a long title that is trimmed in the list is still readable.
- With word wrap on, an image can no longer be dragged wider than the editor pane, where it would be clipped with no way to scroll to it; turn word wrap off to size it up to full resolution and reach it with the horizontal scrollbar.

## [1.0.3] - 2026-07-20

### Added
- Convert to list: select lines (or a table column) in a note and Convert to list - right-click, or Ctrl+Shift+J - turns them into a comma-separated list like PC1,PC2,PC3 for pasting into scripts. Blank lines are dropped and each value is trimmed. A plain-text selection is rewritten in place; a table-cell selection is copied to the clipboard instead, since cells can't be replaced with a single value.
- Color a group's name in the sidebar: right-click a group header > Group color... (or Reset color), the same picker used for per-note title colors. The color is stored per database, travels inside shared .kndb files, and stays with the group when it is renamed.
- Grouped notes now carry the group's color as a connector line down the left edge of the sidebar, so a group reads as one set at a glance and a colored group stands out. The line runs from the group header down through its notes and caps cleanly at the top and bottom; a collapsed group shows a short colored pill that grows down through the notes when the group is expanded. The line itself is now the expand/collapse indicator in place of the old chevron.

### Changed
- Groups now sit at the top of the sidebar, pinned above the ungrouped notes, so they stay reachable instead of scrolling off the bottom as loose notes pile up (issue #8). Dragging a note to the very top files it into the first group.
- The note list now runs flush to the footer instead of stopping about 8px short. When the list is longer than the sidebar its bottom edge fades into the chrome to hint at more below; the fade clears once you scroll to the end, and the scrollbar itself never fades.
- The New note button now steps its label down as the sidebar narrows - "+ New note" to "+ New" to "+" - taking the widest wording that fits, and the sidebar no longer narrows past a clean "+" button (collapse it fully with the chevron instead).
- The keyboard shortcuts overlay (F1), both the list and the visual keyboard, now include the standard editing shortcuts that were always active but undocumented: Undo / Redo (Ctrl+Z / Ctrl+Y), Cut / Copy (Ctrl+X / Ctrl+C), and Select all (Ctrl+A). The keyboard map also marks the Ctrl+1-9 tag toggles.
- The About window description is a little longer, spelling out what KillerNotes is rather than a single line.

### Fixed
- Pasting from Excel (or any app that carries its own colors) dropped black text into the note, which vanished in the dark themes. Pasted content is now normalized to the live theme the moment it lands - neutral black/white/gray text follows the theme like typed text does, while deliberately colored text and highlights are left untouched (the same rule the app already applies when opening a note).
- Tables pasted from Excel arrived with bright gridline cell walls that clashed with the dark themes. Pasted tables now take the app's own table styling (theme card-border color, single-line grid, no cell spacing) so they match tables inserted from the format bar.
- Multi-line text pasted from Notepad (or any plain-text source) arrived with extra line spacing, because the text-to-document paste converter bakes a paragraph margin onto every pasted line that the editor's own typed lines do not carry. Pasted paragraphs now drop that baked margin so they match the editor's default spacing (horizontal rules keep theirs).
- The film grain was almost invisible in the Black theme: its grain opacity was half that of the other dark themes (0.12 vs 0.24) while sitting on the darkest background, so it never read. Black now uses the same grain strength as the Dark theme.
- Filled in the missing translations: 87 strings added over the recent releases (note groups, tags, per-note and per-group colors, spell check, convert-to-list, zoom, the data-folder setting, and the keyboard-shortcut labels) had been falling back to English in the non-English builds. They are now translated across all eight bundled languages - Spanish, French, German, Turkish, Chinese (Simplified and Traditional), Japanese, and Bengali.

## [1.0.2] - 2026-07-20

### Added
- Reorder notes by drag and drop (#4): a third sort button (grip icon) activates custom order, and dragging a note up or down the sidebar arranges it by hand - an accent line shows where it will land. Dragging while sorted by time or alphabet keeps what is on screen and switches to custom order automatically. New and imported notes append at the bottom, and dragging a note out of the app into Teams/Outlook/Explorer still shares it as a .knote exactly as before.
- Note groups (#4): named sections in the sidebar ("House", "University", ...). Right-click a note > Group files it into an existing group, a new one, or back out; dropping a note onto a group header (or between its notes) moves it there too. Headers collapse on click (state remembered per database), and right-clicking a header renames or deletes the group - deleting a group keeps its notes. Groups travel inside shared .kndb files; search results stay flat while a search is active.
- Choose where your databases live (#6): Manage databases gains a "Change data folder" button that repoints storage to any folder - a synced folder, a second drive, or the folder next to the exe for a portable setup. Picking a new folder offers to move the existing .db files along ("Move") or just switch ("Leave them"). The default stays %APPDATA%\KillerNotes.

### Fixed
- Tagging with several notes selected applied the tag to the first note only (#7). The right-click Tags submenu now acts on the whole selection: the check mark means "every selected note has this tag", and toggling a mixed state tags the notes still missing it (a second toggle untags them all).
- On international keyboard layouts AltGr combos were read as Ctrl shortcuts, so AltGr+O (ó on Polish) triggered Ctrl+O Open files instead of typing the character (#5). AltGr arrives as Ctrl+Alt on Windows; those combos now pass through to the editor untouched so AltGr characters and dead keys type normally.
- Further hardening of the password-change file swap (#3): straggler native SQLite handles are forced closed before the swap (a finalizer-held handle kept the old file mapped, which no amount of retrying could outwait), the retry also covers access-denied errors, and if the atomic replace still cannot win, a move-based swap takes over and restores the original database if it fails partway. Notes keep saving in every outcome.
- In the Light themes, accent-filled controls kept text of the same hue, so the "New note" button read as a solid block with no label and the selected row in Manage databases was unreadable. Outline buttons are now a true outline at rest (accent border and text, filling solid only on hover), and the selected database row uses white text like the note list.
- Right-click menus drew a hard rectangular box instead of a soft drop shadow, and correcting that exposed the shadow being clipped at the menu edge. Menu popups are now transparent with a correctly sized soft shadow that sits at the click point without being cut off.

## [1.0.1] - 2026-07-19

### Added
- Color-coded tags (Outlook-style): tag any note with one or more colored labels via right-click > Tags. Tags show as colored pills on the sidebar cards, and clicking a pill filters the list to that tag. Tag definitions (name + color) live per database, so they travel inside shared .kndb/.knote files; a new database starts with a basic six-color set. Manage tags (right-click > Tags > Manage tags...) adds, renames, recolors (via the full color picker), and deletes tags, with deletes and renames rippling through every note. Tag search is instant (the existing full-text index).
- Font size controls (#1): a size dropdown on the format bar shows the selection's size - hover it and scroll to change size (no clicking), or click it for the full list (10-48). Ctrl+Shift+> and Ctrl+Shift+< also grow/shrink the selection from the keyboard, and Ctrl+mouse-wheel zooms the whole editor view (50-300%, remembered across launches, Ctrl+0 resets).
- Full color picker (#1): "More..." in the text color flyout opens the family KillerPDF-style picker - saturation/value square, hue strip, RGB and hex fields, a desktop-wide eyedropper, and 9 customizable saved swatches - for both text color and highlight.
- Per-note title colors (#1): right-click a note > "Title color..." colors that note's title in the sidebar and the editor, "Reset title color" returns it to the theme. Colors travel inside shared .knote/.kndb files.
- Per-note spell check: the abc button on the format bar toggles spell checking for the open note (off by default, remembered per note, lights in the accent while on). Uses the Windows spell checking engine, so it follows your installed Windows languages.
- The notes database gains two columns for these (title_color, spellcheck), added automatically on first open; 1.0.0 databases and shared files keep working unchanged, and old .knote/.kndb files still import.

### Changed
- Sidebar toggle moved from F9 to F5, so the two pane toggles sit together (F5 sidebar, F6 format bar). Tooltips, the F1 overlay, and the keyboard map follow.
- The minimized format bar strip is slimmer so it never overlaps long note titles, restores with a single click (F6 and drag-to-move unchanged), and no longer steals clicks to the title bar or jumps to the left edge on a plain click.
- Right-clicking the markdown/HTML preview no longer pops the IE engine's native context menu - the one piece of the app that could not be themed - it is suppressed instead (Ctrl+A / Ctrl+C still work).
- The app no longer creates an empty "Untitled" note at startup when the database already has notes (#2). Launch reopens the note that was open last time (remembered per database, falling back to the most recently modified), and deleting the open note lands in the most recently edited remaining note instead of spawning a replacement. A fresh empty Untitled is only created when the database has no notes at all, so the undeletable phantom "Untitled" row is gone.

### Fixed
- Password protection could fail with "The process cannot access the file because it is being used by another process" and, worse, leave the app running without an open database, so the next autosave crashed and the session's edits were lost (#3). The rekeyed file swap now retries through transient locks (antivirus and indexer scans of the freshly written file), and if the swap still fails the original database is reopened with the old password and the error is reported in the status bar - notes keep saving.
- Only one KillerNotes instance now runs per desktop session. Two instances sharing the same notes.db was the deterministic way to hit the password-change failure above. A second launch hands its command line (a double-clicked .knote/.kndb) to the running window over a named pipe and exits, so share files still import as before; the running window is brought to the front. --demo launches are exempt, since they only ever touch the scratch demo database.

## [1.0.0] - 2026-07-18

### Added
- Rich text notes: inline image paste, drag-and-drop for text, image files, and raw bitmaps, real FlowDocument tables, and autosave (2s debounce, note switch, alt-tab, and close) plus Ctrl+S.
- SQLite storage at `%APPDATA%\KillerNotes\notes.db` (XamlPackage blobs plus a plain-text index) with FTS5 search-as-you-type.
- Optional whole-database password protection: SQLCipher AES-256, set/change/remove from the title-bar lock button. If a password is forgotten, the unlock screen offers "New database", archiving the locked file (kept on disk, unlockable later) and starting fresh - the encrypted data itself is unrecoverable by design.
- Sharing between techs: right-click a note > "Share note..." saves a .knote (a one-note KillerNotes database, optionally protected with a share password); Manage databases > "Export as .kndb..." shares a whole database (its encryption travels with it). Double-clicking either file opens KillerNotes: .knote imports into the current database (prompting for the share password), .kndb is added to the data folder and switched to. HKCU file associations register on launch - deliberately not .kdb, which belongs to KeePass.
- Drag a note out of the sidebar into Teams, Outlook, or Explorer and it lands as a .knote file (shell CF_HDROP drag-out via a temp export). Drag-outs are unencrypted by design; use Share note... for a password-protected copy.
- Open ordinary files as notes: Ctrl+O, or drop files onto the sidebar, an open note, or the empty editor space. Each file becomes its own note titled by its filename - .txt/.log/.md as text, .html/.htm carrying the source (the preview renders it), .rtf with formatting intact, images inline, and .knote/.kndb routing through the share import.
- Export a note as an ordinary file: F8 or right-click > "Export note as..." - plain text (.txt), rich text with images (.rtf), or a standalone theme-styled web page (.html, images embedded as base64).
- Floating format bar with the KillerPDF annotation-bar behavior: a grain-wrapped band beside the note title that pops out of the top of the pane when a note opens, drags side to side by its grip dots (edge-anchored or parked mid-pane, position remembered), and minimizes - chevron, F6, or double-click the grip - to a slim peek strip with a 120ms animation. Group separators, 14px glyphs, hotkeys in tooltips, and a faint rule between the note title and the content.
- Formatting tools: bold, italic, underline, strikethrough, monospace/code (Consolas), lists, text color + highlight swatch flyout (Word-style "A over an accent bar" button, with auto/none resets), and horizontal rule - all with hotkeys (Ctrl+B/I/U, Ctrl+Shift+S/M/H/R/L/N).
- Table size picker: press (or press-hold-drag) the table button for an Office-style hover grid up to 8x6, plus a custom cols x rows row (up to 50x200); a click-through guard keeps a quick click from inserting by accident.
- Markdown/HTML preview: notes that look like markdown or HTML get a preview toggle in the format bar, opening a theme-styled split pane (Markdig; HTML is sanitized before rendering).
- Sidebar note library: compact search box (magnifier icon, F3 placeholder), dedicated creation-time and alphabetical sort buttons (the active one shows in the accent with a direction arrow, click again to reverse; default creation time, oldest at the top), and collapse via chevron or F9 (state remembered).
- Sidebar icon rail (KillerPDF pattern): a permanent 30px rail on the sidebar's inner edge, right against the content pane in both states - collapse chevron on top, ? (shortcuts) + theme picker pinned at the bottom, glyphs with soft shadows that turn accent on hover.
- Manage databases dialog (title-bar button): browse every .db with size/date/[encrypted]/[active] flags, create, delete with confirm, inline rename (double-click or right-click menu), Copy file (a real file on the clipboard for pasting into Explorer, Teams, or an email), Show in Explorer, Export as .kndb, and switch the active database (silent re-unlock where the session password still fits).
- Hotkeys with an F1 shortcuts overlay: F2 rename, F3/Ctrl+F search, F4 preview, F6 format bar, F8 export, F9 sidebar, F12 About, Ctrl+N new note, Ctrl+O open files, Delete in the list, Esc closes overlays or clears search.
- The F1 overlay has LIST and KEYBOARD views (choice remembered) - the keyboard view is KillerPDF's visual board: a full keyboard diagram with category-colored keycaps per layer (BASE / CTRL / CTRL+SHIFT), hover lift with a marquee for cut-off captions, a detail line, live theme repaint, and holding the real Ctrl or Shift previews that layer.
- The app always opens into a note: launch, database switch, and deleting the open note all land in the newest empty Untitled note (or a fresh one). Clicking the empty editor space starts a new note and typing begins immediately; text or images dropped there start a note carrying them.
- Theme-adaptive note colors: a saved note bakes the text color it was written under, so a dark-mode note read in a light theme (or shared as a .knote to a light-mode coworker) was unreadable. On load, neutral black/white/gray colors are stripped so default text always follows the live theme - deliberately colored text and highlights are untouched, and code-built notes carry the editor font instead of the FlowDocument serif default.
- Demo mode for screenshots (KillerScan pattern): launch with --demo to fill a scratch demo-notes.db with fabricated MSP-flavored notes (checklists, config tables, monospace snippets, colored callouts) with staggered dates - the real database is never opened, and the scratch file re-rolls on every demo launch.
- KillerUI family shell: custom chrome with the KillerPDF red-fill close button and pressed feedback, 6 themes + accent palettes (default Black/Purple), film grain, elevated editor pane with the family drop shadow (carried by a bitmap-cached twin border behind the pane so typing never re-renders the shadow), themed dialogs with OutlineButton primaries and a reddening close X, themed editor context menus and accent-colored text selection, About overlay with update check, window placement persistence, 24px family footer, 25px title-bar icon.
- App icon (from brand/icon.png, used as-is) as the exe icon and in the About card header, plus dedicated .knote and .kndb file icons (placeholders derived from the app icon until custom art lands in brand/): embedded in the exe, extracted to AppData at startup, and wired to the file associations with a full size ladder (16-256 incl 20/40/96, BMP frames) for crisp Explorer rendering. brand/ artwork folder is gitignored.
- Resizable images: click an image in a note for corner handles and drag to scale it, aspect locked, clamped between 40px and the image's natural size. Display-only - the full-resolution original always stays in the database, so shrinking never costs quality, and every note image renders with high-quality (Fant) scaling.
- Single signed exe (~4.3 MB): every dependency including the SQLCipher engine is embedded (Costura + a self-extracting native bootstrap), and releases are Authenticode-signed and timestamped.
- Family install system (KillerScan pattern): portable by default with a PORTABLE badge and one-click per-user install (Start Menu, optional desktop shortcut, Add/Remove Programs entry, no admin), /silent for unattended machine-wide deployment via RMM/winget/choco, and /uninstall that always keeps your notes.
