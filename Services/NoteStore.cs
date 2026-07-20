using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using KillerNotes.Models;

namespace KillerNotes.Services
{
    // SQLite-backed note storage.
    //
    //   - One database file: %APPDATA%\KillerNotes\notes.db
    //   - FTS5 full-text index (title, plain text, tags) kept in sync by triggers,
    //     so search is instant regardless of note count.
    //   - Content is a BLOB: the editor's FlowDocument saved as a XamlPackage
    //     (a zip stream that carries pasted images and tables inside the note).
    //   - Optional password: SQLCipher (AES-256) encrypts the whole file at rest.
    //     With no password set the file is plain SQLite. Setting/removing a password
    //     rewrites the database through sqlcipher_export.
    public static class NoteStore
    {
        private static SqliteConnection? _db;
        private static string? _password;   // key of the currently open db (null = plaintext)

        /// <summary>The stock data folder; DbDir prefers the "DataFolder" setting (#6).</summary>
        public static string DefaultDbDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KillerNotes");

        /// <summary>Folder holding the .db files: the "DataFolder" setting when one was
        /// chosen (Manage databases > Change data folder, #6), else %APPDATA%\KillerNotes.</summary>
        public static string DbDir =>
            App.GetSetting("DataFolder") is string dir && !string.IsNullOrWhiteSpace(dir)
                ? dir : DefaultDbDir;

        /// <summary>Set by --demo (App.OnStartup): overrides the active db with a scratch
        /// demo file so screenshot sessions never touch real notes. In-memory only - the
        /// "ActiveDatabase" setting is not written, so the next normal launch is untouched.</summary>
        public static string? DemoDbFile;

        /// <summary>File name of the active database inside DbDir ("ActiveDatabase" setting;
        /// defaults to notes.db). The Manage databases dialog switches and renames these.</summary>
        public static string ActiveDbFile =>
            DemoDbFile ??
            (App.GetSetting("ActiveDatabase") is string s && !string.IsNullOrWhiteSpace(s) ? s : "notes.db");

        public static string DbPath => Path.Combine(DbDir, ActiveDbFile);

        public static bool IsOpen => _db != null;
        public static bool HasPassword => _password != null;

        // ---- Open / close ----

        /// <summary>True when the active db file exists and cannot be read without a key.</summary>
        public static bool IsEncrypted() => IsEncryptedFile(DbPath);

        /// <summary>Probe any db file (Manage databases uses this for its [encrypted] flags).</summary>
        public static bool IsEncryptedFile(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using var probe = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
                probe.Open();
                using var cmd = probe.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM sqlite_master";
                cmd.ExecuteScalar();
                return false;
            }
            catch (SqliteException) { return true; }
            finally { SqliteConnection.ClearAllPools(); }
        }

        /// <summary>Closes and renames the active db aside (notes-locked-date.db), for the
        /// unlock screen's "New database" escape hatch. Returns the archived path.</summary>
        public static string ArchiveDatabase()
        {
            string src = DbPath;
            Close();
            string dest = Path.Combine(DbDir,
                $"{Path.GetFileNameWithoutExtension(ActiveDbFile)}-locked-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Move(src, dest);
            return dest;
        }

        /// <summary>Opens (creating if needed) the database. Throws SqliteException on a wrong password.</summary>
        public static void Open(string? password = null)
        {
            Close();
            Directory.CreateDirectory(DbDir);
            var csb = new SqliteConnectionStringBuilder { DataSource = DbPath };
            if (!string.IsNullOrEmpty(password)) csb.Password = password;
            _db = new SqliteConnection(csb.ConnectionString);
            _db.Open();
            Exec("SELECT count(*) FROM sqlite_master");   // forces the key check right here
            _password = string.IsNullOrEmpty(password) ? null : password;
            EnsureSchema();
        }

        public static void Close()
        {
            _db?.Dispose();
            _db = null;
            _password = null;
            // Release the pooled handle so the file can be swapped (password changes).
            SqliteConnection.ClearAllPools();
        }

        // Shared by the active db and exported .knote files, so the same engine opens both.
        private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS notes(
    id       INTEGER PRIMARY KEY,
    title    TEXT NOT NULL DEFAULT '',
    notebook TEXT NOT NULL DEFAULT '',
    tags     TEXT NOT NULL DEFAULT '',
    created  TEXT NOT NULL,
    modified TEXT NOT NULL,
    content  BLOB,
    plain    TEXT NOT NULL DEFAULT '',
    title_color TEXT NOT NULL DEFAULT '',
    spellcheck  INTEGER NOT NULL DEFAULT 0
);
CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts
    USING fts5(title, plain, tags, content='notes', content_rowid='id');
CREATE TRIGGER IF NOT EXISTS notes_ai AFTER INSERT ON notes BEGIN
    INSERT INTO notes_fts(rowid, title, plain, tags)
        VALUES (new.id, new.title, new.plain, new.tags);
END;
CREATE TRIGGER IF NOT EXISTS notes_ad AFTER DELETE ON notes BEGIN
    INSERT INTO notes_fts(notes_fts, rowid, title, plain, tags)
        VALUES ('delete', old.id, old.title, old.plain, old.tags);
END;
CREATE TRIGGER IF NOT EXISTS notes_au AFTER UPDATE ON notes BEGIN
    INSERT INTO notes_fts(notes_fts, rowid, title, plain, tags)
        VALUES ('delete', old.id, old.title, old.plain, old.tags);
    INSERT INTO notes_fts(rowid, title, plain, tags)
        VALUES (new.id, new.title, new.plain, new.tags);
END;
CREATE TABLE IF NOT EXISTS tags(
    name  TEXT PRIMARY KEY COLLATE NOCASE,
    color TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS groups(
    name       TEXT PRIMARY KEY COLLATE NOCASE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    collapsed  INTEGER NOT NULL DEFAULT 0
);";

        private static void EnsureSchema()
        {
            bool hadTags = TableExists("tags");
            Exec(SchemaSql);
            EnsureColumns();
            // Seed ONLY when the tags table was just created: user customizations and
            // deletions must never resurrect (Steve, tags design).
            if (!hadTags) SeedDefaultTags();
        }

        private static bool TableExists(string name)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name = $n";
            cmd.Parameters.AddWithValue("$n", name);
            return (long)cmd.ExecuteScalar()! > 0;
        }

        // Outlook-style starter set in the family palette (same hexes as the accent row).
        private static readonly (string Name, string Color)[] DefaultTags =
        [
            ("Red", "#DD504B"), ("Orange", "#E8962C"), ("Yellow", "#E8D44B"),
            ("Green", "#1EA54C"), ("Blue", "#50AEE8"), ("Purple", "#B982E3"),
        ];

        private static void SeedDefaultTags()
        {
            foreach (var t in DefaultTags) AddTag(t.Name, t.Color);
        }

        // 1.0.1 additive columns (per-note title color + spell check). ALTER-on-open is
        // needed because CREATE TABLE IF NOT EXISTS never touches an existing table;
        // PRAGMA-checking first keeps this idempotent and cheap.
        private static void EnsureColumns()
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(notes)";
                using var r = cmd.ExecuteReader();
                while (r.Read()) have.Add(r.GetString(1));
            }
            if (!have.Contains("title_color"))
                Exec("ALTER TABLE notes ADD COLUMN title_color TEXT NOT NULL DEFAULT ''");
            if (!have.Contains("spellcheck"))
                Exec("ALTER TABLE notes ADD COLUMN spellcheck INTEGER NOT NULL DEFAULT 0");
            // 1.0.2: manual drag-and-drop ordering (#4). 0 = never ordered; Create()
            // appends max+1 so new notes land at the bottom of a custom arrangement.
            if (!have.Contains("sort_order"))
                Exec("ALTER TABLE notes ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0");
        }

        // ---- Notes ----

        /// <summary>
        /// Lists notes, oldest-created first by default (the notepad-tabs order). With a search
        /// string, matches the FTS5 index (prefix match per word) in relevance order.
        /// sort: "created-asc" | "created-desc" | "title-asc" | "title-desc".
        /// </summary>
        public static List<Note> List(string? search = null, string sort = "created-asc")
        {
            var results = new List<Note>();
            if (_db == null) return results;

            using var cmd = _db.CreateCommand();
            if (!string.IsNullOrWhiteSpace(search))
            {
                cmd.CommandText = @"
SELECT n.id, n.title, n.notebook, n.tags, n.created, n.modified, substr(n.plain, 1, 120), n.title_color, n.spellcheck, n.sort_order
FROM notes_fts f JOIN notes n ON n.id = f.rowid
WHERE notes_fts MATCH $q ORDER BY rank";
                cmd.Parameters.AddWithValue("$q", FtsQuery(search!));
            }
            else
            {
                string order = sort switch
                {
                    "created-desc" => "created DESC",
                    "title-asc"    => "title COLLATE NOCASE ASC",
                    "title-desc"   => "title COLLATE NOCASE DESC",
                    "custom"       => "sort_order ASC, id ASC",
                    _              => "created ASC",
                };
                cmd.CommandText = "SELECT id, title, notebook, tags, created, modified, substr(plain, 1, 120), title_color, spellcheck, sort_order " +
                                  $"FROM notes ORDER BY {order}";
            }

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                results.Add(new Note
                {
                    Id       = r.GetInt64(0),
                    Title    = r.GetString(1),
                    Notebook = r.GetString(2),
                    Tags     = r.GetString(3),
                    Created  = ParseTs(r.GetString(4)),
                    Modified = ParseTs(r.GetString(5)),
                    Snippet  = FirstLine(r.IsDBNull(6) ? "" : r.GetString(6)),
                    TitleColor = r.IsDBNull(7) ? "" : r.GetString(7),
                    SpellCheck = !r.IsDBNull(8) && r.GetInt64(8) != 0,
                    SortOrder  = r.IsDBNull(9) ? 0 : (int)r.GetInt64(9),
                });
            }
            return results;
        }

        public static long Create(string title)
        {
            using var cmd = _db!.CreateCommand();
            // sort_order = max+1: a new note lands at the BOTTOM of a custom arrangement
            // rather than jumping to the top with the column's 0 default (#4).
            cmd.CommandText = "INSERT INTO notes(title, created, modified, plain, sort_order) " +
                              "VALUES ($t, $now, $now, '', (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM notes)); " +
                              "SELECT last_insert_rowid()";
            cmd.Parameters.AddWithValue("$t", title);
            cmd.Parameters.AddWithValue("$now", Ts(DateTime.Now));
            return (long)cmd.ExecuteScalar()!;
        }

        public static byte[]? LoadContent(long id)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "SELECT content FROM notes WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var v = cmd.ExecuteScalar();
            return v is byte[] b && b.Length > 0 ? b : null;
        }

        public static void Save(long id, string title, byte[] content, string plain)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE notes SET title = $t, content = $c, plain = $p, modified = $m WHERE id = $id";
            cmd.Parameters.AddWithValue("$t", title);
            cmd.Parameters.AddWithValue("$c", content);
            cmd.Parameters.AddWithValue("$p", plain);
            cmd.Parameters.AddWithValue("$m", Ts(DateTime.Now));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Backdates a note (demo mode only - fabricated screenshot data needs a
        /// lived-in sidebar). Normal saves always stamp DateTime.Now.</summary>
        public static void SetTimestamps(long id, DateTime created, DateTime modified)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE notes SET created = $c, modified = $m WHERE id = $id";
            cmd.Parameters.AddWithValue("$c", Ts(created));
            cmd.Parameters.AddWithValue("$m", Ts(modified));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        public static void Delete(long id)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "DELETE FROM notes WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Sets the sidebar/title display color ("#RRGGBB"; "" = theme default).</summary>
        public static void SetTitleColor(long id, string color)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE notes SET title_color = $c WHERE id = $id";
            cmd.Parameters.AddWithValue("$c", color);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Persists the per-note spell check toggle.</summary>
        public static void SetSpellCheck(long id, bool on)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE notes SET spellcheck = $s WHERE id = $id";
            cmd.Parameters.AddWithValue("$s", on ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        // ---- Tags (per-database definitions; assignment is the notes.tags CSV, which
        //      the FTS triggers already index, so tag search/filter costs nothing) ----

        public static List<(string Name, string Color)> ListTags()
        {
            var list = new List<(string, string)>();
            if (_db == null) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT name, color FROM tags ORDER BY rowid";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
            return list;
        }

        /// <summary>Adds a tag definition; an existing name (case-insensitive) wins.</summary>
        public static void AddTag(string name, string color)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO tags(name, color) VALUES ($n, $c)";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", color);
            cmd.ExecuteNonQuery();
        }

        public static void SetTagColor(string name, string color)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE tags SET color = $c WHERE name = $n";
            cmd.Parameters.AddWithValue("$c", color);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Renames a tag definition and rewrites it inside every note's CSV.</summary>
        public static void RenameTag(string oldName, string newName)
        {
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "UPDATE tags SET name = $new WHERE name = $old";
                cmd.Parameters.AddWithValue("$new", newName);
                cmd.Parameters.AddWithValue("$old", oldName);
                cmd.ExecuteNonQuery();
            }
            RewriteTagInNotes(oldName, newName);
        }

        /// <summary>Deletes a tag definition and removes it from every note's CSV.</summary>
        public static void DeleteTag(string name)
        {
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM tags WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }
            RewriteTagInNotes(name, null);
        }

        // ---- Custom order + groups (#4) ----
        // Manual order is one GLOBAL sort_order sequence; a group's internal order is just
        // that sequence filtered, so moving notes between groups never renumbers per-group.
        // Group definitions (order + collapsed state) live in the groups table; assignment
        // is the existing notes.notebook column.

        /// <summary>Renumbers the whole custom order in one transaction (reorder drops
        /// and the first-use seeding both rewrite every row; note counts are small).</summary>
        public static void SetNoteOrders(IEnumerable<(long Id, int Order)> orders)
        {
            using var tx = _db!.BeginTransaction();
            foreach (var o in orders)
            {
                using var cmd = _db!.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE notes SET sort_order = $o WHERE id = $id";
                cmd.Parameters.AddWithValue("$o", o.Order);
                cmd.Parameters.AddWithValue("$id", o.Id);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        /// <summary>Assigns a note to a group ("" = ungrouped).</summary>
        public static void SetNoteGroup(long id, string group)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE notes SET notebook = $g WHERE id = $id";
            cmd.Parameters.AddWithValue("$g", group);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        public static List<(string Name, bool Collapsed)> ListGroups()
        {
            var list = new List<(string, bool)>();
            if (_db == null) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT name, collapsed FROM groups ORDER BY sort_order, rowid";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1) != 0));
            return list;
        }

        /// <summary>Adds a group definition at the end; an existing name (case-insensitive) wins.</summary>
        public static void AddGroup(string name)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO groups(name, sort_order) " +
                              "VALUES ($n, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM groups))";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Renames a group and rewrites the assignment on every member note.
        /// The caller checks for collisions first (the NOCASE primary key would throw).</summary>
        public static void RenameGroup(string oldName, string newName)
        {
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "UPDATE groups SET name = $new WHERE name = $old";
                cmd.Parameters.AddWithValue("$new", newName);
                cmd.Parameters.AddWithValue("$old", oldName);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "UPDATE notes SET notebook = $new WHERE notebook = $old COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$new", newName);
                cmd.Parameters.AddWithValue("$old", oldName);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Deletes a group definition; member notes are kept and become ungrouped.</summary>
        public static void DeleteGroup(string name)
        {
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM groups WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "UPDATE notes SET notebook = '' WHERE notebook = $n COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }
        }

        public static void SetGroupCollapsed(string name, bool collapsed)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE groups SET collapsed = $c WHERE name = $n";
            cmd.Parameters.AddWithValue("$c", collapsed ? 1 : 0);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        public static void SetNoteTags(long id, string tags)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "UPDATE notes SET tags = $t WHERE id = $id";
            cmd.Parameters.AddWithValue("$t", tags);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Shared CSV parser for notes.tags ("a, b" style; commas are stripped
        /// from tag names at entry, so a plain split is safe).</summary>
        public static string[] SplitTags(string tags) =>
            tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();

        // Renames (newName != null) or removes (null) one tag inside every note's CSV.
        // C#-side rewrite on purpose: CSV surgery in SQL is fragile, and note counts
        // are small. The FTS triggers keep the index in sync on each UPDATE.
        private static void RewriteTagInNotes(string name, string? newName)
        {
            var rows = new List<(long Id, string Tags)>();
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "SELECT id, tags FROM notes WHERE tags <> ''";
                using var r = cmd.ExecuteReader();
                while (r.Read()) rows.Add((r.GetInt64(0), r.GetString(1)));
            }
            foreach (var row in rows)
            {
                bool hit = false;
                var outParts = new List<string>();
                foreach (var p in SplitTags(row.Tags))
                {
                    if (string.Equals(p, name, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = true;
                        if (newName != null && !outParts.Contains(newName, StringComparer.OrdinalIgnoreCase))
                            outParts.Add(newName);
                    }
                    else outParts.Add(p);
                }
                if (hit) SetNoteTags(row.Id, string.Join(", ", outParts));
            }
        }

        // ---- Password (SQLCipher) ----

        /// <summary>
        /// Sets, changes, or removes (null/empty) the database password by rewriting the file
        /// through sqlcipher_export with the new key, then reopening. The caller has already
        /// proven knowledge of the current password by having the db open.
        /// </summary>
        public static void SetPassword(string? newPassword)
        {
            if (_db == null) throw new InvalidOperationException("database not open");
            if (string.IsNullOrEmpty(newPassword)) newPassword = null;

            string? oldPassword = _password;   // Close() clears it; kept for rollback
            string tmp = DbPath + ".rekey";
            if (File.Exists(tmp)) File.Delete(tmp);

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "ATTACH DATABASE $file AS rekeyed KEY $key";
                cmd.Parameters.AddWithValue("$file", tmp);
                cmd.Parameters.AddWithValue("$key", newPassword ?? "");
                cmd.ExecuteNonQuery();
            }
            Exec("SELECT sqlcipher_export('rekeyed')");
            Exec("DETACH DATABASE rekeyed");
            Close();

            // Pool clearing alone is not always enough (#3): a straggler sqlite3
            // handle kept alive by a finalizer still has the old file mapped, and
            // the swap below then throws "being used by another process" no matter
            // how long we retry. Force finalization so every native handle on both
            // files is truly closed before touching them.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SqliteConnection.ClearAllPools();

            string bak = DbPath + ".bak";
            try
            {
                ReplaceWithRetry(tmp, DbPath, bak);
            }
            catch
            {
                // The swap failed even after retries: something else still holds the
                // file (an AV scan, a second handle). The original db on disk is
                // untouched, so clean up the rekeyed copy, reopen with the OLD key -
                // the app must never keep running with no database, or the next
                // autosave crashes and loses the session's edits - and let the
                // caller report the error.
                try { File.Delete(tmp); } catch { /* best effort */ }
                Open(oldPassword);
                throw;
            }
            Open(newPassword);
            File.Delete(bak);   // only once the rewritten db opened cleanly
        }

        /// <summary>File.Replace with backoff retries. Antivirus and indexer scans of the
        /// freshly written rekey file cause transient sharing violations on the swap
        /// (issue #3); waiting a moment and retrying beats failing the password change.
        /// If Replace never succeeds, falls back to a plain move-based swap - Replace
        /// needs simultaneous exclusive access to all three paths, while the moves need
        /// one file at a time and restore the original if the second move fails.</summary>
        private static void ReplaceWithRetry(string source, string dest, string backup)
        {
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try { File.Replace(source, dest, backup); return; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(150 * attempt);   // ~150ms .. 1.2s
                }
            }
            if (File.Exists(backup)) File.Delete(backup);   // stale .bak from a past failure
            File.Move(dest, backup);
            try { File.Move(source, dest); }
            catch
            {
                File.Move(backup, dest);   // put the original back; caller reports the error
                throw;
            }
        }

        // ---- Sharing (.knote export / .knote and .kndb import) ----
        // A .knote is just a KillerNotes database containing one note, optionally
        // SQLCipher-encrypted with a share password - the same engine opens both.

        /// <summary>Writes a single note to destPath as a .knote (overwrites).</summary>
        public static void ExportNote(long id, string destPath, string? password)
        {
            if (File.Exists(destPath)) File.Delete(destPath);
            var csb = new SqliteConnectionStringBuilder { DataSource = destPath };
            if (!string.IsNullOrEmpty(password)) csb.Password = password;
            try
            {
                using var dest = new SqliteConnection(csb.ConnectionString);
                dest.Open();
                using (var schema = dest.CreateCommand())
                {
                    schema.CommandText = SchemaSql;
                    schema.ExecuteNonQuery();
                }

                using var read = _db!.CreateCommand();
                read.CommandText =
                    "SELECT title, notebook, tags, created, modified, content, plain, title_color, spellcheck " +
                    "FROM notes WHERE id = $id";
                read.Parameters.AddWithValue("$id", id);
                using var r = read.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException("note not found");

                using var ins = dest.CreateCommand();
                ins.CommandText =
                    "INSERT INTO notes(title, notebook, tags, created, modified, content, plain, title_color, spellcheck) " +
                    "VALUES ($t, $n, $g, $c, $m, $b, $p, $tc, $sc)";
                ins.Parameters.AddWithValue("$t", r.GetString(0));
                ins.Parameters.AddWithValue("$n", r.GetString(1));
                ins.Parameters.AddWithValue("$g", r.GetString(2));
                ins.Parameters.AddWithValue("$c", r.GetString(3));
                ins.Parameters.AddWithValue("$m", r.GetString(4));
                ins.Parameters.AddWithValue("$b", r.GetValue(5));   // blob or DBNull
                ins.Parameters.AddWithValue("$p", r.GetString(6));
                ins.Parameters.AddWithValue("$tc", r.IsDBNull(7) ? "" : r.GetString(7));
                ins.Parameters.AddWithValue("$sc", r.IsDBNull(8) ? 0 : r.GetInt64(8));
                ins.ExecuteNonQuery();

                // Carry the note's tag definitions (name + color) so its chips keep their
                // colors on the receiving machine; the receiver's own defs win on import.
                foreach (string tag in SplitTags(r.GetString(2)))
                {
                    using var tc2 = _db!.CreateCommand();
                    tc2.CommandText = "SELECT color FROM tags WHERE name = $n";
                    tc2.Parameters.AddWithValue("$n", tag);
                    if (tc2.ExecuteScalar() is string col)
                    {
                        using var ti = dest.CreateCommand();
                        ti.CommandText = "INSERT OR IGNORE INTO tags(name, color) VALUES ($n, $c)";
                        ti.Parameters.AddWithValue("$n", tag);
                        ti.Parameters.AddWithValue("$c", col);
                        ti.ExecuteNonQuery();
                    }
                }
            }
            finally { SqliteConnection.ClearAllPools(); }   // release the dest file handle
        }

        /// <summary>Imports every note from a .knote/.kndb file into the OPEN database.
        /// Throws SqliteException on a wrong password. Returns the count imported.</summary>
        public static int ImportNotes(string sourcePath, string? password)
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
            };
            if (!string.IsNullOrEmpty(password)) csb.Password = password;
            try
            {
                using var src = new SqliteConnection(csb.ConnectionString);
                src.Open();

                // Files shared by a 1.0.0 install predate the title_color/spellcheck
                // columns - probe the source schema and substitute defaults, so old
                // .knote/.kndb files import forever.
                var srcCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var pi = src.CreateCommand())
                {
                    pi.CommandText = "PRAGMA table_info(notes)";
                    using var pr = pi.ExecuteReader();
                    while (pr.Read()) srcCols.Add(pr.GetString(1));
                }
                bool extras = srcCols.Contains("title_color") && srcCols.Contains("spellcheck");

                using var read = src.CreateCommand();
                read.CommandText = extras
                    ? "SELECT title, notebook, tags, created, modified, content, plain, title_color, spellcheck FROM notes"
                    : "SELECT title, notebook, tags, created, modified, content, plain, '', 0 FROM notes";
                using var r = read.ExecuteReader();
                int count = 0;
                while (r.Read())
                {
                    using var ins = _db!.CreateCommand();
                    // sort_order appends (see Create) so imports keep a custom arrangement intact.
                    ins.CommandText =
                        "INSERT INTO notes(title, notebook, tags, created, modified, content, plain, title_color, spellcheck, sort_order) " +
                        "VALUES ($t, $n, $g, $c, $m, $b, $p, $tc, $sc, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM notes))";
                    ins.Parameters.AddWithValue("$t", r.GetString(0));
                    ins.Parameters.AddWithValue("$n", r.GetString(1));
                    ins.Parameters.AddWithValue("$g", r.GetString(2));
                    ins.Parameters.AddWithValue("$c", r.GetString(3));
                    ins.Parameters.AddWithValue("$m", r.GetString(4));
                    ins.Parameters.AddWithValue("$b", r.GetValue(5));
                    ins.Parameters.AddWithValue("$p", r.GetString(6));
                    ins.Parameters.AddWithValue("$tc", r.IsDBNull(7) ? "" : r.GetString(7));
                    ins.Parameters.AddWithValue("$sc", r.IsDBNull(8) ? 0 : r.GetInt64(8));
                    ins.ExecuteNonQuery();
                    count++;
                }

                // Merge tag definitions carried by the shared file (files from before the
                // tags feature have no tags table). Local names win via INSERT OR IGNORE.
                using (var tchk = src.CreateCommand())
                {
                    tchk.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='tags'";
                    if ((long)tchk.ExecuteScalar()! > 0)
                    {
                        var defs = new List<(string N, string C)>();
                        using (var td = src.CreateCommand())
                        {
                            td.CommandText = "SELECT name, color FROM tags";
                            using var tr = td.ExecuteReader();
                            while (tr.Read()) defs.Add((tr.GetString(0), tr.GetString(1)));
                        }
                        foreach (var d in defs) AddTag(d.N, d.C);
                    }
                }
                return count;
            }
            finally { SqliteConnection.ClearAllPools(); }
        }

        // ---- Helpers ----

        // SqlCipherBootstrap must run first: it extracts and preloads the embedded native
        // e_sqlcipher.dll. The static provider (not Batteries_V2.Init) is deliberate: the
        // bundle's dynamic loader probes Assembly.Location, which is empty for Costura
        // in-memory assemblies and throws. Plain DllImport resolves to the preloaded module.
        static NoteStore()
        {
            SqlCipherBootstrap.EnsureLoaded();
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlcipher());
        }

        private static void Exec(string sql)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Every whitespace-separated word becomes a quoted prefix term, so user input can
        // never break the FTS5 MATCH syntax and partial words still hit ("proj kil" works).
        private static string FtsQuery(string raw) =>
            string.Join(" ", raw
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => "\"" + t.Replace("\"", "\"\"") + "\"*"));

        private static string Ts(DateTime dt) =>
            dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        private static DateTime ParseTs(string s) =>
            DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt) ? dt : DateTime.MinValue;

        private static string FirstLine(string s)
        {
            s = s.TrimStart();
            int nl = s.IndexOfAny(['\r', '\n']);
            return nl < 0 ? s : s.Substring(0, nl);
        }
    }
}
