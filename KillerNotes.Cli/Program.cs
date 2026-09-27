using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;

#pragma warning disable IDE0130
namespace KillerNotes.Cli
{
    [DataContract]
    public sealed class NoteMatch
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "notebook")] public string Notebook { get; set; } = "";
        [DataMember(Name = "tags")] public string Tags { get; set; } = "";
        [DataMember(Name = "snippet")] public string Snippet { get; set; } = "";
        [DataMember(Name = "modified")] public string Modified { get; set; } = "";
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine("KillerNotes CLI: search <query> [--limit 1..20] [--database absolute.db]");
                return 0;
            }
            if (args.Length < 2 || args[0] != "search")
            {
                Console.Error.WriteLine("Expected search <query> [--limit 1..20] [--database absolute.db]");
                return 2;
            }
            string query = args[1];
            int limit = 10;
            string? database = null;
            for (int i = 2; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) return 2;
                if (args[i] == "--limit" && int.TryParse(args[i + 1], out int count) && count >= 1 && count <= 20)
                    limit = count;
                else if (args[i] == "--database" && Path.IsPathRooted(args[i + 1]))
                    database = args[i + 1];
                else return 2;
            }
            if (string.IsNullOrWhiteSpace(query) || query.Length > 200) return 2;
            try
            {
                var matches = Search(database ?? NoteStore.DbPath, query, limit);
                var serializer = new DataContractJsonSerializer(typeof(List<NoteMatch>));
                using var stream = new MemoryStream();
                serializer.WriteObject(stream, matches);
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 26 || ex.SqliteExtendedErrorCode == 26)
            {
                Console.Error.WriteLine("KillerNotes search cannot open an encrypted or unreadable database. Unlock support is not available yet.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("KillerNotes search failed: " + ex.Message);
                return 1;
            }
        }

        public static List<NoteMatch> Search(string database, string query, int limit)
        {
            if (!Path.IsPathRooted(database) || !File.Exists(database))
                throw new FileNotFoundException("The notes database was not found");
            if (string.IsNullOrWhiteSpace(query) || query.Length > 200 || limit < 1 || limit > 20)
                throw new ArgumentException("Invalid search bounds");
            // Initialize KillerNotes' SQLCipher provider without opening or migrating a database.
            _ = NoteStore.DefaultDbDir;
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using (var readOnly = connection.CreateCommand())
            {
                readOnly.CommandText = "PRAGMA query_only = ON";
                readOnly.ExecuteNonQuery();
            }
            var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var clauses = new List<string>();
            using var command = connection.CreateCommand();
            for (int i = 0; i < words.Length; i++)
            {
                clauses.Add($"(title LIKE $w{i} ESCAPE '\\' OR plain LIKE $w{i} ESCAPE '\\' OR tags LIKE $w{i} ESCAPE '\\')");
                command.Parameters.AddWithValue($"$w{i}", "%" + EscapeLike(words[i]) + "%");
            }
            command.CommandText = "SELECT id, title, notebook, tags, substr(plain, 1, 240), modified " +
                "FROM notes WHERE deleted = '' AND " + string.Join(" AND ", clauses) +
                " ORDER BY modified DESC, id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", limit);
            var matches = new List<NoteMatch>();
            using var rows = command.ExecuteReader();
            while (rows.Read())
                matches.Add(new NoteMatch
                {
                    Id = rows.GetInt64(0), Title = rows.GetString(1), Notebook = rows.GetString(2),
                    Tags = rows.GetString(3), Snippet = rows.IsDBNull(4) ? "" : rows.GetString(4),
                    Modified = rows.GetString(5),
                });
            return matches;
        }

        private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
#pragma warning restore IDE0130
