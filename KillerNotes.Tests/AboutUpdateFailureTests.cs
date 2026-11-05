using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests;

/// <summary>
/// A downloaded update that fails its checksum or signature is refused, and the releases page is
/// no remedy for that, so About says so. Every language carries the message.
/// </summary>
public sealed class AboutUpdateFailureTests
{
    [Fact]
    public void EveryLanguageSaysARefusedUpdateWasNotInstalled()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Strings", "en-US.xaml"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] tables = Directory.GetFiles(Path.Combine(dir!, "Strings"), "*.xaml");
        Assert.Equal(20, tables.Length);
        foreach (string table in tables)
        {
            var strings = XDocument.Load(table).Descendants()
                .Where(e => e.Attribute(x + "Key") != null)
                .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => e.Value);
            Assert.True(strings.TryGetValue("Str_About_UpdateNotVerified", out var refused) && refused.Trim().Length > 0,
                Path.GetFileName(table) + " says a refused update was not installed.");
        }
    }
}
