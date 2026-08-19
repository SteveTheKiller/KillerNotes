using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using KillerNotes.Services;

// First-use speech model chooser.
//
// Whisper needs a model file, and the useful ones are 75-466 MB, so it is downloaded on demand
// rather than bundled - the same reasoning as KillerPDF's OCR language data, and the case where
// download-on-demand genuinely earns its complexity.
//
// The user picks; nothing is chosen for them. A 142 MB download that starts because the app decided
// it knew best is exactly the kind of surprise that makes people distrust an app - especially on a
// tethered connection at a client site.
namespace KillerNotes.Controls
{
    internal sealed class WhisperModelDialog : Window
    {
        private readonly StackPanel _list = new();
        private readonly TextBlock _status = new();
        private readonly ProgressBar _bar = new();
        private Button _goBtn = null!, _cancelBtn = null!;
        private string _choice = "base";
        private CancellationTokenSource? _cts;
        private bool _closeFaded;

        /// <summary>True when a model finished downloading and whisper can now be used.</summary>
        internal bool Installed { get; private set; }

        internal WhisperModelDialog(Window? owner)
        {
            Title = "KillerNotes - Speech model";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.Height;
            Width = 560;
            Owner = owner;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });

            BuildUi();

            Opacity = 0;
            Loaded += (_, _) => Anim.FadeIn(this);
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                Cancel();
                e.Handled = true;
            };
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        private static string L(string key, string fallback) =>
            Application.Current.TryFindResource(key) as string ?? fallback;

        private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

        private void BuildUi()
        {
            var outer = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = Application.Current.TryFindResource("WindowCornerRadius") is CornerRadius r ? r : new CornerRadius(7),
                Margin = Application.Current.TryFindResource("DialogHaloMargin") is Thickness hm ? hm : new Thickness(20),
            };
            outer.SetResourceReference(Border.BorderBrushProperty, "WindowEdgeBrush");
            outer.SetResourceReference(Border.BorderThicknessProperty, "WindowEdgeThickness");
            outer.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            double shadowOp = Application.Current.TryFindResource("PaneShadowOpacity") is double so ? so : 0.60;
            if (shadowOp > 0)
                outer.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 16, ShadowDepth = 5, Direction = 270, Opacity = shadowOp,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
                };

            var root = new Grid();
            if (Application.Current.TryFindResource("GrainTileBrush") is Brush grain)
            {
                double op = Application.Current.TryFindResource("GrainOpacity") is double go ? go : 0.12;
                root.Children.Add(new Border { Background = grain, Opacity = op, IsHitTestVisible = false,
                                               CornerRadius = outer.CornerRadius });
            }

            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(shell);
            shell.Children.Add(BuildTitleBand());

            var body = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
            Grid.SetRow(body, 1);
            shell.Children.Add(body);

            var blurb = new TextBlock
            {
                Text = L("Str_Whisper_Blurb",
                    "Windows' built-in dictation is not very accurate. KillerNotes can use a better speech model instead - it runs entirely on this machine, and nothing is uploaded. Pick one to download."),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
            };
            blurb.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            body.Children.Add(blurb);

            // Start on whatever is in use, so reopening this shows the current state rather than
            // resetting to the recommendation every time.
            string? active = App.GetSetting("WhisperModel");
            if (!string.IsNullOrEmpty(active))
                foreach (var m in WhisperSpeech.Catalog)
                    if (m.File == active) _choice = m.Id;

            BuildChoices();
            body.Children.Add(_list);

            _bar.Height = 6;
            _bar.Margin = new Thickness(0, 12, 0, 0);
            _bar.Visibility = Visibility.Collapsed;
            _bar.Minimum = 0;
            _bar.Maximum = 100;
            // A default ProgressBar is system green on a themed card. Foreground is the fill.
            _bar.SetResourceReference(ForegroundProperty, "OutlineBtnBrush");
            _bar.SetResourceReference(BackgroundProperty, "PaneBrush");
            _bar.SetResourceReference(BorderBrushProperty, "InputBorderBrush");
            body.Children.Add(_bar);

            _status.TextWrapping = TextWrapping.Wrap;
            _status.FontSize = 11;
            _status.Margin = new Thickness(0, 8, 0, 0);
            _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            body.Children.Add(_status);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                // 4, not 14. The last choice's own bottom margin already separates it from the
                // buttons, so 14 on top of that left a visible dead band across the dialog.
                // (2026-08-07)
                Margin = new Thickness(0, 4, 0, 0),
            };
            _cancelBtn = new Button { Content = L("Str_Btn_Cancel", "Cancel"), MinWidth = 84, Height = 30,
                                      Margin = new Thickness(0, 0, 8, 0), Style = S("OutlineButton") };
            _cancelBtn.Click += (_, _) => Cancel();
            _goBtn = new Button { Content = L("Str_Whisper_Download", "Download"), MinWidth = 110, Height = 30,
                                  Style = S("OutlineButton") };
            _goBtn.Click += async (_, _) => await DownloadAsync();
            row.Children.Add(_cancelBtn);
            row.Children.Add(_goBtn);
            body.Children.Add(row);
            UpdateGoButton();   // the radios were built before the button existed

            // Bevels last so the raised edge draws over everything (family pattern).
            var light = new Border { IsHitTestVisible = false };
            light.SetResourceReference(Border.BorderBrushProperty, "BevelLightBrush");
            light.SetResourceReference(Border.BorderThicknessProperty, "BevelLightThickness");
            var dark = new Border { IsHitTestVisible = false };
            dark.SetResourceReference(Border.BorderBrushProperty, "BevelDarkBrush");
            dark.SetResourceReference(Border.BorderThicknessProperty, "BevelDarkThickness");
            root.Children.Add(light);
            root.Children.Add(dark);

            // The shared 5px window frame; nothing on a flat theme. (2026-08-07)
            root.Children.Add(KillerNotes.Controls.DialogChrome.WindowFrame());
            KillerNotes.Controls.DialogChrome.InsetForFrame(shell);

            outer.Child = root;
            Content = outer;
        }

        private Border BuildTitleBand()
        {
            // LEFT padding only, like DialogTitleBar: a 14px right pad floated the close X inset
            // from the window corner, so its hover block sat mid-band with one rounded corner
            // and the close button looked wrong on this dialog (2026-08-08). With the
            // pad gone the X reaches the corner and DialogCaptionButtonsMargin supplies the
            // family 3px inset, identical to every other dialog caption.
            var band = new Border { Padding = new Thickness(14, 0, 0, 0), Cursor = Cursors.SizeAll };
            band.SetResourceReference(Border.BackgroundProperty, "DialogTitleBarBrush");
            band.SetResourceReference(HeightProperty, "DialogTitleBarHeight");
            band.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            // DialogChrome.Wordmark, not a plain TextBlock. This was the last caption in the app
            // still writing its own title, so it showed bare "Speech model" in the UI font while
            // every other window shows the two-run wordmark (and the plain-title twin on a theme
            // that wants one). Same call the picker and the Databases window make.
            // (2026-08-07)
            var caption = DialogChrome.Wordmark(L("Str_Whisper_Title", "Speech model"));

            // DialogChrome, not a hand-rolled TextBlock. This was the last close X in the app still
            // built locally, so it kept the MDL2 character while every other window switched to the
            // drawn shape on a beveled theme - the exact drift that made "the same X everywhere"
            // untrue. CloseGlyph carries the dual glyph, the caption face and the tunnelling click
            // handler (the band's DragMove otherwise swallows it) in one place.
            var close = DialogChrome.CloseGlyph(L("Str_Btn_Cancel", "Cancel"), Cancel);

            var grid = new Grid();
            grid.Children.Add(caption);
            grid.Children.Add(close);
            band.Child = grid;
            return band;
        }

        private void BuildChoices()
        {
            foreach (var (id, file, mb, note) in WhisperSpeech.Catalog)
            {
                bool have = WhisperSpeech.IsInstalled(file);

                // Every piece of text gets an EXPLICIT theme brush. A RadioButton here uses WPF's
                // default template, so anything left to inherit picks up the system control color
                // rather than the palette - which reads correctly on one theme and is invisible on
                // the next.
                var head = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold };
                head.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                head.Inlines.Add(new System.Windows.Documents.Run(NameOf(id)));

                // The size, and "already downloaded", are secondary to the name they follow - so
                // they are muted and a step smaller rather than sharing the heading's weight.
                var size = new System.Windows.Documents.Run(have
                    ? "   " + L("Str_Whisper_Installed", "already downloaded")
                    : $"   {mb} MB");
                size.FontWeight = FontWeights.Normal;
                size.FontSize = 11;
                size.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,
                    have ? "OutlineBtnBrush" : "MutedTextBrush");   // installed reads as the accent
                head.Inlines.Add(size);

                var body = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, FontSize = 11,
                                           Margin = new Thickness(0, 2, 0, 0) };
                body.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");

                // The MODEL'S ACTUAL FILE NAME (2026-08-07). "Fast" and "Recommended" are
                // our labels, not whisper.cpp's - and once a file is on disk the user has no way to
                // tell which of the three it is, or to check a download against the upstream
                // repository. Consolas because it is a filename, dim because it is reference
                // information rather than part of the choice.
                var fileLine = new TextBlock { Text = file, FontFamily = new FontFamily("Consolas"),
                                               FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
                fileLine.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");

                var stack = new StackPanel();
                stack.Children.Add(head);
                stack.Children.Add(body);
                stack.Children.Add(fileLine);

                var radio = new RadioButton
                {
                    Content = stack,
                    GroupName = "whisper",
                    Tag = id,
                    IsChecked = id == _choice,
                    // 4, not 10, on the bottom. This is the gap BETWEEN choices; at 10 the last
                    // one also pushed a wide empty band down onto the buttons.
                    Margin = new Thickness(0, 0, 0, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                radio.SetResourceReference(ForegroundProperty, "TextBrush");
                radio.Checked += (s, _) =>
                {
                    if (s is RadioButton rb && rb.Tag is string t) _choice = t;
                    UpdateGoButton();
                };
                _list.Children.Add(radio);
            }
        }

        /// <summary>The action button says what it will actually do. Reopened from the rail with a
        /// model already downloaded, "Download" would be a lie.</summary>
        private void UpdateGoButton()
        {
            var pick = Array.Find(WhisperSpeech.Catalog, m => m.Id == _choice);
            bool have = pick.File != null && WhisperSpeech.IsInstalled(pick.File);
            _goBtn.Content = have ? L("Str_Whisper_Use", "Use this one") : L("Str_Whisper_Download", "Download");
        }

        private static string NameOf(string id) => id switch
        {
            "tiny" => L("Str_Whisper_Tiny", "Fast"),
            "small" => L("Str_Whisper_Small", "Most accurate"),
            _ => L("Str_Whisper_Base", "Recommended"),
        };

        private void Cancel()
        {
            // A download in flight is canceled but the window stays, so the user sees it stop
            // rather than the dialog vanishing mid-transfer.
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _status.Text = L("Str_Whisper_Canceled", "Download canceled.");
                return;
            }
            // Close(), never DialogResult: OnClosing cancels the first close to run the fade, and
            // assigning DialogResult across that is how dialogs in this family throw. The caller
            // reads Installed instead.
            Close();
        }

        private async Task DownloadAsync()
        {
            var pick = Array.Find(WhisperSpeech.Catalog, m => m.Id == _choice);
            if (pick.File == null) return;

            // Already on disk from a previous session: just adopt it, no download.
            if (WhisperSpeech.IsInstalled(pick.File))
            {
                App.SetSetting("WhisperModel", pick.File);
                Installed = true;
                Close();
                return;
            }

            _goBtn.IsEnabled = false;
            _list.IsEnabled = false;
            _bar.Visibility = Visibility.Visible;
            _bar.Value = 0;
            _status.Text = L("Str_Whisper_Starting", "Starting download...");
            _cts = new CancellationTokenSource();

            try
            {
                await Task.Run(() => Download(pick.File, pick.Mb, _cts.Token), _cts.Token);
                App.SetSetting("WhisperModel", pick.File);
                Installed = true;
                _status.Text = L("Str_Whisper_Done", "Ready. Transcription will use this from now on.");
                Close();
            }
            catch (OperationCanceledException)
            {
                _status.Text = L("Str_Whisper_Canceled", "Download canceled.");
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _goBtn.IsEnabled = true;
                _list.IsEnabled = true;
                _bar.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Streams a model to disk. Written to a .part file and moved into place only on full
        /// success, so an interrupted download can never leave a truncated model that would fail
        /// obscurely inside whisper months later. Same shape as KillerPDF's traineddata download.
        /// </summary>
        private void Download(string file, int expectMb, CancellationToken ct)
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            System.IO.Directory.CreateDirectory(WhisperSpeech.ModelDir);

            string target = WhisperSpeech.PathFor(file);
            string part = target + ".part";

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KillerNotes");

            using (var resp = http.GetAsync(WhisperSpeech.UrlFor(file),
                       System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? (long)expectMb * 1024 * 1024;
                using var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var dst = System.IO.File.Create(part);

                var buf = new byte[81920];
                long got = 0;
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dst.Write(buf, 0, n);
                    got += n;
                    double pct = total > 0 ? got * 100.0 / total : 0;
                    string msg = $"{got / 1048576} / {total / 1048576} MB";
                    Dispatcher.Invoke(() => { _bar.Value = pct; _status.Text = msg; });
                }
                dst.Flush();
            }

            if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
            System.IO.File.Move(part, target);
        }
    }
}
