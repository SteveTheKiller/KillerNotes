using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>
    /// Self-extracts the bundled audio codec natives (libFLAC, libmp3lame) next to the SQLCipher
    /// one, using the same per-version cache and the same reasoning: Costura embeds MANAGED
    /// assemblies only, so a native DLL has to be a manifest resource that the app writes out and
    /// preloads itself.
    ///
    /// Unlike SqlCipherBootstrap this is ALLOWED TO FAIL. SQLCipher missing means no database and
    /// no app; a codec missing means recordings fall back to WAV, which still works. So every entry
    /// point reports availability rather than throwing, and the callers degrade.
    /// </summary>
    internal static class AudioNativeBootstrap
    {
        internal const string FlacDll = "libFLAC.dll";
        internal const string LameDll = "libmp3lame.dll";

        /// <summary>
        /// Whisper's natives, IN DEPENDENCY ORDER. ggml-base first, then the CPU backend, then the
        /// ggml facade, then whisper itself - each links against the ones before it. Preloading them
        /// out of order leaves the loader hunting the PATH for a dependency that has not been
        /// extracted yet, and whisper silently reports unavailable.
        /// </summary>
        private static readonly string[] WhisperDlls =
            ["ggml-base.dll", "ggml-cpu.dll", "ggml.dll", "libwhisper.dll"];

        private static readonly object _gate = new();
        private static bool _done;
        private static string _dir = "";

        internal static bool FlacAvailable { get; private set; }
        internal static bool LameAvailable { get; private set; }
        internal static bool WhisperAvailable { get; private set; }

        /// <summary>Folder the natives were extracted to. Whisper's model files live beside them.</summary>
        internal static string NativeDir
        {
            get { EnsureLoaded(); return _dir; }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>Dumps the diagnostics to the debug output on first use, so a missing codec shows
        /// up in the same window as everything else rather than needing a breakpoint.</summary>
        internal static void Trace() => System.Diagnostics.Debug.WriteLine(
            "[audio-native]\n" + Diagnostics());

        /// <summary>Extracts and preloads whichever codecs are bundled. Safe to call repeatedly and
        /// from any thread; only the first call does work.</summary>
        internal static void EnsureLoaded()
        {
            if (_done) return;
            lock (_gate)
            {
                if (_done) return;
                _done = true;

                var asm = typeof(AudioNativeBootstrap).Assembly;
                string version = asm.GetName().Version?.ToString() ?? "0";
                _dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KillerNotes", "native", version);

                // The loader resolves a DLL's OWN imports by name, and whisper's DLLs import each
                // other. Pointing it at the extract folder means a dependency that was not preloaded
                // is still found there instead of failing the load outright. SqlCipherBootstrap does
                // the same for the same reason.
                Directory.CreateDirectory(_dir);
                SetDllDirectory(_dir);

                FlacAvailable = Extract(FlacDll);
                LameAvailable = Extract(LameDll);

                // All four or none: a partial whisper stack is worse than no whisper at all,
                // because the failure surfaces as a crash inside the loader rather than a
                // fallback to SAPI.
                bool whisper = true;
                foreach (string dll in WhisperDlls) whisper &= Extract(dll);
                WhisperAvailable = whisper;
            }
        }

        /// <summary>Full path of an extracted native, for diagnostics and the About/dependency view.</summary>
        internal static string PathOf(string dll) => Path.Combine(_dir, dll);

        /// <summary>
        /// Why a native is or is not available, one line each. Without this a missing codec is
        /// indistinguishable from a codec that extracted but would not load, and the only symptom is
        /// a feature quietly not appearing.
        /// </summary>
        internal static string Diagnostics()
        {
            EnsureLoaded();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Native folder: " + _dir);
            foreach (string dll in new[] { FlacDll, LameDll, "ggml-base.dll", "ggml-cpu.dll", "ggml.dll", "libwhisper.dll" })
                sb.AppendLine(dll + ": " + (_why.TryGetValue(dll, out string? w) ? w : "not attempted"));
            return sb.ToString();
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> _why = [];

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static bool Extract(string dll)
        {
            string resource = "KillerNotes.AudioNative." + dll;
            string target = Path.Combine(_dir, dll);
            bool embedded = false;
            try
            {
                Directory.CreateDirectory(_dir);
                using var src = typeof(AudioNativeBootstrap).Assembly.GetManifestResourceStream(resource);
                embedded = src != null;
                if (src != null && (!File.Exists(target) || new FileInfo(target).Length != src.Length))
                {
                    // Temp name then swap, so a crash mid-extract never leaves a half-written
                    // dll for the next launch to load.
                    string tmp = target + ".tmp";
                    using (var dst = File.Create(tmp)) src.CopyTo(dst);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                }
            }
            catch (IOException)
            {
                // Another instance may be mid-extract or holding the dll. If a complete copy exists
                // the preload below still works.
            }
            catch (Exception ex) { _why[dll] = "extract failed: " + ex.Message; return false; }

            if (!File.Exists(target))
            {
                _why[dll] = embedded ? "extracted but missing on disk" : "NOT EMBEDDED in this build";
                return false;
            }

            IntPtr h = LoadLibrary(target);
            if (h == IntPtr.Zero)
            {
                _why[dll] = "LoadLibrary failed, win32 error " + Marshal.GetLastWin32Error();
                return false;
            }
            _why[dll] = embedded ? "ok" : "ok (already on disk, not embedded)";
            return true;
        }
    }
}
