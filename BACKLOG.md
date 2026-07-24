# KillerNotes backlog

Unscheduled ideas and follow-ups. Not release notes; move an item into CHANGELOG when it ships.

## Network-share / concurrent-access safeguard

Raised 2026-07-21 (field question: DB on a shared folder, two people using it at once).

The DB can be placed on a shared folder and opened by two people on different
machines at the same time, and nothing stops it. The single-instance guard is a
`Local\` mutex in `App.xaml.cs`, scoped per Windows session, so it only blocks a
second instance on the same machine, not a second machine.

Current behavior on a share:
- Last-write-wins clobbering at whole-note granularity. There is no file watcher,
  so each instance caches notes in memory and never sees the other's changes.
- "database is locked" thrown on simultaneous autosaves (SQLite is single-writer).
- Real corruption risk from unreliable byte-range locking over SMB/NFS (SQLite's
  own caveat). Note: we are on rollback-journal, not WAL, which is the correct
  mode for a share, so at least the WAL-over-share failure is already avoided.

Planned safeguard:
- Detect when `DbPath` resolves to a network / UNC path and show a one-time
  warning that concurrent use is unsafe.
- Optional lock file next to the DB: if present and held by another host, open
  read-only instead of fighting for the write lock. Clear stale locks on clean close.
- Reinforce the intended pattern in the warning copy: one DB per person, share
  individual notes as `.knote` and whole DBs as `.kndb`.

## SketchPad (inline drawing block)

Raised 2026-07-23 (feature brainstorm; direction agreed, not yet scheduled).

An inline drawing canvas you insert into a note the way you insert a table or an
image. Deliberately not a transparent overlay over the whole note and not a separate
note type - a self-contained block sitting in the flow. That sidesteps the reflow
problem: a note is a reflowable FlowDocument, and freehand ink anchored to reflowing
text would drift on every resize, zoom, or font change. A block has fixed internal
coordinates, so nothing drifts.

Model - three stacked layers on one fixed-size canvas:
- Image backdrop (optional): drop or paste an image and it becomes the canvas
  background you draw and label on top of. This is the headline use - mark up a
  screenshot, rack photo, or network diagram right inside the runbook note.
- Ink: freehand strokes (pen, highlighter, eraser).
- Text labels: typed labels placed and dragged anywhere on the canvas.

WPF's `InkCanvas` hosts ink strokes and positioned child elements natively, so all
three layers live on one control instead of a hand-rolled compositor.

Behavior:
- Resizable like an image (corner/edge handles). With a backdrop the whole composite
  scales uniformly - image, ink, and labels together - aspect locked so nothing
  slides off the picture. A blank canvas resizes freely.
- Strokes stored at native canvas resolution; on display the block scales to fit the
  pane width uniformly, so there is no distortion.
- Click into the block to enter draw mode with a small floating tool strip (pen /
  highlighter / eraser / color / clear), mirroring the format-bar pattern. Escape or
  click-out returns to text editing. The wheel still scrolls the note in draw mode.
- Label text feeds the note's FTS index and snippet, so "IDF-2, port 14" scribbled on
  a photo still turns the note up in search. A flattened marked-up image would lose
  that.

Shortcuts:
- F7 and Ctrl+Shift+D both insert a SketchPad block (redundant on purpose - F7 for
  single-press ease, Ctrl+Shift+D to sit with the other inline inserts). Needs an
  open note with editor focus.
- F7 is freed by moving Manage tags off it: Tags -> Ctrl+T, theme/accent flyout ->
  Ctrl+Shift+T. Tags are used more than the theme picker, so tags take the bare key.
  Ctrl+1-9 still toggle individual tags. Update the shortcuts overlay and keyboard
  map strings to match.

Serialization (prototype this first):
- Store per block: ink as ISF (`StrokeCollection.Save`), the optional backdrop image,
  the label set (text + position + style), and the canvas size. Do NOT serialize the
  live `InkCanvas` into the XamlPackage - rebuild it on load from the stored payload.
- Must survive save/load, undo/redo, copy/paste of the note, and riding along in a
  shared `.knote`. Prove the ISF round-trip through the note model before any UI.
- Undo boundary: ink and label edits undo within the focused block; inserting or
  deleting the whole block lives on the note's text undo stack.

Export:
- HTML / RTF: flatten the block to a PNG and embed it (reuses the pasted-image path).
- TXT: drop it or leave a `[sketch]` marker.

Build phases:
1. Prove ISF + payload round-trip through save/load and `.knote` sharing (no UI).
2. Insert an empty resizable `InkCanvas` block at the caret; pen / eraser / color /
   clear; wire F7 + Ctrl+Shift+D; do the tag/theme shortcut swap.
3. Image backdrop (drop/paste), uniform scaling, aspect lock.
4. Text labels, and feed label text into the FTS index/snippet.
5. Export flattening (HTML / RTF / TXT).

Later: straight-line / arrow / snap-to shape tools. This same block is the natural
host for the flowchart idea - shapes on a fixed canvas are just a richer tool mode on
the same surface.
