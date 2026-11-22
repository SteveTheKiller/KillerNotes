using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    // 1.7.1: the menus that were still plain text rows (the tray icon, sort, search icon, dictation, mentions strip,
    // every text box and the unlock dialog's "Use another database") are tile cards on the family card rules: tiles
    // only, no text rows or separators. Set KN_INK_SHOTS to a folder to keep a render of each card.
    [Collection("Application resources")]
    public sealed class ContextCardTests
    {
        [Fact]
        public void EveryLeftoverMenuIsATileCardWithNoTextRows() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var title = (TextBox)window.FindName("TitleBox");
            var dialog = new PasswordDialog("Unlock", "", "Unlock", extraText: "New database", extra2Text: "Open database");
            dialog.UseUnlockLayout("Demo");
            // UiSettingsScope turns both global shortcuts off; the tray card shows them at their defaults here.
            App.SetSetting("QuickNoteHotkey", KillerNotes.Services.QuickNoteHotkey.Choices[0]);
            App.SetSetting("ShowWindowHotkey", KillerNotes.Services.ShowWindowHotkey.Choices[0]);
            var tray = window.BuildTrayCard();
            App.SetSetting("QuickNoteHotkey", "");
            App.SetSetting("ShowWindowHotkey", "");
            var cards = new (string Name, ContextMenu Card, string[] Tiles)[]
            {
                ("Tray", tray, Array.Empty<string>()),
                ("Sort", window.BuildSortCard(), ["created", "title", "custom", "reverse"]),
                ("SearchIcon", window.BuildSearchIconCard(), ["searchbar"]),
                ("Dictation", window.BuildDictationCard(), ["speechmodel"]),
                ("Mentions", window.BuildMentionsCard(), ["hidementions"]),
                ("TextBox", TextFieldCard.Fill(new ContextMenu(), title), ["cut", "copy", "paste", "selectall"]),
                ("OtherDb", dialog.FillOtherDbCard(), ["newdb", "opendb"]),
            };
            string? shots = Environment.GetEnvironmentVariable("KN_INK_SHOTS");
            foreach (var (name, card, ids) in cards)
            {
                Assert.DoesNotContain(card.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string } || item is MenuItem { Command: not null });
                Assert.NotEmpty(NoteCards.Tiles(card));
                foreach (string id in ids)
                {
                    Assert.NotNull(NoteCards.Tile(card, id));
                    Assert.False(string.IsNullOrWhiteSpace(NoteCards.Find(card, id)!.Keys), $"{name} tile {id} has no keys");
                }
                if (shots != null) Shot(window, card, System.IO.Path.Combine(shots, $"card-{name}.png"));
            }
            Assert.Equal(3, NoteCards.Tiles(cards[0].Card).Count());
            if (shots == null) return;
            Application.Current.Resources.MergedDictionaries[0] = ThemeHarness.Create(KillerNotes.Services.Theme.TrashPolka, KillerNotes.Services.Accent.Red);
            foreach (var (name, card, _) in cards) Shot(window, card, System.IO.Path.Combine(shots, $"card-{name}-ink.png"));
            _host?.Close();
            _host = null;
        });

        [Fact]
        public void TheSortCardShowsTheActiveSortAndDimsReverseForCustomOrder() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var card = window.BuildSortCard();
            CardTileHelper.Run(card, "title");
            card = window.BuildSortCard();
            Assert.True(CardTileHelper.Checked(card, "title"));
            Assert.False(CardTileHelper.Checked(card, "created"));
            Assert.True(NoteCards.Find(card, "reverse")!.Enabled);
            CardTileHelper.Run(card, "custom");
            card = window.BuildSortCard();
            Assert.True(CardTileHelper.Checked(card, "custom"));
            Assert.False(NoteCards.Find(card, "reverse")!.Enabled);
            Assert.NotNull(NoteCards.Tile(card, "reverse"));   // a dimmed tile keeps its spot
        });

        [Fact]
        public void TheSortButtonOpensTheSortCard() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            // The shell window is never shown here, so WPF will not open the popup; what the click owns is
            // building a fresh card from the current sort and handing it to the button.
            var sort = (Button)window.FindName("SortBtn");
            CardTileHelper.Run(window.BuildSortCard(), "title");
            sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var first = sort.ContextMenu;
            try
            {
                Assert.NotNull(first);
                Assert.True(CardTileHelper.Checked(first!, "title"));
                Assert.NotNull(NoteCards.Tile(first!, "reverse"));
                first!.IsOpen = false;
                CardTileHelper.Run(first, "created");
                sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.NotSame(first, sort.ContextMenu);
                Assert.True(CardTileHelper.Checked(sort.ContextMenu!, "created"));
            }
            finally { if (sort.ContextMenu != null) sort.ContextMenu.IsOpen = false; }
        });

        // Replays the UI's own triggers: the ContextMenuOpening each element raises on a right-click, and the tray
        // icon's own ShowMenu. Each one has to leave a tile card on the element with the expected tiles.
        [Fact]
        public void EveryRightClickTriggerHandsOutItsTileCard() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var dialog = new PasswordDialog("Unlock", "", "Unlock", extraText: "New database", extra2Text: "Open database");
            dialog.UseUnlockLayout("Demo");
            var title = (TextBox)window.FindName("TitleBox");
            title.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
            var triggers = new (FrameworkElement Element, string[] Tiles)[]
            {
                ((FrameworkElement)window.FindName("SortBtn"), ["created", "title", "custom", "reverse"]),
                ((FrameworkElement)window.FindName("SearchIconButton"), ["searchbar"]),
                ((FrameworkElement)window.FindName("DictationRailBtn"), ["speechmodel"]),
                ((FrameworkElement)window.FindName("BacklinkBar"), ["hidementions"]),
                (title, ["cut", "copy", "paste", "selectall"]),
                ((FrameworkElement)dialog.FindName("OtherDbButton"), ["newdb", "opendb"]),
            };
            foreach (var (element, ids) in triggers)
            {
                element.RaiseEvent(OpeningArgs(element));
                var card = element.ContextMenu;
                try
                {
                    Assert.True(card != null, element.Name + " has no card after its right-click");
                    Assert.DoesNotContain(card!.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string });
                    foreach (string id in ids) Assert.True(NoteCards.Tile(card, id) != null, $"{element.Name} card is missing {id}");
                }
                finally { if (card != null) card.IsOpen = false; }
            }

            var tray = typeof(MainWindow).GetField("_tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            tray.GetType().GetMethod("ShowMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(tray, null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var open = PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>().Select(s => s.RootVisual)
                .SelectMany(Descendants).OfType<ContextMenu>().FirstOrDefault(m => m.IsOpen);
            try
            {
                Assert.True(open != null, "The tray icon's ShowMenu opened no card");
                Assert.Equal(3, NoteCards.Tiles(open!).Count());
                Assert.DoesNotContain(open!.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string });
            }
            finally { if (open != null) open.IsOpen = false; }
        });

        private static ContextMenuEventArgs OpeningArgs(FrameworkElement source)
        {
            var args = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [source, true], null)!;
            args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
            args.Source = source;
            return args;
        }

        private static IEnumerable<DependencyObject> Descendants(Visual? root)
        {
            if (root == null) yield break;
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (VisualTreeHelper.GetChild(root, i) is Visual child)
                    foreach (var d in Descendants(child)) yield return d;
        }

        [Fact]
        public void ATextBoxRightClickGetsTheTextCardAndItsTilesAct() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var title = (TextBox)window.FindName("TitleBox");
            title.Text = "hello";
            title.Select(0, 0);
            title.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
            var menu = Assert.IsType<ContextMenu>(title.ContextMenu);
            Assert.Equal(TextFieldCard.Tag, menu.Tag);
            TextFieldCard.Fill(menu, title);
            Assert.False(NoteCards.Find(menu, "cut")!.Enabled);   // nothing selected, nothing to cut
            CardTileHelper.Run(menu, "selectall");
            Assert.Equal(5, title.SelectionLength);
            TextFieldCard.Fill(menu, title);
            Assert.True(NoteCards.Find(menu, "copy")!.Enabled);
        });

        // The shell window is never shown in WithShell, and WPF will not open a popup on a hidden window, so each
        // render opens over a small shown host instead. A card that still opens at zero size fails the test rather
        // than skipping the render.
        private static Window? _host;

        private static void Shot(Window window, ContextMenu card, string path)
        {
            if (_host == null || !_host.IsLoaded)
            {
                _host = new Window { Content = new Border(), Width = 200, Height = 120, Left = 0, Top = 0,
                    ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
                _host.Show();
            }
            card.PlacementTarget = (FrameworkElement)_host.Content;
            card.Placement = PlacementMode.Relative;
            card.IsOpen = true;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            card.UpdateLayout();
            Assert.True(card.ActualWidth > 0 && card.ActualHeight > 0, "The card did not open for " + System.IO.Path.GetFileName(path));
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth),
                    (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(card);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(path);
                encoder.Save(stream);
            }
            card.IsOpen = false;
        }
    }
}
