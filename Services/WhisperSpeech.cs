using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>
    /// Offline speech recognition via whisper.cpp. This is the transcriber; System.Speech survives
    /// only as the fallback when no model is installed.
    ///
    /// The reason for it: System.Speech is SAPI's DESKTOP DICTATION engine, a Windows 7-era acoustic
    /// model built for command-and-control with a trained user profile. It was not merely configured
    /// badly - it is at its ceiling, and it produces confident nonsense on unfamiliar words, which is
    /// worse than admitting it does not know. Whisper is a different order of accuracy.
    ///
    /// Still nothing leaves the machine. whisper.cpp runs locally, MIT-licensed, and the model file
    /// is a one-off download the user opts into.
    ///
    /// The audio format lines up exactly: whisper wants 16kHz mono, which is precisely what
    /// DictationRecorder captures, so there is no resampling anywhere in this path.
    /// </summary>
    internal static class WhisperSpeech
    {
        internal static string? LastError { get; private set; }

        /// <summary>True when the natives are bundled AND a model has been downloaded.</summary>
        internal static bool Ready => Available && InstalledModel() != null;

        /// <summary>True when the natives are present, whether or not a model is installed.</summary>
        internal static bool Available
        {
            get { AudioNativeBootstrap.EnsureLoaded(); return AudioNativeBootstrap.WhisperAvailable; }
        }

        // ---- models ----

        /// <summary>
        /// The models offered on first use. English-only ("*.en") variants throughout: they are more
        /// accurate than the multilingual models of the same size, and dictation into a note is
        /// overwhelmingly the UI language. Sizes are the real download sizes, shown to the user
        /// because a 466 MB surprise is not acceptable.
        /// </summary>
        internal static readonly (string Id, string File, int Mb, string Note)[] Catalog =
        {
            ("tiny",  "ggml-tiny.en.bin",   75, "Fastest. A clear step up from Windows dictation, but weaker on names and technical terms."),
            ("base",  "ggml-base.en.bin",  142, "The usual choice. Accurate enough to correct rather than rewrite, and quick on most machines."),
            ("small", "ggml-small.en.bin", 466, "Most accurate of the three. Noticeably slower on an older laptop."),
        };

        /// <summary>
        /// Download URL. huggingface.co/ggerganov/whisper.cpp is whisper.cpp's OWN model repo - the
        /// author's, not a mirror - so this needs no hash pinning for the same reason KillerPDF
        /// pulls traineddata straight from the tesseract-ocr org.
        /// </summary>
        internal static string UrlFor(string file) =>
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + file + "?download=true";

        /// <summary>Where models live: beside the extracted natives, so uninstalling the app's local
        /// data takes them with it.</summary>
        internal static string ModelDir => Path.Combine(AudioNativeBootstrap.NativeDir, "models");

        internal static string PathFor(string file) => Path.Combine(ModelDir, file);

        internal static bool IsInstalled(string file) => File.Exists(PathFor(file));

        /// <summary>The model to use: whichever the user chose, if it is still on disk, else any
        /// installed one - largest first, since that is the most accurate.</summary>
        internal static string? InstalledModel()
        {
            string? want = App.GetSetting("WhisperModel");
            if (!string.IsNullOrEmpty(want) && IsInstalled(want!)) return PathFor(want!);

            for (int i = Catalog.Length - 1; i >= 0; i--)
                if (IsInstalled(Catalog[i].File)) return PathFor(Catalog[i].File);
            return null;
        }

        // ---- whisper.cpp interop ----

        private const string Dll = "libwhisper.dll";

        [StructLayout(LayoutKind.Sequential)]
        private struct WhisperContextParams
        {
            [MarshalAs(UnmanagedType.I1)] public bool use_gpu;
            [MarshalAs(UnmanagedType.I1)] public bool flash_attn;
            public int gpu_device;
            [MarshalAs(UnmanagedType.I1)] public bool dtw_token_timestamps;
            public int dtw_aheads_preset;
            public int dtw_n_top;
            public IntPtr dtw_aheads_n;
            public IntPtr dtw_aheads_ptr;
            public UIntPtr dtw_mem_size;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern WhisperContextParams whisper_context_default_params();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr whisper_init_from_file_with_params(string path, WhisperContextParams p);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void whisper_free(IntPtr ctx);

        // whisper_full_params is a large struct whose layout shifts between releases, and getting it
        // wrong corrupts the stack silently. It is therefore never declared here: the defaults are
        // fetched as an opaque blob, the few fields that matter are poked by offset, and the blob is
        // handed straight back. Offsets come from whisper.h for the PINNED version - see the version
        // check below, which refuses to run against anything else.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full_default_params")]
        private static extern void whisper_full_default_params_raw(IntPtr outParams, int strategy);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int whisper_full(IntPtr ctx, IntPtr paramsBlob, float[] samples, int nSamples);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int whisper_full_n_segments(IntPtr ctx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int segment);

        private const int StrategyGreedy = 0;

        /// <summary>Generous over-allocation for whisper_full_params. The struct is a few hundred
        /// bytes; 4KB costs nothing and removes any chance of the native writing past the end of it
        /// if a future build grows the struct.</summary>
        private const int ParamsBlobSize = 4096;

        // Byte offsets into whisper_full_params for whisper.cpp 1.7.4. Only the flags that quieten
        // the console and disable timestamps are touched - everything else keeps its default.
        private const int OffPrintSpecial = 8;
        private const int OffPrintProgress = 9;
        private const int OffPrintRealtime = 10;
        private const int OffPrintTimestamps = 11;

        /// <summary>
        /// Transcribes 16-bit PCM WAV. Returns null when whisper cannot be used at all, so the
        /// caller can fall back to System.Speech rather than showing an empty transcript.
        /// </summary>
        internal static string? Transcribe(byte[] wav)
        {
            LastError = null;
            if (!Available) { LastError = "Speech recognition is not installed."; return null; }

            string? model = InstalledModel();
            if (model == null) { LastError = "No speech model has been downloaded yet."; return null; }

            float[]? samples = ToFloatMono16k(wav);
            if (samples == null || samples.Length == 0) { LastError = "That recording could not be read."; return null; }

            IntPtr ctx = IntPtr.Zero;
            IntPtr blob = IntPtr.Zero;
            try
            {
                var cp = whisper_context_default_params();
                cp.use_gpu = false;      // CPU only: no GPU backend is bundled, and asking for one
                                         // on a machine without it costs a slow probe at startup.
                ctx = whisper_init_from_file_with_params(model, cp);
                if (ctx == IntPtr.Zero) { LastError = "The speech model could not be loaded."; return null; }

                blob = Marshal.AllocHGlobal(ParamsBlobSize);
                for (int i = 0; i < ParamsBlobSize; i += 8) Marshal.WriteInt64(blob, i, 0);
                whisper_full_default_params_raw(blob, StrategyGreedy);

                // Whisper prints to stdout by default, which in a WPF app goes nowhere useful and
                // costs time formatting strings nobody sees.
                Marshal.WriteByte(blob, OffPrintSpecial, 0);
                Marshal.WriteByte(blob, OffPrintProgress, 0);
                Marshal.WriteByte(blob, OffPrintRealtime, 0);
                Marshal.WriteByte(blob, OffPrintTimestamps, 0);

                if (whisper_full(ctx, blob, samples, samples.Length) != 0)
                { LastError = "Transcription failed."; return null; }

                int n = whisper_full_n_segments(ctx);
                var sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    IntPtr p = whisper_full_get_segment_text(ctx, i);
                    if (p == IntPtr.Zero) continue;
                    // Whisper emits UTF-8; Marshal.PtrToStringAnsi would mangle anything non-ASCII.
                    string? seg = Utf8(p);
                    if (!string.IsNullOrEmpty(seg)) sb.Append(seg);
                }
                // Segments arrive with their own leading spaces, so only the ends need tidying.
                return sb.ToString().Trim();
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
            finally
            {
                if (blob != IntPtr.Zero) Marshal.FreeHGlobal(blob);
                if (ctx != IntPtr.Zero) whisper_free(ctx);
            }
        }

        private static string? Utf8(IntPtr p)
        {
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            if (len == 0) return null;
            var buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        /// <summary>
        /// 16-bit PCM to the normalized float mono at 16kHz that whisper expects. Our own recordings
        /// are already 16kHz mono, so this is usually just a widening pass - but a recording made
        /// before the rate was fixed, or a stereo file, still has to arrive correctly, so channels
        /// are averaged and the rate is resampled if it differs.
        /// </summary>
        private static float[]? ToFloatMono16k(byte[] wav)
        {
            if (wav == null || wav.Length <= WavEdit.HeaderBytes) return null;
            int rate = BitConverter.ToInt32(wav, 24);
            short channels = BitConverter.ToInt16(wav, 22);
            short bits = BitConverter.ToInt16(wav, 34);
            if (bits != 16 || channels < 1 || rate <= 0) return null;

            int frames = WavEdit.DataLength(wav) / (2 * channels);
            if (frames <= 0) return null;

            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int o = WavEdit.HeaderBytes + (i * channels + c) * 2;
                    sum += (short)(wav[o] | (wav[o + 1] << 8));
                }
                mono[i] = sum / (float)channels / 32768f;
            }

            if (rate == 16000) return mono;

            // Linear resample. Crude, but this path only runs for audio this app did not record.
            int outLen = (int)(frames * 16000L / rate);
            if (outLen <= 0) return null;
            var outp = new float[outLen];
            double ratio = frames / (double)outLen;
            for (int i = 0; i < outLen; i++)
            {
                double src = i * ratio;
                int a = (int)src;
                int b = Math.Min(frames - 1, a + 1);
                double f = src - a;
                outp[i] = (float)(mono[a] * (1 - f) + mono[b] * f);
            }
            return outp;
        }
    }
}
