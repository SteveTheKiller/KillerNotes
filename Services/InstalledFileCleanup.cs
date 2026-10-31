using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KillerFamily.Deployment
{
    internal static class InstalledFileCleanup
    {
        public static void Remove(string directory, params string[] files)
        {
            foreach (string file in Paths(directory, files))
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            foreach (string folder in Folders(directory, files))
                try { Directory.Delete(folder, recursive: false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        public static string RemovalBatch(string directory, params string[] files)
        {
            var script = new StringBuilder("setlocal DisableDelayedExpansion\r\n");
            bool first = true;
            foreach (string file in Paths(directory, files))
            {
                string path = file.Replace("%", "%%");
                if (first) AppendRetriedDelete(script, path);
                else script.Append("del /q \"").Append(path).Append("\" >nul 2>&1\r\n");
                first = false;
            }
            foreach (string folder in Folders(directory, files))
                script.Append("rmdir \"").Append(folder.Replace("%", "%%")).Append("\" >nul 2>&1\r\n");
            return script.ToString();
        }

        // The first file is the exe, which stays locked until the app has finished and exited, and
        // that can take longer than the script's opening wait. About thirty seconds, then it gives up.
        private static void AppendRetriedDelete(StringBuilder script, string path) => script
            .Append("set /a tries=0\r\n")
            .Append(":retry\r\n")
            .Append("del /q \"").Append(path).Append("\" >nul 2>&1\r\n")
            .Append("if not exist \"").Append(path).Append("\" goto removed\r\n")
            .Append("set /a tries+=1\r\n")
            .Append("if %tries% geq 30 goto removed\r\n")
            .Append("ping -n 2 127.0.0.1 >nul\r\n")
            .Append("goto retry\r\n")
            .Append(":removed\r\n");

        private static IEnumerable<string> Paths(string directory, string[] files)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string file in files)
            {
                if (Path.IsPathRooted(file) || file.IndexOfAny(new[] { ':', '*', '?', '"', '\r', '\n' }) >= 0)
                    throw new ArgumentException("Installed file must be a relative literal path.");
                string path = Path.GetFullPath(Path.Combine(root, file));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Installed file is outside the installation.");
                bool linked = false;
                for (string? parent = Path.GetDirectoryName(path); parent != null && parent.Length >= root.Length - 1;
                     parent = Path.GetDirectoryName(parent))
                    if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    { linked = true; break; }
                if (!linked) yield return path;
            }
        }

        private static IEnumerable<string> Folders(string directory, string[] files)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            return Paths(directory, files).Select(Path.GetDirectoryName).Where(path => path != null)
                .Cast<string>().Append(root).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(path => path.Length);
        }
    }
}
