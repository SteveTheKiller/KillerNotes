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
                if (kv.Key == t) kv.Value.SetResourceReference(Control.BackgroundProperty, "RowSelectedBrush");
                else kv.Value.ClearValue(Control.BackgroundProperty);
            }
            _canvas.Cursor = t == Tool.Pen ? Cursors.Pen : t == Tool.Text ? Cursors.IBeam : t == Tool.Select ? Cursors.Arrow : Cursors.Cross;
            if (t != Tool.Eraser) HideEraseCursor();
        }

        private void PickColor(Color c)
        {
            _penColor = c;
            if (_tool == Tool.Eraser) SetTool(Tool.Pen);
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
