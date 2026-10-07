using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class InputAccessibilityTests
    {
        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate application sources.");
        }

        private static XElement Control(string path, string name) =>
            XDocument.Load(Path.Combine(Root(), path)).Descendants().Single(element =>
                (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

        [Theory]
        [InlineData("Shell/MainWindow.xaml", "FindBox", "Str_KS_Find")]
        [InlineData("Shell/MainWindow.xaml", "ReplaceBox", "Str_ReplacePlaceholder")]
        [InlineData("Shell/MainWindow.xaml", "SidebarReplaceBox", "Str_ReplacePlaceholder")]
        [InlineData("Shell/MainWindow.xaml", "FontSizeSlider", "Str_TT_FontSize")]
        [InlineData("Shell/MainWindow.xaml", "FontHeaderCombo", "Str_Fonts_Headers")]
        [InlineData("Shell/MainWindow.xaml", "FontSidebarCombo", "Str_Fonts_Sidebar")]
        [InlineData("Shell/MainWindow.xaml", "FontContentCombo", "Str_Fonts_Content")]
        [InlineData("Controls/PasswordDialog.xaml", "PwBox", "Str_TT_Lock")]
        [InlineData("Controls/PasswordDialog.xaml", "PwConfirmBox", "Str_Lbl_Confirm")]
        [InlineData("Controls/BackupDialog.xaml", "FolderBox", "Str_Bk_Folder")]
        [InlineData("Controls/BackupDialog.xaml", "IntervalBox", "Str_Bk_Interval")]
        [InlineData("Controls/BackupDialog.xaml", "KeepBox", "Str_Bk_Keep")]
        [InlineData("Controls/BackupDialog.xaml", "BackupList", "Str_Bk_Title")]
        [InlineData("Controls/HistoryDialog.xaml", "VersionList", "Str_Hist_Title")]
        [InlineData("Controls/HistoryDialog.xaml", "Preview", "Str_Hist_Title")]
        [InlineData("Controls/TagsDialog.xaml", "TagList", "Str_Ctx_Tags")]
        [InlineData("Controls/TagsDialog.xaml", "NewNameBox", "Str_Tags_NeedName")]
        [InlineData("Controls/TagsDialog.xaml", "NewColorSwatch", "Str_TT_TagPickColor")]
        [InlineData("Controls/FileDialog.xaml", "RecentsList", "Str_TT_RecentLocations")]
        [InlineData("Controls/FileDialog.xaml", "PlacesList", "Str_Sec_File")]
        [InlineData("Controls/DatabasesDialog.xaml", "TrashRetentionBox", "Str_Trash_Retention")]
        public void InputPeersExposeNamesAndFollowLocaleChanges(string path, string name, string resource) => Sta.Run(() =>
        {
            var source = Control(path, name);
            var markup = new XElement(source.Name, new XAttribute(source.Attribute("AutomationProperties.Name")!));
            var control = (FrameworkElement)XamlReader.Parse(markup.ToString());
            control.Resources[resource] = "Accessible label";
            var peer = UIElementAutomationPeer.CreatePeerForElement(control)!;

            Assert.NotNull(peer);
            Assert.Equal("Accessible label", peer.GetName());
            if (control is PasswordBox) Assert.True(peer.IsPassword());
            if (control is Button)
            {
                Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
                Assert.NotNull(peer.GetPattern(PatternInterface.Invoke));
            }

            control.Resources[resource] = "Translated label";
            Assert.Equal("Translated label", peer.GetName());
        });

        [Fact]
        public void InputPromptAnnouncesItsCurrentHeading() => Sta.Run(() =>
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = Control("Controls/InputDialog.xaml", "ValueBox");
            var markup = new XElement(wpf + "StackPanel", new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(wpf + "TextBlock", new XAttribute(x + "Name", "HeadingText"), new XAttribute("Text", "Rename group")),
                new XElement(source.Name, new XAttribute(source.Attribute("AutomationProperties.Name")!)));
            var panel = (StackPanel)XamlReader.Parse(markup.ToString());
            var label = (TextBlock)panel.Children[0];
            var input = (TextBox)panel.Children[1];
            panel.Measure(new Size(300, 100));
            panel.Arrange(new Rect(0, 0, 300, 100));
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            var peer = new TextBoxAutomationPeer(input);
            Assert.Equal("Rename group", peer.GetName());
            label.Text = "Rename tag";
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.Equal("Rename tag", peer.GetName());
        });
    }
}
