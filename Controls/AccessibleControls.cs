#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KillerFamily.Accessibility
{
    internal static class AccessibleControls
    {
        private static bool _initialized;
        private static readonly IValueConverter TooltipName = new TooltipNameConverter();
        private static readonly IMultiValueConverter RunsName = new RunsNameConverter();
        private static Style? _focusStyle;
        private static Style? _compactFocusStyle;
        private static readonly DependencyProperty OwnedNameProperty = DependencyProperty.RegisterAttached(
            "OwnedName", typeof(BindingBase), typeof(AccessibleControls));
        private static readonly DependencyProperty TrackedTooltipProperty = DependencyProperty.RegisterAttached(
            "TrackedTooltip", typeof(bool), typeof(AccessibleControls), new PropertyMetadata(false));
        private static readonly DependencyProperty TrackedLoadProperty = DependencyProperty.RegisterAttached(
            "TrackedLoad", typeof(bool), typeof(AccessibleControls), new PropertyMetadata(false));
        private static readonly DependencyPropertyDescriptor TooltipDescriptor =
            DependencyPropertyDescriptor.FromProperty(FrameworkElement.ToolTipProperty, typeof(Control));

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            EventManager.RegisterClassHandler(typeof(Control), FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler(OnControlSizeChanged), true);
        }

        private static void OnControlSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (sender is not Control control) return;
            // A class Loaded handler alone does not make WPF broadcast Loaded to every child.
            // The first layout attaches an instance handler, including for later popup content.
            if (!(bool)control.GetValue(TrackedLoadProperty))
            {
                control.Loaded += OnControlLoaded;
                control.SetValue(TrackedLoadProperty, true);
            }
            Prepare(control);
        }

        private static void OnControlLoaded(object sender, RoutedEventArgs args)
        {
            if (sender is not Control control) return;
            Prepare(control);
            if (control is Window window)
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(() => AccessibilityAudit.Record(window)));
        }

        internal static void Prepare(Control control)
        {
            // The focus adorner is shown by WPF for keyboard navigation, without changing layout.
            if (control is not Window && control.Focusable && control.FocusVisualStyle == null)
                control.SetCurrentValue(Control.FocusVisualStyleProperty, IsCompact(control) ? CompactFocusStyle : FocusStyle);

            UpdateName(control);
        }

        private static void UpdateName(Control control)
        {
            var existing = BindingOperations.GetBindingBase(control, AutomationProperties.NameProperty);
            bool owned = existing != null && ReferenceEquals(existing, control.GetValue(OwnedNameProperty));
            if (!owned && IsReadable(AutomationProperties.GetName(control))) return;
            object? content = control is ContentControl button ? button.Content :
                control is HeaderedItemsControl items ? items.Header :
                control is HeaderedContentControl header ? header.Header : null;
            if (!(bool)control.GetValue(TrackedTooltipProperty))
            {
                TooltipDescriptor.AddValueChanged(control, OnTooltipChanged);
                control.Unloaded += OnControlUnloaded;
                control.SetValue(TrackedTooltipProperty, true);
            }
            if (IsReadable(ReadText(content)))
            {
                // WPF names a control from its tooltip before its content, and a rich tooltip
                // names it after its own type ("System.Windows.Controls.TextBlock"). With such a
                // tooltip, the readable content has to be given as the name explicitly.
                if (control.ToolTip == null || control.ToolTip is string)
                {
                    if (owned) BindingOperations.ClearBinding(control, AutomationProperties.NameProperty);
                    control.ClearValue(OwnedNameProperty);
                    return;
                }
                var contentName = new Binding(control is ContentControl ? nameof(ContentControl.Content)
                    : nameof(HeaderedItemsControl.Header)) { Source = control, Converter = TooltipName };
                BindingOperations.SetBinding(control, AutomationProperties.NameProperty, contentName);
                control.SetValue(OwnedNameProperty, contentName);
                return;
            }
            if (!IsReadable(ReadText(control.ToolTip)))
            {
                if (owned) BindingOperations.ClearBinding(control, AutomationProperties.NameProperty);
                control.ClearValue(OwnedNameProperty);
                return;
            }

            // A rich tooltip's localized TextBlock can change without replacing the tooltip. A
            // TextBlock built from Runs keeps its words in the Runs, so the name follows each one.
            TextBlock? label = FindTextBlock(control.ToolTip);
            BindingBase binding;
            if (label != null && string.IsNullOrWhiteSpace(label.Text) && RunsOf(label.Inlines).Any())
            {
                var joined = new MultiBinding { Converter = RunsName };
                foreach (Run run in RunsOf(label.Inlines))
                    joined.Bindings.Add(new Binding(nameof(Run.Text)) { Source = run });
                binding = joined;
            }
            else if (label != null)
                binding = new Binding(nameof(TextBlock.Text)) { Source = label };
            else
                binding = new Binding(nameof(FrameworkElement.ToolTip)) { Source = control, Converter = TooltipName };
            BindingOperations.SetBinding(control, AutomationProperties.NameProperty, binding);
            control.SetValue(OwnedNameProperty, binding);
        }

        private static TextBlock? FindTextBlock(object? value)
        {
            if (value is TextBlock block && IsReadable(BlockText(block))) return block;
            if (value is ContentControl content) return FindTextBlock(content.Content);
            if (value is Panel panel)
                foreach (UIElement child in panel.Children)
                {
                    TextBlock? found = FindTextBlock(child);
                    if (found != null) return found;
                }
            return null;
        }

        private static void OnTooltipChanged(object? sender, EventArgs args)
        {
            if (sender is Control control) UpdateName(control);
        }

        private static void OnControlUnloaded(object sender, RoutedEventArgs args)
        {
            if (sender is not Control control) return;
            TooltipDescriptor.RemoveValueChanged(control, OnTooltipChanged);
            control.Unloaded -= OnControlUnloaded;
            control.SetValue(TrackedTooltipProperty, false);
        }

        internal static string ReadText(object? value)
        {
            if (value is string text) return text.Trim();
            if (value is TextBlock block) return BlockText(block).Trim();
            if (value is ContentControl content) return ReadText(content.Content);
            if (value is Panel panel)
                foreach (UIElement child in panel.Children)
                {
                    string label = ReadText(child);
                    if (IsReadable(label)) return label;
                }
            return string.Empty;
        }

        /// <summary>A TextBlock's words, including those it holds in Runs rather than in Text.</summary>
        private static string BlockText(TextBlock block)
        {
            if (!string.IsNullOrWhiteSpace(block.Text)) return block.Text;
            return string.Concat(RunsOf(block.Inlines).Select(run => run.Text));
        }

        private static IEnumerable<Run> RunsOf(InlineCollection inlines)
        {
            foreach (Inline inline in inlines)
            {
                if (inline is Run run) yield return run;
                else if (inline is Span span)
                    foreach (Run inner in RunsOf(span.Inlines)) yield return inner;
            }
        }

        private static bool IsReadable(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("System.Windows.", StringComparison.Ordinal))
                return false;
            foreach (char character in text)
                if (char.IsLetterOrDigit(character) &&
                    CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.PrivateUse)
                    return true;
            return false;
        }

        private static Style FocusStyle => _focusStyle ??= BuildFocusStyle(2);

        /// <summary>
        /// Icon-sized controls such as rail buttons have no room inside for the outline, which
        /// then lands on the glyph itself. Theirs is drawn just outside the control instead.
        /// </summary>
        private static Style CompactFocusStyle => _compactFocusStyle ??= BuildFocusStyle(-3);

        private static bool IsCompact(Control control) =>
            control.ActualWidth > 0 && control.ActualHeight > 0 &&
            (control.ActualWidth < CompactSize || control.ActualHeight < CompactSize);

        private const double CompactSize = 21;

        private static Style BuildFocusStyle(double inset)
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            foreach (var stroke in new[] { Tuple.Create(SystemColors.WindowTextBrushKey, 3.0),
                                          Tuple.Create(SystemColors.WindowBrushKey, 1.0) })
            {
                var outline = new FrameworkElementFactory(typeof(Rectangle));
                outline.SetValue(FrameworkElement.MarginProperty, new Thickness(inset));
                outline.SetValue(Shape.StrokeThicknessProperty, stroke.Item2);
                outline.SetValue(Shape.StrokeDashArrayProperty, new DoubleCollection { 2, 2 });
                outline.SetResourceReference(Shape.StrokeProperty, stroke.Item1);
                grid.AppendChild(outline);
            }
            var template = new ControlTemplate(typeof(Control)) { VisualTree = grid };
            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Seal();
            return style;
        }

        private sealed class TooltipNameConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
                ReadText(value);

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
                Binding.DoNothing;
        }

        private sealed class RunsNameConverter : IMultiValueConverter
        {
            public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
                string.Concat(values.OfType<string>()).Trim();

            public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
                [];
        }
    }
}
