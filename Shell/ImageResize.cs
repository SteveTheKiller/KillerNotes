using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

// Non-destructive image sizing. The full-resolution original always stays in the
// database (InsertImageAtCaret never downsamples); this partial only controls how
// large the image DISPLAYS: click an image to get corner handles, drag to resize
// (aspect locked, capped at natural size so it can never upscale-blur), and every
// note image renders with high-quality (Fant) scaling so shrinking stays sharp.
namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private Image? _selImage;
        private ImageResizeAdorner? _imgAdorner;

        private void InitImageResize()
        {
            // Without this, UIElements embedded in the document (our images) are inert -
            // clicks never reach them and e.OriginalSource is the document, not the Image.
            Editor.IsDocumentEnabled = true;
            Editor.PreviewMouseLeftButtonDown += Editor_ImagePress;
        }

        // Press ON an image selects it (handles appear); press anywhere else deselects.
        // The caret click-through is untouched - we never mark the event handled.
        private void Editor_ImagePress(object sender, MouseButtonEventArgs e)
        {
            // The adorner sits on the editor's internal adorner layer, so its presses
            // tunnel through this handler first - they belong to the handles, not to us.
            if (e.OriginalSource is ImageResizeAdorner) return;

            if (e.OriginalSource is Image img)
            {
                // Double-click a placed sketch opens it in the SketchPad for editing IN PLACE:
                // "Print to note" then updates this same image where it sits (Editor.cs).
                if (e.ClickCount == 2)
                {
                    // A printed sketch reopens with its editable objects; any other image opens as a
                    // drawable backdrop. Either way Print replaces THIS image in place (Editor.cs).
                    if (Sketch.TryGetData(img, out var payload)) OpenSketchPadForEdit(img, SketchModel.Deserialize(payload));
                    else OpenSketchPadForEditImage(img);
                    e.Handled = true;
                    return;
                }
                if (!ReferenceEquals(img, _selImage)) SelectImage(img);
                // Clicking an image also makes the RichTextBox select its whole block - a
                // full-width highlight bar past the image edges. Collapse that to a caret so
                // only the resize handles show. Deferred so it runs after the click-selection.
                Dispatcher.BeginInvoke(new Action(() =>
                    Editor.Selection.Select(Editor.Selection.Start, Editor.Selection.Start)),
                    System.Windows.Threading.DispatcherPriority.Input);
            }
            else if (_selImage != null) DeselectImage();
        }

        private void SelectImage(Image img)
        {
            DeselectImage();
            var layer = AdornerLayer.GetAdornerLayer(img);
            if (layer == null) return;

            // While word wrap is on, cap the drag at the editor pane width (minus a small edge
            // pad) so an image can't be sized past the wrap edge where it would clip unreachably;
            // wrap off lifts the cap (the horizontal scrollbar can reach a wider image).
            _imgAdorner = new ImageResizeAdorner(img, () =>
                _wordWrap && Editor.ViewportWidth > 0
                    ? System.Math.Max(40, Editor.ViewportWidth - 10)
                    : double.MaxValue);
            _imgAdorner.Resized += MarkDirty;              // persist: Width rides the XamlPackage
            // A floated image also has to resize the box its Floater reserves, or the text keeps
            // wrapping around the OLD footprint (Editor.Float.cs).
            _imgAdorner.Resized += () => RefloatWidth(img);
            _imgAdorner.DismissRequested += DeselectImage;
            layer.Add(_imgAdorner);
            _selImage = img;
        }

        private void DeselectImage()
        {
            if (_imgAdorner != null)
            {
                AdornerLayer.GetAdornerLayer(_imgAdorner.AdornedElement)?.Remove(_imgAdorner);
                _imgAdorner = null;
            }
            _selImage = null;
        }

        // ---- High-quality rendering for every note image ----
        // Images deserialized from a XamlPackage come back without the scaling hint, so
        // this runs on every note load (OpenNote) as well as on insert.

        internal static void ApplyImageQuality(FlowDocument doc)
        {
            foreach (var b in doc.Blocks) FixBlockImages(b);
        }

        private static void FixBlockImages(Block block)
        {
            switch (block)
            {
                case Paragraph p:
                    foreach (var i in p.Inlines) FixInlineImages(i);
                    break;
                case BlockUIContainer buc:
                    FixImage(buc.Child);
                    break;
                case List list:
                    foreach (var li in list.ListItems)
                        foreach (var b in li.Blocks) FixBlockImages(b);
                    break;
                case Table t:
                    foreach (var g in t.RowGroups)
                        foreach (var row in g.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var b in cell.Blocks) FixBlockImages(b);
                    break;
                case Section s:
                    foreach (var b in s.Blocks) FixBlockImages(b);
                    break;
            }
        }

        private static void FixInlineImages(Inline inline)
        {
            switch (inline)
            {
                case InlineUIContainer iuc: FixImage(iuc.Child); break;
                case Span sp:
                    foreach (var i in sp.Inlines) FixInlineImages(i);
                    break;
            }
        }

        internal static void FixImage(UIElement? el)
        {
            if (el is Image img)
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        }
    }

}
