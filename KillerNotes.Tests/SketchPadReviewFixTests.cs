using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KillerNotes.Controls.Sketch;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Review fixes in the SketchPad, driven through the pad's own handlers on a window that is
    /// built and never shown.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class SketchPadReviewFixTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static SketchPadWindow Pad(string tool)
        {
            var palette = ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark));
            Application.Current.Resources.MergedDictionaries.Add(palette);
            var pad = new SketchPadWindow(null, (_, _, _) => { });
            Set(pad, "_tool", Enum.Parse(typeof(SketchPadWindow).GetNestedType("Tool", BindingFlags.NonPublic)!, tool));
            return pad;
        }

        private static T Get<T>(SketchPadWindow pad, string name) =>
            (T)(typeof(SketchPadWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(pad)!;

        private static void Set(SketchPadWindow pad, string name, object? value) =>
            (typeof(SketchPadWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(pad, value);

        private static void Invoke(SketchPadWindow pad, string name, params object[] args) =>
            (typeof(SketchPadWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(pad, args);

        private static void MouseDown(SketchPadWindow pad)
        {
            var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
            Invoke(pad, "CanvasDown", pad, e);
        }

        [Fact]
        public void APolygonInProgressKeepsItsOutlineThroughARender() => Sta.RunApplication(() =>
        {
            var pad = Pad("Polygon");
            Invoke(pad, "PolyClick", new Point(10, 10));
            Invoke(pad, "PolyClick", new Point(60, 10));

            Invoke(pad, "RenderCanvas");

            var canvas = Get<Canvas>(pad, "_canvas");
            Assert.Contains(Get<UIElement>(pad, "_polyLine"), canvas.Children.Cast<UIElement>());
            Assert.Contains(Get<UIElement>(pad, "_polyRubber"), canvas.Children.Cast<UIElement>());
            Assert.Contains(Get<UIElement>(pad, "_polySnap"), canvas.Children.Cast<UIElement>());
        });

        [Fact]
        public void TheEraserOnEmptyCanvasLeavesUndoAndRedoAlone() => Sta.RunApplication(() =>
        {
            var pad = Pad("Eraser");
            var redo = Get<ICollection>(pad, "_redo");
            Get<object>(pad, "_redo").GetType().GetMethod("Push")!.Invoke(Get<object>(pad, "_redo"), [new List<SketchObject>()]);

            MouseDown(pad);

            Assert.Empty(Get<ICollection>(pad, "_undo"));
            Assert.Single(redo);
        });

        [Fact]
        public void TheEraserThatRemovesSomethingPushesOneUndoStep() => Sta.RunApplication(() =>
        {
            var pad = Pad("Eraser");
            var objects = Get<List<SketchObject>>(pad, "_objects");
            var pts = new List<double>();
            for (int i = 0; i <= 40; i++) { pts.Add(i); pts.Add(i); }
            objects.Add(new SketchObject { Kind = SketchKind.Freehand, Width = 2, Pts = pts });

            MouseDown(pad);
            Invoke(pad, "EraseStep", new Point(0, 0));

            Assert.NotEqual(41 * 2, Get<List<SketchObject>>(pad, "_objects").Sum(o => o.Pts.Count));
            Assert.Single(Get<ICollection>(pad, "_undo"));
        });

        [Fact]
        public void PlainEnterDoesNotPrintTheSketch() => Sta.RunApplication(() =>
        {
            var pad = Pad("Pen");
            var defaults = Descendants(pad).OfType<Button>().Where(b => b.IsDefault).ToList();
            Assert.Empty(defaults);
        });

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }
    }
}
