using System.Linq;
using System.Windows.Documents;
using System.Windows.Input;

namespace KillerNotes.Shell
{
    // A horizontal rule is an empty paragraph with a bottom border and a 2pt font (InsertRule_Click
    // in Editor.Format.cs). The caret never rests inside one, because text typed there came out at
    // 2pt (#28). Arrowing or clicking onto a rule moves the caret to the line beside it instead.
    public partial class MainWindow
    {
        private Key _ruleLastKey;
        private bool _ruleMovingCaret;

        private void InitRuleGuard()
        {
            Editor.PreviewKeyDown += (_, e) => _ruleLastKey = e.Key;
            Editor.PreviewMouseDown += (_, _) => _ruleLastKey = Key.None;
            Editor.SelectionChanged += (_, _) => KeepCaretOutOfRule();
        }

        /// <summary>True for an empty horizontal rule paragraph.</summary>
        internal static bool IsRule(Block? block) =>
            block is Paragraph p && p.FontSize <= 2 && p.BorderThickness.Bottom > 0 &&
            p.Inlines.All(inline => inline is Run run && run.Text.Length == 0);

        private void KeepCaretOutOfRule()
        {
            if (_ruleMovingCaret || !Editor.Selection.IsEmpty) return;
            if (Editor.CaretPosition.Paragraph is not { } rule || !IsRule(rule)) return;
            bool backward = _ruleLastKey is Key.Up or Key.Left or Key.PageUp or Key.Home;
            var target = Beside(rule, backward) ?? Beside(rule, !backward);
            if (target == null)
            {
                EnsureEditableTail();
                target = Beside(rule, false);
            }
            if (target == null) return;
            _ruleMovingCaret = true;
            try { Editor.CaretPosition = target; }
            finally { _ruleMovingCaret = false; }
        }

        /// <summary>The nearest caret position outside the rule (and any rules next to it) in one direction.</summary>
        private static TextPointer? Beside(Paragraph rule, bool backward)
        {
            var direction = backward ? LogicalDirection.Backward : LogicalDirection.Forward;
            var at = (backward ? rule.ContentStart : rule.ContentEnd).GetNextInsertionPosition(direction);
            while (at != null && IsRule(at.Paragraph)) at = at.GetNextInsertionPosition(direction);
            return at;
        }
    }
}
