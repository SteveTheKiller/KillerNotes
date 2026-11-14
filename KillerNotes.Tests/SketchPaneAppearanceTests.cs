using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Controls.Sketch;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class SketchPaneAppearanceTests
    {
        [Fact]
        public void FocusingTheCanvasKeepsItsPaneBorderAndProvidesASeparateKeyboardFocusVisual() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var pad = new SketchPadWindow(null, (_, _, _) => { });
            try
            {
                pad.Show();
                pad.Activate();
                pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var canvas = Elements((DependencyObject)pad.Content).OfType<Canvas>().OrderByDescending(c => c.Width*c.Height).First();
                var frame = Elements((DependencyObject)pad.Content).OfType<PickerBorder>()
                    .Single(b => ReferenceEquals(b.Background, Application.Current.FindResource("PaneBrush")));
                Assert.True(canvas.Focus());
                Assert.Same(Application.Current.FindResource("PaneBorderBrush"), frame.BorderBrush);
                Assert.NotNull(canvas.FocusVisualStyle);
            }
            finally { pad.Close(); }
        });

        private static IEnumerable<DependencyObject> Elements(DependencyObject root)
        {
            yield return root;
            for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(root,i))) yield return child;
        }
    }
}
