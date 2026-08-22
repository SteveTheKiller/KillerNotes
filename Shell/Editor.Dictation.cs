using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Services;

// Dictation (F8). A modeless companion window, opened from the sidebar rail, that records the
// microphone and transcribes it offline. Two ways into the note: the transcript as text at the
// caret, or the recording itself as an inline chip you can double-click to play back.
//
// The window is deliberately modeless and singleton, exactly like the SketchPad: you dictate a
// paragraph, print it, keep the pad open, and carry on.
namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private DictationWindow? _dictation;

        /// <summary>m:ss. A const so the format string is written once rather than repeated as an
        /// escaped literal at each call site.</summary>
        private const string DurationFormat = @"m\:ss";

        /// <summary>Chip waveform size. Named because the geometry, the track, the progress copy and
        /// its clip all have to agree, and a mismatch shows up as a progress bar that never reaches
        /// the end of the wave.</summary>
        private const double ChipWaveW = 140, ChipWaveH = 22;

        // ---- transport state (one recording plays at a time, app-wide) ----
        // The audio itself is owned by DictationPlayer; what lives here is only which chip is
        // showing it.
        private System.Windows.Threading.DispatcherTimer? _recTimer;
        private Border? _recChip;
        private int _recOrd = -1;
        private string _recTotalText = "";

        /// <summary>
        /// The chip's waveform, baked into a frozen Geometry of mirrored bars around a center line.
        /// Geometry rather than a custom element because the chip lives in the note's FlowDocument
        /// and has to survive XamlWriter, which refuses non-public types.
        /// </summary>
        private static Geometry WaveformGeometry(float[] peaks, double w, double h, double scale = 1.0)
        {
            var group = new GeometryGroup();
            double mid = h / 2;
            // The center line, so a silent or empty recording still reads as audio rather than as
            // a chip that failed to draw.
            group.Children.Add(new RectangleGeometry(new Rect(0, mid - 0.5, w, 1)));

            if (peaks.Length > 0)
            {
                const double bar = 2, gap = 1;
                int capacity = Math.Max(1, (int)(w / (bar + gap)));
                int stride = Math.Max(1, (int)Math.Ceiling(peaks.Length / (double)capacity));
                int drawn = 0;
                for (int i = 0; i < peaks.Length && drawn < capacity; i += stride, drawn++)
                {
                    // A half-pixel floor: a silent bucket still leaves a mark, so a pause in the
                    // middle of a take does not look like the recording stopped.
                    double half = Math.Max(0.5, peaks[i] * (mid - 1) * scale);
                    double x = drawn * (bar + gap);
                    group.Children.Add(new RectangleGeometry(new Rect(x, mid - half, bar, half * 2)));
                }
            }
            group.Freeze();
            return group;
        }

        // The RAIL ICON toggles: clicking it with the pad open closes the pad
        // (2026-08-08). F8 keeps bring-to-front semantics.
        private void DictationRail_Click(object sender, RoutedEventArgs e)
        {
            if (_dictation != null) { _dictation.Close(); return; }
            OpenDictation();
        }

        /// <summary>
        /// Reopens the speech-model chooser from the rail's right-click menu. The pad offers it once
        /// and then stops asking, which is right for a prompt but left no way to change model, add a
        /// more accurate one later, or recover from picking the wrong one.
        /// </summary>
        private void SpeechModel_Click(object sender, RoutedEventArgs e)
        {
            if (!WhisperSpeech.Available)
            {
                // Nothing to choose from: this build has no speech natives, so transcription is
                // Windows' engine whatever the user picks here.
                StatusText.Text = Loc("Str_Whisper_Unavailable");
                return;
            }
            new WhisperModelDialog(this).ShowDialog();
        }

        /// <summary>Called from the window ctor. See BuildRecordingChip for why the chip's click has
        /// to be caught on the editor rather than on the chip itself.</summary>
        private void InitDictation()
        {
            Editor.PreviewMouseLeftButtonDown += Editor_RecordingPress;
        }

        private void Editor_RecordingPress(object sender, MouseButtonEventArgs e)
        {
            // Walk up from whatever was hit: the click lands on the play glyph, the waveform Path
            // or the duration text far more often than on the chip Border itself.
            DependencyObject? d = e.OriginalSource as DependencyObject;
            while (d != null)
            {
                if (d is Border b && b.Tag is int ord && b.Child is Grid)
                {
                    // ONLY the play glyph plays. The chip used to be one big button, which left
                    // nowhere to take hold of it once floated - the rest of it is the grab handle
                    // now (Editor.Float.cs).
                    if (!ReferenceEquals(e.OriginalSource, ChipParts(b).glyph)) return;
                    ToggleEmbeddedRecording(ord, b);
                    e.Handled = true;
                    // Collapse the block selection the editor would otherwise paint across the
                    // chip - the same deferred trick the image handler uses.
                    Dispatcher.BeginInvoke(new Action(() =>
                        Editor.Selection.Select(Editor.Selection.Start, Editor.Selection.Start)),
                        System.Windows.Threading.DispatcherPriority.Input);
                    return;
                }
                // VisualTreeHelper ONLY on a Visual. The chip sits in a FlowDocument, so a few steps
                // up the walk leaves the visual tree and hits Paragraph / InlineUIContainer, which
                // are ContentElements - VisualTreeHelper.GetParent throws "is not a Visual or
                // Visual3D" on those. LogicalTreeHelper crosses that boundary safely.
                d = d is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
                // No point walking past the editor itself.
                if (ReferenceEquals(d, Editor)) return;
            }
        }

        private void OpenDictation()
        {
            if (_currentId < 0) { StatusText.Text = Loc("Str_St_CalcNoNote"); return; }
            if (_dictation == null)
            {
                _dictation = new DictationWindow(this, PrintDictationToNote, EmbedRecordingInNote);
                _dictation.Closed += (_, _) => { _dictation = null; DictationRailBtn.Tag = null; };
                _dictation.Show();
                DictationRailBtn.Tag = "on";   // light the rail toggle while the pad is open (family pattern)
            }
            else
            {
                if (_dictation.WindowState == WindowState.Minimized) _dictation.WindowState = WindowState.Normal;
                _dictation.Activate();
            }
        }

        /// <summary>"Print to note": drop the transcript in at the caret as ordinary text, so it is
        /// immediately editable and carries the note's own formatting rather than arriving as a
        /// special object.</summary>
        private void PrintDictationToNote(string text)
        {
            if (_currentId < 0) return;
            var caret = Editor.CaretPosition ?? Editor.Document.ContentEnd;
            caret.InsertTextInRun(text);
            // Leave the caret after what was just inserted, so a second print appends rather than
            // stacking backwards on top of itself.
            Editor.CaretPosition = caret.GetPositionAtOffset(text.Length) ?? Editor.Document.ContentEnd;
            MarkDirty();
            Editor.Focus();
        }

        /// <summary>
        /// "Embed recording": store the WAV in the note's side table and drop a chip at the caret
        /// that plays it. The audio is NOT inlined into the note's XamlPackage - a minute of speech
        /// is far larger than the note itself, and inlining would make every load and save of that
        /// note carry it. The chip holds only the ordinal.
        /// </summary>
        private void EmbedRecordingInNote(byte[] wav, int durationMs, int replaceOrd)
        {
            if (_currentId < 0) return;
            // Markdown notes store text, so the chip and its side-table row would be orphaned at
            // the next save (Markdown.cs). Transcribing to text is unaffected and still the
            // useful path here - only embedding the audio itself is refused.
            if (RejectsObject()) return;

            // Editing an existing recording overwrites it in place and refreshes the chip that is
            // already in the note, rather than leaving the original behind next to a second copy.
            // Which ordinal (or none) comes FROM THE PAD on this call - held here it went stale and
            // a brand-new take silently overwrote a recording edited earlier.
            if (replaceOrd >= 0)
            {
                if (!NoteStore.ReplaceRecording(_currentId, replaceOrd, wav, durationMs)) return;

                StopEmbeddedPlayback();          // the old audio may still be loaded
                // Located by ordinal, never re-added: a chip may have been floated or the note
                // reloaded since the pad opened, and adding a fallback chip is what produced a
                // duplicate before.
                var target = FindChip(replaceOrd);
                if (target != null) target.Child = BuildChipBody(replaceOrd, durationMs);
                MarkDirty();
                StatusText.Text = Loc("Str_Dict_Replaced");
                return;
            }

            int ord = NoteStore.AddRecording(_currentId, wav, durationMs);
            if (ord < 0) { StatusText.Text = Loc("Str_Dict_EmbedFailed"); return; }

            var caret = Editor.CaretPosition ?? Editor.Document.ContentEnd;
            new InlineUIContainer(BuildRecordingChip(ord, durationMs), caret);
            MarkDirty();
            Editor.Focus();
        }

        /// <summary>The inline playback chip. Click the play glyph to play, drag the rest of it to
        /// place it; the ordinal rides in Tag so a chip reloaded from a saved note still knows which
        /// recording it belongs to.</summary>
        private Border BuildRecordingChip(int ord, int durationMs)
        {
            var chip = new Border
            {
                Child = BuildChipBody(ord, durationMs),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 2, 0, 2),
                BorderThickness = new Thickness(1),
                // No Hand cursor on the chip as a whole - only the play glyph carries one, because
                // only the play glyph is a button. Floating swaps this for SizeAll.
                Tag = ord,
                ToolTip = Loc("Str_Dict_ChipTip"),
            };
            chip.SetResourceReference(Border.BorderBrushProperty, "OutlineBtnBrush");
            chip.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
            if (Application.Current.TryFindResource("ControlCornerRadius") is CornerRadius cr)
                chip.CornerRadius = cr;

            // NO mouse handler here. The chip lives in an InlineUIContainer inside an EDITABLE
            // RichTextBox, and the editor claims mouse input to its embedded elements to drive the
            // caret and selection - a handler attached to the chip simply never fires, which is why
            // the play glyph did nothing. Clicks are caught on the editor instead
            // (Editor_RecordingPress), the same route the placed images use.
            return chip;
        }

        /// <summary>The chip's contents, separate from its frame so that re-editing a recording can
        /// refresh the waveform and duration in place without replacing the chip itself - which
        /// would lose where the user had positioned it.</summary>
        private Grid BuildChipBody(int ord, int durationMs)
        {
            var label = new TextBlock
            {
                Text = "  " + Loc("Str_Dict_Chip") + "  " +
                       TimeSpan.FromMilliseconds(durationMs).ToString(@"m\:ss"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            // Length readout, right of the waveform. The initializer's text is replaced here: the
            // "Recording" word is redundant now that the waveform shows what the chip is.
            label.Text = TimeSpan.FromMilliseconds(durationMs).ToString(DurationFormat);
            label.FontFamily = new FontFamily("Consolas");
            label.FontSize = 11;
            label.Margin = new Thickness(8, 0, 2, 0);
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // play
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // waveform
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // duration
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // volume dial

            // Play glyph rather than a framed button: the chip IS the frame, and a button inside a
            // button reads as two controls. MDL2 E768 = Play.
            var play = new TextBlock
            {
                Text = char.ConvertFromUtf32(0xE768),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0),
                Cursor = Cursors.Hand,
            };
            play.SetResourceReference(TextBlock.ForegroundProperty, "OutlineBtnBrush");
            Grid.SetColumn(play, 0);
            layout.Children.Add(play);

            // A Path, NOT the WaveformView the pad uses. Saving a note serializes the FlowDocument
            // to a XamlPackage, and XamlWriter can only serialize PUBLIC types - an internal custom
            // element in the document throws "Cannot serialize a non-public type" the moment the
            // note is saved or swapped. Path/Geometry are public framework types, so the chip
            // survives the round trip. Same envelope either way, just baked into geometry.
            byte[]? chipWav = NoteStore.LoadRecording(_currentId, ord);
            Geometry env = WaveformGeometry(chipWav == null ? Array.Empty<float>()
                                                            : DictationRecorder.EnvelopeOf(chipWav, 70),
                                            ChipWaveW, ChipWaveH);

            // Two copies of the same envelope stacked: a dim TRACK underneath and an accent PROGRESS
            // copy on top, clipped to however much has played. That is what turns the chip from a
            // button into a transport - you can see where you are in the take without a scrubber bar
            // taking up room the chip does not have.
            var track = new System.Windows.Shapes.Path
            {
                Width = ChipWaveW, Height = ChipWaveH,
                VerticalAlignment = VerticalAlignment.Center,
                Data = env,
            };
            track.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedTextBrush");

            var played = new System.Windows.Shapes.Path
            {
                Width = ChipWaveW, Height = ChipWaveH,
                VerticalAlignment = VerticalAlignment.Center,
                Data = env,
                // Zero-width at rest. NOT frozen - the clip Rect is what animates during playback.
                Clip = new RectangleGeometry(new Rect(0, 0, 0, ChipWaveH)),
            };
            played.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "OutlineBtnBrush");

            // A second, SHORTER copy of the envelope drawn over the first in a different theme
            // color, so the loud middle of each bar reads differently from its tips. Two Paths
            // rather than a gradient brush because both keep their SetResourceReference and so
            // still follow a theme change - a baked gradient would freeze the chip on whatever
            // palette was active when the note was written.
            var core = new System.Windows.Shapes.Path
            {
                Width = ChipWaveW, Height = ChipWaveH,
                VerticalAlignment = VerticalAlignment.Center,
                Data = WaveformGeometry(chipWav == null ? Array.Empty<float>()
                                                        : DictationRecorder.EnvelopeOf(chipWav, 70),
                                        ChipWaveW, ChipWaveH, CoreScale),
                IsHitTestVisible = false,
            };
            core.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "PrimaryBrush");

            var waveCell = new Grid { VerticalAlignment = VerticalAlignment.Center };
            waveCell.Children.Add(track);
            waveCell.Children.Add(played);
            waveCell.Children.Add(core);
            Grid.SetColumn(waveCell, 1);
            layout.Children.Add(waveCell);

            Grid.SetColumn(label, 2);
            layout.Children.Add(label);

            var dial = BuildVolumeDial();
            Grid.SetColumn(dial, 3);
            layout.Children.Add(dial);

            return layout;
        }

        /// <summary>How much of each bar gets the second color. 0.55 leaves a clear band of the
        /// accent at the tips, which is what makes the two tones legible at 22px tall.</summary>
        private const double CoreScale = 0.55;

        // ---- getting a recording back out of the note ----
        //
        // The audio lives in the database, so a chip is a marker and not a file. These two are the
        // only ways out. Deliberately NOT the Windows share sheet: DataTransferManager is WinRT, and
        // reaching ShowShareUIForWindow from a desktop app means taking on the Windows SDK contracts
        // package - a new dependency for something "copy as file" already covers, since it pastes
        // straight into Outlook, Teams or Explorer.

        /// <summary>The recording the context menu was opened on, or -1.</summary>
        private int CtxRecordingOrd() => _ctxObject is Border b && b.Tag is int ord ? ord : -1;

        /// <summary>Converts stored audio to whatever format the chosen filename asks for, or
        /// returns it unchanged when it already matches. Null means the conversion was not possible,
        /// which is better than writing a file whose contents contradict its extension.</summary>
        private static byte[]? ConvertFor(string path, byte[] stored)
        {
            string want = System.IO.Path.GetExtension(path).ToLowerInvariant();
            string have = AudioCodec.ExtensionFor(stored);
            if (want == have || want.Length == 0) return stored;

            // Everything routes through PCM rather than converting format to format directly, so
            // there is one decode path and one encode path per codec instead of a matrix of them.
            byte[]? pcm = AudioCodec.ToPcm(stored);
            if (pcm == null) return null;

            return want switch
            {
                ".wav" => pcm,
                ".flac" => FlacCodec.FromWav(pcm),
                ".mp3" => LameCodec.FromWav(pcm),
                _ => null,
            };
        }

        /// <summary>Finds the chip for an ordinal by walking the document, for when a remembered
        /// reference has gone stale.</summary>
        private Border? FindChip(int ord)
        {
            foreach (var el in DescendantElements(Editor.Document))
                if (el is Border b && b.Tag is int t && t == ord && b.Child is Grid) return b;
            return null;
        }

        /// <summary>Every UIElement embedded in a document, inline or floated.</summary>
        private static System.Collections.Generic.IEnumerable<UIElement> DescendantElements(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is InlineUIContainer iuc && iuc.Child != null) yield return iuc.Child;
                else if (child is BlockUIContainer buc && buc.Child != null) yield return buc.Child;
                else if (child is DependencyObject d)
                    foreach (var nested in DescendantElements(d)) yield return nested;
            }
        }

        /// <summary>Reopens an embedded recording in the pad. The audio is decoded back to PCM on
        /// the way in, so the pad's slicing works on it exactly as it does on a fresh take. No
        /// editing state is kept here - the pad owns it and hands it back on Embed.</summary>
        private void RecEdit_Click(object sender, RoutedEventArgs e)
        {
            int ord = CtxRecordingOrd();
            if (ord < 0) return;
            byte[]? wav = NoteStore.LoadRecording(_currentId, ord);
            if (wav == null) { StatusText.Text = Loc("Str_Dict_Missing"); return; }

            OpenDictation();
            _dictation?.LoadForEdit(wav, ord);
        }

        private string RecordingFileName(int ord, byte[] data) =>
            Sanitize(TitleBox.Text) + "-" + ord + AudioCodec.ExtensionFor(data);

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "recording";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            return s.Trim().Length == 0 ? "recording" : s.Trim();
        }

        private void RecSaveAs_Click(object sender, RoutedEventArgs e)
        {
            int ord = CtxRecordingOrd();
            if (ord < 0) return;
            // RAW, not decoded: exporting should hand over the stored FLAC rather than a WAV
            // inflated back out of it.
            byte[]? data = NoteStore.LoadRecordingRaw(_currentId, ord);
            if (data == null) { StatusText.Text = Loc("Str_Dict_Missing"); return; }

            // Offer every format this build can actually produce, with whatever the recording is
            // already stored as listed first so the default costs no conversion. FLAC only appears
            // once libFLAC is present - offering a format that would silently save a WAV under a
            // .flac name would be worse than not offering it.
            string stored = AudioCodec.ExtensionFor(data);
            var formats = new System.Collections.Generic.List<string>();
            if (stored == ".flac") formats.Add("FLAC audio|*.flac");
            formats.Add("WAV audio|*.wav");
            if (stored != ".flac" && FlacCodec.Available) formats.Add("FLAC audio|*.flac");
            // MP3 is offered for sending to someone else - lossy, but it plays anywhere.
            if (LameCodec.Available) formats.Add("MP3 audio|*.mp3");

            var dlg = new KillerPDF.Controls.FileDialog(KillerPDF.Controls.FileDialogMode.Save)
            {
                Title = Loc("Str_Dict_SaveAs"),
                FileName = RecordingFileName(ord, data),
                DefaultExt = stored,
                Filter = string.Join("|", formats),
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                byte[]? outp = ConvertFor(dlg.FileName, data);
                if (outp == null)
                {
                    StatusText.Text = LameCodec.LastError ?? FlacCodec.LastError ?? Loc("Str_Dict_Missing");
                    return;
                }
                System.IO.File.WriteAllBytes(dlg.FileName, outp);
                StatusText.Text = Loc("Str_Dict_Saved");
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }

        /// <summary>Puts the recording on the clipboard as a FILE, not as bytes - that is what makes
        /// it pasteable into an email, a chat window or a folder.</summary>
        private void RecCopyFile_Click(object sender, RoutedEventArgs e)
        {
            int ord = CtxRecordingOrd();
            if (ord < 0) return;
            byte[]? data = NoteStore.LoadRecordingRaw(_currentId, ord);
            if (data == null) { StatusText.Text = Loc("Str_Dict_Missing"); return; }

            try
            {
                // Its own folder under TEMP so the file keeps a meaningful name without colliding
                // with another note's recording of the same number.
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KillerNotes", "share");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, RecordingFileName(ord, data));
                System.IO.File.WriteAllBytes(path, data);

                var files = new System.Collections.Specialized.StringCollection { path };
                Clipboard.SetFileDropList(files);
                StatusText.Text = Loc("Str_Dict_Copied");
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }

        /// <summary>Volume sweep, in degrees either side of straight up. 270 degrees of travel is
        /// the physical-knob convention and leaves a visible dead zone at the bottom, so the
        /// pointer never sits ambiguously between minimum and maximum.</summary>
        private const double DialSweep = 135;

        /// <summary>
        /// The chip's volume knob: an outlined circle with a pointer, rotated to match the level.
        /// Built from Border/Grid/Rectangle/RotateTransform because it lives in the note's
        /// FlowDocument, and XamlWriter refuses non-public types on save - so a real custom knob
        /// control is not an option here.
        /// </summary>
        private Border BuildVolumeDial()
        {
            var pointer = new System.Windows.Shapes.Rectangle
            {
                Width = 2,
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
            };
            pointer.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "OutlineBtnBrush");

            // The whole face rotates, not the pointer alone: rotating a top-aligned child about the
            // FACE's center is what makes the pointer sweep the rim instead of spinning in place.
            var face = new Grid
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(VolumeToAngle(DictationPlayer.Volume)),
            };
            face.Children.Add(pointer);

            var dial = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = face,
            };
            dial.SetResourceReference(Border.BorderBrushProperty, "MutedTextBrush");

            // The knob gets a TRANSPARENT GRAB PAD around it, and this is the part that matters: the
            // knob is 18px inside a chip that is draggable everywhere else, so a press a few pixels
            // wide of it started moving the whole recording instead of turning it down. The pad is
            // what carries DialTag, so the whole 34x30 area answers to the volume gesture.
            // Transparent, not null - a null Background is not hit-testable.
            var grab = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(8, 6, 6, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.SizeNS,
                Tag = DialTag,                 // how the editor's press handler recognizes it
                ToolTip = Loc("Str_Dict_Volume"),
                Child = dial,
            };
            return grab;
        }

        /// <summary>Marks the knob. A string, where the chip's Tag is an int - that difference is
        /// what keeps FloatableAt from mistaking the knob for the chip.</summary>
        internal const string DialTag = "vol";

        private static double VolumeToAngle(double v) => (v * 2 - 1) * DialSweep;

        /// <summary>Repaints a knob to match the current volume.</summary>
        internal static void UpdateDial(Border grab)
        {
            // The tagged element is the grab pad, so the face is one level further down than it
            // looks. Falls back to treating the argument as the knob itself for safety.
            var face = (grab.Child as Border)?.Child as Grid ?? grab.Child as Grid;
            if (face?.RenderTransform is RotateTransform rt)
                rt.Angle = VolumeToAngle(DictationPlayer.Volume);
        }

        /// <summary>The chip's moving parts, found by position rather than by name. A chip that came
        /// back from a saved note has been through XamlWriter/XamlReader and carries no namescope, so
        /// FindName would return null on exactly the chips that matter most.</summary>
        private static (TextBlock? glyph, System.Windows.Shapes.Path? played, TextBlock? label) ChipParts(Border chip)
        {
            if (chip.Child is not Grid g) return (null, null, null);
            var glyph = g.Children.Count > 0 ? g.Children[0] as TextBlock : null;
            var played = g.Children.Count > 1 && g.Children[1] is Grid cell && cell.Children.Count > 1
                ? cell.Children[1] as System.Windows.Shapes.Path
                : null;
            var label = g.Children.Count > 2 ? g.Children[2] as TextBlock : null;
            return (glyph, played, label);
        }

        /// <summary>Click on a chip: start it, or pause/resume the one already running. Clicking a
        /// DIFFERENT chip stops the current one first - two recordings talking over each other is
        /// never what was meant.</summary>
        private void ToggleEmbeddedRecording(int ord, Border chip)
        {
            if (DictationPlayer.IsOpen && _recOrd == ord && ReferenceEquals(_recChip, chip))
            {
                // MDL2 E768 Play / E769 Pause.
                var (glyph, _, _) = ChipParts(chip);
                if (DictationPlayer.IsPaused)
                {
                    DictationPlayer.Resume();
                    _recTimer?.Start();
                    if (glyph != null) glyph.Text = char.ConvertFromUtf32(0xE769);
                }
                else
                {
                    DictationPlayer.Pause();
                    _recTimer?.Stop();
                    if (glyph != null) glyph.Text = char.ConvertFromUtf32(0xE768);
                }
                return;
            }

            StopEmbeddedPlayback();
            PlayEmbeddedRecording(ord, chip);
        }

        /// <summary>Plays an embedded recording, with the chip acting as the transport: the waveform
        /// fills as it plays and the duration counts up. See DictationPlayer for why playback is
        /// winmm rather than MediaPlayer or SoundPlayer.</summary>
        private void PlayEmbeddedRecording(int ord, Border chip)
        {
            byte[]? wav = NoteStore.LoadRecording(_currentId, ord);
            if (wav == null) { StatusText.Text = Loc("Str_Dict_Missing"); return; }

            var (glyph, played, label) = ChipParts(chip);

            if (!DictationPlayer.Play(wav))
            {
                StatusText.Text = DictationPlayer.LastError ?? Loc("Str_Dict_Missing");
                return;
            }

            _recChip = chip;
            _recOrd = ord;
            _recTotalText = label?.Text ?? "";
            if (glyph != null) glyph.Text = char.ConvertFromUtf32(0xE769);   // Pause

            // 50ms: fast enough that the progress fill reads as moving rather than stepping, cheap
            // enough to be irrelevant next to the audio itself.
            _recTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50),
            };
            _recTimer.Tick += (_, _) =>
            {
                if (DictationPlayer.IsFinished) { StopEmbeddedPlayback(); return; }
                int total = DictationPlayer.DurationMs;
                if (total <= 0) return;
                double frac = Math.Min(1, DictationPlayer.PositionMs / (double)total);
                if (played?.Clip is RectangleGeometry rg)
                    rg.Rect = new Rect(0, 0, ChipWaveW * frac, ChipWaveH);
                if (label != null)
                    label.Text = TimeSpan.FromMilliseconds(DictationPlayer.PositionMs).ToString(DurationFormat);
            };
            _recTimer.Start();
        }

        /// <summary>Stops playback and puts the chip back to its resting state.</summary>
        private void StopEmbeddedPlayback()
        {
            _recTimer?.Stop();
            _recTimer = null;
            DictationPlayer.Stop();

            if (_recChip != null)
            {
                var (glyph, played, label) = ChipParts(_recChip);
                if (glyph != null) glyph.Text = char.ConvertFromUtf32(0xE768);   // Play
                if (played?.Clip is RectangleGeometry rg) rg.Rect = new Rect(0, 0, 0, ChipWaveH);
                if (label != null && _recTotalText.Length > 0) label.Text = _recTotalText;
                _recChip = null;
            }
            _recOrd = -1;
        }
    }
}
