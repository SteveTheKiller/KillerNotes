using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class GroupManagerPaneAppearanceTests
    {
        [Fact]
        public void BothPanesUseTheOpaquePaneBackgroundAcrossLiveThemeChanges() => CardColorBehaviorTests.WithShell((_, host) =>
        {
            var previous = ThemeManager.Current;
            // An empty cache folder of its own, so the result never depends on what the real profile cache holds.
            string? previousRoot = MotifDiskCache.Root;
            string cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KillerNotesPaneMotif-" + Guid.NewGuid().ToString("N"));
            MotifDiskCache.Root = cache;
            var manager = new GroupManagerWindow(host);
            try
            {
                manager.Show();
                var tree = (TreeView)manager.FindName("Tree");
                var table = (ListBox)manager.FindName("Table");
                var treePane = (Border)tree.Parent;
                var notePane = (Border)((DockPanel)((Grid)table.Parent).Parent).Parent;
                foreach (Theme theme in Enum.GetValues(typeof(Theme)))
                {
                    ThemeManager.Apply(theme);
                    manager.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var expected = Assert.IsType<SolidColorBrush>(Application.Current.FindResource("PaneBrush"));
                    Assert.Equal(255, expected.Color.A);
                    if (Application.Current.TryFindResource("ThemeMotif") is true)
                    {
                        // The kit makes ContentPaneBrush (mirrored as PaneBrush) the Window motif part, so on a motif theme
                        // the panes carry the ink, or the plain pane brush until the piece lands.
                        foreach (var pane in new[] { treePane, notePane })
                        {
                            Assert.Equal(MotifPart.Window, ThemeMotif.GetPart(pane));
                            Assert.True(ReferenceEquals(expected, pane.Background) || pane.Background is DrawingBrush,
                                $"{theme}: the pane shows {pane.Background}, neither the pane brush nor the motif.");
                        }
                    }
                    else
                    {
                        Assert.Same(expected, treePane.Background);
                        Assert.Same(expected, notePane.Background);
                    }
                    Assert.Equal(Colors.Transparent, ((SolidColorBrush)tree.Background).Color);
                    Assert.Equal(Colors.Transparent, ((SolidColorBrush)table.Background).Color);
                }
            }
            finally
            {
                manager.Opacity = 0;
                manager.Close();
                ThemeManager.Apply(previous);
                ThemeMotif.ReleaseAll();
                MotifDiskCache.Root = previousRoot;
                try { if (System.IO.Directory.Exists(cache)) System.IO.Directory.Delete(cache, true); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
    }
}
