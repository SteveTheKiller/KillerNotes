using System;
using System.Windows;

namespace KillerNotes.Controls
{
    /// <summary>
    /// A press that may become a drag: armed by the press, disarmed by the release, taken once
    /// the pointer travels past the system drag distance with the button still down.
    ///
    /// The release is the point. The sidebar kept its drag candidate after the button came up,
    /// so clicking through notes fast started drags of the previous note: WPF moves the pointer
    /// to the next click with the button already down, the stale candidate was past the
    /// threshold, and Windows then sat in its drag loop, eating right-clicks until another
    /// click dropped the note somewhere (KillerNotes 1.7.0, DragGateTests).
    /// </summary>
    internal sealed class DragGate<T> where T : class
    {
        private T? _item;
        private Point _start;

        /// <summary>The armed item, or null.</summary>
        public T? Pending => _item;

        /// <summary>A press on item at the given point (window coordinates). Null disarms.</summary>
        public void Press(T? item, Point at)
        {
            _item = item;
            _start = at;
        }

        /// <summary>The button came up, wherever it came up.</summary>
        public void Release() => _item = null;

        /// <summary>The item to drag when the pointer has travelled far enough with the button
        /// down, else null. factor widens the threshold (group headers resist a twitch).
        /// Taking it disarms, so one press starts at most one drag.</summary>
        public T? TryTake(Point now, bool pressed, double factor = 1)
        {
            if (_item == null || !pressed) return null;
            if (Math.Abs(now.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance * factor &&
                Math.Abs(now.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance * factor) return null;
            var item = _item;
            _item = null;
            return item;
        }
    }
}
