using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>
    /// Microphone capture and offline transcription. No NuGet package, no native DLL and no model
    /// file: capture goes through winmm's waveIn API, which ships with Windows, and recognition
    /// through System.Speech, which ships with .NET Framework. Nothing is uploaded - this is an
    /// encrypted notepad, and dictation must not be the one feature that talks to a network.
    ///
    /// waveIn rather than the far simpler MCI string interface, because MCI hands back only a
    /// finished file: it exposes no sample data, so a live waveform is impossible on top of it.
    /// Owning the PCM buffers gives the meter its levels, the chip its envelope, and full control
    /// of the format - which also removes MCI's habit of refusing a format with an error that
    /// claims the device is busy.
    /// </summary>
    internal static class DictationRecorder
    {
        // 16-bit 16kHz mono PCM: what System.Speech's acoustic model expects (44kHz stereo
        // transcribes noticeably worse), and only ~32KB per second, which matters because the
        // recording is stored inside the note database.
        private const int SampleRate = 16000;
        private const int Channels = 1;
        private const int BitsPerSample = 16;
        private const int BytesPerSample = BitsPerSample / 8 * Channels;

        // ~100ms per buffer, eight of them. Small enough that the meter feels live, plentiful
        // enough that a scheduling hiccup cannot starve the driver and drop audio.
        private const int BufferMs = 100;
        private const int BufferBytes = SampleRate * BytesPerSample * BufferMs / 1000;
        private const int BufferCount = 8;

        internal static bool IsRecording { get; private set; }
        internal static string? LastError { get; private set; }

        /// <summary>Newest peak amplitude, 0..1. Read by the UI timer to drive the live meter.</summary>
        internal static float PeakLevel { get; private set; }

        /// <summary>Milliseconds captured so far.</summary>
        internal static long ElapsedMs
        {
            get { lock (Sync) return _pcm.Length / (long)(SampleRate * BytesPerSample) * 1000
                                 + _pcm.Length % (SampleRate * BytesPerSample) * 1000L / (SampleRate * BytesPerSample); }
        }

        // One peak per ~50ms of audio. This IS the waveform - both the live scroller and the chip
        // draw from it, so what you watch while recording is what you get on the chip afterwards.
        private const int EnvelopeMs = 50;
        private static readonly List<float> _envelope = [];
        private static int _envSamplesInBucket;
        private static float _envBucketPeak;

        /// <summary>A copy of the envelope so far. Copied under the lock - the capture callback
        /// runs on a driver thread and appends to the live one.</summary>
        internal static float[] EnvelopeSnapshot()
        {
            lock (Sync) return [.. _envelope];
        }

        private static readonly object Sync = new();
        private static MemoryStream _pcm = new();

        private static IntPtr _hwi;
        private static WaveInProc? _proc;          // held in a field: if this is collected the
                                                   // driver calls into freed memory and the app dies
        private static IntPtr[] _headers = [];

        // ---- winmm interop ----

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public short wFormatTag, nChannels;
            public int nSamplesPerSec, nAvgBytesPerSec;
            public short nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public int dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        private delegate void WaveInProc(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        private const int WAVE_MAPPER = -1;
        private const int WAVE_FORMAT_PCM = 1;
        private const int CALLBACK_FUNCTION = 0x00030000;
        private const int WIM_DATA = 0x3C0;

        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, WaveInProc cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInStop(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int waveInGetErrorText(int err, System.Text.StringBuilder txt, int len);

        private static bool Ok(int rc, string what)
        {
            if (rc == 0) return true;
            var sb = new System.Text.StringBuilder(256);
            waveInGetErrorText(rc, sb, sb.Capacity);
            LastError = $"{what}: {sb}";
            return false;
        }

        /// <summary>True when the machine has any capture device at all.</summary>
        internal static bool MicrophoneAvailable() => waveInGetNumDevs() > 0;

        internal static bool Start()
        {
            if (IsRecording) return true;
            LastError = null;

            if (waveInGetNumDevs() == 0) { LastError = "No recording device is connected."; return false; }

            lock (Sync)
            {
                _pcm = new MemoryStream();
                _envelope.Clear();
                _envSamplesInBucket = 0;
                _envBucketPeak = 0;
            }
            PeakLevel = 0;

            var fmt = new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_PCM,
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                nAvgBytesPerSec = SampleRate * BytesPerSample,
                nBlockAlign = BytesPerSample,
                wBitsPerSample = BitsPerSample,
                cbSize = 0,
            };

            _proc = OnWaveIn;
            if (!Ok(waveInOpen(out _hwi, WAVE_MAPPER, ref fmt, _proc, IntPtr.Zero, CALLBACK_FUNCTION), "Opening the microphone"))
            { _proc = null; return false; }

            _headers = new IntPtr[BufferCount];
            for (int i = 0; i < BufferCount; i++)
            {
                var hdr = new WAVEHDR
                {
                    lpData = Marshal.AllocHGlobal(BufferBytes),
                    dwBufferLength = BufferBytes,
                };
                IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                Marshal.StructureToPtr(hdr, p, false);
                _headers[i] = p;
                if (!Ok(waveInPrepareHeader(_hwi, p, Marshal.SizeOf<WAVEHDR>()), "Preparing a buffer")) { Teardown(); return false; }
                if (!Ok(waveInAddBuffer(_hwi, p, Marshal.SizeOf<WAVEHDR>()), "Queueing a buffer")) { Teardown(); return false; }
            }

            IsRecording = true;
            if (!Ok(waveInStart(_hwi), "Starting capture")) { IsRecording = false; Teardown(); return false; }
            return true;
        }

        /// <summary>
        /// Driver callback. Runs on a WINDOWS thread, not the UI thread: it must not touch WPF, and
        /// everything it shares is guarded by Sync. It copies the buffer out and immediately
        /// re-queues it, because a buffer not returned promptly is audio dropped on the floor.
        /// </summary>
        private static void OnWaveIn(IntPtr hwi, int uMsg, IntPtr inst, IntPtr param1, IntPtr param2)
        {
            if (uMsg != WIM_DATA || !IsRecording) return;
            try
            {
                var hdr = Marshal.PtrToStructure<WAVEHDR>(param1);
                int n = hdr.dwBytesRecorded;
                if (n > 0)
                {
                    var buf = new byte[n];
                    Marshal.Copy(hdr.lpData, buf, 0, n);
                    Accumulate(buf, n);
                }
                waveInAddBuffer(hwi, param1, Marshal.SizeOf<WAVEHDR>());
            }
            catch { /* shutting down mid-callback; nothing useful to do on a driver thread */ }
        }

        private static void Accumulate(byte[] buf, int n)
        {
            // Peak of this buffer for the live meter, and a running peak per envelope bucket.
            float peak = 0;
            int samples = n / 2;
            const int bucketSamples = SampleRate * EnvelopeMs / 1000;

            lock (Sync)
            {
                _pcm.Write(buf, 0, n);
                for (int i = 0; i < samples; i++)
                {
                    short s = (short)(buf[i * 2] | (buf[i * 2 + 1] << 8));
                    // (int) is NOT redundant. Math.Abs(short) throws OverflowException on
                    // short.MinValue (-32768), which has no positive counterpart in 16 bits - so a
                    // take loud enough to clip to full scale would kill the driver callback thread.
                    float a = Math.Abs((int)s) / 32768f;
                    if (a > peak) peak = a;
                    if (a > _envBucketPeak) _envBucketPeak = a;
                    if (++_envSamplesInBucket >= bucketSamples)
                    {
                        _envelope.Add(_envBucketPeak);
                        _envBucketPeak = 0;
                        _envSamplesInBucket = 0;
                    }
                }
            }
            PeakLevel = peak;
        }

        /// <summary>Stops and returns the take as a complete WAV, or null if nothing was captured.
        /// Bytes rather than a temp file: the caller embeds them straight into the database, and a
        /// recording of someone's voice should not be left lying in TEMP.</summary>
        internal static byte[]? Stop()
        {
            if (!IsRecording) return null;
            IsRecording = false;
            waveInStop(_hwi);
            waveInReset(_hwi);          // returns every queued buffer before we free them
            Teardown();
            PeakLevel = 0;

            byte[] pcm;
            lock (Sync) pcm = _pcm.ToArray();
            // Under a tenth of a second is a mis-click, not a take.
            return pcm.Length < SampleRate * BytesPerSample / 10 ? null : BuildWav(pcm);
        }

        internal static void Cancel()
        {
            if (!IsRecording) { Teardown(); return; }
            IsRecording = false;
            waveInStop(_hwi);
            waveInReset(_hwi);
            Teardown();
            PeakLevel = 0;
            lock (Sync) _pcm = new MemoryStream();
        }

        private static void Teardown()
        {
            foreach (IntPtr p in _headers)
            {
                if (p == IntPtr.Zero) continue;
                try
                {
                    waveInUnprepareHeader(_hwi, p, Marshal.SizeOf<WAVEHDR>());
                    var hdr = Marshal.PtrToStructure<WAVEHDR>(p);
                    if (hdr.lpData != IntPtr.Zero) Marshal.FreeHGlobal(hdr.lpData);
                    Marshal.FreeHGlobal(p);
                }
                catch { /* already torn down */ }
            }
            _headers = [];
            if (_hwi != IntPtr.Zero) { waveInClose(_hwi); _hwi = IntPtr.Zero; }
            _proc = null;
        }

        /// <summary>Wraps raw PCM in a 44-byte canonical WAV header.</summary>
        private static byte[] BuildWav(byte[] pcm)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(['R', 'I', 'F', 'F']);
            w.Write(36 + pcm.Length);
            w.Write(['W', 'A', 'V', 'E', 'f', 'm', 't', ' ']);
            w.Write(16);                                   // PCM fmt chunk size
            w.Write((short)WAVE_FORMAT_PCM);
            w.Write((short)Channels);
            w.Write(SampleRate);
            w.Write(SampleRate * BytesPerSample);           // byte rate
            w.Write((short)BytesPerSample);                 // block align
            w.Write((short)BitsPerSample);
            w.Write(['d', 'a', 't', 'a']);
            w.Write(pcm.Length);
            w.Write(pcm);
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Peak envelope of a stored WAV, for drawing an embedded recording's waveform.
        /// Assumes the 16-bit mono format this class writes; anything else returns empty rather
        /// than drawing noise.</summary>
        internal static float[] EnvelopeOf(byte[] wav, int buckets = 96)
        {
            if (wav.Length <= 44 || buckets <= 0) return [];
            int samples = (wav.Length - 44) / 2;
            if (samples <= 0) return [];
            var env = new float[buckets];
            int per = Math.Max(1, samples / buckets);
            for (int b = 0; b < buckets; b++)
            {
                float peak = 0;
                int start = b * per;
                for (int i = 0; i < per; i++)
                {
                    int s = start + i;
                    int o = 44 + s * 2;
                    if (o + 1 >= wav.Length) break;
                    short v = (short)(wav[o] | (wav[o + 1] << 8));
                    float a = Math.Abs((int)v) / 32768f;   // see Accumulate: Math.Abs(short) overflows
                    if (a > peak) peak = a;
                }
                env[b] = peak;
            }
            return env;
        }

        /// <summary>
        /// Transcribes a WAV offline with System.Speech, on a worker thread (recognition of a long
        /// take takes seconds). Returns null when no recognizer is installed for the UI language -
        /// a real and common case - so the caller can say so rather than show an empty transcript.
        /// </summary>
        internal static string? Transcribe(byte[] wav)
        {
            LastError = null;

            // Whisper first whenever a model is installed. System.Speech below is the fallback, not
            // the default - it is SAPI's desktop dictation engine and is at its ceiling.
            if (WhisperSpeech.Ready)
            {
                string? better = WhisperSpeech.Transcribe(wav);
                if (!string.IsNullOrWhiteSpace(better)) return better;
                LastError = WhisperSpeech.LastError;
                // Fall through: a whisper failure should still get the user SOMETHING.
            }
            string tmp = Path.Combine(Path.GetTempPath(), "kn-dict-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                // SetInputToWaveStream exists, but the recognizer reads lazily and a disposed
                // stream mid-recognition truncates the transcript; a temp file avoids that. It is
                // deleted in the finally, so the audio does not linger.
                File.WriteAllBytes(tmp, wav);

                // Pick the recognizer matching the UI culture rather than taking the default. The
                // parameterless ctor grabs whichever recognizer Windows lists first, which on a
                // machine with several language packs is routinely NOT the language being spoken -
                // and a recognizer listening in the wrong language produces exactly the plausible-
                // looking nonsense that reads as "bad accuracy".
                var want = System.Globalization.CultureInfo.CurrentUICulture;
                var installed = System.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers();
                var match = installed.FirstOrDefault(r => r.Culture.Equals(want))
                         ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == want.TwoLetterISOLanguageName)
                         ?? installed.FirstOrDefault();
                if (match == null) { LastError = "No speech recognizer is installed."; return null; }

                using var engine = new System.Speech.Recognition.SpeechRecognitionEngine(match);

                // The desktop recognizer drops anything it is not confident about, which is why a
                // sentence comes back with words silently missing rather than merely wrong. Taking
                // the threshold down keeps the low-confidence words: easier to correct a wrong word
                // than to notice an absent one.
                try { engine.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 0); } catch { }

                engine.LoadGrammar(new System.Speech.Recognition.DictationGrammar());
                engine.SetInputToWaveFile(tmp);

                var text = new System.Text.StringBuilder();
                while (true)
                {
                    System.Speech.Recognition.RecognitionResult? r;
                    try { r = engine.Recognize(); }
                    catch (InvalidOperationException) { break; }   // end of stream
                    if (r == null) break;
                    if (text.Length > 0) text.Append(' ');
                    text.Append(r.Text);
                }
                return text.ToString();
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        internal static bool RecognizerAvailable()
        {
            if (WhisperSpeech.Ready) return true;
            try { return System.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers().Count > 0; }
            catch { return false; }
        }

        /// <summary>True when whisper is bundled but no model has been downloaded - the case where
        /// the pad should offer the model chooser rather than transcribe with SAPI.</summary>
        internal static bool CanOfferBetterRecognition() => WhisperSpeech.Available && !WhisperSpeech.Ready;
    }
}
