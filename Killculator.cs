using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;

// Killculator (F9). A themed adding-machine panel (MainWindow.xaml KalcPanel) docked in the
// row below the notes list, so the list shrinks and stays visible above it. Opening animates
// the panel Height 0 -> natural (the notes row gives way), reading as a slide up from the
// footer. Two print keys drop into the open note at the caret: Print Sum (Ctrl+Enter) drops the
// readout, Print Equation (Ctrl+Shift+Enter) drops the whole running equation ("12 + 5 = 17 x 3
// = 51"). = / Enter computes. Basic 4-function with % , +/- and backspace; number/operator keys
// type into it while it is open. Display/parse use the invariant culture so the on-screen "."
// round-trips.
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

        // Equation tape for Print Equation: the committed entry as alternating operand / op-token
        // (["12","add","5","mul","3"]). The operand being typed lives in _kalcText and is NOT on
        // the tape until an operator or = commits it, so backspace / % / +/- need no tape handling
        // (they only edit the live operand). _kalcEqualed marks that "=" just completed the tape,
        // so it survives for a print until the next entry begins.
        private readonly List<string> _kalcSeq = new();
        private bool _kalcEqualed;

        // ---- Open / close with a slide animation ----

        private void ToggleKalc()
        {
            if (_kalcOpen) CloseKalc(); else OpenKalc();
        }

        private void KalcRail_Click(object sender, RoutedEventArgs e) => ToggleKalc();   // rail icon (MainWindow.xaml)

        // Clicking anywhere on the pad (readout, gaps) reclaims the keyboard for the calc;
        // clicking into the note hands typing back to the editor. Shortcuts.cs routes the
        // number/operator keys to the calc only while focus is inside the panel.
        private void KalcPanel_Press(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!KalcPanel.IsKeyboardFocusWithin) Keyboard.Focus(KalcPanel);
        }

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
            Keyboard.Focus(KalcPanel);   // opening claims the keyboard, so an equation types immediately
        }

        private void CloseKalc()
        {
            if (!_kalcOpen) return;
            _kalcOpen = false;
            if (KalcPanel.IsKeyboardFocusWithin) Editor.Focus();   // hand typing back to the note
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
                case "printeq": KalcPrintEquation(); return;
                case "clear": _kalcAcc = 0; _kalcOp = null; _kalcText = "0"; _kalcFresh = true; _kalcSeq.Clear(); _kalcEqualed = false; break;
                case "neg":   KalcNeg(); break;
                case "pct":   KalcClearTapeIfEqualed(); _kalcText = KalcFormat(KalcValue() / 100.0); break;
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
            if (_kalcFresh) { KalcClearTapeIfEqualed(); _kalcText = d; _kalcFresh = false; }
            else if (_kalcText == "0") _kalcText = d;
            else if (_kalcText == "-0") _kalcText = "-" + d;
            else if (_kalcText.Replace("-", "").Replace(".", "").Length < 15) _kalcText += d;   // sane cap
        }

        private void KalcDot()
        {
            if (_kalcFresh) { KalcClearTapeIfEqualed(); _kalcText = "0."; _kalcFresh = false; }
            else if (!_kalcText.Contains('.')) _kalcText += ".";
        }

        private void KalcNeg()
        {
            KalcClearTapeIfEqualed();
            if (_kalcText.StartsWith("-")) _kalcText = _kalcText[1..];
            else if (_kalcText != "0") _kalcText = "-" + _kalcText;
        }

        // Editing a settled result (a digit, dot, +/- or % right after "=") starts a brand-new
        // number, so the finished equation tape is dropped rather than being extended or reprinted.
        private void KalcClearTapeIfEqualed()
        {
            if (!_kalcEqualed) return;
            _kalcSeq.Clear();
            _kalcEqualed = false;
        }

        private void KalcBack()
        {
            if (_kalcFresh) return;
            if (_kalcText.Length <= 1 || (_kalcText.Length == 2 && _kalcText[0] == '-')) _kalcText = "0";
            else _kalcText = _kalcText[..^1];
        }

        private void KalcOp(string op)
        {
            // Record the tape BEFORE the chaining compute overwrites the readout with the running
            // total, so the operand is captured as the user typed it.
            KalcClearTapeIfEqualed();   // a new op after "=" continues from the shown result
            if (!_kalcFresh)
            {
                _kalcSeq.Add(_kalcText);   // the operand just entered
                _kalcSeq.Add(op);
            }
            else if (_kalcSeq.Count == 0)   // op at the very start, or continuing from a result
            {
                _kalcSeq.Add(_kalcText);
                _kalcSeq.Add(op);
            }
            else if (_kalcSeq.Count % 2 == 0)   // op pressed after an op with no new operand -> swap it
                _kalcSeq[^1] = op;

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
            // Commit the final operand to the tape before computing. "=" with no fresh operand
            // (e.g. "12 + =") reuses the readout as the operand, matching the computed result.
            if (!_kalcFresh) _kalcSeq.Add(_kalcText);
            else if (_kalcSeq.Count % 2 == 0 && _kalcSeq.Count > 0) _kalcSeq.Add(_kalcText);
            double r = KalcApply(_kalcAcc, KalcValue(), _kalcOp);
            _kalcText = KalcFormat(r);
            _kalcAcc = r;
            _kalcOp = null;
            _kalcFresh = true;
            _kalcEqualed = true;   // tape now holds a complete equation; keep it for a print
        }

        private static double KalcApply(double a, double b, string op) => op switch
        {
            "add" => a + b,
            "sub" => a - b,
            "mul" => a * b,
            "div" => a / b,
            _ => b,
        };

        private double KalcValue() => KalcParse(_kalcText);

        private static double KalcParse(string s)
            => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        // Operator glyph for the printed equation (matches the keypad button faces). Escaped so
        // the symbols survive tooling: U+2212 minus, U+00D7 times, U+00F7 divide.
        private static string KalcGlyph(string op) => op switch
        {
            "add" => "+",
            "sub" => "\u2212",
            "mul" => "\u00D7",
            "div" => "\u00F7",
            _ => "?",
        };

        private static string KalcFormat(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "Error";
            double r = Math.Round(v, 10);
            return r.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        private void KalcShow()
        {
            // "Error" clears the pending state so the next key starts clean.
            if (_kalcText == "Error") { _kalcAcc = 0; _kalcOp = null; _kalcFresh = true; _kalcSeq.Clear(); _kalcEqualed = false; }
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

        // ---- Print: drop into the open note at the caret ----

        // Print Sum (Ctrl+Enter): the readout only.
        private void KalcPrint()
        {
            if (_currentId < 0) { FlashStatus(Loc("Str_St_CalcNoNote")); return; }
            string text = _kalcText == "Error" ? "" : _kalcText;
            if (text.Length == 0) return;
            KalcInsert(text);
        }

        // Print Equation (Ctrl+Shift+Enter): the whole running equation ("12 + 5 = 17 x 3 = 51").
        // With no operation entered it degrades to the bare number, same as Print Sum.
        private void KalcPrintEquation()
        {
            if (_currentId < 0) { FlashStatus(Loc("Str_St_CalcNoNote")); return; }
            if (_kalcText == "Error") return;
            string text = KalcEquationText();
            if (text.Length == 0) return;
            KalcInsert(text);
        }

        // Builds the running-form equation from the tape plus any operand still being typed.
        private string KalcEquationText()
        {
            var eff = new List<string>(_kalcSeq);
            if (!_kalcEqualed)
            {
                if (!_kalcFresh) eff.Add(_kalcText);                       // include the operand mid-entry
                else if (eff.Count > 0 && eff.Count % 2 == 0) eff.RemoveAt(eff.Count - 1);   // drop a trailing, unfilled operator
            }
            // Fewer than 3 tokens means no real operation happened: just the number.
            return eff.Count < 3 ? _kalcText : KalcBuildRunning(eff);
        }

        // seq is operand / op-token / operand / ...  Renders left-to-right with each step's result:
        // ["12","add","5","mul","3"] -> "12 + 5 = 17 x 3 = 51" (matches the calc's no-precedence math).
        private static string KalcBuildRunning(List<string> seq)
        {
            var sb = new StringBuilder(seq[0]);
            double acc = KalcParse(seq[0]);
            for (int i = 1; i + 1 < seq.Count; i += 2)
            {
                string op = seq[i], operand = seq[i + 1];
                acc = KalcApply(acc, KalcParse(operand), op);
                sb.Append(' ').Append(KalcGlyph(op)).Append(' ').Append(operand)
                  .Append(" = ").Append(KalcFormat(acc));
            }
            return sb.ToString();
        }

        // Insert text at the caret (or the note end if the caret sits where text can't go).
        private void KalcInsert(string text)
        {
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
