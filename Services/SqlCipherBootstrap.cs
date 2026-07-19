using System.IO;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>
    /// Keeps the single-exe build self-sufficient for SQLCipher. The native e_sqlcipher.dll (x64)
    /// is embedded as a resource and self-extracted to a per-version cache on first use, the same
    /// pattern KillerPDF uses for its OCR natives (OcrNativeBootstrap). Must run before
    /// SQLitePCL.Batteries_V2.Init() - see the NoteStore static constructor. Thread-safe.
    /// </summary>
    internal static class SqlCipherBootstrap
    {
        private const string ResourceName = "KillerNotes.SqlCipherNative.e_sqlcipher.dll";

        private static readonly object _gate = new();
        private static bool _ready;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>
        /// Extracts the embedded native (skipped when the cached copy already matches by length)
        /// and preloads it, so the SQLitePCLRaw provider's LoadLibrary("e_sqlcipher") resolves to
        /// this module. Natives live in a per-version cache because they must match the app.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_ready) return;
            lock (_gate)
            {
                if (_ready) return;

                var asm = typeof(SqlCipherBootstrap).Assembly;
                string version = asm.GetName().Version?.ToString() ?? "0";
                string nativeDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KillerNotes", "native", version);
                string target = Path.Combine(nativeDir, "e_sqlcipher.dll");

                try
                {
                    Directory.CreateDirectory(nativeDir);
                    using var src = asm.GetManifestResourceStream(ResourceName);
                    if (src != null &&
                        (!File.Exists(target) || new FileInfo(target).Length != src.Length))
                    {
                        // Write to a temp name then swap, so a crash mid-extract never leaves a
                        // half-written dll behind for the next launch to load.
                        string tmp = target + ".tmp";
                        using (var dst = File.Create(tmp))
                            src.CopyTo(dst);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(tmp, target);
                    }
                }
                catch (IOException)
                {
                    // Another instance may be mid-extract or holding the dll; if the file exists
                    // in any complete form the preload below still works.
                }

                if (File.Exists(target))
                {
                    SetDllDirectory(nativeDir);
                    LoadLibrary(target);
                }

                _ready = true;
            }
        }
    }
}
