using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

namespace KillerNotes.Controls
{
    /// <summary>Crossfades palette changes without entering the window's measure/arrange pass.</summary>
    internal static class ThemeTransition
    {
        private const int DurationMs = 180;
        private static readonly Dictionary<FrameworkElement, Adorner> Active = [];

        internal static void CrossFade(FrameworkElement surface, Action applyTheme)
        {
            applyTheme();
        }

        private sealed class SnapshotAdorner : Adorner
        {
            private readonly ImageSource _snapshot;
            internal SnapshotAdorner(UIElement adorned, ImageSource snapshot) : base(adorned)
            {
                _snapshot = snapshot;
                IsHitTestVisible = false;
                SnapsToDevicePixels = true;
            }

            protected override void OnRender(DrawingContext dc)
                => dc.DrawImage(_snapshot, new Rect(AdornedElement.RenderSize));
        }
    }
}
