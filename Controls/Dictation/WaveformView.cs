using System;
using System.Windows;
using System.Windows.Media;

namespace KillerNotes.Controls
{
    /// <summary>
    /// Draws a peak envelope as mirrored bars around a centre line. Used twice: live while
    /// recording (the tail of the envelope, scrolling) and static on an embedded recording's chip.
    /// Both read the same envelope from DictationRecorder, so what you watch while recording is
    /// what you get on the chip afterwards.
    ///
    /// A FrameworkElement with an OnRender override rather than a Canvas full of Rectangles: the
    /// live view repaints five times a second and would otherwise churn hundreds of visuals.
    /// </summary>
    internal sealed class WaveformView : FrameworkElement
    {
        private float[] _peaks = Array.Empty<float>();

        public WaveformView()
        {
            // The brushes are resolved per render, but a live theme switch does not RENDER a
            // static waveform - nothing invalidates it - so it kept the old palette until the
            // next peak/progress change ("when i change themes the waveform didnt change
            // color", Steve, 2026-08-08). Loaded/Unloaded so an embedded chip discarded with
            // its note does not leak through the static event.
            Loaded += (_, _) => KillerNotes.Services.ThemeManager.ThemeChanged += OnThemeChanged;
            Unloaded += (_, _) => KillerNotes.Services.ThemeManager.ThemeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged() => InvalidateVisual();

        /// <summary>How many buckets to show, or 0 for all of them.
        ///
        /// The pad uses 0 even while recording. A scrolling tail was tried and is worse: early in a
        /// take there is less audio than the window holds, so the waveform stopped partway across
        /// and the right-hand third sat empty - then it visibly jumped to full width the moment
        /// recording stopped. Fitting the whole take at all times has neither problem, and shows the
        /// shape of what has been said so far rather than the last six seconds of it.</summary>
        internal int Window { get; set; }

        /// <summary>Bar width and gap in DIPs. Small and fixed rather than derived from the control
        /// width, so the bars stay the same size whether the pad is 380px or maximised.</summary>
        internal double BarWidth { get; set; } = 2;
        internal double BarGap { get; set; } = 1;

        /// <summary>Playback position as 0..1, or a negative value to draw no playhead. Bars behind
        /// it are drawn solid and bars ahead of it dimmed, so the waveform doubles as the transport
        /// instead of needing a scrubber bar under it.</summary>
        internal double Progress
        {
            get => _progress;
            set
            {
                // Only repaint on a visible change. The playback timer ticks 20 times a second and
                // most ticks do not move the playhead a whole pixel.
                if (Math.Abs(value - _progress) < 0.001) return;
                _progress = value;
                InvalidateVisual();
            }
        }
        private double _progress = -1;

        internal void SetPeaks(float[] peaks)
        {
            _peaks = peaks ?? Array.Empty<float>();
            InvalidateVisual();
        }

        /// <summary>Turns a point on the control into a 0..1 position, for click-to-seek.</summary>
        internal double FractionAt(double x) => ActualWidth <= 0 ? 0 : Math.Max(0, Math.Min(1, x / ActualWidth));

        /// <summary>Slice points as 0..1 fractions, sorted. These are marks on the recording, not
        /// edits to it - nothing is removed until a segment between two of them is deleted.</summary>
        internal System.Collections.Generic.List<double> Cuts { get; } = new();

        /// <summary>The segment highlighted for a delete or copy, as an index into the segments the
        /// cuts define, or -1 for none.</summary>
        internal int SelectedSegment
        {
            get => _selected;
            set { if (_selected != value) { _selected = value; InvalidateVisual(); } }
        }
        private int _selected = -1;

        /// <summary>The segment containing a fraction. Segment N runs from cut N-1 to cut N, with
        /// the ends of the recording closing off the first and last.</summary>
        internal int SegmentAt(double fraction)
        {
            int i = 0;
            foreach (double c in Cuts)
            {
                if (fraction < c) return i;
                i++;
            }
            return i;
        }

        /// <summary>The start and end of a segment as fractions.</summary>
        internal (double from, double to) SegmentBounds(int index)
        {
            double from = index <= 0 ? 0 : (index - 1 < Cuts.Count ? Cuts[index - 1] : 1);
            double to = index < Cuts.Count ? Cuts[index] : 1;
            return (from, to);
        }

        internal void ClearCuts()
        {
            Cuts.Clear();
            _selected = -1;
            InvalidateVisual();
        }

        /// <summary>Adds a slice, ignoring one placed on top of an existing cut or on an edge.</summary>
        internal void AddCut(double fraction)
        {
            if (fraction <= 0.001 || fraction >= 0.999) return;
            foreach (double c in Cuts) if (Math.Abs(c - fraction) < 0.002) return;
            Cuts.Add(fraction);
            Cuts.Sort();
            InvalidateVisual();
        }

        internal void Clear() => SetPeaks(Array.Empty<float>());

        private Brush Ink()
        {
            // The accent, falling back to something visible rather than nothing if a palette is
            // mid-swap. Resolved per render so a live theme change is picked up without rewiring.
            if (Application.Current?.TryFindResource("OutlineBtnBrush") is Brush b) return b;
            if (Application.Current?.TryFindResource("PrimaryBrush") is Brush p) return p;
            return Brushes.Gray;
        }

        /// <summary>How much of each bar takes the second colour.</summary>
        private const double CoreScale = 0.55;

        /// <summary>The second waveform colour: a LIGHTER SHADE of the accent, derived per render.
        /// This was PrimaryBrush - a different token - but the warm-accent pass pointed
        /// OutlineBtnBrush and PrimaryBrush at the SAME colour on several themes, which collapsed
        /// the waveform to one flat tone ("can we combine the tan with white or something for
        /// some dimension... two shades of the accent color", Steve, 2026-08-08). Deriving the
        /// core from the accent guarantees two shades on EVERY palette; a non-solid accent falls
        /// back to PrimaryBrush as before.</summary>
        private Brush Core()
        {
            if (Ink() is SolidColorBrush s)
            {
                Color c = s.Color;
                var lighter = Color.FromRgb(
                    (byte)(c.R + (255 - c.R) * 0.55),
                    (byte)(c.G + (255 - c.G) * 0.55),
                    (byte)(c.B + (255 - c.B) * 0.55));
                var b = new SolidColorBrush(lighter);
                b.Freeze();
                return b;
            }
            if (Application.Current?.TryFindResource("PrimaryBrush") is Brush p) return p;
            return Ink();
        }

        private Brush Rule()
        {
            if (Application.Current?.TryFindResource("MutedTextBrush") is Brush b) return b;
            return Brushes.Gray;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Hit-testable background, so the chip's double-click works anywhere on the waveform
            // and not just on the bars. Transparent, not null - null is not hit-testable.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            double mid = h / 2;
            var rule = Rule();
            rule = rule.Clone();
            rule.Opacity = 0.35;
            rule.Freeze();
            // A centre line, so an empty or silent stretch still reads as "audio", not as a bug.
            dc.DrawRectangle(rule, null, new Rect(0, Math.Floor(mid), w, 1));

            // Selected segment first, UNDER the bars: a highlight painted over them would flatten
            // the waveform into a solid block exactly where the user is looking hardest.
            if (_selected >= 0)
            {
                var (from, to) = SegmentBounds(_selected);
                var sel = Ink().Clone();
                sel.Opacity = 0.18;
                sel.Freeze();
                dc.DrawRectangle(sel, null, new Rect(from * w, 0, Math.Max(1, (to - from) * w), h));
            }

            if (_peaks.Length == 0) return;

            double step = BarWidth + BarGap;
            double barW = BarWidth;
            int capacity = Math.Max(1, (int)(w / step));

            // Live view: draw the tail. Fit view: a FIXED number of columns across the full width,
            // each one the peak of the slice of recording that maps to it.
            //
            // The column count being fixed is the whole point, and it is what stopped the live
            // waveform juddering. It used to take one bar per bucket and stride by an INTEGER, so
            // every time the take grew past another multiple of the width the stride ticked over
            // and the entire waveform re-laid itself out - and below that threshold the bar pitch
            // itself changed on every single tick. Ten times a second, that reads as the waveform
            // lurching out in chunks. With the columns fixed, growth only changes the HEIGHTS.
            int first = 0, count = capacity;
            bool fit = Window <= 0;
            if (!fit)
            {
                count = Math.Min(_peaks.Length, Math.Min(Window, capacity));
                first = _peaks.Length - count;
            }
            else
            {
                // Never more columns than there are buckets: a one-second take spread over 300
                // columns is 300 copies of the same 20 values pretending to be detail.
                count = Math.Max(1, Math.Min(capacity, _peaks.Length));
                step = w / count;
                barW = Math.Max(1, step - BarGap);
            }

            var ink = Ink();
            // Bars ahead of the playhead are the same colour at reduced opacity rather than a
            // different brush - on a themed palette a second colour reads as a second meaning.
            Brush ahead = ink;
            if (_progress >= 0)
            {
                ahead = ink.Clone();
                ahead.Opacity = 0.3;
                ahead.Freeze();
            }
            double playX = _progress >= 0 ? _progress * w : -1;

            var coreInk = Core();
            Brush coreAhead = coreInk;
            if (_progress >= 0)
            {
                coreAhead = coreInk.Clone();
                coreAhead.Opacity = 0.3;
                coreAhead.Freeze();
            }

            for (int i = 0; i < count; i++)
            {
                float peak;
                if (!fit)
                {
                    peak = _peaks[first + i];
                }
                else
                {
                    // FRACTIONAL mapping, and the peak of the whole slice rather than one sample
                    // from it. Integer striding threw away the loudest bucket in every group, so a
                    // short sharp sound could vanish entirely depending on where it happened to
                    // land; taking the maximum means anything audible always leaves a mark.
                    double from = i * (double)_peaks.Length / count;
                    double to = (i + 1) * (double)_peaks.Length / count;
                    int a = (int)from;
                    int b = Math.Min(_peaks.Length, Math.Max(a + 1, (int)Math.Ceiling(to)));
                    peak = 0;
                    for (int k = a; k < b; k++) if (_peaks[k] > peak) peak = _peaks[k];
                }

                // A floor of one pixel: a silent bucket should still leave a mark, or the waveform
                // looks like it stopped recording during a pause.
                double half = Math.Max(0.5, peak * (mid - 1));
                double x = i * step;
                if (x + barW > w + 0.5) break;
                dc.DrawRectangle(playX < 0 || x < playX ? ink : ahead, null,
                                 new Rect(x, mid - half, barW, half * 2));

                // A shorter core in a second colour, so a bar reads as loud middle and quieter tips
                // rather than one flat block. Drawn per bar rather than as a gradient brush so it
                // tracks the theme, both colours being resolved per render.
                double core = half * CoreScale;
                if (core > 0.5)
                    dc.DrawRectangle(playX < 0 || x < playX ? coreInk : coreAhead, null,
                                     new Rect(x, mid - core, barW, core * 2));
            }

            // Cut marks last, over everything, dashed so they never read as a playhead.
            if (Cuts.Count > 0)
            {
                var pen = new Pen(Rule(), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
                pen.Freeze();
                foreach (double c in Cuts)
                {
                    double cx = Math.Floor(c * w) + 0.5;   // half-pixel: a 1px line on a device pixel
                    dc.DrawLine(pen, new Point(cx, 0), new Point(cx, h));
                }
            }

            if (playX >= 0) dc.DrawRectangle(ink, null, new Rect(Math.Floor(playX), 0, 1, h));
        }
    }
}
