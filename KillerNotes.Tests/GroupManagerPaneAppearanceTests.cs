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
                    Assert.Same(expected, treePane.Background);
                    Assert.Same(expected, notePane.Background);
                    Assert.Equal(Colors.Transparent, ((SolidColorBrush)tree.Background).Color);
                    Assert.Equal(Colors.Transparent, ((SolidColorBrush)table.Background).Color);
                }
            }
            finally
            {
                manager.CloseForStoreChange();
                ThemeManager.Apply(previous);
            }
        });
    }
}
