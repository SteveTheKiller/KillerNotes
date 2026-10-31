using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using KillerNotes.Controls.Sketch;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The SketchPad's pen gesture, driven through the pad's own handlers on a window that is
    /// built and never shown.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class SketchPadGestureTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static SketchPadWindow Pad()
        {
            var palette = ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark));
            Application.Current.Resources.MergedDictionaries.Add(palette);
            var pad = new SketchPadWindow(null, (_, _, _) => { });
            Set(pad, "_tool", Enum.Parse(typeof(SketchPadWindow).GetNestedType("Tool", BindingFlags.NonPublic)!, "Pen"));
            return pad;
        }

        private static T Get<T>(SketchPadWindow pad, string name) =>
            (T)(typeof(SketchPadWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(pad)!;

        private static void Set(SketchPadWindow pad, string name, object? value) =>
            (typeof(SketchPadWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(pad, value);

        private static void Invoke(SketchPadWindow pad, string name, params object[] args) =>
            (typeof(SketchPadWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(pad, args);

        [Fact]
        public void AStrokeInProgressKeepsOneLivePreviewThatGrowsWithTheStroke() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            Set(pad, "_gesturePts", new List<double> { 10, 10 });
            Invoke(pad, "UpdatePreview", new Point(10, 10));
            var first = Get<UIElement?>(pad, "_previewEl");

            var pts = Get<List<double>>(pad, "_gesturePts");
            pts.Add(20); pts.Add(25);
            Invoke(pad, "UpdatePreview", new Point(20, 25));
            pts.Add(30); pts.Add(15);
            Invoke(pad, "UpdatePreview", new Point(30, 15));

            var live = Assert.IsType<Polyline>(Get<UIElement?>(pad, "_previewEl"));
            Assert.Same(first, live);
            Assert.Equal(3, live.Points.Count);
            Assert.Equal(new Point(30, 15), live.Points[2]);
            Assert.Single(Get<Canvas>(pad, "_canvas").Children.OfType<Polyline>());
        });

        [Fact]
        public void ALostMouseCaptureEndsTheStrokeInsteadOfLeavingItRunning() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            var canvas = Get<Canvas>(pad, "_canvas");
            Set(pad, "_dragging", true);
            Set(pad, "_gesturePts", new List<double> { 10, 10, 40, 40, 70, 20 });
            Invoke(pad, "UpdatePreview", new Point(70, 20));

            canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });

            Assert.False(Get<bool>(pad, "_dragging"));
            Assert.Null(Get<UIElement?>(pad, "_previewEl"));
            var stroke = Assert.Single(Get<List<SketchObject>>(pad, "_objects"));
            Assert.Equal(SketchKind.Freehand, stroke.Kind);
            Assert.Equal(6, stroke.Pts.Count);
        });

        [Fact]
        public void ALostMouseCaptureEndsAResizeAndABend() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            var canvas = Get<Canvas>(pad, "_canvas");
            Set(pad, "_resizing", true);
            Set(pad, "_curving", true);

            canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });

            Assert.False(Get<bool>(pad, "_resizing"));
            Assert.False(Get<bool>(pad, "_curving"));
        });
    }
}
