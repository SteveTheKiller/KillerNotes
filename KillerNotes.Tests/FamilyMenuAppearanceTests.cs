using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class FamilyMenuAppearanceTests
    {
        [Fact]
        public void FontsActionUsesTheLiveMenuFont() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var app = Application.Current;
            var menu = (ContextMenu)window.FindName("ThemeMenu");
            bool local = app.Resources.Keys.Cast<object>().Contains("MenuFontFamily");
            var previous = app.FindResource("MenuFontFamily");
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            try
            {
                ((Button)window.FindName("ThemeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                menu.IsOpen = false;
                menu.PlacementTarget = target;
                menu.Placement = PlacementMode.Bottom;
                menu.CustomPopupPlacementCallback = null;
                menu.IsOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                string name = (string)app.FindResource("Str_Fonts_Open");
                var label = menu.Items.OfType<Button>().SelectMany(button => Descendants(button))
                    .OfType<TextBlock>().Single(text => text.Text == name);
                Assert.Equal(((FontFamily)previous).Source, label.FontFamily.Source);

                app.Resources["MenuFontFamily"] = new FontFamily("Arial");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Equal("Arial", label.FontFamily.Source);
            }
            finally
            {
                menu.IsOpen = false;
                owner.Close();
                if (local) app.Resources["MenuFontFamily"] = previous;
                else app.Resources.Remove("MenuFontFamily");
            }
        });

        [Fact]
        public void LightColorPickerCaptionHasNoTextShadow() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var app = Application.Current;
            app.Resources.MergedDictionaries[0] = ThemeHarness.Create(Theme.Light, Accent.Blue);
            var picker = new ColorPickerDialog(null, Colors.Red);
            try
            {
                var caption = Descendants((DependencyObject)picker.Content).OfType<TextBlock>()
                    .Single(text => text.Text == (string)app.FindResource("Str_Dlg_PickColor"));
                Assert.Null(caption.Effect);
            }
            finally { picker.Close(); }
        });

        [Fact]
        public void MultiNoteCardFitsItsContentWithoutClippingThePalette() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long first = NoteStore.Create("A");
            long second = NoteStore.Create("B");
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            var menu = (ContextMenu)window.FindName("NoteCard");
            try
            {
                host.ShowNoteCard(new[] { first, second }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                menu.UpdateLayout();
                Assert.InRange(menu.ActualWidth, 280, 403.99);
                // 1.7.1: the palette sits in the Color tile's flyout, and every tile fits the card's face.
                var face = (FrameworkElement)menu.Template.FindName("MenuRoot", menu);
                var palette = CardTileHelper.Swatches(menu);
                Assert.Equal(11, palette.Children.OfType<Button>().Count());
                foreach (var tile in Shell.NoteCards.Tiles(menu))
                {
                    Assert.InRange(tile.TranslatePoint(new Point(), face).X, 0, face.ActualWidth);
                    Assert.InRange(tile.TranslatePoint(new Point(tile.ActualWidth, 0), face).X, 0, face.ActualWidth);
                }
            }
            finally { menu.IsOpen = false; owner.Close(); }
        });

        [Fact]
        public void UnpickedDarkCardSegmentsUseReadableNeutralTextAndOnlyThePickedChoiceUsesAccent() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var app = Application.Current;
            var choice = new RadioButton { Content = "Rendered", IsChecked = false, Style = (Style)app.FindResource("CardSegment") };
            var track = new Border { Background = (Brush)app.FindResource("MenuHoverBrush"), Child = choice };
            track.Measure(new Size(200, 40));
            track.Arrange(new Rect(0, 0, 200, 40));
            track.UpdateLayout();
            var text = ((SolidColorBrush)choice.Foreground).Color;
            var fill = ((SolidColorBrush)track.Background).Color;

            Assert.Equal(text.R, text.G);
            Assert.Equal(text.G, text.B);
            Assert.True(ColorContrast.Contrast(text, fill) >= 4.5);
            choice.IsChecked = true;
            Assert.Equal(((SolidColorBrush)app.FindResource("PrimaryBrush")).Color, ((SolidColorBrush)choice.Foreground).Color);
        });
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
