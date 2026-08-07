using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;   // PlacementMode, for the waveform's editing menu
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    /// <summary>
    /// Dictation pad (F8). A modeless companion window, built the same way as the SketchPad: family
    /// card chrome, a themed caption band, bevels on the bevelled themes, and a fade in and out.
    ///
    /// Record -> stop -> transcribe, then either drop the text into the note at the caret or embed
    /// the recording itself for playback. Recording and recognition are both offline
    /// (Services/DictationRecorder.cs) - nothing is uploaded.
    /// </summary>
    internal sealed class DictationWindow : Window
    {
        private readonly Action<string> _printText;
        /// <summary>(wav, durationMs, replaceOrd) - replaceOrd is -1 to add a new recording, or the
        /// ordinal of the one being edited. Passed per call rather than held by the host, so it
        /// cannot go stale between opening the pad and pressing Embed.</summary>
        private readonly Action<byte[], int, int> _embedAudio;

        private Border _outerBorder = null!, _bevelLight = null!, _bevelDark = null!;
        private Button _recordBtn = null!, _transcribeBtn = null!, _printBtn = null!, _embedBtn = null!, _playBtn = null!;
        private TextBox _transcript = null!;
        private TextBlock _status = null!, _elapsed = null!;
        private WaveformView _wave = null!;
        // 100ms: fast enough that the waveform scrolls smoothly against a 50ms envelope bucket,
        // slow enough not to repaint the window pointlessly.
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };

        private byte[]? _wav;
        private int _durationMs;
        private bool _closeFaded;

        internal DictationWindow(Window? owner, Action<string> printText, Action<byte[], int, int> embedAudio)
        {
            _printText = printText;
            _embedAudio = embedAudio;

            Title = "KillerNotes - Dictation";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            // Landscape, not portrait: the waveform is the thing being worked on and it reads along
            // the X axis, so width is what buys precision when slicing. The transcript is a couple
            // of sentences and does not need the depth it had.
            Width = 760; Height = 430;
            MinWidth = 520; MinHeight = 360;
            Owner = owner;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            { CaptionHeight = 0, ResizeBorderThickness = new Thickness(8), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });

            BuildUi();

            Opacity = 0;
            Loaded += (_, _) => Anim.FadeIn(this);
            _tick.Tick += (_, _) =>
            {
                _elapsed.Text = Format(DictationRecorder.ElapsedMs);
                _wave.SetPeaks(DictationRecorder.EnvelopeSnapshot());
            };

            // Esc closes. WindowStyle.None means there are no caption buttons, so without this and
            // the X on the title band the window could only be dismissed by closing the app.
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                Close();
                e.Handled = true;
            };
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Abandon an in-flight take rather than leaving the capture device open.
            if (DictationRecorder.IsRecording) DictationRecorder.Cancel();
            _tick.Stop();
            // The take only ever lived in memory, so closing without embedding leaves no audio of
            // the user anywhere on disk.
            _playTick?.Stop();
            DictationPlayer.Stop();
            _wav = null;
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        private static string Format(long ms) =>
            TimeSpan.FromMilliseconds(ms).ToString(ms >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");

        private static string L(string key, string fallback) =>
            Application.Current.TryFindResource(key) as string ?? fallback;

        private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

        private void BuildUi()
        {
            _outerBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = Application.Current.TryFindResource("WindowCornerRadius") is CornerRadius r ? r : new CornerRadius(7),
                Margin = new Thickness(20),
            };
            _outerBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            _outerBorder.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            double shadowOp = Application.Current.TryFindResource("PaneShadowOpacity") is double so ? so : 0.60;
            if (shadowOp > 0)
                _outerBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 5, Direction = 270, Opacity = shadowOp,
                  RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality };

            var root = new Grid();
            if (Application.Current.TryFindResource("GrainTileBrush") is Brush grain)
            {
                double op = Application.Current.TryFindResource("GrainOpacity") is double go ? go : 0.12;
                root.Children.Add(new Border { Background = grain, Opacity = op, IsHitTestVisible = false,
                                               CornerRadius = _outerBorder.CornerRadius });
            }

            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // caption band
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // content
            root.Children.Add(shell);

            shell.Children.Add(BuildTitleBand());

            var body = new Grid { Margin = new Thickness(16, 10, 16, 14) };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // 0 controls
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // 1 waveform
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // 2 status
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // 3 transcript
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // 4 buttons
            Grid.SetRow(body, 1);
            shell.Children.Add(body);

            BuildControls(body);
            BuildTranscript(body);
            BuildButtons(body);

            // Bevels last, so the raised edge draws over everything (family pattern).
            _bevelLight = new Border { IsHitTestVisible = false };
            _bevelLight.SetResourceReference(Border.BorderBrushProperty, "BevelLightBrush");
            _bevelLight.SetResourceReference(Border.BorderThicknessProperty, "BevelLightThickness");
            _bevelDark = new Border { IsHitTestVisible = false };
            _bevelDark.SetResourceReference(Border.BorderBrushProperty, "BevelDarkBrush");
            _bevelDark.SetResourceReference(Border.BorderThicknessProperty, "BevelDarkThickness");
            root.Children.Add(_bevelLight);
            root.Children.Add(_bevelDark);

            _outerBorder.Child = root;
            Content = _outerBorder;
        }

        private Border BuildTitleBand()
        {
            var band = new Border { Padding = new Thickness(14, 0, 14, 0), Cursor = Cursors.SizeAll };
            band.SetResourceReference(Border.BackgroundProperty, "DialogTitleBarBrush");
            band.SetResourceReference(HeightProperty, "DialogTitleBarHeight");
            band.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            var caption = new TextBlock
            {
                Text = L("Str_Dict_Title", "Dictation"),
                FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "ChromeTextBrush");

            // Close X. The window had NO way to close it: no caption buttons (WindowStyle.None),
            // no Esc handler, and the family close button is drawn per-window rather than inherited.
            // A TextBlock, not a Button. A Button with Background="Transparent" still carries WPF's
            // DEFAULT template, which paints the system highlight behind it on hover - that is the
            // filled block, and no brush or theme reaches it. The family rule is that only the
            // GLYPH reddens; a filled red box is the Windows caption treatment and wrong on a
            // floating card. MDL2 E8BB is the same close glyph the caption buttons use.
            var close = new TextBlock
            {
                Text = char.ConvertFromUtf32(0xE8BB),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Padding = new Thickness(8, 4, 4, 4),   // a comfortable target without a visible box
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,      // Transparent, not null: null is not hit-testable
                Cursor = Cursors.Hand,
                ToolTip = L("Str_Dict_Close", "Close (Esc)"),
            };
            close.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            close.MouseEnter += (_, _) => close.SetResourceReference(TextBlock.ForegroundProperty, "DangerRed");
            close.MouseLeave += (_, _) => close.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            // PreviewMouseLeftButtonDown, NOT MouseLeftButtonUp. The band below handles
            // MouseLeftButtonDown to drag the window; that fires as the event bubbles up from this
            // glyph and DragMove() then runs a modal drag loop, so the matching ButtonUp never
            // arrives here and the X did nothing. Tunnelling gets it first, and marking it handled
            // stops the drag from starting on a click aimed at the close button.
            close.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };

            var head = new Grid();
            head.Children.Add(caption);
            head.Children.Add(close);
            band.Child = head;
            Grid.SetRow(band, 0);
            return band;
        }

        private void BuildControls(Grid body)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            _recordBtn = new Button { Content = L("Str_Dict_Record", "Record"), MinWidth = 96, Height = 30,
                                      Style = S("OutlineButton") };
            _recordBtn.Click += (_, _) => ToggleRecord();
            row.Children.Add(_recordBtn);

            _playBtn = new Button { Content = L("Str_Dict_Play", "Play"), MinWidth = 76, Height = 30,
                                    Margin = new Thickness(8, 0, 0, 0), IsEnabled = false, Style = S("SurfaceButton") };
            _playBtn.Click += (_, _) => Play();
            row.Children.Add(_playBtn);

            _transcribeBtn = new Button { Content = L("Str_Dict_Transcribe", "Transcribe"), MinWidth = 96, Height = 30,
                                          Margin = new Thickness(8, 0, 0, 0), IsEnabled = false, Style = S("SurfaceButton") };
            _transcribeBtn.Click += (_, _) => Transcribe();
            row.Children.Add(_transcribeBtn);

            _elapsed = new TextBlock { Text = "0:00", FontFamily = new FontFamily("Consolas"), FontSize = 14,
                                       VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            _elapsed.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            row.Children.Add(_elapsed);

            Grid.SetRow(row, 0);
            body.Children.Add(row);

            // Live waveform, in its own framed strip under the controls. Window = 0 throughout, so
            // it always fits the WHOLE take across the full width - see WaveformView.Window for why
            // the scrolling tail was dropped.
            var waveFrame = new Border
            {
                // Tall enough to slice against. The waveform is the editing surface now, not a
                // level meter, so it gets the vertical space the transcript used to hog.
                Height = 132,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(4, 3, 4, 3),
            };
            waveFrame.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
            waveFrame.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
            if (Application.Current.TryFindResource("ControlCornerRadius") is CornerRadius wr)
                waveFrame.CornerRadius = wr;

            _wave = new WaveformView { Window = 0, Cursor = Cursors.Hand };
            // Scrub: press to jump, hold and move to scrape through the take. Capture so a drag that
            // wanders outside the waveform keeps seeking instead of stopping dead at the edge.
            _wave.MouseLeftButtonDown += (_, ev) =>
            {
                if (_wav == null) return;
                _wave.CaptureMouse();
                SeekTo(_wave.FractionAt(ev.GetPosition(_wave).X));
            };
            _wave.MouseMove += (_, ev) =>
            {
                if (_wav == null || !_wave.IsMouseCaptured || ev.LeftButton != MouseButtonState.Pressed) return;
                SeekTo(_wave.FractionAt(ev.GetPosition(_wave).X));
            };
            _wave.MouseLeftButtonUp += (_, _) => _wave.ReleaseMouseCapture();
            // Right-click selects the segment under the cursor and opens the editing menu, so the
            // thing being acted on is highlighted before any of the verbs are read.
            _wave.MouseRightButtonDown += (_, ev) =>
            {
                if (_wav == null) return;
                _menuFraction = _wave.FractionAt(ev.GetPosition(_wave).X);
                _wave.SelectedSegment = _wave.SegmentAt(_menuFraction);
                ShowWaveMenu();
            };
            waveFrame.Child = _wave;
            Grid.SetRow(waveFrame, 1);
            body.Children.Add(waveFrame);

            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
            _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            // Say up front when the machine has no recogniser, rather than after a take is recorded.
            _status.Text = DictationRecorder.RecognizerAvailable()
                ? L("Str_Dict_Ready", "Ready. Record, then transcribe or embed the audio.")
                : L("Str_Dict_NoEngine", "No speech recogniser is installed, so transcription is unavailable. Recording and embedding still work.");
            Grid.SetRow(_status, 2);
            body.Children.Add(_status);
        }

        private void BuildTranscript(Grid body)
        {
            _transcript = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Style = S("DarkTextBox"),
            };
            Grid.SetRow(_transcript, 3);
            body.Children.Add(_transcript);
        }

        private void BuildButtons(Grid body)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                                       Margin = new Thickness(0, 12, 0, 0) };

            _embedBtn = new Button { Content = L("Str_Dict_Embed", "Embed recording"), MinWidth = 130, Height = 30,
                                     Margin = new Thickness(0, 0, 8, 0), IsEnabled = false, Style = S("SurfaceButton") };
            _embedBtn.Click += (_, _) => Embed();
            row.Children.Add(_embedBtn);

            _printBtn = new Button { Content = L("Str_Dict_Print", "Print to note"), MinWidth = 120, Height = 30,
                                     IsDefault = true, Style = S("OutlineButton") };
            _printBtn.Click += (_, _) => PrintText();
            row.Children.Add(_printBtn);

            Grid.SetRow(row, 4);
            body.Children.Add(row);
        }

        // ---- Actions ----

        private void ToggleRecord()
        {
            if (DictationRecorder.IsRecording)
            {
                _durationMs = (int)DictationRecorder.ElapsedMs;
                _wav = DictationRecorder.Stop();
                _tick.Stop();
                _recordBtn.Content = L("Str_Dict_Record", "Record");
                _elapsed.Text = Format(_durationMs);

                if (_wav == null)
                {
                    _wave.Clear();
                    _status.Text = DictationRecorder.LastError
                        ?? L("Str_Dict_Empty", "Nothing was captured. Check that a microphone is connected.");
                    return;
                }
                // Swap the scrolling tail for the whole take, so what is left on screen is the
                // shape of the thing about to be embedded.
                _wave.Window = 0;
                _wave.SetPeaks(PadPeaks(_wav));
                _playBtn.IsEnabled = true;
                _embedBtn.IsEnabled = true;
                _transcribeBtn.IsEnabled = DictationRecorder.RecognizerAvailable();

                // Transcribe straight away rather than waiting to be asked. Stopping a dictation
                // take always means "now turn that into text" - the button stays as a way to run it
                // again, but it is no longer a step you have to remember.
                if (_transcribeBtn.IsEnabled) Transcribe();
                else _status.Text = L("Str_Dict_Recorded", "Recorded. Transcribe it, embed it, or both.");
                return;
            }

            // Starting a new take discards the previous one - two recordings in one window would
            // leave the embed button ambiguous about which it means.
            _wav = null;
            _playBtn.IsEnabled = _embedBtn.IsEnabled = _transcribeBtn.IsEnabled = false;

            if (!DictationRecorder.Start())
            {
                _status.Text = DictationRecorder.LastError
                    ?? L("Str_Dict_NoMic", "Could not open the microphone.");
                return;
            }
            // A new take is a NEW recording, whatever the pad was opened for. Without this, starting
            // a fresh recording after having opened one to edit left Embed still pointed at the old
            // ordinal, so it overwrote that instead of adding.
            _editOrd = -1;
            _embedBtn.Content = L("Str_Dict_Embed", "Embed recording");

            _wave.Clear();
            _recordBtn.Content = L("Str_Dict_Stop", "Stop");
            _status.Text = L("Str_Dict_Recording", "Recording...");
            _elapsed.Text = "0:00";
            _tick.Start();
        }

        /// <summary>
        /// Loads an already-embedded recording for editing. The caller has decoded it to PCM, so
        /// slicing works on it exactly as on a fresh take; re-embedding overwrites the original
        /// rather than adding a second copy (Editor.Dictation.cs).
        /// </summary>
        internal void LoadForEdit(byte[] wav, int ord)
        {
            if (DictationRecorder.IsRecording) return;   // never clobber a take in progress

            // The ordinal being edited lives HERE, not in MainWindow. Held there it outlived the
            // edit - close the pad, record something new, press Embed, and the fresh take silently
            // overwrote the recording that had been opened for editing hours earlier instead of
            // being added. The window knows when that context ends; the host does not.
            _editOrd = ord;
            _wav = wav;
            _durationMs = WavEdit.DurationMs(wav);
            _undo = null;                 // the edit history belongs to the previous take
            _wave.Window = 0;             // whole take, not the scrolling tail
            _wave.ClearCuts();
            _wave.SetPeaks(PadPeaks(wav));
            _wave.Progress = -1;
            _elapsed.Text = Format(_durationMs);

            _playBtn.IsEnabled = _embedBtn.IsEnabled = true;
            _transcribeBtn.IsEnabled = DictationRecorder.RecognizerAvailable();
            _embedBtn.Content = L("Str_Dict_SaveChanges", "Save changes");
            _status.Text = L("Str_Dict_Editing", "Editing an embedded recording. Slice it, then save your changes.");
            Activate();
        }

        /// <summary>
        /// Envelope for the pad's waveform, at a bucket count taken from how wide the waveform
        /// actually is. EnvelopeOf's default of 96 was sized for the chip; on the landscape pad it
        /// filled about half the width and the rest read as a rendering fault. Asking for one bucket
        /// per two pixels also means resizing the window sharpens the waveform rather than
        /// stretching it.
        /// </summary>
        private float[] PadPeaks(byte[] wav)
        {
            int buckets = _wave.ActualWidth > 0 ? (int)(_wave.ActualWidth / 2) : 240;
            return DictationRecorder.EnvelopeOf(wav, Math.Max(96, Math.Min(1200, buckets)));
        }

        // ---- waveform editing ----

        /// <summary>Where the editing menu was opened, as a 0..1 position. Captured on the click
        /// because by the time an item is chosen the mouse is over the menu.</summary>
        private double _menuFraction;

        /// <summary>The last copied segment, as a standalone WAV. Kept in the window rather than on
        /// the system clipboard: this is audio being spliced within one recording, and putting it on
        /// the clipboard would trample whatever the user was copying elsewhere.</summary>
        private static byte[]? _clip;

        /// <summary>One undo level, taken before every destructive edit.</summary>
        private byte[]? _undo;

        private void ShowWaveMenu()
        {
            var menu = new ContextMenu { PlacementTarget = _wave, Placement = PlacementMode.MousePoint };

            menu.Items.Add(Item(L("Str_Dict_Slice", "Slice here"), "", () =>   // MDL2 Cut (scissors)
            {
                _wave.AddCut(_menuFraction);
                _wave.SelectedSegment = -1;
            }));

            var (from, to) = _wave.SegmentBounds(_wave.SelectedSegment);
            bool wholeThing = _wave.Cuts.Count == 0;

            menu.Items.Add(Item(L("Str_Dict_CopySeg", "Copy segment"), "",
                () => _clip = WavEdit.Extract(_wav!, MsAt(from), MsAt(to))));

            // Deleting the only segment would delete the recording, which is what Record does over.
            var del = Item(L("Str_Dict_DeleteSeg", "Delete segment"), "", () => ApplyEdit(
                WavEdit.Remove(_wav!, MsAt(from), MsAt(to)), from));
            del.IsEnabled = !wholeThing;
            menu.Items.Add(del);

            var paste = Item(L("Str_Dict_PasteSeg", "Paste segment here"), "",
                () => ApplyEdit(WavEdit.Insert(_wav!, MsAt(_menuFraction), _clip!), _menuFraction));
            paste.IsEnabled = _clip != null;
            menu.Items.Add(paste);

            if (_wave.Cuts.Count > 0)
                menu.Items.Add(Item(L("Str_Dict_ClearCuts", "Clear slices"), null, () => _wave.ClearCuts()));

            var undo = Item(L("Str_Dict_UndoEdit", "Undo edit"), "", () =>
            {
                if (_undo == null) return;
                _wav = _undo; _undo = null;
                RefreshAfterEdit(0);
            });
            undo.IsEnabled = _undo != null;
            menu.Items.Add(undo);

            menu.IsOpen = true;
        }

        private static MenuItem Item(string header, string? glyph, Action run)
        {
            var mi = new MenuItem { Header = header };
            if (glyph != null)
                mi.Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 };
            mi.Click += (_, _) => run();
            return mi;
        }

        /// <summary>A 0..1 position as milliseconds into the current recording.</summary>
        private int MsAt(double fraction) => (int)(fraction * _durationMs);

        /// <summary>Commits an edit, keeping one undo level. A null result means the edit would have
        /// left nothing behind, and is ignored rather than emptying the pad.</summary>
        private void ApplyEdit(byte[]? result, double keepAt)
        {
            if (result == null || _wav == null) return;
            _undo = _wav;
            _wav = result;
            RefreshAfterEdit(keepAt);
        }

        private void RefreshAfterEdit(double keepAt)
        {
            if (_wav == null) return;
            StopPlayback();
            _durationMs = WavEdit.DurationMs(_wav);
            _wave.Window = 0;
            _wave.SetPeaks(PadPeaks(_wav));
            // Cuts are fractions of a recording whose length just changed, so they no longer point
            // at the audio they were placed against. Clearing is honest; rescaling would silently
            // move every mark to somewhere the user did not put it.
            _wave.ClearCuts();
            _elapsed.Text = Format(_durationMs);
            _status.Text = L("Str_Dict_Edited", "Edited. Play it back, or transcribe it again.");
            _wave.Progress = keepAt > 0 ? Math.Min(1, keepAt) : -1;
        }

        /// <summary>Drives the playhead and the Play/Pause caption. Separate from the recording
        /// timer, which is measuring something else entirely.</summary>
        private System.Windows.Threading.DispatcherTimer? _playTick;

        /// <summary>Whether the speech-model offer has already been made this session. Asked once,
        /// not once per take.</summary>
        private bool _modelOffered;

        /// <summary>The embedded recording being edited, or -1 for a new take. Decides whether
        /// Embed overwrites or adds. Reset whenever recording starts, because a new take is a new
        /// recording however the pad was opened.</summary>
        private int _editOrd = -1;

        /// <summary>Play / Pause / Resume, on the one button. Which of the three it is comes from
        /// the player's own state rather than a flag kept here, so the caption cannot drift out of
        /// step with what is actually coming out of the speaker.</summary>
        private void Play()
        {
            if (_wav == null) return;

            if (DictationPlayer.IsOpen && !DictationPlayer.IsFinished)
            {
                if (DictationPlayer.IsPaused) DictationPlayer.Resume();
                else DictationPlayer.Pause();
                SyncPlayButton();
                return;
            }

            if (!DictationPlayer.Play(_wav))
            {
                _status.Text = DictationPlayer.LastError ?? L("Str_Dict_Failed", "Playback failed.");
                return;
            }
            StartPlayTick();
        }

        private void StartPlayTick()
        {
            SyncPlayButton();
            _playTick ??= new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50),
            };
            if (_playTick.Tag == null)   // wire the handler once, not once per play
            {
                _playTick.Tag = "wired";
                _playTick.Tick += (_, _) =>
                {
                    if (DictationPlayer.IsFinished) { StopPlayback(); return; }
                    int total = DictationPlayer.DurationMs;
                    if (total <= 0) return;
                    _wave.Progress = Math.Min(1, DictationPlayer.PositionMs / (double)total);
                    _elapsed.Text = TimeSpan.FromMilliseconds(DictationPlayer.PositionMs).ToString(@"m\:ss");
                };
            }
            _playTick.Start();
        }

        private void StopPlayback()
        {
            _playTick?.Stop();
            DictationPlayer.Stop();
            _wave.Progress = -1;
            if (_wav != null) _elapsed.Text = TimeSpan.FromMilliseconds(_durationMs).ToString(@"m\:ss");
            SyncPlayButton();
        }

        private void SyncPlayButton()
        {
            bool playing = DictationPlayer.IsOpen && !DictationPlayer.IsPaused && !DictationPlayer.IsFinished;
            _playBtn.Content = playing ? L("Str_Dict_Pause", "Pause") : L("Str_Dict_Play", "Play");
        }

        /// <summary>Click or drag anywhere on the waveform to move the playhead. The waveform is the
        /// scrubber - a separate slider under it would say the same thing twice.</summary>
        private void SeekTo(double fraction)
        {
            if (_wav == null) return;

            // Seeking something that has finished, or was never started, begins playback there
            // rather than doing nothing - which is what clicking into a waveform implies.
            if (!DictationPlayer.IsOpen || DictationPlayer.IsFinished)
            {
                if (!DictationPlayer.Play(_wav, (int)(fraction * _durationMs))) return;
                StartPlayTick();
            }
            else DictationPlayer.Seek((int)(fraction * DictationPlayer.DurationMs));

            _wave.Progress = fraction;
            SyncPlayButton();
        }

        private void Transcribe()
        {
            if (_wav == null) return;

            // One line per native saying whether it embedded, extracted and loaded. If the model
            // chooser does not appear, this says which of the three failed.
            Services.AudioNativeBootstrap.Trace();

            // Offer the better engine BEFORE spending time on the worse one. This fires once: after
            // a model is installed, or after the user declines, _modelOffered keeps the pad from
            // nagging on every take.
            if (!_modelOffered && DictationRecorder.CanOfferBetterRecognition())
            {
                _modelOffered = true;
                var dlg = new WhisperModelDialog(this);
                dlg.ShowDialog();
                // Either way we carry straight on and transcribe - declining means SAPI, not
                // nothing, and a user who just waited for a download should not have to ask twice.
            }

            _transcribeBtn.IsEnabled = false;
            _status.Text = L("Str_Dict_Working", "Transcribing...");
            byte[] wav = _wav;

            // Off the UI thread: a minute of audio takes seconds to recognise and would otherwise
            // freeze the window mid-click.
            System.Threading.Tasks.Task.Run(() =>
            {
                string? text = DictationRecorder.Transcribe(wav);
                string? err = DictationRecorder.LastError;
                Dispatcher.Invoke(() =>
                {
                    _transcribeBtn.IsEnabled = true;
                    if (text == null)
                    {
                        _status.Text = err ?? L("Str_Dict_Failed", "Transcription failed.");
                        return;
                    }
                    if (text.Length == 0)
                    {
                        _status.Text = L("Str_Dict_NoSpeech", "No speech was recognised in that recording.");
                        return;
                    }
                    // Append rather than replace: a second pass over a re-recorded take should not
                    // silently throw away text the user has already edited.
                    _transcript.Text = _transcript.Text.Length == 0 ? text : _transcript.Text + " " + text;
                    _status.Text = L("Str_Dict_Done", "Transcribed. Edit it here, then print to the note.");
                });
            });
        }

        private void PrintText()
        {
            string text = _transcript.Text.Trim();
            if (text.Length == 0) { _status.Text = L("Str_Dict_NoText", "There is no text to print."); return; }
            _printText(text);
            _status.Text = L("Str_Dict_Printed", "Printed to the note.");
        }

        private void Embed()
        {
            if (_wav == null) return;
            try
            {
                _embedAudio(_wav, _durationMs, _editOrd);
                _status.Text = L("Str_Dict_Embedded", "Recording embedded in the note.");
                // Disabled after a successful embed: the take stays loaded so it can still be
                // played or transcribed, and pressing Embed a second time would put a DUPLICATE
                // chip in the note. Recording again, or loading one to edit, re-enables it.
                _embedBtn.IsEnabled = false;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
    }
}
