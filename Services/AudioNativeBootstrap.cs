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

        /// <summary>
        /// Whisper's natives, IN DEPENDENCY ORDER. ggml-base first, then the ggml facade, then
        /// whisper itself - each links against the ones before it. Preloading them out of order
        /// leaves the loader hunting the PATH for a dependency that has not been extracted yet, and
        /// whisper silently reports unavailable.
        /// </summary>
        private static readonly string[] WhisperDlls =
            ["ggml-base.dll", "ggml.dll", "libwhisper.dll"];

        /// <summary>
        /// The CPU backend, one build per instruction-set level. They are only extracted here;
        /// ggml loads the best one this processor supports (LoadCpuBackend). The 1.9.5 update first
        /// shipped a single baseline CPU build, which ran transcription many times slower.
        /// </summary>
        private static readonly string[] CpuVariantDlls =
            ["ggml-cpu-x64.dll", "ggml-cpu-sse42.dll", "ggml-cpu-sandybridge.dll", "ggml-cpu-haswell.dll", "ggml-cpu-alderlake.dll", "ggml-cpu-skylakex.dll", "ggml-cpu-icelake.dll", "ggml-cpu-zen4.dll"];

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
                bool anyCpu = false;
                foreach (string dll in CpuVariantDlls) anyCpu |= Extract(dll, load: false);
                WhisperAvailable = whisper && anyCpu && LoadCpuBackend();
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
            foreach (string dll in new[] { FlacDll, LameDll }.Concat(WhisperDlls).Concat(CpuVariantDlls))
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

        /// <summary>Has ggml scan the native folder and register the fastest CPU variant this
        /// processor runs. Whisper never does this itself; without it there is no backend at all.</summary>
        private static bool LoadCpuBackend()
        {
            try
            {
                ggml_backend_load_all_from_path(_dir);
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

        private static bool Extract(string dll, bool load = true)
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
            if (!load) { _why[dll] = embedded ? "extracted" : "on disk, not embedded"; return true; }

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
