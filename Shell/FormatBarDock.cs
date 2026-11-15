using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    /// <summary>
    /// The top of the note pane: the title area and the format bar. Floating, the default, the bar
    /// hangs over the title from the pane's top edge and keeps its own alignment and margin, so
    /// showing it never moves the title or the note. Docked, the bar takes its own row across the
    /// width and the title starts below it.
    /// </summary>
    [ContentProperty(nameof(Content))]
    public sealed class FormatBarDock : FrameworkElement
    {
        private UIElement? _bar, _content;
        private bool _docked;
        private readonly List<UIElement> _attached = new();

        /// <summary>The title area under or beside the bar.</summary>
        public UIElement? Content
        {
            get => _content;
            set => Swap(ref _content, value);
        }

        /// <summary>The format bar.</summary>
        public UIElement? Bar
        {
            get => _bar;
            set => Swap(ref _bar, value);
        }

        /// <summary>True to give the bar its own row above the title instead of floating it.</summary>
        public bool Docked
        {
            get => _docked;
            set
            {
                if (_docked == value) return;
                _docked = value;
                Reattach();
                InvalidateMeasure();
            }
        }

        private void Swap(ref UIElement? slot, UIElement? value)
        {
            if (ReferenceEquals(slot, value)) return;
            if (slot is not null) RemoveLogicalChild(slot);
            slot = value;
            if (value is not null) AddLogicalChild(value);
            Reattach();
            InvalidateMeasure();
        }

        // Docked, the bar comes first so Tab reaches it before the title. Floating, it overlaps the
        // title, so it is drawn last to stay on top.
        private IEnumerable<UIElement> InDrawingOrder()
        {
            if (_docked && _bar is not null) yield return _bar;
            if (_content is not null) yield return _content;
            if (!_docked && _bar is not null) yield return _bar;
        }

        // WPF composes children in the order they were attached, so every change re-attaches them.
        private void Reattach()
        {
            foreach (UIElement child in _attached) RemoveVisualChild(child);
            _attached.Clear();
            foreach (UIElement child in InDrawingOrder())
            {
                AddVisualChild(child);
                _attached.Add(child);
            }
        }

        protected override int VisualChildrenCount => _attached.Count;

        protected override Visual GetVisualChild(int index) =>
            index >= 0 && index < _attached.Count ? _attached[index] : throw new ArgumentOutOfRangeException(nameof(index));

        protected override System.Collections.IEnumerator LogicalChildren
        {
            get
            {
                var children = new List<UIElement>();
                if (_bar is not null) children.Add(_bar);
                if (_content is not null) children.Add(_content);
                return children.GetEnumerator();
            }
        }

        protected override Size MeasureOverride(Size available)
        {
            _bar?.Measure(new Size(available.Width, double.PositiveInfinity));
            _content?.Measure(new Size(available.Width, double.PositiveInfinity));
            Size bar = _bar?.DesiredSize ?? default, content = _content?.DesiredSize ?? default;
            double width = Math.Max(bar.Width, content.Width);
            // Floating, the bar hangs over the title and adds no height of its own beyond it.
            double height = _docked ? bar.Height + content.Height : Math.Max(bar.Height, content.Height);
            return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double barHeight = _bar?.DesiredSize.Height ?? 0;
            // The bar's slot is the top band across the width; its own alignment and margin place it
            // inside that band, which is how the floating bar slides.
            _bar?.Arrange(new Rect(0, 0, final.Width, barHeight));
            double top = _docked ? barHeight : 0;
            _content?.Arrange(new Rect(0, top, final.Width, Math.Max(0, final.Height - top)));
            return final;
        }
    }
}
