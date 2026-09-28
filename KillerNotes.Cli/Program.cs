using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;

#pragma warning disable IDE0130
namespace KillerNotes.Cli
{
    [DataContract]
    public sealed class McpRequest
    {
        [DataMember(Name = "arguments")] public string[] Arguments { get; set; } = [];
    }

    [DataContract]
    public sealed class McpResponse
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "json")] public string Json { get; set; } = "";
        [DataMember(Name = "error")] public string Error { get; set; } = "";
    }

    [DataContract]
    public sealed class NoteSummary
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "group")] public string Group { get; set; } = "";
        [DataMember(Name = "tags")] public string Tags { get; set; } = "";
        [DataMember(Name = "snippet")] public string Snippet { get; set; } = "";
        [DataMember(Name = "created")] public string Created { get; set; } = "";
        [DataMember(Name = "modified")] public string Modified { get; set; } = "";
        [DataMember(Name = "format")] public string Format { get; set; } = "rich";
        [DataMember(Name = "pinned")] public bool Pinned { get; set; }
    }

    [DataContract]
    public sealed class NoteContent
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "group")] public string Group { get; set; } = "";
        [DataMember(Name = "tags")] public string Tags { get; set; } = "";
        [DataMember(Name = "created")] public string Created { get; set; } = "";
        [DataMember(Name = "modified")] public string Modified { get; set; } = "";
        [DataMember(Name = "format")] public string Format { get; set; } = "rich";
        [DataMember(Name = "pinned")] public bool Pinned { get; set; }
        [DataMember(Name = "content")] public string Content { get; set; } = "";
        [DataMember(Name = "truncated")] public bool Truncated { get; set; }
    }

    [DataContract]
    public sealed class NamedItem
    {
        [DataMember(Name = "name")] public string Name { get; set; } = "";
        [DataMember(Name = "color")] public string Color { get; set; } = "";
        [DataMember(Name = "count")] public int Count { get; set; }
    }

    [DataContract]
    public sealed class NoteLink
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "resolved")] public bool Resolved { get; set; }
    }

    [DataContract]
    public sealed class NoteVersion
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "saved")] public string Saved { get; set; } = "";
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "format")] public string Format { get; set; } = "rich";
        [DataMember(Name = "size")] public long Size { get; set; }
    }

    [DataContract]
    public sealed class VaultStats
    {
        [DataMember(Name = "notes")] public int Notes { get; set; }
        [DataMember(Name = "groups")] public int Groups { get; set; }
        [DataMember(Name = "tags")] public int Tags { get; set; }
        [DataMember(Name = "links")] public int Links { get; set; }
        [DataMember(Name = "historyVersions")] public int HistoryVersions { get; set; }
        [DataMember(Name = "trashedNotes")] public int TrashedNotes { get; set; }
        [DataMember(Name = "pinnedNotes")] public int PinnedNotes { get; set; }
        [DataMember(Name = "markdownNotes")] public int MarkdownNotes { get; set; }
    }

    public static class Program
    {
        private const int DefaultLimit = 20;
        private const int MaximumLimit = 50;
        private const int DefaultContentLimit = 12000;
        private const int MaximumContentLimit = 50000;
        private const string SummarySelect = "SELECT id, title, notebook, tags, substr(plain, 1, 240), created, modified, format, pinned FROM notes";

        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--help") { PrintHelp(); return 0; }
            try
            {
                var values = args.ToList();
                string database = TakeStringOption(values, "--database") ?? NoteStore.DbPath;
                if (values.Count == 0) return UsageError();
                string command = values[0].ToLowerInvariant();
                if (IsWriteCommand(command)) return ForwardWrite(args);
                values.RemoveAt(0);
                switch (command)
                {
                    case "search":
                        int searchLimit = TakeLimit(values, 1); RequireCount(values, 1);
                        WriteJson(Search(database, values[0], searchLimit)); break;
                    case "list":
                        string? group = TakeStringOption(values, "--group");
                        string? tag = TakeStringOption(values, "--tag");
                        int listLimit = TakeLimit(values); RequireCount(values, 0);
                        WriteJson(List(database, group, tag, listLimit)); break;
                    case "get":
                        int maxChars = TakeIntOption(values, "--max-chars", DefaultContentLimit, 1, MaximumContentLimit, 1);
                        RequireCount(values, 1); WriteJson(Get(database, ParsePositiveId(values[0]), maxChars)); break;
                    case "groups": RequireCount(values, 0); WriteJson(Groups(database)); break;
                    case "tags": RequireCount(values, 0); WriteJson(Tags(database)); break;
                    case "backlinks":
                        int backlinksLimit = TakeLimit(values, 1); RequireCount(values, 1);
                        WriteJson(Backlinks(database, ParsePositiveId(values[0]), backlinksLimit)); break;
                    case "links":
                        int linksLimit = TakeLimit(values, 1); RequireCount(values, 1);
                        WriteJson(Links(database, ParsePositiveId(values[0]), linksLimit)); break;
                    case "history":
                        int historyLimit = TakeLimit(values, 1); RequireCount(values, 1);
                        WriteJson(History(database, ParsePositiveId(values[0]), historyLimit)); break;
                    case "stats": RequireCount(values, 0); WriteJson(Stats(database)); break;
                    default: return UsageError();
                }
                return 0;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 26 || ex.SqliteExtendedErrorCode == 26)
            {
                Console.Error.WriteLine("KillerNotes cannot open an encrypted or unreadable database. Unlock support is not available yet.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("KillerNotes command failed: " + ex.Message);
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("KillerNotes CLI read-only commands:");
            Console.WriteLine("  search <query> [--limit 1..50] [--database absolute.db]");
            Console.WriteLine("  list [--group name] [--tag name] [--limit 1..50] [--database absolute.db]");
            Console.WriteLine("  get <id> [--max-chars 1..50000] [--database absolute.db]");
            Console.WriteLine("  groups|tags|stats [--database absolute.db]");
            Console.WriteLine("  backlinks|links|history <id> [--limit 1..50] [--database absolute.db]");
            Console.WriteLine("  create --title title --content markdown [--group name] [--tags names] [--title-color color]");
            Console.WriteLine("  update --id id [--title title] [--content markdown] [--group name] [--tags names] [--title-color color]");
            Console.WriteLine("  create-group --name name [--parent path] [--color color]");
            Console.WriteLine("  set-group-color --name path --color color");
            Console.WriteLine("  set-title-color --id id --color color");
            Console.WriteLine("  import-image --path absolute-image [--group name]");
            Console.WriteLine("  export --id id --output new-absolute-path.(txt|md|html|knote)");
        }

        private static bool IsWriteCommand(string command) => command is "create" or "update" or "create-group" or "set-group-color" or "set-title-color" or "import-image" or "export";

        private static int ForwardWrite(string[] args)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", $"KillerNotes-MCP-{Process.GetCurrentProcess().SessionId}", PipeDirection.InOut);
                pipe.Connect(3000);
                byte[] request = Serialize(new McpRequest { Arguments = args });
                var writer = new BinaryWriter(pipe, Encoding.UTF8, true);
                writer.Write(request.Length); writer.Write(request); writer.Flush();
                var reader = new BinaryReader(pipe, Encoding.UTF8, true);
                int responseLength = reader.ReadInt32();
                if (responseLength < 1 || responseLength > 1_000_000) throw new InvalidDataException("KillerNotes returned an invalid response");
                var response = Deserialize<McpResponse>(reader.ReadBytes(responseLength));
                if (response == null || !response.Success) { Console.Error.WriteLine(response?.Error ?? "KillerNotes returned no response"); return 1; }
                Console.WriteLine(response.Json); return 0;
            }
            catch (TimeoutException) { Console.Error.WriteLine("Open and unlock KillerNotes before using write commands"); return 1; }
            catch (IOException ex) { Console.Error.WriteLine("KillerNotes bridge failed: " + ex.Message); return 1; }
        }

        private static int UsageError()
        {
            Console.Error.WriteLine("Run KillerNotes --cli --help for supported commands.");
            return 2;
        }

        public static List<NoteSummary> Search(string database, string query, int limit)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length > 200) throw new ArgumentException("Invalid search query");
            using var connection = OpenReadOnly(database);
            using var command = connection.CreateCommand();
            var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var clauses = new List<string>();
            for (int i = 0; i < words.Length; i++)
            {
                clauses.Add($"(title LIKE $w{i} ESCAPE '\\' OR plain LIKE $w{i} ESCAPE '\\' OR tags LIKE $w{i} ESCAPE '\\')");
                command.Parameters.AddWithValue($"$w{i}", "%" + EscapeLike(words[i]) + "%");
            }
            command.CommandText = SummarySelect + " WHERE deleted = '' AND " + string.Join(" AND ", clauses) + " ORDER BY modified DESC, id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", ValidateLimit(limit));
            return ReadSummaries(command);
        }

        public static List<NoteSummary> List(string database, string? group, string? tag, int limit)
        {
            using var connection = OpenReadOnly(database);
            using var command = connection.CreateCommand();
            var clauses = new List<string> { "deleted = ''" };
            if (!string.IsNullOrWhiteSpace(group)) { clauses.Add("notebook = $group COLLATE NOCASE"); command.Parameters.AddWithValue("$group", group!.Trim()); }
            if (!string.IsNullOrWhiteSpace(tag))
            {
                clauses.Add("(',' || replace(tags, ', ', ',') || ',') LIKE $tag ESCAPE '\\' COLLATE NOCASE");
                command.Parameters.AddWithValue("$tag", "%," + EscapeLike(tag!.Trim()) + ",%");
            }
            command.CommandText = SummarySelect + " WHERE " + string.Join(" AND ", clauses) + " ORDER BY pinned DESC, modified DESC, id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", ValidateLimit(limit));
            return ReadSummaries(command);
        }

        public static NoteContent Get(string database, long id, int maximumCharacters)
        {
            using var connection = OpenReadOnly(database);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, title, notebook, tags, created, modified, format, pinned, plain FROM notes WHERE id = $id AND deleted = ''";
            command.Parameters.AddWithValue("$id", id);
            using var row = command.ExecuteReader();
            if (!row.Read()) throw new InvalidOperationException("The note was not found");
            string content = row.IsDBNull(8) ? "" : row.GetString(8);
            int cap = ValidateContentLimit(maximumCharacters);
            return new NoteContent
            {
                Id = row.GetInt64(0), Title = row.GetString(1), Group = row.GetString(2), Tags = row.GetString(3),
                Created = row.GetString(4), Modified = row.GetString(5), Format = FormatName(row.GetInt64(6)),
                Pinned = row.GetInt64(7) != 0, Content = content.Length <= cap ? content : content[..cap], Truncated = content.Length > cap,
            };
        }

        public static List<NamedItem> Groups(string database)
        {
            using var connection = OpenReadOnly(database); using var command = connection.CreateCommand();
            command.CommandText = "SELECT g.name, g.color, count(n.id) FROM groups g LEFT JOIN notes n ON n.notebook = g.name COLLATE NOCASE AND n.deleted = '' GROUP BY g.name, g.color ORDER BY g.sort_order, g.name COLLATE NOCASE";
            return ReadNamedItems(command);
        }

        public static List<NamedItem> Tags(string database)
        {
            using var connection = OpenReadOnly(database); using var command = connection.CreateCommand();
            command.CommandText = "SELECT t.name, t.color, count(n.id) FROM tags t LEFT JOIN notes n ON n.deleted = '' AND (',' || replace(n.tags, ', ', ',') || ',') LIKE '%,' || replace(t.name, '%', '\\%') || ',%' ESCAPE '\\' COLLATE NOCASE GROUP BY t.name, t.color ORDER BY t.name COLLATE NOCASE";
            return ReadNamedItems(command);
        }

        public static List<NoteLink> Backlinks(string database, long id, int limit)
        {
            using var connection = OpenReadOnly(database); using var command = connection.CreateCommand();
            command.CommandText = "SELECT n.id, n.title FROM notes target JOIN note_links l ON l.target = target.title COLLATE NOCASE JOIN notes n ON n.id = l.src AND n.deleted = '' WHERE target.id = $id AND target.deleted = '' ORDER BY n.modified DESC LIMIT $limit";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$limit", ValidateLimit(limit));
            return ReadLinks(command, true);
        }

        public static List<NoteLink> Links(string database, long id, int limit)
        {
            using var connection = OpenReadOnly(database); using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(dst.id, -1), l.target FROM note_links l JOIN notes src ON src.id = l.src AND src.deleted = '' LEFT JOIN notes dst ON dst.title = l.target COLLATE NOCASE AND dst.deleted = '' WHERE l.src = $id ORDER BY l.target COLLATE NOCASE LIMIT $limit";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$limit", ValidateLimit(limit));
            return ReadLinks(command, null);
        }

        public static List<NoteVersion> History(string database, long id, int limit)
        {
            using var connection = OpenReadOnly(database); using var command = connection.CreateCommand();
            command.CommandText = "SELECT h.id, h.saved, h.title, h.format, length(h.content) FROM note_history h JOIN notes n ON n.id = h.note_id AND n.deleted = '' WHERE h.note_id = $id ORDER BY h.saved DESC, h.id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$limit", ValidateLimit(limit));
            var result = new List<NoteVersion>(); using var rows = command.ExecuteReader();
            while (rows.Read()) result.Add(new NoteVersion { Id = rows.GetInt64(0), Saved = rows.GetString(1), Title = rows.GetString(2), Format = FormatName(rows.GetInt64(3)), Size = rows.IsDBNull(4) ? 0 : rows.GetInt64(4) });
            return result;
        }

        public static VaultStats Stats(string database)
        {
            using var connection = OpenReadOnly(database);
            return new VaultStats
            {
                Notes = Scalar(connection, "SELECT count(*) FROM notes WHERE deleted = ''"), Groups = Scalar(connection, "SELECT count(*) FROM groups"),
                Tags = Scalar(connection, "SELECT count(*) FROM tags"), Links = Scalar(connection, "SELECT count(*) FROM note_links l JOIN notes n ON n.id = l.src AND n.deleted = ''"),
                HistoryVersions = Scalar(connection, "SELECT count(*) FROM note_history h JOIN notes n ON n.id = h.note_id AND n.deleted = ''"),
                TrashedNotes = Scalar(connection, "SELECT count(*) FROM notes WHERE deleted <> ''"), PinnedNotes = Scalar(connection, "SELECT count(*) FROM notes WHERE deleted = '' AND pinned <> 0"),
                MarkdownNotes = Scalar(connection, "SELECT count(*) FROM notes WHERE deleted = '' AND format = 1"),
            };
        }

        private static SqliteConnection OpenReadOnly(string database)
        {
            if (!Path.IsPathRooted(database) || !File.Exists(database)) throw new FileNotFoundException("The notes database was not found");
            _ = NoteStore.DefaultDbDir;
            var builder = new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            var connection = new SqliteConnection(builder.ConnectionString); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA query_only = ON"; command.ExecuteNonQuery();
            return connection;
        }

        private static List<NoteSummary> ReadSummaries(SqliteCommand command)
        {
            var result = new List<NoteSummary>(); using var rows = command.ExecuteReader();
            while (rows.Read()) result.Add(new NoteSummary
            {
                Id = rows.GetInt64(0), Title = rows.GetString(1), Group = rows.GetString(2), Tags = rows.GetString(3), Snippet = rows.IsDBNull(4) ? "" : rows.GetString(4),
                Created = rows.GetString(5), Modified = rows.GetString(6), Format = FormatName(rows.GetInt64(7)), Pinned = rows.GetInt64(8) != 0,
            });
            return result;
        }

        private static List<NamedItem> ReadNamedItems(SqliteCommand command)
        {
            var result = new List<NamedItem>(); using var rows = command.ExecuteReader();
            while (rows.Read()) result.Add(new NamedItem { Name = rows.GetString(0), Color = rows.GetString(1), Count = rows.GetInt32(2) });
            return result;
        }

        private static List<NoteLink> ReadLinks(SqliteCommand command, bool? resolved)
        {
            var result = new List<NoteLink>(); using var rows = command.ExecuteReader();
            while (rows.Read()) { long id = rows.GetInt64(0); result.Add(new NoteLink { Id = id, Title = rows.GetString(1), Resolved = resolved ?? id >= 0 }); }
            return result;
        }

        private static int Scalar(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void WriteJson<T>(T value)
        {
            Console.WriteLine(SerializeJson(value));
        }

        internal static string SerializeJson<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T)); using var stream = new MemoryStream();
            serializer.WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray());
        }

        internal static byte[] Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T)); using var stream = new MemoryStream();
            serializer.WriteObject(stream, value); return stream.ToArray();
        }

        internal static T Deserialize<T>(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(stream) ?? throw new InvalidDataException("Invalid bridge message"));
        }

        private static int TakeLimit(List<string> values, int positionalCount = 0) => TakeIntOption(values, "--limit", DefaultLimit, 1, MaximumLimit, positionalCount);
        private static int TakeIntOption(List<string> values, string name, int fallback, int minimum, int maximum, int positionalCount = 0)
        {
            int index = values.IndexOf(name); if (index < 0) return fallback;
            if (index < positionalCount || index + 1 >= values.Count || !int.TryParse(values[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < minimum || parsed > maximum)
                throw new ArgumentException("Invalid " + name + " value");
            values.RemoveRange(index, 2); return parsed;
        }

        private static string? TakeStringOption(List<string> values, string name)
        {
            int index = values.IndexOf(name); if (index < 0) return null;
            if (index + 1 >= values.Count || string.IsNullOrWhiteSpace(values[index + 1])) throw new ArgumentException("Invalid " + name + " value");
            string value = values[index + 1]; values.RemoveRange(index, 2); return value;
        }

        private static void RequireCount(List<string> values, int count) { if (values.Count != count) throw new ArgumentException("Invalid command arguments"); }
        private static long ParsePositiveId(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0 ? id : throw new ArgumentException("Invalid note id");
        private static int ValidateLimit(int value) => value is >= 1 and <= MaximumLimit ? value : throw new ArgumentException("Invalid result limit");
        private static int ValidateContentLimit(int value) => value is >= 1 and <= MaximumContentLimit ? value : throw new ArgumentException("Invalid content limit");
        private static string FormatName(long value) => value == 1 ? "markdown" : "rich";
        private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
#pragma warning restore IDE0130
