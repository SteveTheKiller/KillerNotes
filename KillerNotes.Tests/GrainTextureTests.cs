using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class GrainTextureTests
    {
        private static double Hue(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            double h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            return h < 0 ? h + 360 : h;
        }

        private static byte[] Pixels(BitmapSource bmp)
        {
            var pixels = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
            bmp.CopyPixels(pixels, bmp.PixelWidth * 4, 0);
            return pixels;
        }

        [Fact]
        public void AGreenAccentLeansPurple()
        {
            var lean = GrainTexture.LeanFor(Color.FromRgb(0x00, 0xff, 0x66), Colors.Black);
            Assert.NotNull(lean);
            Assert.InRange(Hue(lean!.Value), 270, 300);
        }

        [Fact]
        public void ABlueAccentLeansOrangeBrown()
        {
            var lean = GrainTexture.LeanFor(Color.FromRgb(0x50, 0xae, 0xe8), Color.FromRgb(0x1c, 0x1c, 0x1c));
            Assert.NotNull(lean);
            Assert.InRange(Hue(lean!.Value), 15, 45);
        }

        [Fact]
        public void BloodsPeachAccentLeansOrangeBrownNotBlue()
        {
            var lean = GrainTexture.LeanFor(Color.FromRgb(0xf8, 0xc9, 0x9e), Color.FromRgb(0x24, 0x0c, 0x0d));
            Assert.NotNull(lean);
            Assert.InRange(Hue(lean!.Value), 15, 45);
        }

        [Fact]
        public void ARedAccentLeansOrangeBrown()
        {
            var lean = GrainTexture.LeanFor(Color.FromRgb(0xff, 0x29, 0x29), Colors.Black);
            Assert.NotNull(lean);
            Assert.InRange(Hue(lean!.Value), 15, 45);
        }

        [Fact]
        public void AWhiteAccentLeansFromTheBackgroundHue()
        {
            var lean = GrainTexture.LeanFor(Colors.White, Color.FromRgb(0x00, 0x1a, 0x28));
            Assert.NotNull(lean);
            Assert.InRange(Hue(lean!.Value), 15, 45);
        }

        [Fact]
        public void ANeutralThemeHasNoLean()
        {
            Assert.Null(GrainTexture.LeanFor(Colors.White, Color.FromRgb(0x1c, 0x1c, 0x1c)));
        }

        [Fact]
        public void NeutralGrainIsSoftGrayInManyShades() => Sta.Run(() =>
        {
            var pixels = Pixels(GrainTexture.Generate(null));
            var shades = new HashSet<byte>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                Assert.Equal(pixels[i], pixels[i + 1]);
                Assert.Equal(pixels[i + 1], pixels[i + 2]);
                Assert.Equal(70, pixels[i + 3]);
                shades.Add(pixels[i]);
            }
            Assert.True(shades.Count > 150, $"only {shades.Count} shades");
        });

        [Fact]
        public void NeutralGrainIncludesDistinctDarkAndBrightSpecks() => Sta.Run(() =>
        {
            var pixels = Pixels(GrainTexture.Generate(null));
            int dark = 0, bright = 0, total = pixels.Length / 4;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] <= 16) dark++;
                if (pixels[i] >= 239) bright++;
            }
            Assert.True(dark >= total * 0.20, $"Dark specks: {dark * 100.0 / total:F1}%");
            Assert.True(bright >= total * 0.20, $"Bright specks: {bright * 100.0 / total:F1}%");
        });

        [Fact]
        public void HighlightGrainPreservesSparkleWithoutGrayHaze() => Sta.Run(() =>
        {
            var normal = Pixels(GrainTexture.Generate(null));
            var highlights = Pixels(GrainTexture.Generate(null, highlightsOnly: true));
            double normalLight = 0, highlightLight = 0;
            int normalPeak = 0, highlightPeak = 0;
            for (int i = 0; i < normal.Length; i += 4)
            {
                int before = normal[i] * normal[i + 3];
                int after = highlights[i] * highlights[i + 3];
                normalLight += before;
                highlightLight += after;
                normalPeak = Math.Max(normalPeak, before);
                highlightPeak = Math.Max(highlightPeak, after);
            }
            Assert.Equal(normalPeak, highlightPeak);
            Assert.True(highlightLight < normalLight * 0.65, $"Highlight grain adds {highlightLight / normalLight:P1} of the prior light while retaining its peak sparkle.");
        });

        [Fact]
        public void LeaningGrainTintsSomeSpecksAndIsDeterministic() => Sta.Run(() =>
        {
            var lean = Color.FromRgb(0xba, 0x61, 0xd8);
            var first = Pixels(GrainTexture.Generate(lean));
            var second = Pixels(GrainTexture.Generate(lean));
            Assert.Equal(first, second);

            int tinted = 0, total = first.Length / 4;
            for (int i = 0; i < first.Length; i += 4)
                if (first[i] != first[i + 1] || first[i + 1] != first[i + 2]) tinted++;
            Assert.InRange(tinted, total * 30 / 100, total * 50 / 100);
        });
    }
}
