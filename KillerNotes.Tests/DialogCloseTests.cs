using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;
using Xunit.Abstractions;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    /// <summary>
    /// The dialog caption X sits as far from the bar's right edge as from its top, and reads at
    /// a comfortable size, measured from pixels rendered off screen.
    /// </summary>
    public sealed class DialogCloseTests
    {
        private readonly ITestOutputHelper _o;
        public DialogCloseTests(ITestOutputHelper o) => _o = o;

        [Fact]
        public void TheCloseXIsCenteredInItsCorner() => Sta.RunApplication(() =>
        {
            Application app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var ds = new List<ResourceDictionary>();
            foreach (string p in new[] { "Themes/Black.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
            { var d = new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + p, UriKind.Relative) }; app.Resources.MergedDictionaries.Add(d); ds.Add(d); }
            var t = new ResourceDictionary
            {
                ["DialogTitleBarHeight"] = 32.0, ["DialogTitleMargin"] = new Thickness(6, 0, 0, 0),
                ["AboutCloseWidth"] = 28.0, ["AboutCloseHeight"] = 26.0, ["AboutCloseGlyph"] = "✕",
                ["AboutCloseFont"] = new FontFamily("Segoe UI"), ["AboutCloseFg"] = Brushes.White,
                ["CaptionGlyphWeight"] = FontWeights.Normal, ["DialogTitleBarBrush"] = Brushes.Black,
                ["CaptionButtonBrush"] = Brushes.Transparent, ["WordmarkVisibility"] = Visibility.Collapsed,
                ["PlainTitleVisibility"] = Visibility.Collapsed,
            };
            app.Resources.MergedDictionaries.Add(t);
            var bar = new KillerNotes.Controls.DialogTitleBar();
            var root = new Border { Width = 360, Background = Brushes.Black, Child = bar };
            root.Measure(new Size(360, double.PositiveInfinity)); root.Arrange(new Rect(root.DesiredSize)); root.UpdateLayout();
            int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bmp.Render(root);
            var px = new byte[w * h * 4]; bmp.CopyPixels(px, w * 4, 0);
            int top = h, bottom = -1, left = w, right = -1;
            for (int y = 0; y < h; y++) for (int x = w - 50; x < w; x++)
                if (px[(y * w + x) * 4 + 1] > 90) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x); }
            _o.WriteLine($"bar {w}x{h}; glyph x {left}..{right} y {top}..{bottom}; gap top {top}, right {w - 1 - right}, bottom {h - 1 - bottom}; size {right - left + 1}x{bottom - top + 1}");
            app.Resources.MergedDictionaries.Remove(t);
            foreach (var d in ds) app.Resources.MergedDictionaries.Remove(d);
            Assert.True(bottom >= 0, "No X was drawn.");
            Assert.True(top == w - 1 - right, $"The X sits {top}px from the top and {w - 1 - right}px from the right.");
            Assert.True(right - left + 1 >= 10, $"The X is {right - left + 1}px wide.");
        });
    }
}
