using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace KillerNotes.Services
{
    public sealed class GroupUndoUnavailableException : InvalidOperationException { }

    public sealed class GroupUndoConflictException : InvalidOperationException
    {
        public string Path { get; }
        public GroupUndoConflictException(string path) : base("A group already exists at the undo destination.") => Path = path;
    }

    public static partial class NoteStore
    {
        internal static void EnsureGroupUndoPathsAvailable(IEnumerable<string> paths, string? excludedSourcePath = null)
        {
            if (!IsOpen || IsReadOnly) throw new GroupUndoUnavailableException();
            var existing = new HashSet<string>(ListGroupTree()
                .Where(g => excludedSourcePath == null ||
                    !(string.Equals(g.Path, excludedSourcePath, StringComparison.OrdinalIgnoreCase) ||
                      g.Path.StartsWith(excludedSourcePath + GroupSep, StringComparison.OrdinalIgnoreCase)))
                .Select(g => g.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
                if (existing.Contains(path)) throw new GroupUndoConflictException(path);
        }

        /// <summary>Deletes a group branch and returns the action that restores it.</summary>
        public static Action? DeleteGroupWithUndo(string path)
        {
            if (!IsOpen || IsReadOnly) return null;
            var groups = new List<(string Path, string Parent, long Order, bool Collapsed, string Color)>();
            var notes = new List<(long Id, string Group)>();
            using (var command = _db!.CreateCommand())
            {
                command.CommandText = "SELECT name, parent, sort_order, collapsed, color FROM groups WHERE name = $path OR name LIKE $branch ESCAPE '\\' ORDER BY sort_order, rowid";
                command.Parameters.AddWithValue("$path", path);
                command.Parameters.AddWithValue("$branch", EscapeLike(path + GroupSep) + "%");
                using var reader = command.ExecuteReader();
                while (reader.Read()) groups.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3) != 0, reader.IsDBNull(4) ? "" : reader.GetString(4)));
            }
            if (groups.Count == 0) return null;
            using (var command = _db!.CreateCommand())
            {
                command.CommandText = "SELECT id, notebook FROM notes WHERE notebook = $path OR notebook LIKE $branch ESCAPE '\\'";
                command.Parameters.AddWithValue("$path", path);
                command.Parameters.AddWithValue("$branch", EscapeLike(path + GroupSep) + "%");
                using var reader = command.ExecuteReader();
                while (reader.Read()) notes.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            string databasePath = Path.GetFullPath(DbPath);
            DeleteGroup(path);
            return () =>
            {
                if (!IsOpen || IsReadOnly || !string.Equals(Path.GetFullPath(DbPath), databasePath, StringComparison.OrdinalIgnoreCase))
                    throw new GroupUndoUnavailableException();
                EnsureGroupUndoPathsAvailable(groups.Select(g => g.Path));
                using var transaction = _db.BeginTransaction();
                foreach (var group in groups)
                {
                    using var command = _db.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO groups(name, parent, sort_order, collapsed, color) VALUES ($name, $parent, $order, $collapsed, $color)";
                    command.Parameters.AddWithValue("$name", group.Path);
                    command.Parameters.AddWithValue("$parent", group.Parent);
                    command.Parameters.AddWithValue("$order", group.Order);
                    command.Parameters.AddWithValue("$collapsed", group.Collapsed ? 1 : 0);
                    command.Parameters.AddWithValue("$color", group.Color);
                    command.ExecuteNonQuery();
                }
                foreach (var note in notes)
                {
                    using var command = _db.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE notes SET notebook = $group WHERE id = $id";
                    command.Parameters.AddWithValue("$group", note.Group);
                    command.Parameters.AddWithValue("$id", note.Id);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            };
        }
    }
}
