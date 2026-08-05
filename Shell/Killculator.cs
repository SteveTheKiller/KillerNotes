using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using KillerNotes.Services;

// Killculator (F9). A themed adding-machine panel (MainWindow.xaml KalcPanel) docked in the
// row below the notes list, so the list shrinks and stays visible above it. Opening animates
// the panel Height 0 -> natural (the notes row gives way), reading as a slide up from the
// footer. Two print keys drop into the open note at the caret: Print Sum (Ctrl+Enter) drops the
// readout, Print Equation (Ctrl+Shift+Enter) drops the whole running equation ("12 + 5 = 17 x 3
// = 51"). = / Enter computes. Basic 4-function with % , +/- and backspace; number/operator keys
// type into it while it is open. Display/parse use the invariant culture so the on-screen "."
// round-trips.
namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private bool _kalcOpen;
        private bool _kalcAutoExpanded; // we popped a collapsed sidebar open just to show the pad

        // The arithmetic, the equation tape and the display formatting all live in
        // Services/CalcEngine.cs - this partial is the panel, the keypad and the caret insert.
        private readonly CalcEngine _kalc = new();

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
        // The three window-level tokens are ours; everything else is arithmetic and belongs to
        // the engine, which reports back whether it consumed the token.
        private void KalcInput(string tok)
        {
            switch (tok)
            {
                case "close": CloseKalc(); return;
                case "print": KalcPrint(); return;
                case "printeq": KalcPrintEquation(); return;
            }
            if (_kalc.Input(tok)) KalcShow();
        }

        private void KalcShow()
        {
            _kalc.ClearIfError();   // "Error" clears the pending state so the next key starts clean
            KalcDisplay.Text = _kalc.Display;
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
            string text = _kalc.Display == "Error" ? "" : _kalc.Display;
            if (text.Length == 0) return;
            KalcInsert(text);
        }

        // Print Equation (Ctrl+Shift+Enter): the whole running equation ("12 + 5 = 17 x 3 = 51").
        // With no operation entered it degrades to the bare number, same as Print Sum.
        private void KalcPrintEquation()
        {
            if (_currentId < 0) { FlashStatus(Loc("Str_St_CalcNoNote")); return; }
            if (_kalc.Display == "Error") return;
            string text = _kalc.EquationText();
            if (text.Length == 0) return;
            KalcInsert(text);
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
