using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KillerNotes.Controls.Sketch;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The sketch pad's right-click card (1.7.0 card menus). An object's card names the object,
    /// puts the verbs that keep it on a quick strip and Delete under a rule; the empty canvas's
    /// card holds Add image and Paste image, with Clear all under the rule. The pad is built and
    /// never shown, the clipboard is never read (the builder takes its answer), and each control
    /// is checked against the shortcut the canvas binds for it.
    /// </summary>
    [Collection("Application resources")]
    public sealed class SketchCardTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

        private static SketchPadWindow Pad(params SketchObject[] objects)
        {
            var pad = new SketchPadWindow(null, (_, _, _) => { });
            if (objects.Length > 0) pad.LoadObjects(objects);
            return pad;
        }

        /// <summary>The pad's own copy of an object: LoadObjects clones what it is given.</summary>
        private static SketchObject Placed(SketchPadWindow pad, int index) =>
            ((List<SketchObject>)typeof(SketchPadWindow).GetField("_objects", Members)!.GetValue(pad)!)[index];

        private static List<SketchObject> Objects(SketchPadWindow pad) =>
            (List<SketchObject>)typeof(SketchPadWindow).GetField("_objects", Members)!.GetValue(pad)!;

        private static SketchObject Line(bool arced) => new()
        {
            Kind = SketchKind.Line,
            Pts = arced ? [10, 10, 60, 40, 120, 10] : [10, 10, 120, 80],
        };

        private static SketchObject Label(bool bold) => new() { Kind = SketchKind.Text, Text = "Hello", X = 40, Y = 40, Bold = bold };

        private static SketchObject Picture() => new() { Kind = SketchKind.Image, X = 10, Y = 10, W = 40, H = 30, Opacity = 0.4 };

        private static string HeaderOf(ContextMenu card) =>
            AutomationProperties.GetName((MenuItem)card.Items[0]);

        private static List<Button> Quick(ContextMenu card)
        {
            var strip = card.Items.OfType<MenuItem>().Single(m => m.Header is Border { Child: UniformGrid });
            return ((UniformGrid)((Border)strip.Header).Child).Children.OfType<Button>().ToList();
        }

        private static List<MenuItem> Rows(ContextMenu card) =>
            card.Items.OfType<MenuItem>().Where(m => m.Header is string).ToList();

        private static Button QuickNamed(ContextMenu card, string name) =>
            Quick(card).Single(b => AutomationProperties.GetName(b) == name);

        private static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        private static void Click(MenuItem m) => m.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        [Fact]
        public void ALineCardNamesItAndOffersArcBesideTheReorderButtons() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false));
            var card = pad.BuildCanvasCard(Placed(pad, 0), canPaste: false);

            Assert.Equal("Line", HeaderOf(card));
            Assert.Equal(new[] { "Bring to front", "Send to back", "Duplicate", "Arc line" },
                Quick(card).Select(b => AutomationProperties.GetName(b)));
            Assert.Equal(new[] { "Front", "Back", "Duplicate", "Arc line" }, Quick(card).Select(b => (string)b.Tag));
            Assert.Equal(new[] { "Bring to front (Ctrl+Shift+Up)", "Send to back (Ctrl+Shift+Down)", "Duplicate (Ctrl+D)", "Arc line (Ctrl+Shift+A)" },
                Quick(card).Select(b => (string)b.ToolTip));
            Assert.Equal(300 + 44, card.MaxWidth);
        });

        [Fact]
        public void AnArcedLineOffersStraightenInsteadOfArc() => WithDark(() =>
        {
            var pad = Pad(Line(arced: true));
            var card = pad.BuildCanvasCard(Placed(pad, 0), canPaste: false);
            var straighten = QuickNamed(card, "Straighten line");
            Assert.Equal("Straighten line (Ctrl+Shift+L)", straighten.ToolTip);
            Assert.DoesNotContain(Quick(card), b => AutomationProperties.GetName(b) == "Arc line");

            Click(straighten);
            Assert.Equal(4, Placed(pad, 0).Pts.Count);   // the same Straighten the old row ran
        });

        [Fact]
        public void DeleteIsAListRowUnderARuleWithItsShortcut() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false), Label(bold: false));
            var card = pad.BuildCanvasCard(Placed(pad, 0), canPaste: false);

            var delete = Assert.Single(Rows(card));
            Assert.Equal("Delete", delete.Header);
            Assert.Equal("Delete", delete.InputGestureText);
            Assert.True(card.Items[card.Items.IndexOf(delete) - 1] is MenuItem { Header: Border { Child: UniformGrid } }, "The row should sit right under the strip, whose lower rule separates them.");
            Assert.DoesNotContain(Quick(card), b => AutomationProperties.GetName(b) == "Delete");

            var keep = Placed(pad, 1);
            Click(delete);
            Assert.Same(keep, Assert.Single(Objects(pad)));
        });

        [Fact]
        public void ATextCardCarriesEditAndBoldAndBoldNamesWhichWayItGoes() => WithDark(() =>
        {
            var pad = Pad(Label(bold: false));
            var label = Placed(pad, 0);
            var card = pad.BuildCanvasCard(label, canPaste: false);

            Assert.Equal("Text", HeaderOf(card));
            Assert.Equal(5, Quick(card).Count);
            Assert.Equal("Edit text (F10)", QuickNamed(card, "Edit text").ToolTip);
            var bold = QuickNamed(card, "Bold");
            Assert.Equal("Bold (Ctrl+B)", bold.ToolTip);
            Click(bold);
            Assert.True(label.Bold);

            var again = pad.BuildCanvasCard(label, canPaste: false);
            Assert.Equal("Remove bold (Ctrl+B)", QuickNamed(again, "Remove bold").ToolTip);
            Assert.Equal("Bold", QuickNamed(again, "Remove bold").Tag);
        });

        [Fact]
        public void AnImageCardResetsOpacity() => WithDark(() =>
        {
            var pad = Pad(Picture());
            var image = Placed(pad, 0);
            var card = pad.BuildCanvasCard(image, canPaste: false);

            Assert.Equal("Image", HeaderOf(card));
            var reset = QuickNamed(card, "Reset opacity");
            Assert.Equal("Reset opacity (Ctrl+Shift+O)", reset.ToolTip);
            Click(reset);
            Assert.Equal(1, image.Opacity);
        });

        [Fact]
        public void FrontAndDuplicateRunWhatTheOldRowsRan() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false), Label(bold: false));
            var line = Placed(pad, 0);
            Click(QuickNamed(pad.BuildCanvasCard(line, canPaste: false), "Bring to front"));
            Assert.Same(line, Objects(pad).Last());

            Click(QuickNamed(pad.BuildCanvasCard(line, canPaste: false), "Send to back"));
            Assert.Same(line, Objects(pad).First());

            Click(QuickNamed(pad.BuildCanvasCard(line, canPaste: false), "Duplicate"));
            Assert.Equal(3, Objects(pad).Count);
            Assert.Equal(SketchKind.Line, Objects(pad).Last().Kind);
            Assert.NotSame(line, Objects(pad).Last());
        });

        [Theory]
        [InlineData(SketchKind.Freehand, "Drawing")]
        [InlineData(SketchKind.Arrow, "Arrow")]
        [InlineData(SketchKind.Rect, "Shape")]
        [InlineData(SketchKind.Ellipse, "Shape")]
        [InlineData(SketchKind.Polygon, "Shape")]
        public void EveryKindHasAName(SketchKind kind, string name) => WithDark(() =>
        {
            var pad = Pad(new SketchObject { Kind = kind, Pts = [0, 0, 20, 20] });
            var card = pad.BuildCanvasCard(Placed(pad, 0), canPaste: false);
            Assert.Equal(name, HeaderOf(card));
            Assert.InRange(Quick(card).Count, 3, 5);
        });

        [Fact]
        public void TheCanvasCardGreysPasteWithNoImageAndClearAllOnAnEmptyPad() => WithDark(() =>
        {
            var pad = Pad();
            var card = pad.BuildCanvasCard(null, canPaste: false);

            Assert.Equal("SketchPad", HeaderOf(card));
            Assert.Equal(new[] { "Add image...", "Paste image" }, Quick(card).Select(b => AutomationProperties.GetName(b)));
            Assert.Equal("Add image... (I)", QuickNamed(card, "Add image...").ToolTip);
            var paste = QuickNamed(card, "Paste image");
            Assert.False(paste.IsEnabled);
            Assert.Equal("Paste image (Ctrl+V)", paste.ToolTip);

            var clear = Assert.Single(Rows(card));
            Assert.Equal("Clear all", clear.Header);
            Assert.Equal("F8", clear.InputGestureText);
            Assert.False(clear.IsEnabled);
            Assert.True(card.Items[card.Items.IndexOf(clear) - 1] is MenuItem { Header: Border { Child: UniformGrid } }, "The row should sit right under the strip, whose lower rule separates them.");
        });

        [Fact]
        public void TheCanvasCardEnablesPasteAndClearAllWhenThereIsSomethingToActOn() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false));
            var card = pad.BuildCanvasCard(null, canPaste: true);
            Assert.True(QuickNamed(card, "Paste image").IsEnabled);
            var clear = Assert.Single(Rows(card));
            Assert.True(clear.IsEnabled);
            Click(clear);
            Assert.Empty(Objects(pad));
        });

        [Fact]
        public void TheNewCaptionsAreInEveryLanguage()
        {
            string root = Root();
            foreach (string strings in Directory.GetFiles(Path.Combine(root, "Strings"), "*.xaml"))
            {
                string text = File.ReadAllText(strings);
                foreach (string key in new[] { "Front", "Back", "Drawing", "Line", "Arrow", "Text", "Image" })
                    Assert.Contains("x:Key=\"Str_SketchCard_" + key + "\"", text);
            }
        }

        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate the application sources");
        }

        private static void WithDark(Action body) => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
            foreach (string p in new[] { "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + p, UriKind.Relative) });
            try { body(); }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
        });
    }
}
