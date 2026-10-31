using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KillerNotes.Cli;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Global options are read in option position, so a value that happens to be named like
    /// one is left with its command, and the bridge's limits and exit codes agree with the app's.</summary>
    public sealed class CliOptionTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

        public CliOptionTests() => Directory.CreateDirectory(_folder);

        public void Dispose() { try { Directory.Delete(_folder, true); } catch { /* best effort */ } }

        [Theory]
        [InlineData("create|--title|t|--content|x|--json", "create|--title|t|--content|x")]
        [InlineData("list|--json", "list")]
        [InlineData("search|foo|--json", "search|foo")]
        [InlineData("get|5|--max-chars|10|--json", "get|5|--max-chars|10")]
        [InlineData("create|--title|t|--content|--json", "create|--title|t|--content|--json")]
        [InlineData("update|--id|3|--title|--json", "update|--id|3|--title|--json")]
        [InlineData("convert|--id|1|--format|markdown|--allow-loss|--json", "convert|--id|1|--format|markdown|--allow-loss")]
        [InlineData("delete-group|--name|x|--confirm|--json", "delete-group|--name|x|--confirm")]
        [InlineData("import-folder|--path|p|--dry-run|--json", "import-folder|--path|p|--dry-run")]
        public void ATrailingJsonSwitchIsRemovedOnlyInOptionPosition(string given, string expected) =>
            Assert.Equal(expected.Split('|'), Program.StripJsonSwitch(given.Split('|')));

        [Theory]
        [InlineData("create|--title|t|--content|--database")]
        [InlineData("update|--id|3|--content|--database")]
        [InlineData("list|--group|--database")]
        public void ADatabaseNamedValueIsNotTheDatabaseOption(string given)
        {
            var values = new List<string>(given.Split('|'));
            Assert.Null(Program.TakeDatabase(values));
            Assert.Equal(given.Split('|'), values);
        }

        [Theory]
        [InlineData("list|--database|C:/x.db|--limit|3", "list|--limit|3")]
        [InlineData("search|foo|--database|C:/x.db", "search|foo")]
        [InlineData("get|5|--max-chars|10|--database|C:/x.db", "get|5|--max-chars|10")]
        public void TheDatabaseOptionInOptionPositionIsTaken(string given, string rest)
        {
            var values = new List<string>(given.Split('|'));
            Assert.Equal("C:/x.db", Program.TakeDatabase(values));
            Assert.Equal(rest.Split('|'), values);
        }

        [Fact]
        public void ADatabaseOptionWithoutAValueStillFails()
        {
            Assert.Throws<ArgumentException>(() => Program.TakeDatabase(new List<string> { "get", "5", "--database" }));
        }

        [Fact]
        public void ContentFileLargerThanTheServerAcceptsIsRefusedHere()
        {
            string file = Path.Combine(_folder, "big.md");
            File.WriteAllText(file, new string('a', 500_001), Encoding.UTF8);
            Assert.Throws<ArgumentException>(() => Program.InlineContentFile(["create", "--title", "t", "--content-file", file]));
        }

        [Fact]
        public void ContentFileOfManyBytesButFewCharactersIsAccepted()
        {
            string file = Path.Combine(_folder, "euro.md");
            File.WriteAllText(file, new string('\u20AC', 400_000), Encoding.UTF8);
            string[] result = Program.InlineContentFile(["create", "--title", "t", "--content-file", file]);
            Assert.Equal(400_000, result[4].Length);
        }

        [Theory]
        [InlineData("cutoff")]
        [InlineData("empty")]
        [InlineData("notjson")]
        public void AnAnswerThatCannotBeReadAfterTheRequestWasSentIsAnUnknownOutcome(string kind)
        {
            using var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            if (kind == "cutoff") { w.Write(10); w.Write(new byte[] { 1, 2, 3 }); }
            else if (kind == "empty") w.Write(0);
            else { byte[] junk = Encoding.UTF8.GetBytes("not json"); w.Write(junk.Length); w.Write(junk); }
            w.Flush();
            stream.Position = 0;

            var original = Console.Error;
            Console.SetError(new StringWriter());
            try { Assert.Equal(Program.UnknownOutcomeExitCode, Program.ReadAnswer(stream, TimeSpan.FromSeconds(2))); }
            finally { Console.SetError(original); }
        }

        [Fact]
        public void AnAnswerLargerThanAMegabyteIsRead()
        {
            using var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            w.Write(1_500_000); w.Write(new byte[1_500_000]); w.Flush();
            stream.Position = 0;
            var (body, outcome) = Program.ReadResponseFrame(stream, TimeSpan.FromSeconds(5));
            Assert.Equal(Program.ResponseOutcome.Complete, outcome);
            Assert.Equal(1_500_000, body!.Length);
        }
    }
}
