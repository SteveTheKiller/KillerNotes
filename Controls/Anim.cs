using System;
using System.Windows;
using System.Windows.Media.Animation;

// KillerUI kit.
namespace KillerNotes.Controls
{
    // Shared fade used across the whole app so every surface - the main window,
    // dialogs and flyouts - fades in with the same timing and easing.
    internal static class Anim
    {
        // Standard fade duration in milliseconds, shared by all surfaces.
        public const int FadeMs = 150;

        // Fades an element's opacity from 0 to 1 over FadeMs with an ease-out curve.
        public static void FadeIn(UIElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(FadeMs)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        /// <summary>
        /// Fades a window out and then closes it for real. Call from an OnClosing override, or
        /// wire it to a close button; it returns true if it took over the close, in which case
        /// the caller must cancel this one and do nothing else.
        ///
        /// Driven per composition frame rather than with a DoubleAnimation, matching
        /// ThemeManager's palette fade and for the same reason: Timeline-based animation is
        /// suppressed outright in some environments (remote sessions, "show animations in
        /// Windows" turned off) and fails silently when it is, which reads as a window that
        /// vanishes instead of fading. A per-frame opacity write always runs.
        /// </summary>
        public static bool FadeOutAndClose(Window window, ref bool alreadyFaded)
        {
            if (alreadyFaded || !window.IsLoaded || window.Opacity <= 0.01) return false;
            alreadyFaded = true;

            // Release FadeIn's animation FIRST. It is a DoubleAnimation with the default
            // FillBehavior.HoldEnd, so it keeps holding Opacity after it finishes - and a held
            // animation outranks a local value, which means every per-frame write below was being
            // silently discarded and the window just sat at full opacity until the timer closed it.
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 1;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            double from = window.Opacity;
            EventHandler? tick = null;
            tick = (_, _) =>
            {
                double t = clock.Elapsed.TotalMilliseconds / FadeMs;
                if (t >= 1)
                {
                    System.Windows.Media.CompositionTarget.Rendering -= tick;
                    window.Opacity = 0;
                    // Off the render callback before closing: tearing the window down inside a
                    // Rendering handler reenters composition.
                    window.Dispatcher.BeginInvoke(new Action(window.Close));
                    return;
                }
                window.Opacity = from * (1 - t * t);   // quadratic ease-in, mirrors FadeIn
            };
            System.Windows.Media.CompositionTarget.Rendering += tick;
            return true;
        }

        /// <summary>Fade plus a horizontal glide from dx px to rest (negative dx = in from
        /// the left). Used by the rail flyouts so they read as sliding out of the sidebar.</summary>
        public static void SlideInX(UIElement element, double dx)
        {
            var tt = new System.Windows.Media.TranslateTransform(dx, 0);
            element.RenderTransform = tt;
            FadeIn(element);
            var a = new DoubleAnimation(dx, 0, new Duration(TimeSpan.FromMilliseconds(FadeMs)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            a.Completed += (_, _) => element.RenderTransform = null;
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, a);
        }
    }
}
