using System;
using System.IO;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    // The save picker's typed name. A name pasted with its quotes ("report.csv") is a normal name:
    // changing the file type swaps its extension instead of crashing, and a quoted full path counts
    // as a path the picker can accept without a folder listing.
    public sealed class PickerTypedNameTests
    {
        private static readonly string[] Exts = { ".csv", ".txt" };

        [Theory]
        [InlineData("\"report.csv\"", ".txt", "report.txt")]
        [InlineData("report.csv", ".txt", "report.txt")]
        [InlineData("report.csv", ".csv", null)]
        [InlineData("notes.md", ".txt", null)]          // an extension the user typed by hand stays
        [InlineData("report", ".txt", null)]
        [InlineData("", ".txt", null)]
        [InlineData("report.csv", null, null)]
        public void ChangingTheTypeSwapsOnlyAFilterExtension(string typed, string? newExt, string? expected)
            => Assert.Equal(expected, FileDialog.SwapTypedExtension(typed, newExt, Exts));

        [Theory]
        [InlineData("\"C:\\out\\report.txt\"", true)]
        [InlineData("C:\\out\\report.txt", true)]
        [InlineData("report.txt", false)]
        public void AQuotedFullPathIsAcceptedWithoutAListing(string typed, bool expected)
            => Assert.Equal(expected, PickerAcceptRule.CanAccept(false, false, false, false, typed));

        // Accept is re-evaluated as the name is typed, so a failed listing does not leave it off
        // while the user types a full path (the dialog wires the name box's TextChanged).
        [Fact]
        public void TheNameBoxRefreshesAcceptAsItChanges()
        {
            string xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Controls", "FileDialog.xaml"));
            Assert.Contains("TextChanged=\"FileNameBox_TextChanged\"", xaml);
        }

        // A picture that cannot be decoded (a corrupt file, a format with no codec installed) is
        // remembered as having no thumbnail, so scrolling past it does not decode it on every redraw.
        [Fact]
        public void AnUndecodablePictureIsRememberedAsHavingNoThumbnail()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerNotes.Tests", "thumbs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string bad = Path.Combine(folder, "bad.png");
            File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            try
            {
                Assert.Null(ShellIcons.Thumbnail(bad, 48));
                Assert.True(ShellIcons.HasThumbnailEntry(bad, 48), "The failed decode was not remembered.");
                File.Delete(bad);   // a failed decode leaves no handle on the file
            }
            finally { Directory.Delete(folder, true); }
        }

        private static string RepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "KillerNotes.csproj"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("KillerNotes.csproj not found above the test output.");
        }
    }
}
