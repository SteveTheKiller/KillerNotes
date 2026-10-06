using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private Border ShowDatabaseLoading()
        {
            var overlay = CreateDatabaseLoadingOverlay(Loc("Str_Busy_OpeningDatabase"));
            FrameHost.Children.Add(overlay);
            return overlay;
        }

        private static Border CreateDatabaseLoadingOverlay(string text)
        {
            var spinner = new Ellipse
            {
                Width = 34, Height = 34, StrokeThickness = 3,
                StrokeDashArray = [5.5, 3.5],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            spinner.Stroke = Brushes.White;
            var rotation = new RotateTransform();
            spinner.RenderTransform = rotation;
            rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9)))
                { RepeatBehavior = RepeatBehavior.Forever });
            var message = new TextBlock
            {
                Text = text, Foreground = Brushes.White,
                FontSize = 14, FontFamily = new FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(spinner, message.Text);
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(spinner);
            panel.Children.Add(message);
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 0x12, 0x12, 0x12)),
                Child = panel, Cursor = System.Windows.Input.Cursors.Wait
            };
            Panel.SetZIndex(overlay, 10050);
            return overlay;
        }

        private void HideDatabaseLoading(Border overlay)
        {
            if (overlay.Child is StackPanel panel && panel.Children[0] is Ellipse spinner &&
                spinner.RenderTransform is RotateTransform rotation)
                rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            FrameHost.Children.Remove(overlay);
        }
    }
}
