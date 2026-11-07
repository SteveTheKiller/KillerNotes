using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The title bar database name sits level with the library icon beside it. The kit
    /// ThemeManager's FileNameTransform is KillerPDF's 3px drop, and every take of the kit used
    /// to hand that drop to the label, so the label pins its own identity transform instead of
    /// reading the theme token.</summary>
    public sealed class TitleBarDatabaseNameTests
    {
        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate the application sources");
        }

        [Fact]
        public void TheDatabaseNameKeepsAnIdentityTransformInsteadOfTheThemeShift()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var document = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var label = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ActiveDatabaseText");

            Assert.Equal("{x:Static Transform.Identity}", (string?)label.Attribute("RenderTransform"));
        }
    }
}
