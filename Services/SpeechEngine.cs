using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace KillerNotes.Services
{
    /// <summary>
    /// The whisper.cpp speech engine is not inside the exe. It is downloaded with the first speech
    /// model, in the build that matches this processor, so the app carries no speech code until
    /// someone uses dictation. Four files: ggml-base, ggml and libwhisper, plus ONE CPU backend
    /// picked here from what the processor supports. Every file is pinned by SHA-256 and checked
    /// before it is moved into place; a file that does not match is never loaded.
    /// Built from third_party/audio (README.md there has the build and the hashes).
    /// </summary>
    internal static class SpeechEngine
    {
        internal const string Version = "1.9.5";

        internal static readonly string BaseUrl =
            "https://github.com/SteveTheKiller/KillerNotes/releases/download/speech-engine-" + Version + "/";

        /// <summary>Loaded in this order: each links against the ones before it.</summary>
        internal static readonly (string File, string Sha256, long Bytes)[] Core =
        [
            ("ggml-base.dll", "90d9411c713e71e96937457fba6432f08cd9595ad539b3e21f5130fb82e42811", 1093120),
            ("ggml.dll", "b300f1f6a73ce224e950630937d19a11b06aa051d0596d27bd2b2e82057d063c", 855040),
            ("libwhisper.dll", "36cc9ea86e96973cca62e2aca3ced617f24e27faea551f11fbd4180a5fb3680f", 1204736),
        ];

        /// <summary>The CPU backends, slowest first. AVX2 covers Intel from 2013 (Haswell) and AMD
        /// from 2015 on; the AVX-512 builds were left out because they add little for speech.</summary>
        internal static readonly (string Level, string File, string Sha256, long Bytes)[] CpuBuilds =
        [
            ("x64", "ggml-cpu-x64.dll", "0a5084ffbd679e89ebca1ba5910499a8d5e3ac7d378cab5fa917fdcea4fa2fce", 1302528),
            ("sse42", "ggml-cpu-sse42.dll", "9d5dc858f4afd6fa9d9d03af2854766383c3679db7b476b39dd5dc18c9291fa6", 1315328),
            ("avx", "ggml-cpu-sandybridge.dll", "c943c52adba6d3054913e5431f297651e99a7768b0cab0bf37362da691e23cbe", 1574912),
            ("avx2", "ggml-cpu-haswell.dll", "a270be86d5657ac4bc61865dd78a628a9820f5708d485a11b23c9f9dfdc3b5ba", 1718272),
        ];

        private const int PF_SSE4_2_INSTRUCTIONS_AVAILABLE = 38;
        private const int PF_AVX_INSTRUCTIONS_AVAILABLE = 39;
        private const int PF_AVX2_INSTRUCTIONS_AVAILABLE = 40;

        [DllImport("kernel32.dll")]
        private static extern bool IsProcessorFeaturePresent(int feature);

        /// <summary>The fastest CPU build this processor runs. Windows versions that predate a
        /// feature flag report it absent, which only ever picks a slower, safe build.</summary>
        internal static (string Level, string File, string Sha256, long Bytes) CpuBuild => CpuBuildFor(
            IsProcessorFeaturePresent(PF_AVX2_INSTRUCTIONS_AVAILABLE),
            IsProcessorFeaturePresent(PF_AVX_INSTRUCTIONS_AVAILABLE),
            IsProcessorFeaturePresent(PF_SSE4_2_INSTRUCTIONS_AVAILABLE));

        internal static (string Level, string File, string Sha256, long Bytes) CpuBuildFor(bool avx2, bool avx, bool sse42) =>
            CpuBuilds.First(b => b.Level == (avx2 ? "avx2" : avx ? "avx" : sse42 ? "sse42" : "x64"));

        /// <summary>Beside the models, outside the per-version folders, so an app update does not
        /// download the engine again; the engine version is its own folder.</summary>
        internal static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerNotes", "speech", Version);

        private static (string File, string Sha256, long Bytes)[] Files()
        {
            var cpu = CpuBuild;
            return [.. Core, (cpu.File, cpu.Sha256, cpu.Bytes)];
        }

        internal static bool IsInstalled => Files().All(f => File.Exists(Path.Combine(Dir, f.File)));

        internal static long DownloadBytes => Files().Where(f => !File.Exists(Path.Combine(Dir, f.File))).Sum(f => f.Bytes);

        /// <summary>Downloads whichever engine files are missing. Each goes to a .part file, is
        /// checked against its pinned hash, and only then takes its real name.</summary>
        internal static void Download(Action<long, long> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Dir);
            long total = DownloadBytes, done = 0;
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KillerNotes");
            foreach (var (file, sha, _) in Files())
            {
                string target = Path.Combine(Dir, file);
                if (File.Exists(target)) continue;
                string part = target + ".part";
                using (var resp = http.GetAsync(BaseUrl + file, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult())
                {
                    resp.EnsureSuccessStatusCode();
                    using var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    using var dst = File.Create(part);
                    var buf = new byte[81920];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        dst.Write(buf, 0, n);
                        done += n;
                        progress(done, total);
                    }
                }
                if (!HashMatches(part, sha))
                {
                    File.Delete(part);
                    throw new InvalidDataException(file + " did not match its expected checksum, so it was not installed.");
                }
                if (File.Exists(target)) File.Delete(target);
                File.Move(part, target);
            }
        }

        internal static bool HashMatches(string path, string sha256)
        {
            using var stream = File.OpenRead(path);
            using var hasher = SHA256.Create();
            string actual = BitConverter.ToString(hasher.ComputeHash(stream)).Replace("-", "");
            return string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
