using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KillerNotes.Services
{
    internal sealed class MarkdownFence
    {
        private char _marker;
        private int _length;
        private string _language = "Plain";

        public List<(int Start, string Text, string? Language)> ReadParagraph(string text)
        {
            var lines = new List<(int, string, string?)>();
            int start = 0;
            foreach (string line in text.Split('\n'))
            {
                lines.Add((start, line.TrimEnd('\r'), Read(line.TrimEnd('\r'))));
                start += line.Length + 1;
            }
            return lines;
        }

        // Null means prose, Markdown means a delimiter, and Plain means unlabeled code.
        public string? Read(string line)
        {
            var match = Regex.Match(line, @"^ {0,3}(`{3,}|~{3,})(.*)$");
            if (_length > 0)
            {
                if (match.Success && match.Groups[1].Value[0] == _marker &&
                    match.Groups[1].Length >= _length && string.IsNullOrWhiteSpace(match.Groups[2].Value))
                {
                    _length = 0;
                    return "Markdown";
                }
                return _language;
            }
            if (!match.Success) return null;
            string info = match.Groups[2].Value.Trim();
            if (match.Groups[1].Value[0] == '`' && info.Contains("`")) return null;
            _marker = match.Groups[1].Value[0];
            _length = match.Groups[1].Length;
            string[] names = info.Split(new[] { ' ', '\t', '{' }, StringSplitOptions.RemoveEmptyEntries);
            string name = names.Length > 0 ? names[0] : "";
            _language = name.ToLowerInvariant() switch
            {
                "powershell" or "ps1" or "pwsh" or "ps" => "PowerShell",
                "js" or "javascript" => "JavaScript", "ts" or "typescript" => "TypeScript",
                "cs" or "csharp" or "c#" => "CSharp", "py" or "python" => "Python",
                "sh" or "bash" or "shell" => "Bash", "html" => "Html", "xml" => "Xml",
                "xaml" => "Xaml", "vue" => "Vue", "json" => "Json", "yaml" or "yml" => "Yaml",
                "css" => "Css", "sql" => "Sql", "md" or "markdown" => "Markdown", _ => "Plain"
            };
            return "Markdown";
        }
    }
}
