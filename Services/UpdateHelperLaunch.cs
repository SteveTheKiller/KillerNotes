using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>
    /// Starts the self-update helper as one PowerShell process whose whole script travels in its command line, so no
    /// file the user account can write is read after the helper (possibly elevated) starts.
    /// </summary>
    internal static class UpdateHelperLaunch
    {
        internal static ProcessStartInfo StartInfo(string script, bool elevate)
        {
            using var compressed = new MemoryStream();
            byte[] source = Encoding.UTF8.GetBytes(script);
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(source, 0, source.Length);
            string bootstrap = "$stream=[IO.MemoryStream]::new([Convert]::FromBase64String('" + Convert.ToBase64String(compressed.ToArray()) + "'));" +
                "$reader=[IO.StreamReader]::new([IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionMode]::Decompress));" +
                "try{&([scriptblock]::Create($reader.ReadToEnd()))}finally{$reader.Dispose();$stream.Dispose()}";
            string arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap));
            if (arguments.Length > 30000) throw new IOException("The update helper command is too long.");
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"), arguments)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
            };
            if (elevate) start.Verb = "runas";
            return start;
        }

        /// <summary>Waits for each process id to exit. Zero and negative ids are skipped.</summary>
        internal static string WaitFor(params int[] processIds)
        {
            var script = new StringBuilder("$ErrorActionPreference = 'Stop'\n");
            foreach (int id in processIds)
                if (id > 0) script.Append("while (Get-Process -Id ").Append(id).Append(" -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }\n");
            return script.ToString();
        }
    }
}
