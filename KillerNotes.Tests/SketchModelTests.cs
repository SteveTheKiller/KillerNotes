using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Models;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class SketchModelTests
    {
        // An arced line stores start, control, end and is drawn as a quadratic bezier through the
        // first and last of them. The control point is not on the curve.
        private static SketchObject ArcedLine() => new()
        {
            Kind = SketchKind.Line, Width = 2, Pts = [0, 0, 100, 200, 200, 0],
        };

        [Fact]
        public void TheEraserBreaksAnArcedLineWhereTheCurveIs()
        {
            // The curve peaks at (100, 100), halfway between the chord and the control point.
            var result = SketchModel.EraseAt([ArcedLine()], new Point(100, 100), 6);

            Assert.DoesNotContain(result, o => o.Kind == SketchKind.Line);
            Assert.Equal(2, result.Count);
            Assert.All(result, o => Assert.Equal(SketchKind.Freehand, o.Kind));
        }

        [Fact]
        public void TheEraserLeavesAnArcedLineAloneWhenItOnlyTouchesTheInvisibleControlPoint()
        {
            var line = ArcedLine();

            var result = SketchModel.EraseAt([line], new Point(100, 200), 6);

            Assert.Same(line, Assert.Single(result));
        }

        private static string Png()
        {
            var bmp = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return System.Convert.ToBase64String(ms.ToArray());
        }

        [Fact]
        public void APlacedImageIsDecodedOnceNoMatterHowOftenTheCanvasIsRebuilt() => Sta.Run(() =>
        {
            var img = new SketchObject { Kind = SketchKind.Image, Img = Png(), W = 20, H = 20 };

            var first = ((Image)SketchModel.BuildElement(img)).Source;
            var second = ((Image)SketchModel.BuildElement(img)).Source;
            var copy = ((Image)SketchModel.BuildElement(img.Clone())).Source;

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.Same(first, copy);
        });

        [Fact]
        public void ReplacingAnImagesPixelsDecodesThemAgain() => Sta.Run(() =>
        {
            var img = new SketchObject { Kind = SketchKind.Image, Img = Png(), W = 20, H = 20 };
            var before = ((Image)SketchModel.BuildElement(img)).Source;

            var other = BitmapSource.Create(3, 3, 96, 96, PixelFormats.Bgra32, null, new byte[36], 12);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(other));
            using var ms = new MemoryStream();
            enc.Save(ms);
            img.Img = System.Convert.ToBase64String(ms.ToArray());
            var after = ((Image)SketchModel.BuildElement(img)).Source;

            Assert.NotSame(before, after);
            Assert.Equal(3, ((BitmapSource)after).PixelWidth);
        });
    }
}
