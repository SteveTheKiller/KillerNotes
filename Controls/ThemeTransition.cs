using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Generic;

namespace KillerNotes.Controls
{
    /// <summary>Crossfades palette changes without entering the window's measure/arrange pass.</summary>
    internal static class ThemeTransition
    {
        private const int DurationMs = 180;
        private static readonly Dictionary<FrameworkElement, Adorner> Active = [];

        /// <summary>
        /// Applies a palette change behind a crossfade.
        ///
        /// BOTH sides are bitmaps, and that is the whole point. The obvious implementation -
        /// snapshot the old palette, apply the new one, fade the snapshot out over the LIVE
        /// window - throbs: every glyph is drawn twice for the length of the fade, once baked
        /// into the snapshot and once re-rendered live underneath, and because ClearType weights
        /// subpixels per glyph those two copies do not sum back to the original weight. Text
        /// visibly thickens through the middle and thins at the end. No duration or easing curve
        /// removes it, because the cause is compositing live text against a bitmap of itself.
        ///
        /// So the live tree never takes part. We capture the outgoing palette, apply the theme,
        /// capture the incoming palette, and blend those two frozen images while the opaque
        /// incoming one hides the real window. Identical layout on both sides means every glyph
        /// sits on the same pixels in both images, so the blend is a straight linear interpolation
        /// from the old text colour to the new one and the weight never changes.
        /// </summary>
        internal static void CrossFade(FrameworkElement surface, Action applyTheme)
        {
            AdornerLayer? layer = AdornerLayer.GetAdornerLayer(surface);
            ImageSource? before = layer is null ? null : Snapshot(surface);

            // No adorner layer (no AdornerDecorator above us) or nothing rendered yet: the theme
            // still has to change. A missing transition is cosmetic; a skipped Apply is not.
            if (layer is null || before is null) { applyTheme(); return; }

            // Note the ordering: the theme is applied and the adorner added only afterwards. An
            // exception here therefore cannot strand a snapshot over the live window, which is
            // the failure the previous apply-inside-the-adorner version had to guard against.
            applyTheme();
            // Force the new palette through layout before capturing it. Colours alone do not
            // reflow, but a theme also carries CornerRadius and Thickness values that do.
            surface.UpdateLayout();
            ImageSource? after = Snapshot(surface);

            // Nothing to blend against - let the new palette stand rather than fade to nothing.
            if (after is null) return;

            // Clicking a second theme while the first fade is still running must replace the
            // stale pair, not stack another adorner on top of it.
            if (Active.TryGetValue(surface, out Adorner running))
            {
                layer.Remove(running);
                Active.Remove(surface);
            }

            var adorner = new SnapshotAdorner(surface, before, after);
            layer.Add(adorner);
            Active[surface] = adorner;

            // Animate the INNER blend, not the adorner's own Opacity. Fading the adorner as a
            // whole would dissolve both images toward the live window and reintroduce exactly
            // the live-text blending this class exists to avoid.
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(DurationMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            fade.Completed += (_, _) => Drop(layer, surface, adorner);
            adorner.BeginAnimation(SnapshotAdorner.BlendProperty, fade);

            // Belt and braces: Completed does not fire if the clock is detached (the adorner
            // removed, the layer torn down, the window closing), and a stranded adorner is a
            // frozen copy of the window over the live UI. Drop is idempotent.
            var sweep = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DurationMs + 250) };
            sweep.Tick += (s, _) =>
            {
                ((DispatcherTimer)s).Stop();
                Drop(layer, surface, adorner);
            };
            sweep.Start();
        }

        /// <summary>Removes a snapshot pair and forgets it. Safe to call more than once for the
        /// same adorner - both the animation's Completed and the sweep timer call it.</summary>
        private static void Drop(AdornerLayer layer, FrameworkElement surface, Adorner adorner)
        {
            layer.Remove(adorner);
            if (Active.TryGetValue(surface, out Adorner current) && ReferenceEquals(current, adorner))
                Active.Remove(surface);
        }

        /// <summary>Freezes the surface as it looks right now. RenderTargetBitmap.Render reads the
        /// existing visual tree, so this never triggers a measure/arrange of the live window.</summary>
        private static ImageSource? Snapshot(FrameworkElement surface)
        {
            double w = surface.ActualWidth, h = surface.ActualHeight;
            if (w < 1 || h < 1) return null;

            // Snapshot at the surface's real device scale. Hardcoding 96 DPI resamples the frozen
            // copy on a scaled display, and a resampled copy of text is soft for the whole fade.
            double dpiX = 96.0, dpiY = 96.0;
            PresentationSource? src = PresentationSource.FromVisual(surface);
            if (src?.CompositionTarget != null)
            {
                dpiX *= src.CompositionTarget.TransformToDevice.M11;
                dpiY *= src.CompositionTarget.TransformToDevice.M22;
            }

            try
            {
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(w * dpiX / 96.0), (int)Math.Ceiling(h * dpiY / 96.0),
                    dpiX, dpiY, PixelFormats.Pbgra32);
                rtb.Render(surface);
                rtb.Freeze();
                return rtb;
            }
            catch { return null; }   // out of video memory on a very large surface
        }

        private sealed class SnapshotAdorner : Adorner
        {
            /// <summary>1 = fully the outgoing palette, 0 = fully the incoming one.</summary>
            internal static readonly DependencyProperty BlendProperty =
                DependencyProperty.Register(nameof(Blend), typeof(double), typeof(SnapshotAdorner),
                    new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

            internal double Blend
            {
                get => (double)GetValue(BlendProperty);
                set => SetValue(BlendProperty, value);
            }

            private readonly ImageSource _before, _after;

            internal SnapshotAdorner(UIElement adorned, ImageSource before, ImageSource after) : base(adorned)
            {
                _before = before;
                _after = after;
                IsHitTestVisible = false;
                SnapsToDevicePixels = true;
            }

            protected override void OnRender(DrawingContext dc)
            {
                var area = new Rect(AdornedElement.RenderSize);
                // Incoming palette first and fully opaque: it hides the live window, so no live
                // text is ever part of the blend. The outgoing palette then fades away on top.
                dc.DrawImage(_after, area);
                dc.PushOpacity(Blend);
                dc.DrawImage(_before, area);
                dc.Pop();
            }
        }
    }
}
