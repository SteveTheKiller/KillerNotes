using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KillerNotes.Models;

namespace KillerNotes.Controls
{
    internal sealed partial class SketchPadWindow
    {
        // ---- Undo / redo ----

        private void PushUndo()
        {
            _undo.Push(SketchModel.CloneList(_objects));
            _redo.Clear();
            UpdateUndoButtons();
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Push(SketchModel.CloneList(_objects));
            var prev = _undo.Pop();
            _objects.Clear(); _objects.AddRange(prev);
            _sel = null;
            RenderCanvas(); UpdateUndoButtons();
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Push(SketchModel.CloneList(_objects));
            var next = _redo.Pop();
            _objects.Clear(); _objects.AddRange(next);
            _sel = null;
            RenderCanvas(); UpdateUndoButtons();
        }

        private void ClearAll()
        {
            if (_objects.Count == 0) return;
            PushUndo();
            _objects.Clear();
            _sel = null;
            RenderCanvas();
        }

        private void UpdateUndoButtons()
        {
            _undoBtn.IsEnabled = _undo.Count > 0;
            _redoBtn.IsEnabled = _redo.Count > 0;
        }

        // ---- Tool / color / width / fill / opacity state ----

        private void SetTool(Tool t)
        {
            if (PolyActive && t != Tool.Polygon) CancelPoly();
            if (t != Tool.Select && _sel != null) { _sel = null; RenderCanvas(); }
            _tool = t;
            foreach (var kv in _toolBtns)
            {
                if (kv.Key == t)
                {
                    // Foreground as well as Background. SelectionBg/SelectionFg are a matched pair;
                    // setting only the fill left a dark glyph on a dark selection, so the active
                    // tool was invisible - which is the whole point of marking it.
                    kv.Value.SetResourceReference(Control.BackgroundProperty, "SelectionBg");
                    kv.Value.SetResourceReference(Control.ForegroundProperty, "SelectionFg");
                }
                else
                {
                    kv.Value.ClearValue(Control.BackgroundProperty);
                    kv.Value.ClearValue(Control.ForegroundProperty);
                }
            }
            _canvas.Cursor = t == Tool.Pen ? Cursors.Pen : t == Tool.Text ? Cursors.IBeam : t == Tool.Select ? Cursors.Arrow : Cursors.Cross;
            if (t != Tool.Eraser) HideEraseCursor();
        }

        private void PickColor(Color c)
        {
            _penColor = c;
            if (_tool == Tool.Eraser) SetTool(Tool.Pen);
            // A SELECTED object recolors too. The palette only ever set the pen for the NEXT
            // stroke, so selecting a text label and clicking swatches did nothing at all
            // ("theres no way to change the text color in the sketchpad", Steve, 2026-08-08).
            // One undo step; a filled shape keeps its fill's own alpha while the hue follows.
            if (_sel != null)
            {
                PushUndo();
                if (_sel.Fill is string f && f.Length == 9)
                    _sel.Fill = $"#{f.Substring(1, 2)}{c.R:X2}{c.G:X2}{c.B:X2}";
                _sel.Color = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
                RenderCanvas();
            }
        }

        private void CycleWidth()
        {
            int i = Array.IndexOf(Widths, (int)_penWidth);
            _penWidth = Widths[(i + 1) % Widths.Length];
            SetWidthDot();
        }

        private void SetWidthDot()
        {
            double d = Math.Max(3, Math.Min(18, _penWidth));
            _widthDot.Width = d; _widthDot.Height = d;
        }

        private void CycleOpacity()
        {
            int i = Array.IndexOf(Alphas, _fillAlpha);
            if (i < 0) i = 1;
            _fillAlpha = (byte)Alphas[(i + 1) % Alphas.Length];
            _opacityText.Text = OpacityLabel();
        }

        private string OpacityLabel() => $"{(int)Math.Round(_fillAlpha / 255.0 * 100)}%";

        private void ToggleFill()
        {
            _fill = !_fill;
            if (_fill) _fillSquare.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
            else { _fillSquare.ClearValue(Border.BackgroundProperty); _fillSquare.Background = Brushes.Transparent; }
        }
    }
}
