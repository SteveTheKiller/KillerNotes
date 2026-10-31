using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using KillerNotes.Features.About;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>An uninstall takes the working files an interrupted update left behind, however new
    /// they are, or the install folder never goes away. A recovery copy marked to keep stays.</summary>
    public sealed class UninstallLeftoverTests : IDisposable
    {
        private const string Exe = "KillerNotes.exe";
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

        public UninstallLeftoverTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private void Touch(params string[] names)
        {
            foreach (string name in names) File.WriteAllText(Path.Combine(_dir, name), "x");
        }

        private static string Hex() => Guid.NewGuid().ToString("N");

        [Fact]
        public void TheUninstallListHoldsEveryUpdateLeftoverAndNothingElse()
        {
            string[] leftovers =
            [
                $"{Exe}.{Hex()}.bak", $"{Exe}.old-{Hex()}", $"{Exe}.new-{Hex()}", $"{Exe}.restore-{Hex()}",
                $".update-{Hex()}.exe", $".update-{Hex()}.exe.recovery.txt",
            ];
            string kept = $"{Exe}.{Hex()}.bak";
            Touch(leftovers);
            Touch(kept, kept + ".keep", Exe, "readme.txt", $"{Exe}.old-mine", $"Other.exe.{Hex()}.bak", $"{Exe}.notes.bak");

            Assert.Equal(leftovers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                ExecutableDeployment.UninstallLeftovers(_dir, Exe).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void AFolderThatIsGoneHasNoLeftovers() =>
            Assert.Empty(ExecutableDeployment.UninstallLeftovers(Path.Combine(_dir, "missing"), Exe));
    }

    /// <summary>A failed update handoff says why; a declined UAC prompt was the user's own answer.</summary>
    public sealed class UpdateHandoffFailureTests
    {
        private static string Loc(string key) => key + ":{0}";

        [Fact]
        public void TheReasonReachesTheUser() =>
            Assert.Equal("Str_About_UpdateFailed:Download retained at: C:\\x.exe",
                AboutController.HandoffFailureText(new IOException("Download retained at: C:\\x.exe"), Loc));

        [Fact]
        public void DecliningUacSaysNothing() =>
            Assert.Null(AboutController.HandoffFailureText(new Win32Exception(1223), Loc));
    }
}
