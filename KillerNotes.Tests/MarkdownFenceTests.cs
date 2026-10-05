using KillerNotes.Services;
using KillerNotes.Shell;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using Xunit;

namespace KillerNotes.Tests
{
    public class MarkdownFenceTests
    {
        [Fact]
        public void DeclaredLanguageUsesRealEditorTokensAtTheCorrectOffsets()
        {
            var fence = new MarkdownFence();
            var parts = fence.ReadParagraph("```ps1\nGet-Item $value\n```\nprose");
            var type = typeof(MainWindow).GetNestedType("CodeLanguage", BindingFlags.NonPublic)!;
            var method = typeof(MainWindow).GetMethod("CollectSyntaxTokens", BindingFlags.NonPublic | BindingFlags.Static)!;
            var tokens = (List<(int Start, int Length, Color Color)>)method.Invoke(null,
                new object[] { parts[1].Text, Enum.Parse(type, parts[1].Language!), false })!;
            Assert.Contains(tokens, t => parts[1].Text.Substring(t.Start, t.Length) == "$value");
            Assert.All(tokens, t => Assert.InRange(t.Start + t.Length, 1, parts[1].Text.Length));
            var plain = (List<(int Start, int Length, Color Color)>)method.Invoke(null,
                new object[] { "123 $value", Enum.Parse(type, "Plain"), false })!;
            Assert.Empty(plain);
        }
        [Fact]
        public void SoftLineBreaksWithinOneParagraphKeepLanguageAndOffsets()
        {
            var fence = new MarkdownFence();
            var parts = fence.ReadParagraph("```ps1\nGet-Item\n```\n**prose**");
            Assert.Equal("PowerShell", parts[1].Language);
            Assert.Equal(7, parts[1].Start);
            Assert.Equal("Markdown", parts[2].Language);
            Assert.Null(parts[3].Language);
            Assert.Null(fence.Read("outside"));
        }
        [Theory]
        [InlineData("powershell", "PowerShell")]
        [InlineData("ps1", "PowerShell")]
        [InlineData("js", "JavaScript")]
        [InlineData("json", "Json")]
        [InlineData("python", "Python")]
        [InlineData("unknown", "Plain")]
        [InlineData("", "Plain")]
        public void CarriesTheDeclaredLanguageAndReturnsToProse(string info, string language)
        {
            var fence = new MarkdownFence();
            Assert.Null(fence.Read("# Title"));
            Assert.Equal("Markdown", fence.Read("```" + info));
            Assert.Equal(language, fence.Read("return value"));
            Assert.Equal(language, fence.Read(""));
            Assert.Equal("Markdown", fence.Read("```"));
            Assert.Null(fence.Read("Prose after code"));
        }

        [Fact]
        public void ShorterOrDifferentMarkersDoNotCloseTheFence()
        {
            var fence = new MarkdownFence();
            fence.Read("  ~~~~sql");
            Assert.Equal("Sql", fence.Read("~~~"));
            Assert.Equal("Sql", fence.Read("````"));
            Assert.Equal("Sql", fence.Read("~~~~ trailing text"));
            Assert.Equal("Markdown", fence.Read("~~~~~"));
            Assert.Null(fence.Read("outside"));
        }
    }
}
