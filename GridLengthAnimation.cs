using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace KillerNotes
{
    /// <summary>Animates a Grid ColumnDefinition/RowDefinition size (a GridLength), which WPF
    /// has no built-in animation for. Pixel values only - enough for the sidebar collapse and
    /// expand slide (Sidebar.cs). Set From/To in device-independent pixels and an optional
    /// EasingFunction.</summary>
    public class GridLengthAnimation : AnimationTimeline
    {
        public override Type TargetPropertyType => typeof(GridLength);
        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(double), typeof(GridLengthAnimation));
        public double From { get => (double)GetValue(FromProperty); set => SetValue(FromProperty, value); }

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(double), typeof(GridLengthAnimation));
        public double To { get => (double)GetValue(ToProperty); set => SetValue(ToProperty, value); }

        public IEasingFunction? EasingFunction { get; set; }

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue,
                                               AnimationClock clock)
        {
            double from = From, to = To;
            double p = clock.CurrentProgress ?? 0;
            if (EasingFunction != null) p = EasingFunction.Ease(p);
            return new GridLength(from + (to - from) * p, GridUnitType.Pixel);
        }
    }
}
