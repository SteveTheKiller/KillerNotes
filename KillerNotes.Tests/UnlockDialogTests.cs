using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KillerNotes.Controls;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The 1.7.0 unlock layout is for the database unlock prompt only. The same dialog also sets,
    /// changes and removes a password (an empty box is how protection comes off), tests a backup
    /// and opens a shared note, and those prompts keep the plain layout and its behavior.
    /// </summary>
    [Collection("Application resources")]
    public sealed class UnlockDialogTests
    {
        [Fact]
        public void TheUnlockLayoutLeadsWithTheFileAndOneButton() => WithResources(() =>
        {
            var dialog = new PasswordDialog("Unlock KillerNotes", "detail", "Unlock",
                extraText: "New", extra2Text: "Open...", showCancel: false);
            try
            {
                dialog.UseUnlockLayout("New_Main.db");

                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(dialog, "UnlockHeader").Visibility);
                Assert.Equal("New_Main.db", Named<TextBlock>(dialog, "UnlockTitle").Text);
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(dialog, "HeadingText").Visibility);
                var ok = Named<Button>(dialog, "OkButton");
                Assert.Same(Named<Panel>(dialog, "UnlockFooter"), ok.Parent);
                Assert.Equal(HorizontalAlignment.Stretch, ok.HorizontalAlignment);
                Assert.Equal("New", Named<MenuItem>(dialog, "OtherDbNew").Header);
                Assert.Equal("Open...", Named<MenuItem>(dialog, "OtherDbOpen").Header);
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(dialog, "ErrorLine").Visibility);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void AWrongPasswordShowsUnderTheBoxAndClearsOnTyping() => WithResources(() =>
        {
            var dialog = new PasswordDialog("Unlock", "", "Unlock", extraText: "New", extra2Text: "Open...", showCancel: false);
            try
            {
                dialog.UseUnlockLayout("Main.db", "Wrong password - try again");
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(dialog, "ErrorLine").Visibility);
                Assert.Equal("Wrong password - try again", Named<TextBlock>(dialog, "ErrorText").Text);

                Named<PasswordBox>(dialog, "PwBox").Password = "x";

                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(dialog, "ErrorLine").Visibility);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void TheRevealedPasswordIsTheOneSubmitted() => WithResources(() =>
        {
            var dialog = new PasswordDialog("Unlock", "", "Unlock");
            try
            {
                Named<PasswordBox>(dialog, "PwBox").Password = "secret";
                var reveal = Named<Button>(dialog, "RevealButton");
                reveal.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, reveal));
                var plain = Named<TextBox>(dialog, "PwPlain");
                Assert.Equal("secret", plain.Text);
                plain.Text = "secret2";

                var ok = Named<Button>(dialog, "OkButton");
                ok.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, ok));

                Assert.True(dialog.Confirmed);
                Assert.Equal("secret2", dialog.Password);
            }
            finally { dialog.Close(); }
        });

        [Fact]
        public void OtherPromptsKeepThePlainLayoutAndAnEmptyPasswordStillSubmits() => WithResources(() =>
        {
            var dialog = new PasswordDialog("Change or remove password", "Leave both empty to remove.", "Apply", showConfirm: true);
            try
            {
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(dialog, "UnlockHeader").Visibility);
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(dialog, "UnlockFooter").Visibility);
                var ok = Named<Button>(dialog, "OkButton");
                Assert.NotSame(Named<Panel>(dialog, "UnlockFooter"), ok.Parent);
                Assert.True(ok.IsEnabled);

                ok.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, ok));

                Assert.True(dialog.Confirmed);
                Assert.Equal("", dialog.Password);
                Assert.Equal("", dialog.PasswordConfirm);
            }
            finally { dialog.Close(); }
        });

        [Theory]
        [InlineData("created", "\uE823")]
        [InlineData("title", "\uE8CB")]
        [InlineData("custom", "\uE76F")]
        public void TheSortButtonWearsTheSameGlyphAsItsMenuRow(string field, string glyph)
            => Assert.Equal(glyph, MainWindow.SortGlyphFor(field));

        private static T Named<T>(Window window, string name) where T : class
            => window.FindName(name) as T ?? throw new InvalidOperationException(name + " is missing");

        private static void WithResources(Action body) => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var previous = app.Resources.MergedDictionaries.ToArray();
            try
            {
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative)
                    });
                body();
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in previous) app.Resources.MergedDictionaries.Add(dictionary);
            }
        });
    }
}
