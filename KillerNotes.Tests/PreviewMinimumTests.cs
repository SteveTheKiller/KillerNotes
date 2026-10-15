using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class PreviewMinimumTests
    {
        [Fact]
        public void SplitPreviewStaysInsideTheViewportWithScaleAndAuxiliaryColumns() => Sta.Run(() =>
        {
            foreach (double scale in new[] { .7, 1, 1.5, 2.5 })
            foreach (double sidebar in new[] { 24, Math.Max(300 / scale, 160), Math.Max(480 / scale, 160) })
            foreach (double auxiliary in new[] { 0, 268 })
            foreach (double ratio in new[] { .05, .95 })
            {
                var fixture = new Layout(scale, sidebar, auxiliary, ratio);
                fixture.Measure(3000);
                double minimum = fixture.Minimum("Split");
                fixture.Measure(minimum);
                Assert.True(fixture.Right <= minimum - 1 + .5,
                    $"scale={scale}, sidebar={sidebar}, auxiliary={auxiliary}, ratio={ratio}, right={fixture.Right}, minimum={minimum}");
                Assert.True(fixture.Editor.ActualWidth >= 199.5);
                Assert.True(fixture.Preview.ActualWidth >= 199.5);
                Assert.Equal(720, fixture.Minimum("Source"));
                Assert.Equal(720, fixture.Minimum("Rendered"));
            }
        });

        [Fact]
        public void OldFixedMinimumReproducesTheScaledPreviewOverflow() => Sta.Run(() =>
        {
            var fixture = new Layout(1.5, 280 / 1.5, 0, .5);
            fixture.Measure(720);
            Assert.True(fixture.Right > 720);
            fixture.Measure(fixture.Minimum("Split"));
            Assert.True(fixture.Right <= fixture.Root.ActualWidth - 1 + .5);
        });

        private sealed class Layout
        {
            private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            private readonly MainWindow window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            public readonly Border Root = new Border { BorderThickness = new Thickness(1) };
            private readonly Grid host = new Grid();
            private readonly Grid area = new Grid();
            private readonly Border preview = new Border();
            public readonly ColumnDefinition Editor = new ColumnDefinition { MinWidth = 200 };
            public readonly ColumnDefinition Preview = new ColumnDefinition { MinWidth = 200 };

            public Layout(double scale, double sidebar, double auxiliary, double ratio)
            {
                host.LayoutTransform = new ScaleTransform(scale, scale);
                var sidebarColumn = new ColumnDefinition { Width = new GridLength(sidebar) };
                host.ColumnDefinitions.Add(sidebarColumn);
                host.ColumnDefinitions.Add(new ColumnDefinition());
                var pane = new Border { Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1), Child = area };
                Grid.SetColumn(pane, 1);
                host.Children.Add(pane);
                var gutter = new ColumnDefinition { Width = new GridLength(auxiliary > 0 ? 48 : 0) };
                var divider = new ColumnDefinition { Width = new GridLength(6) };
                var outline = new ColumnDefinition { Width = new GridLength(auxiliary > 0 ? 220 : 0) };
                Editor.Width = new GridLength(ratio, GridUnitType.Star);
                Preview.Width = new GridLength(1 - ratio, GridUnitType.Star);
                foreach (var column in new[] { gutter, Editor, divider, Preview, outline }) area.ColumnDefinitions.Add(column);
                Grid.SetColumn(preview, 3);
                area.Children.Add(preview);
                Root.Child = host;
                Set("_appScale", scale);
                Set("RootBorder", Root);
                Set("RootGrid", new Grid());
                Set("ContentPane", pane);
                Set("SidebarCol", sidebarColumn);
                Set("GutterCol", gutter);
                Set("OutlineCol", outline);
                Set("PreviewDividerCol", divider);
                Set("EditorCol", Editor);
                Set("PreviewCol", Preview);
            }

            private void Set(string name, object value) => typeof(MainWindow).GetField(name, Fields)!.SetValue(window, value);
            public double Minimum(string mode)
            {
                Set("_previewMode", Enum.Parse(typeof(MainWindow).GetNestedType("PreviewMode", BindingFlags.NonPublic)!, mode));
                return (double)typeof(MainWindow).GetMethod("PreviewMinimumWidth", Fields)!.Invoke(window, null)!;
            }
            public void Measure(double width)
            {
                Root.Measure(new Size(width, 500));
                Root.Arrange(new Rect(0, 0, width, 500));
                Root.UpdateLayout();
            }
            public double Right => preview.TranslatePoint(new Point(preview.ActualWidth, 0), Root).X;
        }
    }
}
