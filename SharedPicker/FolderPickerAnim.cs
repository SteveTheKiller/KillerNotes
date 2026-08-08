using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace KillerShell
{
    // Shared fade used across the whole app so every surface - the main window,
    // dialogs and flyouts - fades in with the same timing and easing. (KillerUI / Grunge)
    internal static class Anim
    {
        public const int FadeMs = 150;

        public static void FadeIn(UIElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(FadeMs)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
    }
}
