using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // The horizontal rule's style flyout. Hovering the toolbar's rule button (or pressing Down
    // while it has focus) opens it under the button; it picks the line pattern, weight and color
    // of the next rule, and restyles any rules inside the selection as one undo step. It is a
    // Popup, not a ContextMenu: a menu captures the mouse, so one that opened on hover would eat
    // the next click anywhere on the toolbar. It closes once the pointer has left both the button
    // and the card, or on a press outside, as the table size picker does.
    public partial class MainWindow
    {
        private const string RuleStyleSetting = "RuleStyle";
        private static readonly string[] RuleSwatches =
            { "#5A5A5A", "#DD504B", "#E8962C", "#E8D44B", "#1EA54C", "#50AEE8", "#9A6AE8", "#E85CA8", "#FFFFFF" };
        private static readonly string[] RuleWeightNames = { "Str_Rule_Thin", "Str_Rule_Medium", "Str_Rule_Thick" };

        private readonly DispatcherTimer _ruleHover = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly DispatcherTimer _ruleLeave = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private int _ruleOutsideTicks;
        private RuleAppearance _ruleShown = RuleAppearance.Default;

        private RuleAppearance CurrentRuleStyle() => RuleAppearance.FromSetting(App.GetSetting(RuleStyleSetting));

        private void InitRuleFlyout()
        {
            foreach (var pattern in new[] { TableLinePattern.Solid, TableLinePattern.Dotted, TableLinePattern.Dashed })
                RulePatternRow.Children.Add(RuleChoice("Str_TableStyle_" + pattern, () => _ruleShown with { Pattern = pattern }));
            for (int i = 0; i < RuleAppearance.Weights.Length; i++)
            {
                double weight = RuleAppearance.Weights[i];
                RuleWeightRow.Children.Add(RuleChoice(RuleWeightNames[i], () => _ruleShown with { Thickness = weight }));
            }
            foreach (string hex in RuleSwatches)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var swatch = new Button { Style = (Style)FindResource("ThemeSwatch"), Background = new SolidColorBrush(color),
                    Tag = hex, Margin = new Thickness(0, 0, 5, 4) };
                swatch.Click += (_, _) => PickRuleStyle(_ruleShown with { Color = color });
                RuleColorRow.Children.Add(swatch);
            }

            FmtRuleBtn.MouseEnter += (_, _) => { if (!RulePopup.IsOpen) _ruleHover.Start(); };
            FmtRuleBtn.MouseLeave += (_, _) => _ruleHover.Stop();
            FmtRuleBtn.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Down) return;
                OpenRuleFlyout();
                RulePatternRow.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, "on"))?.Focus();
                e.Handled = true;
            };
            _ruleHover.Tick += (_, _) =>
            {
                _ruleHover.Stop();
                if (FmtRuleBtn.IsMouseOver && FmtRuleBtn.IsEnabled) OpenRuleFlyout();
            };
            _ruleLeave.Tick += (_, _) =>
            {
                if (!RulePopup.IsOpen) { _ruleLeave.Stop(); return; }
                // Two checks in a row outside: the pointer crossing the gap between the button and
                // the card must not close it.
                if (FmtRuleBtn.IsMouseOver || RuleFlyoutPanel.IsMouseOver || RuleFlyoutPanel.IsKeyboardFocusWithin)
                    _ruleOutsideTicks = 0;
                else if (++_ruleOutsideTicks >= 2) CloseRuleFlyout();
            };
            PreviewMouseDown += (_, _) =>
            {
                if (RulePopup.IsOpen && !FmtRuleBtn.IsMouseOver && !RuleFlyoutPanel.IsMouseOver) CloseRuleFlyout();
            };
            Deactivated += (_, _) => CloseRuleFlyout();
            LocationChanged += (_, _) => CloseRuleFlyout();
            StateChanged += (_, _) => CloseRuleFlyout();
        }

        private Button RuleChoice(string nameKey, Func<RuleAppearance> choose)
        {
            var sample = new Border { Height = 6 };
            var button = new Button { Style = (Style)FindResource("RuleChoiceButton"), Content = sample };
            button.SetResourceReference(ToolTipProperty, nameKey);
            button.SetResourceReference(AutomationProperties.NameProperty, nameKey);
            button.Click += (_, _) => PickRuleStyle(choose());
            button.DataContext = choose;
            return button;
        }

        private void OpenRuleFlyout()
        {
            if (_currentId < 0) return;
            // A selection holding rules shows the first one's style, so the flyout reads what it
            // is about to change; otherwise it shows the style the next rule will get.
            _ruleShown = RuleStyle.RulesIn(Editor.Document.Blocks, Editor.Selection).Select(RuleStyle.Read).FirstOrDefault()
                         ?? CurrentRuleStyle();
            RefreshRuleFlyout();
            RulePopup.IsOpen = true;
            if (RulePopup.Child is UIElement card) Anim.FadeIn(card);
            _ruleOutsideTicks = 0;
            _ruleLeave.Start();
        }

        private void CloseRuleFlyout()
        {
            _ruleLeave.Stop();
            RulePopup.IsOpen = false;
        }

        private void PickRuleStyle(RuleAppearance appearance)
        {
            _ruleShown = appearance;
            App.SetSetting(RuleStyleSetting, appearance.ToSetting());
            if (CanEditCurrentNote() && RuleStyle.Restyle(Editor, Editor.Selection, appearance) > 0) MarkDirty();
            RefreshRuleFlyout();
        }

        private void RefreshRuleFlyout()
        {
            RulePreview.BorderThickness = new Thickness(0, 0, 0, _ruleShown.Thickness);
            RulePreview.BorderBrush = RuleStyle.Brush(_ruleShown);
            foreach (Button choice in RulePatternRow.Children.OfType<Button>().Concat(RuleWeightRow.Children.OfType<Button>()))
            {
                RuleAppearance offered = ((Func<RuleAppearance>)choice.DataContext)();
                // Each sample draws its own choice in the current color; the pattern row keeps the
                // current weight and the weight row the current pattern.
                var sample = (Border)choice.Content;
                sample.BorderThickness = new Thickness(0, 0, 0, offered.Thickness);
                sample.BorderBrush = RuleStyle.Brush(offered);
                choice.Tag = offered == _ruleShown ? "on" : null;
            }
            // The current color wears the accent ring; the others keep ThemeSwatch's own.
            foreach (Button swatch in RuleColorRow.Children.OfType<Button>())
                if (swatch.Background is SolidColorBrush b && b.Color == _ruleShown.Color)
                    swatch.SetResourceReference(BorderBrushProperty, "PrimaryBrush");
                else swatch.ClearValue(BorderBrushProperty);
        }

        private void RuleFlyout_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            CloseRuleFlyout();
            FmtRuleBtn.Focus();
            e.Handled = true;
        }

        private void RuleMoreColors_Click(object sender, RoutedEventArgs e)
        {
            RuleAppearance shown = _ruleShown;
            CloseRuleFlyout();
            var dlg = new ColorPickerDialog(this, shown.Color);
            // Confirmed, not ShowDialog() == true: the close fade nulls DialogResult
            // (ColorPickerDialog.Confirmed doc).
            dlg.ShowDialog();
            if (dlg.Confirmed) PickRuleStyle(shown with { Color = dlg.SelectedColor });
        }

        private void RuleFlyoutInsert_Click(object sender, RoutedEventArgs e) => InsertRule_Click(sender, e);
    }
}
