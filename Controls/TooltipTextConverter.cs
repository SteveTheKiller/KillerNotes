#nullable enable
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace KillerFamily.Layout
{
    /// <summary>
    /// Keeps the family hover popup compact. A string tooltip is shown through Convert, which moves a
    /// trailing shortcut such as "(Ctrl+S)" onto its own line, and WrapContent makes the text blocks
    /// inside a structured tooltip wrap at 300 px instead of running across the screen.
    /// </summary>
    public sealed class TooltipTextConverter : IValueConverter
    {
        public static readonly DependencyProperty WrapContentProperty = DependencyProperty.RegisterAttached(
            "WrapContent", typeof(bool), typeof(TooltipTextConverter), new PropertyMetadata(false, OnWrapContentChanged));

        public static bool GetWrapContent(DependencyObject element) => (bool)element.GetValue(WrapContentProperty);
        public static void SetWrapContent(DependencyObject element, bool value) => element.SetValue(WrapContentProperty, value);

        private static void OnWrapContentChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            if (element is not ToolTip tip || e.NewValue is not true) return;
            void Wrap() { if (tip.Content is DependencyObject content) WrapText(content); }
            Wrap();
            tip.Loaded += (_, _) => Wrap();
            tip.Opened += (_, _) => Wrap();
        }

        private static void WrapText(DependencyObject content)
        {
            if (content is TextBlock text)
            {
                text.MaxWidth = 300;
                text.TextWrapping = TextWrapping.Wrap;
            }
            foreach (object child in LogicalTreeHelper.GetChildren(content))
                if (child is DependencyObject node) WrapText(node);
        }

        private static readonly Regex ShortcutSuffix = new(@"\s+\((?=[^()]*\b(?:Ctrl|Alt|Shift|F\d{1,2}|Esc|Enter|Space|Tab|Delete|Backspace)\b)([^()]*)\)$");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string text ? ShortcutSuffix.Replace(text, "\n$1") : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
