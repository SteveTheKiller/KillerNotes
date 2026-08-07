using System;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>
    /// Playback for embedded recordings: winmm's waveOut, the exact mirror of DictationRecorder's
    /// waveIn. Nothing to install and nothing to ship alongside the exe.
    ///
    /// NOT WPF's MediaPlayer, which was the obvious choice and the wrong one: it routes through the
    /// Windows Media Player components, so on an N edition of Windows, on Server, or anywhere the
    /// Media Feature Pack is absent it fails outright with "Windows Media Player version 10 or later
    /// is required". An app that records its own audio must not need a media stack to play it back.
    ///
    /// NOT System.Media.SoundPlayer either: it plays and nothing more - no position, no pause, no
    /// stop - so the chip could never show progress or be interrupted.
    ///
    /// waveOut gives byte-accurate position (waveOutGetPosition), real pause/resume, and immediate
    /// stop, with the audio held in memory rather than written to TEMP - which also means a
    /// recording of someone's voice is never left on disk just to be played.
    /// </summary>
    internal static class DictationPlayer
    {
        internal static string? LastError { get; private set; }

        /// <summary>True from Play() until Stop(), whether or not it is currently paused.</summary>
        internal static bool IsOpen => _hwo != IntPtr.Zero;
        internal static bool IsPaused { get; private set; }

        /// <summary>Set by the driver when the buffer finishes. Polled by the UI timer rather than
        /// raised as an event: the callback runs on a driver thread and must not touch WPF.</summary>
        internal static bool IsFinished { get; private set; }

        internal static int DurationMs { get; private set; }

        /// <summary>Milliseconds played. Byte position from the driver rather than a wall clock, so
        /// it stays true across a pause and cannot drift away from what is being heard.</summary>
        internal static int PositionMs
        {
            get
            {
                if (_hwo == IntPtr.Zero || _bytesPerSec <= 0) return _startMs;
                var t = new MMTIME { wType = TIME_BYTES };
                if (waveOutGetPosition(_hwo, ref t, Marshal.SizeOf<MMTIME>()) != 0) return _startMs;
                if (t.wType != TIME_BYTES) return _startMs;   // driver answered in another unit
                // The device only ever counts from the start of the buffer it was handed, and after
                // a seek that buffer begins partway in - so the seek point has to be added back.
                return _startMs + (int)(t.val * 1000L / _bytesPerSec);
            }
        }

        /// <summary>
        /// Restarts playback from a point, in milliseconds.
        ///
        /// waveOut has no seek: a buffer is handed to the device whole and plays to its end. So a
        /// seek is a fresh Play() of the same audio from a later byte offset, which is why the WAV
        /// is kept in _source. Cheap in practice - the audio is already in memory.
        /// </summary>
        internal static void Seek(int ms)
        {
            if (_source == null) return;
            bool wasPaused = IsPaused;
            Play(_source, Math.Max(0, Math.Min(ms, DurationMs)));
            if (wasPaused) Pause();   // a seek while paused must not start it playing
        }

        private static byte[]? _source;
        private static int _startMs;

        /// <summary>
        /// Playback volume, 0..1. Applied per waveOut device rather than to the system mixer, so
        /// turning a note's recording down does not touch anything else the machine is playing.
        /// Sticky across recordings: set it once and every clip honours it.
        /// </summary>
        internal static double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0, Math.Min(1, value));
                ApplyVolume();
            }
        }
        private static double _volume = 0.8;

        private static void ApplyVolume()
        {
            if (_hwo == IntPtr.Zero) return;
            // One 16-bit level per channel packed into a DWORD, right in the high word. A mono
            // device ignores the high word, so setting both is correct either way.
            uint one = (uint)Math.Round(_volume * 0xFFFF);
            try { waveOutSetVolume(_hwo, (one << 16) | one); } catch { }
        }

        private static IntPtr _hwo, _hdr, _data;
        private static WaveOutProc? _proc;    // field, not a local: a collected delegate means the
                                              // driver calls into freed memory
        private static int _bytesPerSec;

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

        // MMTIME's union is 8 bytes wide (its widest member is the SMPTE struct). Declaring only the
        // 4-byte field actually used would under-size the struct and let the driver write past it.
        [StructLayout(LayoutKind.Sequential)]
        private struct MMTIME
        {
            public int wType;
            public int val;      // cb, when wType is TIME_BYTES
            public int pad;
        }

        private delegate void WaveOutProc(IntPtr hwo, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        private const int WAVE_MAPPER = -1;
        private const int WAVE_FORMAT_PCM = 1;
        private const int CALLBACK_FUNCTION = 0x00030000;
        private const int WOM_DONE = 0x3BD;
        private const int TIME_BYTES = 0x0004;

        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, WaveOutProc cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutPause(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveOutRestart(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveOutGetPosition(IntPtr h, ref MMTIME t, int size);
        [DllImport("winmm.dll")] private static extern int waveOutSetVolume(IntPtr h, uint vol);
        [DllImport("winmm.dll")] private static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int waveOutGetErrorText(int err, System.Text.StringBuilder txt, int len);

        private static bool Ok(int rc, string what)
        {
            if (rc == 0) return true;
            var sb = new System.Text.StringBuilder(256);
            waveOutGetErrorText(rc, sb, sb.Capacity);
            LastError = $"{what}: {sb}";
            return false;
        }

        /// <summary>
        /// Starts playing a WAV held in memory. Any previous playback is stopped first - one
        /// recording at a time.
        /// </summary>
        internal static bool Play(byte[] wav, int startMs = 0)
        {
            byte[]? keep = wav;      // Stop() clears _source; hold the reference across it
            Stop();
            LastError = null;
            _source = keep;

            if (waveOutGetNumDevs() == 0) { LastError = "No playback device is available."; return false; }
            if (!ParseWav(wav, out WAVEFORMATEX fmt, out int off, out int len))
            { LastError = "That recording could not be read."; return false; }

            _bytesPerSec = fmt.nAvgBytesPerSec > 0
                ? fmt.nAvgBytesPerSec
                : fmt.nSamplesPerSec * fmt.nBlockAlign;
            if (_bytesPerSec <= 0) { LastError = "That recording has no usable format."; return false; }
            DurationMs = (int)(len * 1000L / _bytesPerSec);

            // Skip into the audio for a seek. Rounded DOWN to a whole sample frame: starting
            // mid-frame shifts every following byte by one and turns speech into static.
            int skip = 0;
            if (startMs > 0)
            {
                skip = (int)(startMs / 1000.0 * _bytesPerSec);
                int align = fmt.nBlockAlign > 0 ? fmt.nBlockAlign : 1;
                skip -= skip % align;
                skip = Math.Max(0, Math.Min(skip, len));
            }
            _startMs = (int)(skip * 1000L / _bytesPerSec);
            off += skip;
            len -= skip;
            if (len <= 0) { IsFinished = true; return false; }

            IsFinished = false;
            IsPaused = false;

            _proc = OnWaveOut;
            if (!Ok(waveOutOpen(out _hwo, WAVE_MAPPER, ref fmt, _proc, IntPtr.Zero, CALLBACK_FUNCTION), "Opening the speaker"))
            { _hwo = IntPtr.Zero; _proc = null; return false; }

            ApplyVolume();   // the device opens at full scale; carry the user's setting over

            // The driver reads from this memory for the life of the buffer, so it is unmanaged and
            // freed only in Teardown - a pinned managed array would have to stay pinned just as long
            // and would fragment the heap for no gain.
            _data = Marshal.AllocHGlobal(len);
            Marshal.Copy(wav, off, _data, len);

            var hdr = new WAVEHDR { lpData = _data, dwBufferLength = len };
            _hdr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
            Marshal.StructureToPtr(hdr, _hdr, false);

            if (!Ok(waveOutPrepareHeader(_hwo, _hdr, Marshal.SizeOf<WAVEHDR>()), "Preparing playback")) { Teardown(); return false; }
            if (!Ok(waveOutWrite(_hwo, _hdr, Marshal.SizeOf<WAVEHDR>()), "Starting playback")) { Teardown(); return false; }
            return true;
        }

        /// <summary>Driver callback, on a driver thread: sets a flag and nothing else.</summary>
        private static void OnWaveOut(IntPtr hwo, int uMsg, IntPtr inst, IntPtr p1, IntPtr p2)
        {
            if (uMsg == WOM_DONE) IsFinished = true;
        }

        internal static void Pause()
        {
            if (_hwo == IntPtr.Zero || IsPaused) return;
            if (Ok(waveOutPause(_hwo), "Pausing")) IsPaused = true;
        }

        internal static void Resume()
        {
            if (_hwo == IntPtr.Zero || !IsPaused) return;
            if (Ok(waveOutRestart(_hwo), "Resuming")) IsPaused = false;
        }

        internal static void Stop()
        {
            if (_hwo == IntPtr.Zero) { Teardown(); return; }
            // Reset before unpreparing: it returns the queued buffer, and unpreparing a buffer the
            // driver still owns fails and leaks it.
            try { waveOutReset(_hwo); } catch { }
            Teardown();
            IsPaused = false;
            IsFinished = false;
            DurationMs = 0;
            _startMs = 0;
            _source = null;
        }

        private static void Teardown()
        {
            try
            {
                if (_hwo != IntPtr.Zero && _hdr != IntPtr.Zero)
                    waveOutUnprepareHeader(_hwo, _hdr, Marshal.SizeOf<WAVEHDR>());
            }
            catch { }
            if (_hdr != IntPtr.Zero) { try { Marshal.FreeHGlobal(_hdr); } catch { } _hdr = IntPtr.Zero; }
            if (_data != IntPtr.Zero) { try { Marshal.FreeHGlobal(_data); } catch { } _data = IntPtr.Zero; }
            if (_hwo != IntPtr.Zero) { try { waveOutClose(_hwo); } catch { } _hwo = IntPtr.Zero; }
            _proc = null;
            _bytesPerSec = 0;
        }

        /// <summary>
        /// Reads the fmt and data chunks out of a RIFF/WAVE file. Chunk walking rather than assuming
        /// the canonical 44-byte header: our own recorder writes exactly that, but a WAV that has
        /// been through another tool routinely carries LIST or fact chunks first, and a fixed offset
        /// would play those as audio.
        /// </summary>
        private static bool ParseWav(byte[] w, out WAVEFORMATEX fmt, out int dataOffset, out int dataLength)
        {
            fmt = default; dataOffset = 0; dataLength = 0;
            if (w == null || w.Length < 12) return false;
            if (w[0] != 'R' || w[1] != 'I' || w[2] != 'F' || w[3] != 'F') return false;
            if (w[8] != 'W' || w[9] != 'A' || w[10] != 'V' || w[11] != 'E') return false;

            bool haveFmt = false;
            int p = 12;
            while (p + 8 <= w.Length)
            {
                string id = "" + (char)w[p] + (char)w[p + 1] + (char)w[p + 2] + (char)w[p + 3];
                int size = BitConverter.ToInt32(w, p + 4);
                int body = p + 8;
                if (size < 0 || body + size > w.Length) size = w.Length - body;   // truncated tail

                if (id == "fmt " && size >= 16)
                {
                    fmt = new WAVEFORMATEX
                    {
                        wFormatTag     = BitConverter.ToInt16(w, body),
                        nChannels      = BitConverter.ToInt16(w, body + 2),
                        nSamplesPerSec = BitConverter.ToInt32(w, body + 4),
                        nAvgBytesPerSec= BitConverter.ToInt32(w, body + 8),
                        nBlockAlign    = BitConverter.ToInt16(w, body + 12),
                        wBitsPerSample = BitConverter.ToInt16(w, body + 14),
                        cbSize         = 0,
                    };
                    // Only plain PCM. waveOut can be handed a compressed format and will simply
                    // refuse to open, which would surface as a meaningless device error.
                    if (fmt.wFormatTag != WAVE_FORMAT_PCM) return false;
                    haveFmt = true;
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = size;
                }

                p = body + size + (size & 1);   // chunks are word-aligned
                if (haveFmt && dataLength > 0) break;
            }
            return haveFmt && dataLength > 0;
        }
    }
}
