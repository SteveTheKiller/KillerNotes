using System;
using System.IO;
using KillerNotes.Cli;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Command-line parsing that runs before any database is touched.</summary>
    public sealed class CliArgumentTests : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".md");

        public CliArgumentTests() => File.WriteAllText(_file, "# from file");

        public void Dispose() { try { File.Delete(_file); } catch { /* best effort */ } }

        [Fact]
        public void Content_file_is_read_into_content()
        {
            string[] result = Program.InlineContentFile(["create", "--title", "t", "--content-file", _file]);
            Assert.Equal(["create", "--title", "t", "--content", "# from file"], result);
        }

        [Fact]
        public void An_option_value_that_looks_like_a_flag_is_left_alone()
        {
            string[] args = ["create", "--title", "--content-file", "--tags", "a"];
            Assert.Equal(args, Program.InlineContentFile(args));
        }

        [Fact]
        public void A_title_of_dash_dash_content_does_not_block_content_file()
        {
            string[] result = Program.InlineContentFile(["create", "--title", "--content", "--content-file", _file]);
            Assert.Equal(["create", "--title", "--content", "--content", "# from file"], result);
        }

        [Fact]
        public void Content_and_content_file_together_are_rejected()
        {
            Assert.Throws<ArgumentException>(() =>
                Program.InlineContentFile(["create", "--content", "x", "--content-file", _file]));
        }

        [Fact]
        public void Help_with_a_command_name_prints_help()
        {
            var original = Console.Out;
            var captured = new StringWriter();
            Console.SetOut(captured);
            try { Assert.Equal(0, Program.Main(["--help", "create"])); }
            finally { Console.SetOut(original); }
            Assert.Contains("create", captured.ToString());
        }
    }
}
