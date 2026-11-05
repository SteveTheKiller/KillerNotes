using KillerNotes.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace KillerNotes.Tests
{
    // The release certificate's SimpleName, read from an installed signed release. The updater has
    // to hold every signer check to this exact name, not to a fragment of it.
    public sealed class UpdateTrustRegressionTests
    {
        private const string ReleasePublisher = "Open Source Developer Stephen Riley";

        [Fact]
        public void ElevatedHelperHoldsTheInstalledCopyToTheFullPublisherName()
        {
            string staged = Path.Combine(Path.Combine(Path.GetTempPath(), "KillerNotes.Tests"), "elevated-signer-" + Guid.NewGuid().ToString("N") + ".exe");
            UpdateImagePublication.RememberVerified(staged, new string('A', 64), new string('B', 40));
            try
            {
                var start = TrustedUpdate.Publication(staged, @"C:\Program Files\KillerNotes\KillerNotes.exe", "9.0.0",
                    new string('C', 64), 1, 0, 2, 0, new string('D', 40));
                string body = Unwrap(start.Arguments);

                Assert.Equal(ReleasePublisher, PlanValue(body, "Signer"));
                // Start-KillerUpdatedApp (target) 'reopen' $true (original signer) (publisher) ...
                int relaunch = body.IndexOf("Start-KillerUpdatedApp (", StringComparison.Ordinal);
                Assert.True(relaunch > 0, "The elevated helper relaunches through the guard.");
                MatchCollection data = Regex.Matches(body.Substring(relaunch), @"Unicode\.GetString\(\[Convert\]::FromBase64String\('([A-Za-z0-9+/=]*)'\)\)");
                Assert.True(data.Count >= 3, "The relaunch passes its target, signer and publisher.");
                Assert.Equal(ReleasePublisher, Encoding.Unicode.GetString(Convert.FromBase64String(data[2].Groups[1].Value)));
            }
            finally { if (File.Exists(staged + ".recovery.txt")) File.Delete(staged + ".recovery.txt"); }
        }

        [Fact]
        public void UpdateFromANameThatOnlyContainsThePublisherIsRefused()
        {
            var incoming = new AssemblyName("KillerNotes") { Version = new Version(9, 0, 0, 0) };
            var error = Assert.Throws<SecurityException>(() => ReleaseIdentity.Verify(incoming, "9.0.0", new Version(1, 4, 1),
                (false, "(not signed)"), (true, "Stephen Rileyson"), requireInstalledSignature: false));
            Assert.Equal("The update is not signed by the publisher.", error.Message);
        }

        [Fact]
        public void SignedCopyAcceptsANewerReleaseFromTheSamePublisher()
        {
            var incoming = new AssemblyName("KillerNotes") { Version = new Version(9, 0, 0, 0) };
            ReleaseIdentity.Verify(incoming, "9.0.0", new Version(1, 4, 1),
                (true, ReleasePublisher), (true, ReleasePublisher), requireInstalledSignature: true);
        }

        [Theory]
        [InlineData(".install-")]
        [InlineData(".previous-")]
        [InlineData(".failed-")]
        public void UninstallTakesTheInstallerPublicationLeftovers(string marker)
        {
            string folder = Path.Combine(Path.Combine(Path.GetTempPath(), "KillerNotes.Tests"), "leftovers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string name = "KillerNotes.exe" + marker + "0123456789abcdef0123456789abcdef";
                File.WriteAllText(Path.Combine(folder, name), "old image");
                File.WriteAllText(Path.Combine(folder, "KillerNotes.exe" + marker + "notaguid"), "unrelated");

                Assert.Equal(new[] { name }, ExecutableDeployment.UninstallLeftovers(folder, "KillerNotes.exe"));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Theory]
        [InlineData(0u, 0u, true)]
        [InlineData(0x80092013u, 0u, true)]
        [InlineData(0x80092013u, 0x800B0101u, false)]
        [InlineData(0x800B010Eu, 0u, true)]
        [InlineData(0x800B010Eu, 0x800B0101u, false)]
        [InlineData(0x800B010Cu, 0u, false)]
        [InlineData(0x80096010u, 0u, false)]
        public void OnlyAnUnreachableRevocationServerEarnsTheCacheOnlyRetry(uint first, uint retry, bool trusted)
        {
            var calls = new System.Collections.Generic.List<(uint revocation, uint flags)>();
            bool result = CodeSignature.VerifyTrust((revocation, flags) =>
            {
                calls.Add((revocation, flags));
                return calls.Count == 1 ? first : retry;
            });

            Assert.Equal(trusted, result);
            // WTD_REVOKE_WHOLECHAIN first; the retry is WTD_REVOKE_NONE with cache-only retrieval.
            Assert.Equal((1u, 0x80u), calls[0]);
            Assert.Equal(first == 0x80092013u || first == 0x800B010Eu ? 2 : 1, calls.Count);
            if (calls.Count == 2) Assert.Equal((0u, 0x1010u), calls[1]);
        }

        [Fact]
        public void EveryInstallLeaseIsScopedToItsFolder()
        {
            // The update helpers lock Global\KillerTools-Install-KillerNotes-<folder hash>. An install
            // that takes the bare product lock would not exclude them.
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "KillerNotes.csproj"))) root = Path.GetDirectoryName(root);
            Assert.NotNull(root);
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                    || file.Contains("KillerNotes.Tests") || file.EndsWith("InstallerExitCodes.cs", StringComparison.Ordinal)) continue;
                foreach (Match call in Regex.Matches(File.ReadAllText(file), @"InstallerExitCodes\.Acquire\(([^()]*(\([^()]*\))?[^()]*)\)"))
                    Assert.True(call.Groups[1].Value.Contains(","), Path.GetFileName(file) + " takes an unscoped install lease: " + call.Value);
            }
        }

        [Fact]
        public void PerUserHelperRecordsTheInstalledCopyBeforeCheckingTheDownload()
        {
            // The app has already closed when the helper starts. A download that fails its check must
            // still find the installed copy's identity recorded, or recovery refuses to reopen it.
            string path = Path.Combine(Path.Combine(Path.GetTempPath(), "KillerNotes.Tests"), "capture-order-" + Guid.NewGuid().ToString("N") + ".exe");
            UpdateImagePublication.RememberVerified(path, new string('A', 64), new string('B', 40));
            try
            {
                string script = UpdateImagePublication.Script(path, @"C:\Apps\KillerNotes.exe", false,
                    "https://example.invalid/releases", "KillerNotes", originalHash: new string('C', 64));
                int function = script.IndexOf("function Invoke-UpdateImagePublication", StringComparison.Ordinal);
                int capture = script.IndexOf("& $ops.CaptureOriginal $plan", function, StringComparison.Ordinal);
                int downloadCheck = script.IndexOf("$plan.Download", function, StringComparison.Ordinal);
                Assert.True(function > 0 && capture > 0 && downloadCheck > 0, "The publication function captures and checks.");
                Assert.True(capture < downloadCheck, "The installed copy's identity is captured before the download is opened.");
            }
            finally { if (File.Exists(path + ".recovery.txt")) File.Delete(path + ".recovery.txt"); }
        }

        // Reverses ImmutablePowerShell.Arguments: the script travels gzip-compressed inside Base64.
        internal static string Unwrap(string arguments)
        {
            Match payload = Regex.Match(arguments, @"FromBase64String\('([A-Za-z0-9+/=]+)'\)");
            Assert.True(payload.Success, "The helper carries its script as Base64.");
            using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(payload.Groups[1].Value)), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        // Plan values are written as Base64 so no quoting can break out of them.
        internal static string PlanValue(string script, string name)
        {
            Match value = Regex.Match(script, "(?m)^" + Regex.Escape(name) + @" = \[Text\.Encoding\]::UTF8\.GetString\(\[Convert\]::FromBase64String\('([A-Za-z0-9+/=]*)'\)\)");
            Assert.True(value.Success, "The plan sets " + name + ".");
            return Encoding.UTF8.GetString(Convert.FromBase64String(value.Groups[1].Value));
        }
    }
}
