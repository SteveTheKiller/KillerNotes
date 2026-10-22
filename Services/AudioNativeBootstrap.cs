using System;
using System.IO;
using System.Linq;
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

        // Whisper is not embedded: SpeechEngine downloads it with the first model, in the build
        // for this processor, and EnsureWhisper loads it from there.

        private static readonly object _gate = new();
        private static bool _done;
        private static string _dir = "";

        internal static bool FlacAvailable { get; private set; }
        internal static bool LameAvailable { get; private set; }
        /// <summary>True once the downloaded speech engine is loaded (EnsureWhisper).</summary>
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
            foreach (string dll in new[] { FlacDll, LameDll }.Concat(SpeechEngine.Core.Select(c => c.File)))
                sb.AppendLine(dll + ": " + (_why.TryGetValue(dll, out string? w) ? w : "not attempted"));
            sb.AppendLine("CPU backend: " + (_cpuBackend ?? "not loaded"));
            return sb.ToString();
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> _why = [];

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("ggml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ggml_backend_load_all_from_path([MarshalAs(UnmanagedType.LPUTF8Str)] string dir);

        [DllImport("ggml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr ggml_backend_dev_count();

        [DllImport("ggml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ggml_backend_dev_get(UIntPtr index);

        [DllImport("ggml-base.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ggml_backend_dev_description(IntPtr device);

        private static string? _cpuBackend;

        /// <summary>
        /// Loads the downloaded speech engine once it is on disk. Safe to call repeatedly: it does
        /// nothing until the engine is installed, then loads it the first time it is asked. All
        /// three core files load or none counts as available, because a partial whisper stack
        /// fails inside the loader rather than falling back to Windows speech.
        /// </summary>
        internal static bool EnsureWhisper()
        {
            if (WhisperAvailable) return true;
            EnsureLoaded();
            lock (_gate)
            {
                if (WhisperAvailable) return true;
                if (!SpeechEngine.IsInstalled) return false;
                foreach (var (file, _, _) in SpeechEngine.Core)
                {
                    IntPtr h = LoadLibrary(Path.Combine(SpeechEngine.Dir, file));
                    _why[file] = h == IntPtr.Zero ? "LoadLibrary failed, win32 error " + Marshal.GetLastWin32Error() : "ok";
                    if (h == IntPtr.Zero) return false;
                }
                WhisperAvailable = LoadCpuBackend(SpeechEngine.Dir);
                return WhisperAvailable;
            }
        }

        /// <summary>Has ggml register the CPU backend in the engine folder. Whisper never does this
        /// itself; without it there is no backend at all.</summary>
        private static bool LoadCpuBackend(string dir)
        {
            try
            {
                ggml_backend_load_all_from_path(dir);
                ulong count = ggml_backend_dev_count().ToUInt64();
                if (count == 0) { _cpuBackend = "no CPU variant would load"; return false; }
                _cpuBackend = Marshal.PtrToStringAnsi(ggml_backend_dev_description(ggml_backend_dev_get(UIntPtr.Zero))) ?? "loaded";
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                _cpuBackend = ex.Message;
                return false;
            }
        }

        private static bool Extract(string dll)
        {
            string resource = "KillerNotes.AudioNative." + dll;
            string target = Path.Combine(_dir, dll);
            bool embedded = false;
            try
            {
                using var src = typeof(AudioNativeBootstrap).Assembly.GetManifestResourceStream(resource);
                embedded = src != null;
                if (src != null)
                {
                    // Checked against the embedded copy by hash and replaced if it differs, not
                    // trusted because its size matches (NativeCache).
                    using var bytes = new MemoryStream();
                    src.CopyTo(bytes);
                    NativeCache.PublishVerified(target, bytes.ToArray());
                }
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
