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
using System.Windows.Shapes;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The Databases and Tags dialogs' right-click cards (1.7.0), built with the real theme and
    /// strings and never opened. Every control has to reach the handler its old menu row
    /// called and show its shortcut, and none of the real handlers run here: Show file starts
    /// Explorer, Copy file writes the clipboard, and the rest open dialogs.
    /// </summary>
    [Collection("Application resources")]
    public sealed class DialogCardTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Color[] Palette =
        [
            Color.FromRgb(0xE8, 0x50, 0x50), Color.FromRgb(0xE8, 0x9A, 0x50), Color.FromRgb(0xE8, 0xD0, 0x50),
            Color.FromRgb(0x7A, 0xC8, 0x50), Color.FromRgb(0x50, 0xC8, 0x9A), Color.FromRgb(0x50, 0xAE, 0xE8),
            Color.FromRgb(0x50, 0x6E, 0xE8), Color.FromRgb(0x9A, 0x50, 0xE8), Color.FromRgb(0xE8, 0x50, 0xB4),
            Color.FromRgb(0x9A, 0x9A, 0x9A),
        ];

        // ---- Databases ----

        private static (ContextMenu Card, MenuItem[] Rows, int[] Clicks) DatabaseCard()
        {
            int[] clicks = new int[5];
            var rows = Enumerable.Range(0, 5).Select(i =>
            {
                var row = new MenuItem { Header = "row " + i };
                row.Click += (_, _) => clicks[i]++;
                return row;
            }).ToArray();
            var card = DatabasesDialog.BuildDbCard(rows[0], rows[1], rows[2], rows[3], rows[4]);
            return (card, rows, clicks);
        }

        [Fact]
        public void TheDatabaseCardHasAHeaderWithDeleteAndFourTilesTwoByTwo() => WithTheme(() =>
        {
            var (card, _, clicks) = DatabaseCard();
            DatabasesDialog.SetDbCardHeader(card, "notes.db", "16 KB");

            Assert.Equal(KillerNotes.Shell.NoteCards.Width + 44, card.MaxWidth);
            Assert.Same(Application.Current.TryFindResource("CardMenu"), card.Style);
            Assert.Equal(new[] { "notes.db", "16 KB" }, HeaderTexts(card));

            var buttons = DatabasesDialog.QuickButtons(card);
            Assert.Equal(new[] { "Rename", "Copy file", "Export", "Show file" }, buttons.Select(b => (string)b.Tag));
            // Tiles only: no text rows or separators, and four tiles sit two by two.
            Assert.DoesNotContain(card.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string });
            Assert.Equal(new[] { 0, 1, 0, 1 }, buttons.Select(Grid.GetColumn));
            Assert.Equal(new[] { 0, 0, 1, 1 }, buttons.Select(Grid.GetRow));

            // Delete is the header's icon button, with its shortcut, and reaches the Delete row.
            var delete = CardTileHelper.HeaderDelete(card);
            Assert.Equal("Ctrl+D", AutomationProperties.GetAcceleratorKey(delete));
            delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, delete));
            Assert.Equal(new[] { 0, 0, 0, 0, 1 }, clicks);
        });

        [Fact]
        public void EveryDatabaseQuickButtonShowsItsShortcut() => WithTheme(() =>
        {
            var (card, _, _) = DatabaseCard();
            var buttons = DatabasesDialog.QuickButtons(card);
            string[] gestures = ["F2", "Alt+C", "Ctrl+E", "Ctrl+R"];
            for (int i = 0; i < gestures.Length; i++)
            {
                Assert.EndsWith("(" + gestures[i] + ")", (string)buttons[i].ToolTip);
                Assert.Equal(gestures[i], AutomationProperties.GetAcceleratorKey(buttons[i]));
            }
            // Copy file and Export kept their old rows' explanations.
            Assert.StartsWith((string)Application.Current.FindResource("Str_TT_CopyFile"), (string)buttons[1].ToolTip);
            Assert.StartsWith((string)Application.Current.FindResource("Str_TT_ExportKndb"), (string)buttons[2].ToolTip);
        });

        [Fact]
        public void EachDatabaseQuickButtonClicksItsOwnRow() => WithTheme(() =>
        {
            var (card, _, clicks) = DatabaseCard();
            var buttons = DatabasesDialog.QuickButtons(card);
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttons[i]));
                Assert.Equal(Enumerable.Range(0, 5).Select(n => n <= i ? 1 : 0), clicks);
            }
        });

        [Fact]
        public void TheDatabaseRowsCallTheOldMenuHandlersAndShowTheirShortcuts() => WithTheme(() =>
        {
            using var folder = new DatabaseFolder("one.db");
            var dialog = new DatabasesDialog();
            try
            {
                var expected = new Dictionary<string, (string Handler, string Gesture)>
                {
                    ["RenameItem"] = ("RenameMenu_Click", "F2"),
                    ["CopyItem"] = ("CopyFileMenu_Click", "Alt+C"),
                    ["ExportItem"] = ("ExportMenu_Click", "Ctrl+E"),
                    ["RevealItem"] = ("RevealMenu_Click", "Ctrl+R"),
                    ["DeleteItem"] = ("Delete_Click", "Ctrl+D"),
                };
                foreach (var pair in expected)
                {
                    var row = (MenuItem)typeof(DatabasesDialog).GetField(pair.Key, Any)!.GetValue(dialog)!;
                    Assert.Equal(new[] { pair.Value.Handler }, ClickHandlers(row));
                    Assert.Equal(pair.Value.Gesture, row.InputGestureText);
                }

                var list = (ListBox)dialog.FindName("DbList");
                var card = list.ContextMenu!;
                var delete = (MenuItem)typeof(DatabasesDialog).GetField("DeleteItem", Any)!.GetValue(dialog)!;
                Assert.Same(delete, card.Tag);   // the header's Delete button raises this row

                // The header names the selected database over its details line, and keeps its Delete.
                typeof(DatabasesDialog).GetMethod("DbList_ContextMenuOpening", Any)!.Invoke(dialog, [list, null]);
                var header = HeaderTexts(card);
                Assert.Equal("one.db", header[0]);
                Assert.Contains("KB", header[1]);
                Assert.NotNull(KillerNotes.Shell.NoteCards.HeaderDelete(card));

                // Rename is the one handler safe to run here: it swaps the row's name for a box.
                var rename = DatabasesDialog.QuickButtons(card)[0];
                rename.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, rename));
                Assert.IsType<TextBox>(((StackPanel)((ListBoxItem)list.SelectedItem).Content).Children[0]);
            }
            finally { dialog.Close(); }
        });

        // ---- Tags ----

        private sealed class TagCalls
        {
            public int Rename, Recolor, Delete;
            public string? Color;
        }

        private static (ContextMenu Card, TagCalls Calls) TagCard()
        {
            var calls = new TagCalls();
            var card = TagsDialog.BuildTagCard("work", "#50AEE8", Palette,
                rename: () => calls.Rename++, recolor: () => calls.Recolor++,
                setColor: hex => calls.Color = hex, delete: () => calls.Delete++);
            return (card, calls);
        }

        private static SwatchRow Swatches(ContextMenu card) =>
            (SwatchRow)Drawer(card).Child;

        // Runs the dispatcher for a while, so an open card lays out and a slide finishes.
        private static void Settle(int ms)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static Border Drawer(ContextMenu card) =>
            card.Items.OfType<MenuItem>().Select(m => m.Header).OfType<Border>().Single(b => b.Child is SwatchRow);

        [Fact]
        public void TheTagCardHasItsNameAndDeleteAColorRowAndARenameTile() => WithTheme(() =>
        {
            var (card, _) = TagCard();

            Assert.Equal(KillerNotes.Shell.NoteCards.Width + 44, card.MaxWidth);
            var head = (DockPanel)((MenuItem)card.Items[0]).Header;
            Assert.Equal(Color.FromRgb(0x50, 0xAE, 0xE8), ((SolidColorBrush)head.Children.OfType<Ellipse>().Single().Fill).Color);
            Assert.Equal("work", head.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First().Text);
            // The dot sits before the name, which fills what is left of the row.
            Assert.True(head.Children.IndexOf(head.Children.OfType<Ellipse>().Single()) < head.Children.IndexOf(head.Children.OfType<StackPanel>().Single()));

            var swatches = Swatches(card);
            Assert.Equal(Visibility.Collapsed, swatches.Children[0].Visibility);   // no "no color" for a tag
            Assert.Equal("on", ((Button)swatches.Children[6]).Tag);                // the tag's own color is ringed

            Assert.DoesNotContain(card.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string });
            // Rename and Color side by side; the swatch row waits under them, slid in until Color is clicked.
            Assert.Equal(new[] { "Rename", "Color" }, KillerNotes.Shell.NoteCards.Tiles(card).Select(b => (string)b.Tag));
            Assert.Same(Drawer(card), ((MenuItem)card.Items[2]).Header);
            Assert.Equal(0, Drawer(card).MaxHeight);
            var rename = CardTileHelper.Tile(card, "rename");
            Assert.Equal("Rename", rename.Tag);
            Assert.Equal("F2", AutomationProperties.GetAcceleratorKey(rename));
            var delete = CardTileHelper.HeaderDelete(card);
            Assert.Equal("Delete tag", AutomationProperties.GetName(delete));
            Assert.Equal("Ctrl+D", AutomationProperties.GetAcceleratorKey(delete));
        });

        [Fact]
        public void TheTagCardPickerIsChangeColorAndShowsAltC() => WithTheme(() =>
        {
            var (card, calls) = TagCard();
            var picker = Swatches(card).Children.OfType<Button>().Last();
            Assert.Equal("Change color (Alt+C)", picker.ToolTip);

            picker.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, picker));
            Assert.Equal(1, calls.Recolor);
            Assert.Null(calls.Color);
        });

        [Fact]
        public void TheTagCardSwatchesRowsReachTheirActions() => WithTheme(() =>
        {
            var (card, calls) = TagCard();
            var swatch = (Button)Swatches(card).Children[1];
            swatch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, swatch));
            Assert.Equal("#E85050", calls.Color);

            CardTileHelper.Click(card, "rename");
            var delete = CardTileHelper.HeaderDelete(card);
            delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, delete));
            Assert.Equal((1, 1, 0), (calls.Rename, calls.Delete, calls.Recolor));
        });

        [Fact]
        public void TheColorTileSlidesTheSwatchRowOutOnOneLineAndBackIn() => WithTheme(() =>
        {
            // Opened for real over a small shown host. Color keeps the card open and grows its bottom by the swatch
            // row, every swatch and the picker on one line; a second click slides it back to nothing.
            var (card, _) = TagCard();
            var host = new Window { Content = new Border(), Width = 200, Height = 120, Left = 0, Top = 0,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            host.Show();
            try
            {
                card.PlacementTarget = (FrameworkElement)host.Content;
                card.Placement = PlacementMode.Relative;
                card.IsOpen = true;
                Settle(100);
                var drawer = Drawer(card);
                double closed = card.ActualHeight;
                Assert.True(closed > 0, "The tag card did not lay out");
                Assert.Equal(0, drawer.ActualHeight);

                var color = CardTileHelper.Tile(card, "color");
                color.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, color));
                Settle(400);
                Assert.True(card.IsOpen);
                var swatches = Swatches(card);
                Assert.Equal(swatches.ActualHeight + swatches.Margin.Top + swatches.Margin.Bottom, drawer.ActualHeight, 1);
                Assert.Equal(closed + drawer.ActualHeight, card.ActualHeight, 1);
                var buttons = swatches.Children.OfType<Button>().Where(b => b.Visibility == Visibility.Visible).ToList();
                double top = buttons[0].TranslatePoint(new Point(), card).Y;
                Assert.All(buttons, b => Assert.Equal(top + buttons[0].ActualHeight / 2,
                    b.TranslatePoint(new Point(), card).Y + b.ActualHeight / 2, 1));
                if (Environment.GetEnvironmentVariable("KN_INK_SHOTS") is string shots)
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth),
                        (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(card);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(System.IO.Path.Combine(shots, "card-Tag-colors-out.png"));
                    encoder.Save(stream);
                }

                color.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, color));
                Settle(400);
                Assert.Equal(0, drawer.ActualHeight);
                Assert.Equal(closed, card.ActualHeight, 1);
            }
            finally { card.IsOpen = false; host.Close(); }
        });

        [Fact]
        public void EveryTagRowCarriesACard() => WithTheme(() =>
        {
            using var store = new TempStore();
            NoteStore.AddTag("work", "#ff0000");
            var dialog = new TagsDialog();
            try
            {
                var item = (ListBoxItem)((ListBox)dialog.FindName("TagList")).Items[0];
                Assert.Same(Application.Current.TryFindResource("CardMenu"), item.ContextMenu!.Style);
                // The row buttons keep their order: the Alt+C / F2 / Ctrl+D keys find them by index.
                var actions = ((Grid)item.Content).Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToList();
                Assert.Equal(new[] { "Alt+C", "F2", "Ctrl+D" }, actions.Select(AutomationProperties.GetAcceleratorKey));
            }
            finally { dialog.Close(); }
        });

        // ---- harness ----

        /// <summary>The header's title and details lines (a NoteCards header: Delete docked right, then the text).</summary>
        private static List<string> HeaderTexts(ContextMenu card) =>
            ((DockPanel)((MenuItem)card.Items[0]).Header).Children.OfType<StackPanel>().Single()
                .Children.OfType<TextBlock>().Select(t => t.Text).ToList();

        /// <summary>Lays a card out at its own width without opening it. A ContextMenu refuses any
        /// parent but its popup, so it is laid out as a root of its own.</summary>
        private static FrameworkElement Lay(ContextMenu card)
        {
            card.ApplyTemplate();
            card.Measure(new Size(card.MaxWidth, double.PositiveInfinity));
            card.Arrange(new Rect(card.DesiredSize));
            card.UpdateLayout();
            return card;
        }

        private static string[] ClickHandlers(MenuItem row)
        {
            var store = typeof(UIElement).GetProperty("EventHandlersStore", Any)!.GetValue(row)!;
            var handlers = (RoutedEventHandlerInfo[])store.GetType().GetMethod("GetRoutedEventHandlers")!
                .Invoke(store, [MenuItem.ClickEvent])!;
            return [.. handlers.Select(h => h.Handler.Method.Name)];
        }

        private sealed class DatabaseFolder : IDisposable
        {
            public string Dir { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

            public DatabaseFolder(params string[] names)
            {
                Directory.CreateDirectory(Dir);
                foreach (string name in names) File.WriteAllBytes(System.IO.Path.Combine(Dir, name), new byte[16]);
                NoteStore.DbDirOverride = Dir;
                NoteStore.DemoDbFile = names[0];
            }

            public void Dispose()
            {
                NoteStore.DbDirOverride = null;
                NoteStore.DemoDbFile = null;
                try { Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
            }
        }

        private static void WithTheme(Action body) => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            ResourceDictionary themeDict = ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark));
            var merged = Application.Current.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(themeDict);
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
