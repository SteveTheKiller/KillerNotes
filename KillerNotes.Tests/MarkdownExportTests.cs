using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Models;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    // A Markdown export is two things, the text and its pictures, and only the text can be swapped
    // atomically. So the new pictures go in under new names beside the old ones, the text changes
    // over last, and the old pictures leave only after that. A failure at any point leaves the
    // previous export byte for byte.
    public sealed class MarkdownExportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kn-md-export-" + Guid.NewGuid().ToString("N"));
        private string Target => Path.Combine(_dir, "note.md");
        private string Assets => Path.Combine(_dir, "note.assets");

        public MarkdownExportTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void AFailedPublishLeavesTheOldExportByteForByte() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            var before = Snapshot();

            using (new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => MarkdownExport.Write(Doc(Colors.Blue), Target));

            Assert.Equal(before, Snapshot());
        });

        [Fact]
        public void AnAssetThatFailsPartwayLeavesTheOldExportByteForByte() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            var before = Snapshot();
            int writes = 0;

            Assert.Throws<IOException>(() => MarkdownExport.Write(Doc(Colors.Blue, Colors.Green), Target,
                writeAsset: (path, bytes) =>
                {
                    if (++writes == 2) throw new IOException("The disk is full.");
                    File.WriteAllBytes(path, bytes);
                }));

            Assert.Equal(2, writes);
            Assert.Equal(before, Snapshot());
        });

        [Fact]
        public void ASuccessfulExportReplacesTheTextAndItsPictures() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            var old = LinkedFiles();

            MarkdownExport.Write(Doc(Colors.Blue), Target);

            var links = LinkedFiles();
            Assert.Single(links);
            Assert.NotEqual(old, links);
            Assert.True(File.Exists(Path.Combine(_dir, links[0])));
            Assert.Equal(Color(Colors.Blue), Pixel(Path.Combine(_dir, links[0])));
            Assert.False(File.Exists(Path.Combine(_dir, old[0])));
        });

        [Fact]
        public void AnExportFromBeforeTheseNamesIsCleanedUpToo() => Sta.Run(() =>
        {
            Directory.CreateDirectory(Assets);
            File.WriteAllBytes(Path.Combine(Assets, "img-1.png"), [1, 2, 3]);
            File.WriteAllText(Target, "![](note.assets/img-1.png)\r\n");

            MarkdownExport.Write(Doc(Colors.Blue), Target);

            Assert.False(File.Exists(Path.Combine(Assets, "img-1.png")));
            Assert.True(File.Exists(Path.Combine(_dir, LinkedFiles().Single())));
        });

        [Fact]
        public void ExportingTheSameNoteAgainChangesNothing() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            var before = Snapshot();

            MarkdownExport.Write(Doc(Colors.Red), Target);

            Assert.Equal(before, Snapshot());
        });

        [Fact]
        public void AFailedFirstExportLeavesNothingBehind() => Sta.Run(() =>
        {
            Assert.Throws<IOException>(() => MarkdownExport.Write(Doc(Colors.Red), Target,
                publish: (_, _) => throw new IOException("The disk is full.")));

            Assert.Empty(Directory.GetFileSystemEntries(_dir));
        });

        [Fact]
        public void AFileThatKeptItsNameWithOtherBytesIsNotWrittenOver() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            string asset = Path.Combine(_dir, LinkedFiles().Single());
            File.WriteAllBytes(asset, [1, 2, 3]);

            using (new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => MarkdownExport.Write(Doc(Colors.Red), Target));

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(asset));
            Assert.Single(Directory.GetFiles(Assets));
        });

        [Fact]
        public void FilesTheExportDidNotMakeAreLeftAlone() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            string keep = Path.Combine(Assets, "my drawing.psd");
            string linked = Path.Combine(Assets, "cover.jpg");
            File.WriteAllBytes(keep, [1]);
            File.WriteAllBytes(linked, [2]);
            File.AppendAllText(Target, "![](note.assets/cover.jpg)\r\n");

            MarkdownExport.Write(Doc(Colors.Blue), Target);

            Assert.True(File.Exists(keep));
            Assert.True(File.Exists(linked));
        });

        [Fact]
        public void ANoteWithNoPicturesRemovesTheOldFolder() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);

            MarkdownExport.Write(new FlowDocument(new Paragraph(new Run("words"))), Target);

            Assert.False(Directory.Exists(Assets));
        });

        [Fact]
        public void WithNothingToProtectAFolderThatCannotBeMadeStillExportsTheText() => Sta.Run(() =>
        {
            File.WriteAllText(Assets, "a file where the folder should go");

            Assert.True(MarkdownExport.Write(Doc(Colors.Red), Target));

            Assert.DoesNotContain("note.assets", File.ReadAllText(Target));
            Assert.Equal("a file where the folder should go", File.ReadAllText(Assets));
        });

        [Fact]
        public void AFolderThatCannotBeMadeNeverCostsAnExistingExportItsPictures() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            var before = Snapshot();

            Assert.Throws<UnauthorizedAccessException>(() => MarkdownExport.Write(Doc(Colors.Blue), Target,
                writeAsset: (_, _) => throw new UnauthorizedAccessException("no")));

            Assert.Equal(before, Snapshot());
        });

        [Fact]
        public void RecordingsTakeTheSameRoute() => Sta.Run(() =>
        {
            var take = new Attachment(AttachmentKind.Recording, [1, 2, 3, 4], 2500);
            var doc = new FlowDocument(new Paragraph(new InlineUIContainer(new Image
            {
                Source = Picture(Colors.Red), ToolTip = Attachments.Marker(take.Kind, take.Hash),
            })));

            Assert.False(MarkdownExport.Write(doc, Target, hash => hash == take.Hash ? take : null));

            string audio = Directory.GetFiles(Assets).Single();
            Assert.StartsWith("recording-", Path.GetFileName(audio));
            Assert.Equal(take.Payload, File.ReadAllBytes(audio));
            Assert.Contains("[Recording 0:02](note.assets/" + Path.GetFileName(audio) + ")", File.ReadAllText(Target));
        });

        // In the open editor a recording is its playback chip, not the marked picture a stored
        // note holds, and an export of unsaved edits walks that document.
        [Fact]
        public void AChipInTheOpenEditorIsWrittenLikeAStoredRecording() => Sta.Run(() =>
        {
            var take = new Attachment(AttachmentKind.Recording, [5, 6, 7], 1000);
            var chip = new Border { Tag = new RecordingRef(take.Hash, 1000).ToTag() };
            var doc = new FlowDocument(new Paragraph(new InlineUIContainer(chip)));
            doc.Blocks.Add(new BlockUIContainer(new Border { Tag = new RecordingRef(take.Hash, 1000).ToTag() }));

            Assert.False(MarkdownExport.Write(doc, Target, hash => hash == take.Hash ? take : null));

            Assert.Equal(take.Payload, File.ReadAllBytes(Directory.GetFiles(Assets).Single()));
            Assert.Equal(2, Regex.Matches(File.ReadAllText(Target), @"\[Recording 0:01\]").Count);
        });

        [Fact]
        public void AChipWithNoAudioCountsAsLeftOut() => Sta.Run(() =>
        {
            var chip = new Border { Tag = new RecordingRef("nothing-stored", 1000).ToTag() };

            Assert.True(MarkdownExport.Write(new FlowDocument(new Paragraph(new InlineUIContainer(chip))), Target, _ => null));
        });

        [Fact]
        public void AnExportThatLeftSomethingOutKeepsTheOldFiles() => Sta.Run(() =>
        {
            Directory.CreateDirectory(Assets);
            File.WriteAllBytes(Path.Combine(Assets, "recording-1.flac"), [1, 2, 3]);
            File.WriteAllText(Target, "[Recording 0:01](note.assets/recording-1.flac)\r\n");
            var take = new Attachment(AttachmentKind.Recording, [5, 6, 7], 1000);
            var doc = new FlowDocument(new Paragraph(new InlineUIContainer(new Image
            {
                Source = Picture(Colors.Red), ToolTip = Attachments.Marker(take.Kind, take.Hash),
            })));

            Assert.True(MarkdownExport.Write(doc, Target));

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Assets, "recording-1.flac")));
        });

        [Fact]
        public void AHandEditedLinkTheDiskCannotNameDoesNotUndoTheExport() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);
            File.AppendAllText(Target, "![](note.assets/img-old.png \"a title\")\r\n");

            MarkdownExport.Write(Doc(Colors.Blue), Target);

            Assert.True(File.Exists(Path.Combine(_dir, LinkedFiles().Single())));
        });

        [Fact]
        public void AssetNamesStayShort() => Sta.Run(() =>
        {
            MarkdownExport.Write(Doc(Colors.Red), Target);

            Assert.True(Path.GetFileName(Directory.GetFiles(Assets).Single()).Length <= "img-12345678.png".Length);
        });

        private static FlowDocument Doc(params Color[] colors)
        {
            var doc = new FlowDocument();
            foreach (var color in colors)
                doc.Blocks.Add(new Paragraph(new InlineUIContainer(new Image { Source = Picture(color) })));
            return doc;
        }

        private static BitmapSource Picture(Color color) =>
            BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new[] { color.B, color.G, color.R, color.A }, 4);

        private static uint Color(Color color) => (uint)(color.B | color.G << 8 | color.R << 16 | color.A << 24);

        private static uint Pixel(string png)
        {
            using var stream = File.OpenRead(png);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var pixels = new byte[4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 4, 0);
            return BitConverter.ToUInt32(pixels, 0);
        }

        private List<string> LinkedFiles() => [.. Regex.Matches(File.ReadAllText(Target), @"\]\(<?([^)>]+)>?\)")
            .Cast<Match>().Select(m => m.Groups[1].Value)];

        /// <summary>Every file under the export folder, by name and content.</summary>
        private SortedDictionary<string, string> Snapshot()
        {
            var files = new SortedDictionary<string, string>();
            foreach (string path in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
                files[path.Substring(_dir.Length)] = Convert.ToBase64String(File.ReadAllBytes(path));
            return files;
        }
    }
}
