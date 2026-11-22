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
    /// The sketch pad's right-click card on the family card rules (Shell/NoteCards.cs). An object's
    /// card names the object with Delete as the header's icon button, then what that kind can do and
    /// Front, Back and Duplicate as tiles, every row full; the empty canvas's card is one row of Add
    /// image, Paste image and Clear all. No text rows anywhere. The pad is built and never shown, the
    /// clipboard is never read (the builder takes its answer), and each tile is checked against the
    /// shortcut the canvas binds for it.
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

        private static List<Button> Tiles(ContextMenu card) => KillerNotes.Shell.NoteCards.Tiles(card).ToList();

        private static Button Tile(ContextMenu card, string id) => CardTileHelper.Tile(card, id);

        private static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        private static void NoTextRows(ContextMenu card) =>
            Assert.DoesNotContain(card.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string });

        [Fact]
        public void ALineCardPutsArcFirstThenTheReorderTilesTwoByTwo() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false));
            var card = pad.BuildCanvasCard(Placed(pad, 0), canPaste: false);

            Assert.Equal("Line", HeaderOf(card));
            Assert.Equal(new[] { "Arc line", "Front", "Back", "Duplicate" }, Tiles(card).Select(b => (string)b.Tag));
            Assert.Equal(new[] { "Arc line (Ctrl+Shift+A)", "Bring to front (Ctrl+Shift+Up)", "Send to back (Ctrl+Shift+Down)", "Duplicate (Ctrl+D)" },
                Tiles(card).Select(b => (string)b.ToolTip));
            Assert.Equal(new[] { "Ctrl+Shift+A", "Ctrl+Shift+Up", "Ctrl+Shift+Down", "Ctrl+D" }, Tiles(card).Select(AutomationProperties.GetAcceleratorKey));
            Assert.Equal(KillerNotes.Shell.NoteCards.Width + 44, card.MaxWidth);
            NoTextRows(card);
        });

        [Fact]
        public void AnArcedLineOffersStraightenInsteadOfArc() => WithDark(() =>
        {
            var pad = Pad(Line(arced: true));
            var card = pad.BuildCanvasCard(Placed(pad, 0), canPaste: false);
            var straighten = Tile(card, "straighten");
            Assert.Equal("Straighten line (Ctrl+Shift+L)", straighten.ToolTip);
            Assert.Null(KillerNotes.Shell.NoteCards.Tile(card, "arc"));

            Click(straighten);
            Assert.Equal(4, Placed(pad, 0).Pts.Count);   // the same Straighten the old row ran
        });

        [Fact]
        public void DeleteIsTheHeadersIconButtonWithItsShortcut() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false), Label(bold: false));
            var card = pad.BuildCanvasCard(Placed(pad, 0), canPaste: false);

            var delete = CardTileHelper.HeaderDelete(card);
            Assert.Equal("Delete", AutomationProperties.GetName(delete));
            Assert.Equal("Delete", AutomationProperties.GetAcceleratorKey(delete));
            Assert.DoesNotContain(Tiles(card), b => (string)b.Tag == "Delete");

            var keep = Placed(pad, 1);
            Click(delete);
            Assert.Same(keep, Assert.Single(Objects(pad)));
        });

        [Fact]
        public void ATextCardIsFiveTilesWithEditAndBoldFirstAndBoldShowsWhetherItIsOn() => WithDark(() =>
        {
            var pad = Pad(Label(bold: false));
            var label = Placed(pad, 0);
            var card = pad.BuildCanvasCard(label, canPaste: false);

            Assert.Equal("Text", HeaderOf(card));
            Assert.Equal(new[] { "Edit text", "Bold", "Front", "Back", "Duplicate" }, Tiles(card).Select(b => (string)b.Tag));
            // Five tiles: a row of three, then a row of two wider ones, so no row has a gap.
            Assert.Equal(new[] { 3, 2 }, KillerNotes.Shell.NoteCards.RowSizes(5));
            Assert.Equal("Edit text (F10)", Tile(card, "edittext").ToolTip);
            var bold = Tile(card, "bold");
            Assert.Equal("Bold (Ctrl+B)", bold.ToolTip);
            Assert.False(CardTileHelper.Checked(card, "bold"));
            Click(bold);
            Assert.True(label.Bold);

            var again = pad.BuildCanvasCard(label, canPaste: false);
            Assert.Equal("Remove bold (Ctrl+B)", Tile(again, "bold").ToolTip);
            Assert.Equal("Bold", Tile(again, "bold").Tag);
            Assert.True(CardTileHelper.Checked(again, "bold"));
        });

        [Fact]
        public void AnImageCardResetsOpacity() => WithDark(() =>
        {
            var pad = Pad(Picture());
            var image = Placed(pad, 0);
            var card = pad.BuildCanvasCard(image, canPaste: false);

            Assert.Equal("Image", HeaderOf(card));
            Assert.Equal(new[] { "Reset opacity", "Front", "Back", "Duplicate" }, Tiles(card).Select(b => (string)b.Tag));
            var reset = Tile(card, "resetopacity");
            Assert.Equal("Reset opacity (Ctrl+Shift+O)", reset.ToolTip);
            Click(reset);
            Assert.Equal(1, image.Opacity);
        });

        [Fact]
        public void FrontAndDuplicateRunWhatTheOldRowsRan() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false), Label(bold: false));
            var line = Placed(pad, 0);
            Click(Tile(pad.BuildCanvasCard(line, canPaste: false), "front"));
            Assert.Same(line, Objects(pad).Last());

            Click(Tile(pad.BuildCanvasCard(line, canPaste: false), "back"));
            Assert.Same(line, Objects(pad).First());

            Click(Tile(pad.BuildCanvasCard(line, canPaste: false), "duplicate"));
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
            Assert.InRange(Tiles(card).Count, 3, 5);
            NoTextRows(card);
        });

        [Fact]
        public void TheCanvasCardDimsPasteWithNoImageAndClearAllOnAnEmptyPad() => WithDark(() =>
        {
            var pad = Pad();
            var card = pad.BuildCanvasCard(null, canPaste: false);

            Assert.Equal("SketchPad", HeaderOf(card));
            Assert.Equal(new[] { "Add image", "Paste image", "Clear all" }, Tiles(card).Select(b => (string)b.Tag));
            Assert.Equal("Add image... (I)", Tile(card, "addimage").ToolTip);
            var paste = Tile(card, "pasteimage");
            Assert.False(paste.IsEnabled);
            Assert.Equal("Paste image (Ctrl+V)", paste.ToolTip);
            var clear = Tile(card, "clearall");
            Assert.Equal("F8", AutomationProperties.GetAcceleratorKey(clear));
            Assert.False(clear.IsEnabled);
            Assert.Null(KillerNotes.Shell.NoteCards.HeaderDelete(card));
            NoTextRows(card);
        });

        [Fact]
        public void TheCanvasCardEnablesPasteAndClearAllWhenThereIsSomethingToActOn() => WithDark(() =>
        {
            var pad = Pad(Line(arced: false));
            var card = pad.BuildCanvasCard(null, canPaste: true);
            Assert.True(Tile(card, "pasteimage").IsEnabled);
            var clear = Tile(card, "clearall");
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
