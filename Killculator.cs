using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;

// Killculator (F9). A themed adding-machine panel (MainWindow.xaml KalcPanel) docked in the
// row below the notes list, so the list shrinks and stays visible above it. Opening animates
// the panel Height 0 -> natural (the notes row gives way), reading as a slide up from the
// footer. The Print key drops the current readout into the open note at the caret; = / Enter
// computes. Basic 4-function with % , +/- and backspace; number/operator keys type into it
// while it is open. Display/parse use the invariant culture so the on-screen "." round-trips.
namespace KillerNotes
{
    public partial class MainWindow
    {
        private bool _kalcOpen;
        private bool _kalcAutoExpanded; // we popped a collapsed sidebar open just to show the pad
        private double _kalcAcc;        // stored left operand
        private string? _kalcOp;        // pending op: add / sub / mul / div
        private string _kalcText = "0"; // what the readout shows
        private bool _kalcFresh = true; // next digit starts a new entry (after an op or result)

        // ---- Open / close with a slide animation ----

        private void ToggleKalc()
        {
            if (_kalcOpen) CloseKalc(); else OpenKalc();
        }

        private void KalcRail_Click(object sender, RoutedEventArgs e) => ToggleKalc();   // rail icon (MainWindow.xaml)

        private void OpenKalc()
        {
            if (_kalcOpen) return;
            _kalcOpen = true;
            // If the sidebar is collapsed, pop it open (without changing the saved preference)
            // so the pad is visible; CloseKalc tucks it back. (Steve, 2026-07-22)
            if (_sidebarCollapsed)
            {
                _sidebarCollapsed = false;
                ApplySidebarState(animate: true);   // Sidebar.cs
                _kalcAutoExpanded = true;
            }
            // Measure the natural height at the current sidebar width, then grow into it. The
            // panel sits in an Auto row, so the notes row (star) gives way as it grows.
            KalcPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
            KalcPanel.Visibility = Visibility.Visible;
            KalcPanel.Height = double.NaN;
            KalcPanel.UpdateLayout();
            double h = KalcPanel.ActualHeight > 0 ? KalcPanel.ActualHeight : 380;
            KalcPanel.Height = 0;
            var grow = new DoubleAnimation(0, h, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            grow.Completed += (_, _) => { if (_kalcOpen) { KalcPanel.BeginAnimation(FrameworkElement.HeightProperty, null); KalcPanel.Height = h; } };
            KalcPanel.BeginAnimation(FrameworkElement.HeightProperty, grow);
        }

        private void CloseKalc()
        {
            if (!_kalcOpen) return;
            _kalcOpen = false;
            // Restore the collapsed sidebar if we were the ones who popped it open.
            if (_kalcAutoExpanded)
            {
                _kalcAutoExpanded = false;
                _sidebarCollapsed = true;
                ApplySidebarState(animate: true);   // Sidebar.cs
            }
            double h = KalcPanel.ActualHeight > 0 ? KalcPanel.ActualHeight : 380;
            KalcPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
            KalcPanel.Height = h;
            var shrink = new DoubleAnimation(h, 0, TimeSpan.FromMilliseconds(190))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.Stop };
            shrink.Completed += (_, _) =>
            {
                if (_kalcOpen) return;
                KalcPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
                KalcPanel.Height = 0;
                KalcPanel.Visibility = Visibility.Collapsed;
            };
            KalcPanel.BeginAnimation(FrameworkElement.HeightProperty, shrink);
        }

        // ---- Button dispatch (all keypad buttons share this via Tag) ----

        private void Kalc_Key(object sender, RoutedEventArgs e)
        {
            string tok = (sender as Button)?.Tag as string ?? "";
            KalcInput(tok);
        }

        // Shared by the buttons and the keyboard shortcuts (Shortcuts.cs) while the pad is open.
        private void KalcInput(string tok)
        {
            switch (tok)
            {
                case "close": CloseKalc(); return;
                case "print": KalcPrint(); return;
                case "clear": _kalcAcc = 0; _kalcOp = null; _kalcText = "0"; _kalcFresh = true; break;
                case "neg":   KalcNeg(); break;
                case "pct":   _kalcText = KalcFormat(KalcValue() / 100.0); break;
                case "back":  KalcBack(); break;
                case "dot":   KalcDot(); break;
                case "eq":    KalcEquals(); break;
                case "add": case "sub": case "mul": case "div": KalcOp(tok); break;
                default:
                    if (tok.Length == 1 && tok[0] >= '0' && tok[0] <= '9') KalcDigit(tok);
                    break;
            }
            KalcShow();
        }

        private void KalcDigit(string d)
        {
            if (_kalcFresh) { _kalcText = d; _kalcFresh = false; }
            else if (_kalcText == "0") _kalcText = d;
            else if (_kalcText == "-0") _kalcText = "-" + d;
            else if (_kalcText.Replace("-", "").Replace(".", "").Length < 15) _kalcText += d;   // sane cap
        }

        private void KalcDot()
        {
            if (_kalcFresh) { _kalcText = "0."; _kalcFresh = false; }
            else if (!_kalcText.Contains('.')) _kalcText += ".";
        }

        private void KalcNeg()
        {
            if (_kalcText.StartsWith("-")) _kalcText = _kalcText[1..];
            else if (_kalcText != "0") _kalcText = "-" + _kalcText;
        }

        private void KalcBack()
        {
            if (_kalcFresh) return;
            if (_kalcText.Length <= 1 || (_kalcText.Length == 2 && _kalcText[0] == '-')) _kalcText = "0";
            else _kalcText = _kalcText[..^1];
        }

        private void KalcOp(string op)
        {
            // A pending op with a freshly typed operand computes first (chaining: 5 + 3 + ...).
            if (_kalcOp != null && !_kalcFresh)
            {
                _kalcAcc = KalcApply(_kalcAcc, KalcValue(), _kalcOp);
                _kalcText = KalcFormat(_kalcAcc);
            }
            else _kalcAcc = KalcValue();
            _kalcOp = op;
            _kalcFresh = true;
        }

        private void KalcEquals()
        {
            if (_kalcOp == null) return;
            double r = KalcApply(_kalcAcc, KalcValue(), _kalcOp);
            _kalcText = KalcFormat(r);
            _kalcAcc = r;
            _kalcOp = null;
            _kalcFresh = true;
        }

        private static double KalcApply(double a, double b, string op) => op switch
        {
            "add" => a + b,
            "sub" => a - b,
            "mul" => a * b,
            "div" => a / b,
            _ => b,
        };

        private double KalcValue()
            => double.TryParse(_kalcText, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static string KalcFormat(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "Error";
            double r = Math.Round(v, 10);
            return r.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        private void KalcShow()
        {
            // "Error" clears the pending state so the next key starts clean.
            if (_kalcText == "Error") { _kalcAcc = 0; _kalcOp = null; _kalcFresh = true; }
            KalcDisplay.Text = _kalcText;
        }

        // Keyboard entry while the pad is open (Shortcuts.cs routes bare keys here). Returns
        // true when the key was a calc key and was consumed, so it does not reach the editor.
        private bool TryKalcKey(Key key)
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            string? tok = key switch
            {
                >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
                >= Key.D0 and <= Key.D9 when !shift => ((char)('0' + (key - Key.D0))).ToString(),
                Key.D8 when shift => "mul",            // Shift+8 = *
                Key.OemPlus when shift => "add",       // Shift+= = +
                Key.OemPlus => "eq",                   // = computes
                Key.Add => "add",
                Key.OemMinus or Key.Subtract => "sub",
                Key.Multiply => "mul",
                Key.Divide => "div",
                Key.OemQuestion when !shift => "div",  // US "/" key
                Key.Decimal or Key.OemPeriod => "dot",
                Key.Return => "eq",
                Key.Back => "back",
                _ => null,
            };
            if (tok == null) return false;
            KalcInput(tok);
            return true;
        }

        // ---- Print: drop the readout into the open note at the caret ----

        private void KalcPrint()
        {
            if (_currentId < 0) { FlashStatus(Loc("Str_St_CalcNoNote")); return; }
            string text = _kalcText == "Error" ? "" : _kalcText;
            if (text.Length == 0) return;

            var caret = Editor.CaretPosition?.GetInsertionPosition(LogicalDirection.Forward)
                        ?? Editor.Document.ContentEnd;
            try
            {
                caret.InsertTextInRun(text);
                Editor.CaretPosition = caret.GetPositionAtOffset(text.Length) ?? caret;
            }
            catch
            {
                // Caret sat somewhere text can't be inserted (e.g. beside an image): append to the end.
                var end = Editor.Document.ContentEnd;
                end.InsertTextInRun(text);
                Editor.CaretPosition = end;
            }
            MarkDirty();
            FlashStatus(Loc("Str_St_CalcPrinted"));
        }
    }
}
