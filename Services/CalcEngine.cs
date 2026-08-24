using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>
    /// The Killculator's adding-machine engine: basic 4-function with %, +/- and backspace, plus
    /// the equation tape that Print Equation renders ("12 + 5 = 17 x 3 = 51").
    ///
    /// No-precedence math, exactly like a desk calculator: each operator computes the pending
    /// result first, so the tape and the readout always agree. Display and parse use the invariant
    /// culture so the on-screen "." round-trips.
    ///
    /// UI-free by design - Shell/Killculator.cs owns the panel, the keypad and the caret insert.
    /// </summary>
    internal sealed class CalcEngine
    {
        private double _acc;            // stored left operand
        private string? _op;            // pending op: add / sub / mul / div
        private string _text = "0";     // what the readout shows
        private bool _fresh = true;     // next digit starts a new entry (after an op or result)

        // Equation tape for Print Equation: the committed entry as alternating operand / op-token
        // (["12","add","5","mul","3"]). The operand being typed lives in _text and is NOT on the
        // tape until an operator or = commits it, so backspace / % / +/- need no tape handling
        // (they only edit the live operand). _equaled marks that "=" just completed the tape, so
        // it survives for a print until the next entry begins.
        private readonly List<string> _seq = [];
        private bool _equaled;

        /// <summary>What the readout should show.</summary>
        internal string Display => _text;

        /// <summary>Feeds one keypad token to the engine. Returns false for the tokens the engine
        /// does not own (close / print / printeq), which the shell handles itself.</summary>
        internal bool Input(string tok)
        {
            if (tok is "close" or "print" or "printeq") return false;

            switch (tok)
            {
                case "clear": Reset(); break;
                case "neg":   Neg(); break;
                case "pct":   ClearTapeIfEqualed(); _text = Format(Value() / 100.0); break;
                case "back":  Back(); break;
                case "dot":   Dot(); break;
                case "eq":    Equals(); break;
                case "add": case "sub": case "mul": case "div": Op(tok); break;
                default:
                    if (tok.Length == 1 && tok[0] >= '0' && tok[0] <= '9') Digit(tok);
                    break;
            }
            return true;
        }

        /// <summary>"Error" clears the pending state so the next key starts clean. Called by the
        /// shell as it paints the readout.</summary>
        internal void ClearIfError()
        {
            if (_text == "Error") Reset(keepText: true);
        }

        private void Reset(bool keepText = false)
        {
            _acc = 0; _op = null; _fresh = true; _seq.Clear(); _equaled = false;
            if (!keepText) _text = "0";
        }

        private void Digit(string d)
        {
            if (_fresh) { ClearTapeIfEqualed(); _text = d; _fresh = false; }
            else if (_text == "0") _text = d;
            else if (_text == "-0") _text = "-" + d;
            else if (_text.Replace("-", "").Replace(".", "").Length < 15) _text += d;   // sane cap
        }

        private void Dot()
        {
            if (_fresh) { ClearTapeIfEqualed(); _text = "0."; _fresh = false; }
            else if (!_text.Contains('.')) _text += ".";
        }

        private void Neg()
        {
            ClearTapeIfEqualed();
            if (_text.StartsWith("-")) _text = _text[1..];
            else if (_text != "0") _text = "-" + _text;
        }

        // Editing a settled result (a digit, dot, +/- or % right after "=") starts a brand-new
        // number, so the finished equation tape is dropped rather than being extended or reprinted.
        private void ClearTapeIfEqualed()
        {
            if (!_equaled) return;
            _seq.Clear();
            _equaled = false;
        }

        private void Back()
        {
            if (_fresh) return;
            if (_text.Length <= 1 || (_text.Length == 2 && _text[0] == '-')) _text = "0";
            else _text = _text[..^1];
        }

        private void Op(string op)
        {
            // Record the tape BEFORE the chaining compute overwrites the readout with the running
            // total, so the operand is captured as the user typed it.
            if (_equaled)
            {
                // An operator after "=" CONTINUES the finished equation instead of starting a new
                // one, which is what makes the tape read "12 + 5 = 17 x 3 = 51". The tape already
                // ends with the operand that produced the shown result, so only the operator joins
                // it, and the result is never re-added as a fresh left operand. This is the
                // opposite of ClearTapeIfEqualed, which a digit, dot, +/- or % calls because those
                // start a brand-new number. _seq cannot be empty here: "=" only sets _equaled with
                // a pending op, and an op always puts two tokens on the tape first.
                _equaled = false;
                _seq.Add(op);
            }
            else if (!_fresh)
            {
                _seq.Add(_text);   // the operand just entered
                _seq.Add(op);
            }
            else if (_seq.Count == 0)   // op at the very start
            {
                _seq.Add(_text);
                _seq.Add(op);
            }
            else if (_seq.Count % 2 == 0)   // op pressed after an op with no new operand -> swap it
                _seq[^1] = op;

            // A pending op with a freshly typed operand computes first (chaining: 5 + 3 + ...).
            if (_op != null && !_fresh)
            {
                _acc = Apply(_acc, Value(), _op);
                _text = Format(_acc);
            }
            else _acc = Value();
            _op = op;
            _fresh = true;
        }

        private void Equals()
        {
            if (_op == null) return;
            // Commit the final operand to the tape before computing. "=" with no fresh operand
            // (e.g. "12 + =") reuses the readout as the operand, matching the computed result.
            if (!_fresh) _seq.Add(_text);
            else if (_seq.Count % 2 == 0 && _seq.Count > 0) _seq.Add(_text);
            double r = Apply(_acc, Value(), _op);
            _text = Format(r);
            _acc = r;
            _op = null;
            _fresh = true;
            _equaled = true;   // tape now holds a complete equation; keep it for a print
        }

        /// <summary>Builds the running-form equation from the tape plus any operand still being
        /// typed. Degrades to the bare number when no real operation happened.</summary>
        internal string EquationText()
        {
            var eff = new List<string>(_seq);
            if (!_equaled)
            {
                if (!_fresh) eff.Add(_text);                                     // include the operand mid-entry
                else if (eff.Count > 0 && eff.Count % 2 == 0) eff.RemoveAt(eff.Count - 1);   // drop a trailing, unfilled operator
            }
            // Fewer than 3 tokens means no real operation happened: just the number.
            return eff.Count < 3 ? _text : BuildRunning(eff);
        }

        // seq is operand / op-token / operand / ...  Renders left-to-right with each step's result:
        // ["12","add","5","mul","3"] -> "12 + 5 = 17 x 3 = 51" (matches the calc's no-precedence math).
        private static string BuildRunning(List<string> seq)
        {
            var sb = new StringBuilder(seq[0]);
            double acc = Parse(seq[0]);
            for (int i = 1; i + 1 < seq.Count; i += 2)
            {
                string op = seq[i], operand = seq[i + 1];
                acc = Apply(acc, Parse(operand), op);
                sb.Append(' ').Append(Glyph(op)).Append(' ').Append(operand)
                  .Append(" = ").Append(Format(acc));
            }
            return sb.ToString();
        }

        private static double Apply(double a, double b, string op) => op switch
        {
            "add" => a + b,
            "sub" => a - b,
            "mul" => a * b,
            "div" => a / b,
            _ => b,
        };

        private double Value() => Parse(_text);

        private static double Parse(string s)
            => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        // Operator glyph for the printed equation (matches the keypad button faces). Built from
        // codepoints, never typed literally, so this file stays 0 non-ASCII bytes and the symbols
        // cannot be mangled by tooling: U+2212 minus, U+00D7 times, U+00F7 divide.
        private static string Glyph(string op) => op switch
        {
            "add" => "+",
            "sub" => ((char)0x2212).ToString(),
            "mul" => ((char)0x00D7).ToString(),
            "div" => ((char)0x00F7).ToString(),
            _ => "?",
        };

        private static string Format(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "Error";
            double r = Math.Round(v, 10);
            return r.ToString("0.##########", CultureInfo.InvariantCulture);
        }
    }
}
