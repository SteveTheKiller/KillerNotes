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
