using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class NewNoteButtonThemeTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NewNoteUsesGrayOnlyForClassicCaption(bool classic) => Sta.Run(() =>
        {
            var button = new Button { Content = "+ New", Style = LoadStyle() };
            button.Resources["UseDialogCaption"] = classic;
            button.Resources["PrimaryFillBrush"] = Brushes.Maroon;
            button.Resources["OnPrimaryFillBrush"] = Brushes.White;
            button.Resources["OutlineFaceBrush"] = Brushes.Silver;
            button.Resources["OutlineTextBrush"] = Brushes.Black;
            button.Measure(new Size(180, 40));
            button.Arrange(new Rect(0, 0, 180, 40));
            button.UpdateLayout();

            Assert.Equal(classic ? Colors.Silver : Colors.Maroon,
                Assert.IsType<SolidColorBrush>(button.Background).Color);
            Assert.Equal(classic ? Colors.Black : Colors.White,
                Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            var face = Assert.IsType<Border>(button.Template.FindName("border", button));
            Assert.Equal(button.Background, face.Background);
        });

        private static Style LoadStyle()
        {
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Controls", "Controls.xaml")))
                root = Path.GetDirectoryName(root);
            if (root == null) throw new DirectoryNotFoundException("KillerNotes source root not found");
            XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = XDocument.Load(Path.Combine(root, "Controls", "Controls.xaml"));
            var dictionary = new XElement(w + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName));
            foreach (string key in new[] { "PrimaryButton", "NewNoteButton" })
                dictionary.Add(new XElement(source.Root!.Elements(w + "Style")
                    .Single(style => (string?)style.Attribute(x + "Key") == key)));
            return (Style)((ResourceDictionary)XamlReader.Parse(dictionary.ToString()))["NewNoteButton"];
        }
    }
}
