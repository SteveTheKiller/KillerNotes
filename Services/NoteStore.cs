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

        public static string DbDir  => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KillerNotes");

        /// <summary>File name of the active database inside DbDir ("ActiveDatabase" setting;
        /// defaults to notes.db). The Manage databases dialog switches and renames these.</summary>
        public static string ActiveDbFile =>
            App.GetSetting("ActiveDatabase") is string s && !string.IsNullOrWhiteSpace(s) ? s : "notes.db";

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
    plain    TEXT NOT NULL DEFAULT ''
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
END;";

        private static void EnsureSchema() => Exec(SchemaSql);

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
SELECT n.id, n.title, n.notebook, n.tags, n.created, n.modified, substr(n.plain, 1, 120)
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
                    _              => "created ASC",
                };
                cmd.CommandText = "SELECT id, title, notebook, tags, created, modified, substr(plain, 1, 120) " +
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
                });
            }
            return results;
        }

        public static long Create(string title)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "INSERT INTO notes(title, created, modified, plain) VALUES ($t, $now, $now, ''); " +
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

        public static void Delete(long id)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "DELETE FROM notes WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
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

            string bak = DbPath + ".bak";
            File.Replace(tmp, DbPath, bak);
            Open(newPassword);
            File.Delete(bak);   // only once the rewritten db opened cleanly
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
                    "SELECT title, notebook, tags, created, modified, content, plain FROM notes WHERE id = $id";
                read.Parameters.AddWithValue("$id", id);
                using var r = read.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException("note not found");

                using var ins = dest.CreateCommand();
                ins.CommandText =
                    "INSERT INTO notes(title, notebook, tags, created, modified, content, plain) " +
                    "VALUES ($t, $n, $g, $c, $m, $b, $p)";
                ins.Parameters.AddWithValue("$t", r.GetString(0));
                ins.Parameters.AddWithValue("$n", r.GetString(1));
                ins.Parameters.AddWithValue("$g", r.GetString(2));
                ins.Parameters.AddWithValue("$c", r.GetString(3));
                ins.Parameters.AddWithValue("$m", r.GetString(4));
                ins.Parameters.AddWithValue("$b", r.GetValue(5));   // blob or DBNull
                ins.Parameters.AddWithValue("$p", r.GetString(6));
                ins.ExecuteNonQuery();
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
                using var read = src.CreateCommand();
                read.CommandText =
                    "SELECT title, notebook, tags, created, modified, content, plain FROM notes";
                using var r = read.ExecuteReader();
                int count = 0;
                while (r.Read())
                {
                    using var ins = _db!.CreateCommand();
                    ins.CommandText =
                        "INSERT INTO notes(title, notebook, tags, created, modified, content, plain) " +
                        "VALUES ($t, $n, $g, $c, $m, $b, $p)";
                    ins.Parameters.AddWithValue("$t", r.GetString(0));
                    ins.Parameters.AddWithValue("$n", r.GetString(1));
                    ins.Parameters.AddWithValue("$g", r.GetString(2));
                    ins.Parameters.AddWithValue("$c", r.GetString(3));
                    ins.Parameters.AddWithValue("$m", r.GetString(4));
                    ins.Parameters.AddWithValue("$b", r.GetValue(5));
                    ins.Parameters.AddWithValue("$p", r.GetString(6));
                    ins.ExecuteNonQuery();
                    count++;
                }
                return count;
            }
            finally { SqliteConnection.ClearAllPools(); }
        }

        // ---- Helpers ----

        static NoteStore() => SQLitePCL.Batteries_V2.Init();

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
