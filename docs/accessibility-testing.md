# Keyboard accessibility testing

Branch: `accessibility-keyboard-navigation`, based on KillerNotes 1.3.2.

Use a disposable database. Run each keyboard test with JAWS and NVDA separately,
and record the Windows version, screen-reader version and exact announcement.
The source checks do not establish screen-reader behaviour or visual compatibility.

## Build on Windows

Install the .NET 10 SDK, then run from the repository directory:

```powershell
dotnet build KillerNotes.csproj -c Release
dotnet test KillerNotes.Tests/KillerNotes.Tests.csproj -c Release
```

Run `bin\Release\net48\KillerNotes.exe`. Close the installed copy first.
The test build uses the application's normal settings and data location; use a
separate Windows test account if you need to isolate your existing settings.

## Retest matrix

| Test | Expected result |
| --- | --- |
| From the note body, press F1 | Focus moves to the first shortcut in List view. The key and its description are announced together. |
| With F1 open, type text and try Ctrl+N, Delete and formatting shortcuts | The note and notebook remain unchanged. |
| In F1, use arrows and Tab/Shift+Tab | Arrows navigate each column; Tab moves between columns and help controls. Background controls cannot receive focus. All rows can be brought into view. |
| Close F1 with Escape, F1, the close button or a backdrop click | Focus returns to the previous control; the note caret and selection are preserved. |
| Open and close F1 repeatedly | No stale disabled controls or delayed close animation interferes with the next opening. |
| Open F1 from the note list or title field | Closing returns focus to that same control. |
| Switch F1 to Keyboard view, close and reopen | Keyboard view remains selected. Focus stays inside help. The List button provides access to the readable shortcut list. |
| Press Alt+O in a note containing headings | Focus moves to the first outline entry and its heading text is announced. |
| In the outline, use arrows, then Enter or Space | The chosen heading is reached and focus moves to the note body. |
| In the outline, press Escape or Alt+O | Focus returns to the note body. Alt+O also closes the outline. |
| Open the outline in a note without headings | Focus reaches the outline list; no exception occurs. |
| Click an outline heading with the mouse | The existing jump-to-heading behaviour still works. |
| Press Alt+T with templates configured | Focus reaches the first template; each menu entry announces its title. Arrows and Enter create a note from the selected template. |
| Cancel Alt+T with Escape | Focus returns to the original control. |
| Press Alt+T without a templates group, or with an empty group | The explanatory entry is focusable and readable. Activating it creates no note. |
| Press Alt+N from the title or body | Focus moves to the current note-list item without opening a different note. A collapsed sidebar expands. |
| Press Alt+E from the note list or title | Focus moves to the current note body without moving its caret or changing its selection. |
| Press Alt+N and Alt+E from the rendered preview | The shortcuts work through the browser bridge. Alt+E switches Rendered view to Source so the editor can receive focus. Split view remains Split. |
| Press Alt+E with no note open | No note is created and no exception occurs. |
| Use AltGr characters in the note | Existing AltGr typing still works; Ctrl+Alt must not trigger the new navigation shortcuts. |

## Table picker

In a rich-text note, focus the table button and test Enter and Space separately.
Columns should receive focus with its value selected. Tab and Shift+Tab should
cycle through Columns, Rows and Insert, with the field names announced. Enter
in either field or activation of Insert should insert the requested size and
return focus to the editor. Escape should close the picker without inserting
and return focus to the table button. Check invalid dimensions, and confirm the
existing mouse hover/click and press-drag-release gestures still work.
Ctrl+Shift+T should continue to insert a 3 x 3 table.

## Body tags

Use `[#Azure,#EntraId,#PIM]` in rich-text or Markdown notes. Spaces after commas
are allowed; names can contain letters, numbers, underscores, dots and hyphens.
The tags become available after autosave. Manual tag assignments stay separate:
removing a bracketed tag leaves a matching manual assignment intact. Body tags
must be removed by editing the body, rather than toggling them in the Tags menu.
Fenced/indented code, inline code, escaped groups and Markdown links are ignored.

Place the caret on each individual tag and press Ctrl+Enter. The menu should
focus its first item and list at most five other matching notes, most recently
modified first. Match whole tag names without regard to case; exclude trashed
notes and the current note. Enter opens the chosen note and focuses its body.
Escape restores the original caret. With no matches, the focused menu item
announces that there are no other notes with that tag. Outside tags, existing
Ctrl+Enter behaviour is unchanged, including the calculator command.

Check tags beyond the first 120 characters, duplicates, removal, search/filter,
manual assignment/undo, restart, and conversion between rich text and Markdown.
Tag definitions can be renamed/deleted in Manage tags, but occurrences in the
body remain authoritative and must be edited there too.

## Tag autocomplete

Create existing tags using Manage tags, then type `[#` in a rich-text or Markdown
note. Suggestions should appear A-Z without moving focus from the editor.
Typing narrows the list by prefix, ignoring case; the suggestion count is a
polite live-region announcement. Down Arrow moves focus to the first suggestion,
and Up/Down announce individual tag names. Tab completes the selected name and
returns the caret to the editor. It inserts no comma or closing bracket.
Escape dismisses without changing the text or caret. With no matches, continue
typing a new tag normally. Suggestions only include names supported by bracketed
tags (letters, numbers, underscores, dots and hyphens).

Test `[#Azure,#` and spaces after commas, Tab directly from the editor, selection
followed by Tab, typing/Backspace while browsing suggestions, mouse selection,
Escape, outside clicks, moving the caret away, opening another note and F1.
Check ordinary headings, wikilinks, escaped brackets and code blocks/spans never
trigger suggestions. Check Ctrl+Z undoes completion as one action and existing
wikilink completion still works. Run all focus/announcement tests with JAWS and
NVDA; automation tests cannot establish screen-reader behaviour.

## Visual and mouse checks

Compare the build with 1.3.2 in Light, Dark and 98SE themes, plus the user's normal
theme. Check normal and increased app scaling. The existing two-column shortcuts
layout, outline text, menu row layout, colours and fonts should remain intact.
Only focused navigation rows gain a theme-coloured outline. Verify scrolling,
mouse hover and backdrop dismissal, including when the preview is visible.

## Validation status

Local source checks: changed XAML parses, string references resolve, and
`git diff --check` passes. Windows compilation, the existing test suite, visual
comparison, and JAWS/NVDA tests are pending.
