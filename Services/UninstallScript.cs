using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using KillerFamily.Deployment;

namespace KillerNotes.Services
{
    /// <summary>The deferred self-delete an uninstall hands to cmd, so the exe can exit first.</summary>
    internal static class UninstallScript
    {
        /// <summary>With <paramref name="removeFolder"/> the script also removes the folder it sits in, on the
        /// same line that deletes the script, because cmd cannot read another line from a deleted file.</summary>
        internal static string Build(string directory, string[] files, int pings = 3, bool removeFolder = false) =>
            "@echo off\r\n" +
            // cmd reads this file in the console's code page, and the install path can hold any
            // character (a user name with an accent, say). The file is written as UTF-8.
            "chcp 65001 >nul\r\n" +
            "ping -n " + pings + " 127.0.0.1 >nul\r\n" +
            InstalledFileCleanup.RemovalBatch(directory, files) +
            (removeFolder ? "del \"%~f0\" >nul 2>&1 & rmdir \"%~dp0\" >nul 2>&1\r\n" : "del \"%~f0\"\r\n");

        /// <summary>A new folder with a random name that only SYSTEM and Administrators can open, so
        /// nothing running as the signed-in user can change a script before an elevated cmd runs it.</summary>
        internal static string CreateAdminOnlyFolder(string root)
        {
            string folder = Path.Combine(root, "KillerTools-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(folder)) throw new IOException("The script folder already exists.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (string sid in new[] { "S-1-5-32-544", "S-1-5-18" })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(folder, security);
            return folder;
        }

        /// <summary>Writes the script and returns its path. An elevated script goes in its own
        /// administrators-only folder under the Windows directory; the others go in the user's temp
        /// folder under a random name.</summary>
        internal static string Write(string script, bool elevated, string? adminRoot = null)
        {
            string folder = elevated
                ? CreateAdminOnlyFolder(adminRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"))
                : Path.GetTempPath();
            string bat = Path.Combine(folder, "killernotes_uninstall_" + Guid.NewGuid().ToString("N") + ".bat");
            byte[] bytes = new UTF8Encoding(false).GetBytes(script);
            using var stream = new FileStream(bat, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            return bat;
        }
    }
}
