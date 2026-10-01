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
