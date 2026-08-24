// ═══════════════════════════════════════════════════════════
//  GRAPH  -  every link between notes, drawn
// ═══════════════════════════════════════════════════════════
//
// The picture of what note_links holds. It is a VIEW and nothing else: it never writes, so no
// layout can damage a note and closing it costs nothing.
//
// WHY THIS IS CHEAP HERE AND EXPENSIVE IN OBSIDIAN: the links are already a table in the same
// SQLite file as the notes, so building the graph is one query. There is no vault to scan, no
// file watcher, and no reindex on open.
//
// ── THE THREE THINGS THAT MAKE IT SMOOTH, all learned the hard way (2026-08-23) ──
//
//  1. THE VISUALS ARE BUILT ONCE. The first version cleared the canvas and recreated every
//     ellipse, label and line 60 times a second, measuring each label as it went. Now every node
//     owns its visuals for the window's lifetime and a frame writes only Canvas.Left/Top and four
//     line coordinates.
//
//  2. THE SIMULATION DOES NOT RUN ON SCREEN. This is the one that actually fixed the sputtering,
//     and the cause is WPF, not the physics:
//
//       AllowsTransparency = true makes this a LAYERED window. Every frame composites through a
//       far slower path than an ordinary window, and this one also carries a full-size
//       DropShadowEffect and a tiled grain ImageBrush over the top. Animating a hundred elements
//       at 60fps through that is close to the worst case WPF has. Tightening the force loop does
//       nothing: for 30 nodes it is microseconds and was never the problem.
//
//     So the layout is SOLVED OFF THE UI THREAD, to completion, over plain double arrays that
//     touch no WPF object. Then the nodes EASE from where they are to where they landed over a
//     third of a second, and the window goes completely static. There is no continuous physics.
//
//     It is also why no graph library is wanted here. A dependency brings its own renderer and
//     meets the same layered-window ceiling; the fix is to stop drawing every frame.
//
//  3. THE LAYOUT COOLS. Fruchterman-Reingold with a falling temperature that clamps how far any
//     node may move per pass, plus a weak gravity toward the middle. Without the gravity there is
//     nothing holding the graph together and it expands until the boundary clamp stops it - which
//     is how every node ended up parked on an edge with the centre empty.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    public sealed class GraphWindow : Window
    {
        private sealed class Node
        {
            public long Id;
            public string Title = "";
            public double X, Y;
            public double FromX, FromY, ToX, ToY;   // the ease, once a solve has finished
            public bool Ghost;
            // Velocity, carried BETWEEN ticks. d3-force's velocity Verlet integrator keeps a
            // velocity per node and decays it (velocityDecay), rather than recomputing a
            // displacement from scratch each pass and clamping how far it may travel. That is
            // what makes motion look like momentum settling instead of nodes being teleported a
            // capped distance every frame, and it is why every attempt to tune a step clamp here
            // produced either twitching or lurching (2026-08-23).
            public double VX, VY;

            // FIXED position, d3's fx/fy. Non-null means "this node is held" - the simulation
            // writes its position from here and zeroes its velocity. Set while a node is under
            // the cursor and cleared on release. It is not a mode, has no command, and nothing in
            // the menu refers to it.
            public double? FixX, FixY;
            public int Degree;
            public string Group = "";
            public string? GroupHex;    // the sidebar color of the group this note lives in
            public Ellipse Dot = null!;
            public TextBlock Label = null!;
            // RENDER-time position. Writing a TranslateTransform never invalidates layout, where
            // Canvas.SetLeft/SetTop do - which is the difference between a frame that costs a
            // render and a frame that costs a full arrange pass over every element.
            public TranslateTransform DotMove = new();
            public TranslateTransform LabelMove = new();
            public double R;
            public Brush Accent = Brushes.Gray;
            public Brush GroupBrush = Brushes.Gray;
            /// <summary>The group's own name, without the parent path - what the sidebar shows.</summary>
            public string GroupLeaf
            {
                get
                {
                    if (Group.Length == 0) return "";
                    // GroupSep is a string constant, so the offset is its LENGTH, not 1 - it
                    // happens to be one character today and that is not something to rely on.
                    int cut = Group.LastIndexOf(NoteStore.GroupSep, StringComparison.Ordinal);
                    return cut < 0 ? Group : Group[(cut + NoteStore.GroupSep.Length)..];
                }
            }
        }

        private static Brush? ParseBrush(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var b = new SolidColorBrush(c);
                b.Freeze();   // frozen: shared across nodes and never mutated
                return b;
            }
            catch (FormatException) { return null; }
        }

        private readonly List<Node> _nodes = [];
        private readonly List<(Node A, Node B)> _edges = [];
        private System.Windows.Shapes.Path _edgeLayer = null!;
        // TRANSPARENT, not null. A Canvas with a null Background receives no mouse input at all,
        // so right-clicking the empty field between nodes hit nothing (2026-08-23).
        private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
        private readonly Random _rng = new(12345);   // seeded: the same database lays out the same way
        private readonly Action<long> _open;
        private readonly Action<string> _create;
        private readonly long _focusId;
        private Node? _drag;

        // A CLICK IS NOT A DRAG. The pointer has to travel past the system drag threshold before
        // any of the drag machinery runs - see BeginDrag. Starting the simulation on mouse-down
        // meant selecting a node ran the forces, and since a circle, grid or group arrangement is
        // not a force equilibrium, one click collapsed it into a blob and manual placement was
        // impossible.
        private Point _dragFrom;
        private bool _dragging;   // the threshold has been crossed and BeginDrag has run
        private Node? _menuNode;

        // The ease. 30fps for a third of a second, then nothing - short enough that even a layered
        // window carries it comfortably.
        private readonly DispatcherTimer _ease = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly DispatcherTimer _resizeSettle = new() { Interval = TimeSpan.FromMilliseconds(180) };

        // Long enough to read a line of text twice without hurrying, short enough that it is gone
        // before you have started arranging anything.
        private static readonly TimeSpan LegendLinger = TimeSpan.FromSeconds(10);
        private readonly DispatcherTimer _legendFade = new() { Interval = LegendLinger };
        private TextBlock _legend = null!;
        private TextBlock _legendHint = null!;   // the "?" the legend leaves behind

        /// <summary>True once the legend has faded and its strip has been given back to the graph.
        /// UsableBox reads it, so the drawable area really does grow rather than the text merely
        /// becoming invisible above reserved space.</summary>
        private bool _legendGone;
        private DateTime _easeStart;
        // Long enough to READ as movement. At 340ms the graph effectively teleported - the
        // rearrangement was over before the eye followed it, so it looked like no animation at
        // all (2026-08-23). This is the one animation in the window, and it is what makes an
        // Arrange feel like the graph reorganising itself rather than a screen swap. Still a
        // fixed, bounded transition, so it costs nothing like a per-frame simulation would.
        private static readonly TimeSpan EaseTime = TimeSpan.FromMilliseconds(820);

        private const double Cool = 0.982, TempFloor = 0.35;
        private const double Gravity = 0.022;
        // How far a fit may ENLARGE a layout to fill the window. Generous, because the usual case
        // is a graph that should fill the space it is given; bounded, because a two-node graph
        // blown up without limit is two dots in opposite corners joined by a very long line.
        private const double MaxFitScale = 4.0;
        private const int SolvePasses = 600;

        private bool _solving, _solveQueued;
        private double[] _solvedX = [], _solvedY = [];

        // When set, only notes in this group (or one of its subgroups) are drawn, along with
        // whatever they link to. The whole-notebook graph answers "what is the shape of all
        // this"; a group's graph answers "how does THIS subject hang together", which is the more
        // useful question once a notebook is big enough to need groups at all.
        private readonly string? _groupFilter;

        public GraphWindow(Action<long> openNote, Action<string> createNote, long focusId, string title,
                           string? groupFilter = null)
        {
            _open = openNote;
            _create = createNote;
            _focusId = focusId;
            _groupFilter = groupFilter;
            Title = title;
            Width = 1040; Height = 740;
            MinWidth = 520; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // FAMILY CHROME, not the OS caption. Every other window in this app draws its own.
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.CanResize;
            Background = Brushes.Transparent;
            // A REFERENCE window, meant to sit open beside the notes while you work - so it needs
            // a taskbar entry and Alt+Tab, and it must not be Owner-ed, which would pin it
            // permanently above the main window. Same reasoning as the SketchPad and Dictation pads.
            ShowInTaskbar = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(TryFindResource("UseDialogCaption") != null ? 4 : 24),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });

            Content = BuildChrome(title);
            Build();
            BuildNodeMenu();
            // The menu's "Color by group" starts checked, but nothing had ever ACTED on that -
            // the nodes were built in the accent and stayed there, so the colors simply never
            // appeared (2026-08-23). The initial state has to be applied, not just declared.
            ApplyNodeColors();

            // FADE IN AND OUT, like every other window in the family. KillerPDF, the SketchPad and
            // the Dictation pad all do this; the graph was the one window that snapped in and out
            // (2026-08-23). Opacity starts at 0 so the first painted frame is already transparent
            // rather than flashing the card at full opacity.
            Opacity = 0;
            _ease.Tick += (_, _) => EaseStep();
            // A resize FITS what is on screen rather than re-solving it. Re-solving skipped every
            // pinned node, so after an arrange - which pins everything - maximizing left the
            // layout at its old coordinates while the canvas grew, and the nodes ended up
            // scattered and off screen (2026-08-23). Fitting is unconditional: pinned or not,
            // everything ends up inside the window.
            _resizeSettle.Tick += (_, _) => { _resizeSettle.Stop(); FitAllIntoView(); };
            _legendFade.Tick += (_, _) => DismissLegend();
            Loaded += (_, _) => { Anim.FadeIn(this); Seed(); Resolve(); _legendFade.Start(); };
            Closed += (_, _) => { _ease.Stop(); _resizeSettle.Stop(); _legendFade.Stop(); StopLive(); };

            _canvas.MouseLeftButtonDown += CanvasDown;
            _canvas.MouseMove += CanvasMove;
            _canvas.MouseLeftButtonUp += (_, e) =>
            {
                if (_banding) { FinishBand(e.GetPosition(_canvas)); _canvas.ReleaseMouseCapture(); return; }
                DropDraggedNode();
            };
            _canvas.MouseLeave += (_, _) => DropDraggedNode();
            _canvas.Children.Add(_band);   // above the edges, below nothing that matters
            // SizeChanged fires on every frame of a window drag, so debounce - re-solving on each
            // one meant the graph never settled while the window was being resized.
            SizeChanged += (_, _) => { _resizeSettle.Stop(); _resizeSettle.Start(); };
            KeyDown += OnKey;
        }

        // ── Chrome ───────────────────────────────────────────────────────────────────────

        private Button _maxBtn = null!;

        private UIElement BuildChrome(string title)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(BuildCaption(title));

            var surface = new Grid();
            surface.Children.Add(_canvas);
            // A legend, because a hollow node is not self-explanatory - "why are some nodes yellow
            // and some hollow?" was the first thing asked about it (2026-08-23).
            //
            // IT LEAVES. The legend answers a question you have once, and then it is a permanent
            // caption on a view whose whole job is to show as much graph as it can - so it reads
            // its line, waits, and hands the space back (LegendLinger). It behaves like a status
            // message rather than a label, and the text lives on as the canvas tooltip afterwards
            // so nothing is actually lost.
            _legend = new TextBlock
            {
                Text = Str("Str_Graph_Legend",
                           "Filled = a note.   Outlined = linked but not written yet.   Drag to arrange, right-click for more."),
                FontSize = 11,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(14, 0, 0, 10),
            };
            _legend.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            surface.Children.Add(_legend);

            // What the legend leaves behind: a single muted "?" in the same corner, holding the
            // same sentence in its tooltip. A tooltip on the whole canvas was the alternative and
            // it had no affordance at all - nothing on screen said the explanation still existed,
            // and it fired wherever the pointer happened to rest. One glyph is a target you can
            // aim at and roughly free of the clutter the legend was.
            //
            // Consolas and a literal ?, the same way the sidebar rail draws its shortcuts button.
            _legendHint = new TextBlock
            {
                Text = "?",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Opacity = 0,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand,
                // A TEXTBLOCK WITH NO BACKGROUND IS NOT HIT-TESTABLE - null does not take mouse
                // input, so the first version could not be hovered or clicked at all and the
                // tooltip never had a chance to appear. Transparent does take input, and the
                // padding turns one narrow glyph into a target big enough to actually hit.
                Background = Brushes.Transparent,
                Padding = new Thickness(7, 3, 7, 3),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                // Sits where the legend's first character sat, so it reads as what is left of it.
                // The margin is the legend's less the padding, so the glyph lands in the same spot.
                Margin = new Thickness(8, 0, 0, 6),
                ToolTip = _legend.Text,
            };
            _legendHint.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            // Lights on hover, so it says it is worth pointing at rather than looking like a
            // stray character.
            _legendHint.MouseEnter += (_, _) =>
                _legendHint.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            _legendHint.MouseLeave += (_, _) =>
                _legendHint.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            // Clicking brings the whole line back for another ten seconds. Hovering for a tooltip
            // is the discoverable half; a click that did nothing was just a dead glyph.
            _legendHint.MouseLeftButtonUp += (_, _) => RestoreLegend();
            surface.Children.Add(_legendHint);
            Grid.SetRow(surface, 1);
            grid.Children.Add(surface);

            // ONE SURFACE, and it is BackgroundBrush - the tier the title bar and the app body
            // share. The caption used to sit on DialogTitleBarBrush over a PaneBrush canvas, which
            // drew a purple band above a teal field and made the window look like two windows
            // stacked (2026-08-23).
            // NO opaque child painting the background. The fill has to be on the OUTER border
            // itself, because a Border's CornerRadius rounds only its own background - a child
            // Border filling the same rectangle paints straight over the rounded corners and the
            // card comes out square (2026-08-23; the same trap BuildCanvas documents).
            var body = new Grid();
            body.Children.Add(grid);
            // The content still has to be clipped to the radius, and ClipToBounds cannot do it -
            // it clips to the rectangle. A rounded geometry, resized with the window.
            body.SizeChanged += (_, e) =>
            {
                double r = CardRadius().TopLeft;
                body.Clip = new RectangleGeometry(new Rect(e.NewSize), r, r);
            };
            // Grain LAST and hit-test invisible, so it covers the caption and the canvas alike -
            // the family rule that a pane's texture goes over its contents, not under them.
            var grain = new Border { IsHitTestVisible = false };
            grain.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
            grain.SetResourceReference(OpacityProperty, "GrainOpacity");
            body.Children.Add(grain);

            // The grip goes INSIDE the card. Parented to the window root it sat out in the 20px
            // transparent shadow halo, floating clear of the window it belongs to (2026-08-23).
            var grip = DialogChrome.ResizeGrip(this);
            if (grip is FrameworkElement g)
            {
                g.HorizontalAlignment = HorizontalAlignment.Right;
                g.VerticalAlignment = VerticalAlignment.Bottom;
                g.Margin = new Thickness(0, 0, 4, 4);
            }
            body.Children.Add(grip);

            // THE SKETCHPAD'S CHROME, not a new one. That window already solved every part of
            // this and its comments carry the reasons; the first attempt here hand-rolled a halo
            // from DialogHaloMargin (10) while using a 16px blur, and WPF expands an effect's
            // render bounds by the FULL blur radius - so the shadow was wider than the room it had
            // and got clipped square (2026-08-23). The pads use 20 for exactly that reason.
            // NO ClipToBounds. The drop shadow is an Effect on THIS element, and clipping to the
            // element's own bounds clips the effect with it - which is why the shadow was cut off
            // square (2026-08-23). SketchPad's outer border sets neither, for the same reason.
            _outerBorder = new Border { Child = body };
            _outerBorder.SetResourceReference(Border.BorderBrushProperty, "WindowEdgeBrush");
            _outerBorder.SetResourceReference(Border.BorderThicknessProperty, "WindowEdgeThickness");
            _outerBorder.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");

            StateChanged += (_, _) => { UpdateWindowCorners(); SyncMaxGlyph(); };
            KillerNotes.Services.ThemeManager.ThemeChanged += UpdateWindowCorners;
            Closed += (_, _) => KillerNotes.Services.ThemeManager.ThemeChanged -= UpdateWindowCorners;
            UpdateWindowCorners();

            // NO resize grip. WindowChrome's ResizeBorderThickness above already makes all four
            // edges and corners draggable, so a grip is decoration that does nothing here - and
            // this window has no footer for one to belong to (2026-08-23).
            return _outerBorder;
        }

        private Border _outerBorder = null!;

        /// <summary>Rounded floating card normally; squared off and flush - no halo, no shadow -
        /// when maximized or on a flat theme. Copied wholesale from SketchPadWindow, including the
        /// 20px halo and the resize-border figure that has to track it: on a flat theme the halo
        /// is 0, so a 24px grab would sit entirely ON the window and swallow the caption.</summary>
        private void UpdateWindowCorners()
        {
            bool max = WindowState == WindowState.Maximized;
            bool flat = Application.Current?.TryFindResource("UseDialogCaption") != null;
            _outerBorder.CornerRadius = max ? new CornerRadius(0) : CardRadius();
            _outerBorder.Margin = max || flat ? new Thickness(0) : new Thickness(20);
            // NULL, never an opacity-0 effect: an Effect always costs an offscreen surface and is
            // one renderer quirk from ghosting; absence cannot ghost.
            _outerBorder.Effect = max || flat ? null : CardShadowOrNull();
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(flat ? 4 : 24),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        private static CornerRadius CardRadius() =>
            Application.Current?.TryFindResource("WindowCornerRadius") is CornerRadius r ? r : new CornerRadius(7);

        private static System.Windows.Media.Effects.DropShadowEffect? CardShadowOrNull()
        {
            if (Application.Current?.TryFindResource("PaneShadowOpacity") is not double o || o <= 0) return null;
            return new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = o,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
            };
        }

        /// <summary>Wordmark left, the three real window buttons right. Minimize and maximize are
        /// here because this window is meant to STAY OPEN while you work in the notes; a reference
        /// window you cannot minimize or maximize is a dialog pretending to be a tool.</summary>
        private UIElement BuildCaption(string title)
        {
            var bar = new Grid { Background = Brushes.Transparent };
            bar.SetResourceReference(HeightProperty, "TitleBarHeight");
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.ClickCount == 2) ToggleMaximize();
                else if (WindowState != WindowState.Maximized) DragMove();
            };

            var mark = (FrameworkElement)DialogChrome.Wordmark(title);
            mark.SetResourceReference(MarginProperty, "TitleBarPadding");
            Grid.SetColumn(mark, 0);
            bar.Children.Add(mark);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(CaptionButton("", "ChromeButton", () => WindowState = WindowState.Minimized));
            _maxBtn = CaptionButton("", "ChromeButton", ToggleMaximize);
            buttons.Children.Add(_maxBtn);
            buttons.Children.Add(CaptionButton(null, "ChromeCloseButton", Close));
            Grid.SetColumn(buttons, 1);
            bar.Children.Add(buttons);
            Grid.SetRow(bar, 0);
            return bar;
        }

        private static Button CaptionButton(string? glyph, string styleKey, Action onClick)
        {
            var b = new Button();
            if (Application.Current?.TryFindResource(styleKey) is Style s) b.Style = s;
            // ChromeCloseButton draws its own X from the template - the one definition of that
            // glyph in the app - so the close button passes no content.
            if (glyph != null)
            {
                b.Content = glyph;
                b.FontFamily = new FontFamily("Segoe MDL2 Assets");
                b.FontSize = 10;
                b.SetResourceReference(ForegroundProperty, "CaptionGlyphBrush");
            }
            b.Click += (_, _) => onClick();
            return b;
        }

        private void ToggleMaximize() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void SyncMaxGlyph() =>
            _maxBtn.Content = WindowState == WindowState.Maximized ? "" : "";

        // ── Data ─────────────────────────────────────────────────────────────────────────

        /// <summary>Reads the database once. Ghost nodes stand in for link targets no note carries
        /// - a note you keep pointing at and have not written is worth seeing, not hiding.</summary>
        private void Build()
        {
            // Group colors, so a node can be painted by which part of the notebook it lives in.
            // The colors already exist and already mean something to the reader - they are the
            // ones on the sidebar's group headers - so this needs no new palette and no legend
            // beyond the groups themselves.
            var groupColor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, _, color) in NoteStore.ListGroups())
                if (!string.IsNullOrWhiteSpace(color)) groupColor[name] = color;

            var byId = new Dictionary<long, Node>();
            foreach (var n in NoteStore.List())
            {
                if (string.IsNullOrWhiteSpace(n.Title)) continue;
                var node = new Node { Id = n.Id, Title = n.Title, Group = n.Notebook ?? "" };
                // Walk up the group path: a note in "Client sites/Northwind Dental" takes that
                // group's color, or its parent's if only the parent is colored.
                string path = node.Group;
                while (path.Length > 0)
                {
                    if (groupColor.TryGetValue(path, out string? c)) { node.GroupHex = c; break; }
                    int cut = path.LastIndexOf(NoteStore.GroupSep, StringComparison.Ordinal);
                    if (cut < 0) break;
                    path = path[..cut];
                }
                byId[n.Id] = node;
            }

            var ghosts = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            var pairs = new List<(Node, Node)>();
            foreach (var (srcId, dstId, target) in NoteStore.AllLinks())
            {
                if (!byId.TryGetValue(srcId, out var a)) continue;
                Node b;
                if (dstId >= 0)
                {
                    if (!byId.TryGetValue(dstId, out var found)) continue;
                    b = found;
                }
                else
                {
                    if (!ghosts.TryGetValue(target, out var g))
                        ghosts[target] = g = new Node { Id = -1, Title = target, Ghost = true };
                    b = g;
                }
                if (ReferenceEquals(a, b)) continue;   // a note linking itself is not an edge
                pairs.Add((a, b));
                a.Degree++; b.Degree++;
            }

            // ISOLATED NOTES ARE LEFT OUT on purpose. A new database is a hundred notes with no
            // links, and a hundred unconnected dots says nothing - the graph is about structure.
            foreach (var n in byId.Values.Where(n => n.Degree > 0)) _nodes.Add(n);
            _nodes.AddRange(ghosts.Values);

            // A group filter is applied LAST, over the finished graph, and keeps anything the
            // group touches rather than only the group itself: a subject's shape includes what it
            // reaches out to. Subgroups count as inside, since the path is a prefix.
            if (_groupFilter is string gf && gf.Length > 0)
            {
                bool InGroup(Node v) =>
                    !v.Ghost && (v.Group.Equals(gf, StringComparison.OrdinalIgnoreCase)
                                 || v.Group.StartsWith(gf + NoteStore.GroupSep, StringComparison.OrdinalIgnoreCase));

                var keep = new HashSet<Node>(_nodes.Where(InGroup));
                foreach (var (a, b) in pairs)
                {
                    if (InGroup(a)) keep.Add(b);
                    if (InGroup(b)) keep.Add(a);
                }
                _nodes.RemoveAll(v => !keep.Contains(v));
                pairs.RemoveAll(p => !keep.Contains(p.Item1) || !keep.Contains(p.Item2));
                // Degrees were counted against the whole notebook; recount so node size reflects
                // the graph actually on screen.
                foreach (var v in _nodes) v.Degree = 0;
                foreach (var (a, b) in pairs) { a.Degree++; b.Degree++; }
            }
            BuildVisuals(pairs);
        }

        private static Brush Res(string key, Color fallback) =>
            Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

        /// <summary>Creates every visual ONCE. Nothing here runs again while the window is open.</summary>
        private void BuildVisuals(List<(Node A, Node B)> pairs)
        {
            var edgeBrush = Res("DimTextBrush", Color.FromRgb(0x80, 0x80, 0x80));
            var textBrush = Res("TextBrush", Colors.White);
            var accent = Res("PrimaryBrush", Color.FromRgb(0xB8, 0x29, 0xFF));
            var ghostBrush = Res("MutedTextBrush", Color.FromRgb(0x88, 0x88, 0x88));
            // ONE frozen shadow for every node. Frozen means WPF can share it across the whole
            // canvas instead of tracking one per element, which matters when there are hundreds.
            var dotShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Direction = 270,
                Opacity = 0.45, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            };
            dotShadow.Freeze();

            // ONE Path for every edge, added first so the nodes sit above the lines. Forty Line
            // elements meant forty things for WPF to measure and arrange on each frame; one Path
            // with a rebuilt geometry is a single render-level change.
            _edgeLayer = new System.Windows.Shapes.Path
            {
                Stroke = edgeBrush,
                StrokeThickness = 1,
                Opacity = 0.45,
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(_edgeLayer);
            foreach (var (a, b) in pairs) _edges.Add((a, b));

            foreach (var n in _nodes)
            {
                n.R = Math.Min(22, 6 + Math.Sqrt(n.Degree) * 3);   // size carries degree: hubs stand out
                n.Accent = accent;
                n.GroupBrush = ParseBrush(n.GroupHex) ?? accent;
                n.Dot = new Ellipse
                {
                    Width = n.R * 2, Height = n.R * 2,
                    Fill = n.Ghost ? Brushes.Transparent : accent,
                    Stroke = n.Ghost ? ghostBrush : accent,
                    StrokeThickness = n.Ghost ? 1.4 : 0,
                    StrokeDashArray = n.Ghost ? [3, 2] : null,
                    Cursor = n.Ghost ? Cursors.Arrow : Cursors.Hand,
                    ToolTip = n.Ghost
                        ? n.Title + "  (no note yet)"
                        : (n.GroupLeaf.Length > 0 ? n.Title + "\n" + n.GroupLeaf : n.Title),
                    Tag = n,
                    // A small shadow lifts the nodes off the field so the edges read as passing
                    // BEHIND them. Shared and frozen - one effect object for every node, because
                    // an Effect each would be one offscreen surface each.
                    Effect = dotShadow,
                };
                n.Label = new TextBlock
                {
                    Text = n.Title.Length > 28 ? n.Title[..28] + "..." : n.Title,
                    Foreground = n.Ghost ? ghostBrush : textBrush,
                    FontSize = 11, IsHitTestVisible = false,
                };
                // Measured ONCE. The text never changes, so its width never changes.
                n.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // Positioned by transform, never by Canvas.Left/Top - see Paint.
                n.Dot.RenderTransform = n.DotMove;
                n.Label.RenderTransform = n.LabelMove;
                _canvas.Children.Add(n.Dot);
                _canvas.Children.Add(n.Label);
            }
        }

        private double AreaW => Math.Max(80, _canvas.ActualWidth);
        private double AreaH => Math.Max(80, _canvas.ActualHeight);

        // ── The usable box ───────────────────────────────────────────────────────────────
        //
        // ONE definition, shared by the fit and by every arrange. A node is not the whole of what
        // gets drawn: its label hangs BELOW it and spills either side, and the legend owns a strip
        // along the bottom. Centring the nodes therefore does not centre the ink - the drawing
        // ends up sitting high with its labels crowding the legend, which is exactly how a
        // perfectly symmetrical circle still looked off-centre (2026-08-23).
        //
        // So the box below is where NODE CENTRES may go, already inset for everything that hangs
        // off them, and it is deliberately asymmetric: less room needed above a node than below.
        private const double LabelDrop = 18;    // label height under a node
        private const double LegendBand = 26;   // the legend strip along the bottom
        private const double SideRoom = 86;     // half a long label

        private (double L, double T, double R, double B) UsableBox()
        {
            double maxR = _nodes.Count == 0 ? 12 : _nodes.Max(v => v.R);
            // Once the legend has gone its strip belongs to the graph again - this is the single
            // place that has to know, because every arrangement and every fit reads the box.
            double band = _legendGone ? 0 : LegendBand;
            return (SideRoom,
                    maxR + 10,
                    Math.Max(SideRoom + 1, AreaW - SideRoom),
                    Math.Max(maxR + 11, AreaH - band - LabelDrop - maxR));
        }

        /// <summary>Puts the legend back and starts its clock again, from the "?". The band is
        /// re-reserved and the graph re-fits into the shorter box, which is the same move as the
        /// dismissal in reverse.</summary>
        private void RestoreLegend()
        {
            if (!_legendGone) return;
            _legendGone = false;
            _legendHint.Visibility = Visibility.Collapsed;
            _legend.Visibility = Visibility.Visible;
            Anim.FadeIn(_legend);
            FitAllIntoView();
            _legendFade.Start();
        }

        /// <summary>Fades the legend out and hands its strip back to the graph. Idempotent, so a
        /// second call (the timer having already fired) does nothing.</summary>
        private void DismissLegend()
        {
            _legendFade.Stop();
            if (_legendGone || _legend == null) return;
            Anim.FadeOut(_legend, () =>
            {
                // COLLAPSED, not merely transparent: a zero-opacity TextBlock still occupies its
                // corner and would keep the strip reserved in spirit if the margin ever mattered.
                _legend.Visibility = Visibility.Collapsed;
                _legendGone = true;
                // The sentence is not lost, it moves into the "?" that takes the corner over.
                _legendHint.Visibility = Visibility.Visible;
                Anim.FadeIn(_legendHint);
                // Re-fit into the taller box, so the space is actually reclaimed by the drawing
                // rather than left as a blank margin. EaseStep carries the pins along, so this is
                // safe with Hold positions on.
                FitAllIntoView();
            });
        }

        private void Seed()
        {
            double cx = AreaW / 2, cy = AreaH / 2;
            foreach (var n in _nodes)
            {
                // A ring rather than uniform noise: starting every node near the middle makes the
                // first frames an explosion, which reads as broken.
                double a = _rng.NextDouble() * Math.PI * 2;
                double r = 60 + _rng.NextDouble() * Math.Min(cx, cy);
                n.X = cx + Math.Cos(a) * r;
                n.Y = cy + Math.Sin(a) * r;
            }
            if (_nodes.FirstOrDefault(n => n.Id == _focusId) is Node f) { f.X = cx; f.Y = cy; }
            Paint();
        }

        // ── Solve, then ease ─────────────────────────────────────────────────────────────

        /// <summary>Runs the whole layout on a worker and eases into the result. Safe to call as
        /// often as you like: a request made while one is running collapses into a single
        /// follow-up rather than queueing a backlog.</summary>
        private async void Resolve()
        {
            if (_nodes.Count == 0 || _solving) { _solveQueued = _nodes.Count > 0; return; }
            _solving = true;
            try
            {
                do
                {
                    _solveQueued = false;
                    double w = AreaW, h = AreaH;
                    // A GENTLE solve moves only what is CONNECTED to the node that just moved.
                    // Running the global force model even at low temperature gave every node in
                    // the graph a small nudge, so shifting one node twitched things on the far
                    // side of the window for no reason a reader could see (2026-08-23). Freezing
                    // everything else makes the effect local and explainable: the neighbours
                    // rearrange, nothing else does.
                    var index = new Dictionary<Node, int>(_nodes.Count);
                    for (int i = 0; i < _nodes.Count; i++) index[_nodes[i]] = i;
                    var snap = _nodes.Select(v => (v.X, v.Y)).ToArray();
                    var edges = _edges.Select(e => (index[e.A], index[e.B])).ToArray();
                    // The box goes IN. The solver cannot ask the canvas anything from a worker
                    // thread, so the box has to travel with the work.
                    var box = UsableBox();
                    await Task.Run(() => Solve(snap, edges, w, h, box));

                    // ONE FIT, and it happens HERE. The solver used to finish with a fit of its
                    // own against a bare 30px margin, which knew nothing about labels or the
                    // legend - so a solved layout kept putting bottom-row labels off the edge
                    // (2026-08-23). The solver now only decides the shape; the usable box decides
                    // where it goes, and it is the same box every arrange uses.
                    FitInto(_solvedX, _solvedY);
                    for (int i = 0; i < _nodes.Count && i < _solvedX.Length; i++)
                    {
                        _nodes[i].FromX = _nodes[i].X;
                        _nodes[i].FromY = _nodes[i].Y;
                        _nodes[i].ToX = _solvedX[i];
                        _nodes[i].ToY = _solvedY[i];
                    }
                    _easeStart = DateTime.UtcNow;
                    _ease.Start();
                } while (_solveQueued);
            }
            finally { _solving = false; }
        }

        /// <summary>The entire layout, start to finish, on a background thread. Touches no WPF
        /// object - only doubles - which is what makes it safe off the UI thread and fast enough
        /// to finish inside a frame or two.</summary>
        private void Solve((double X, double Y)[] snap, (int A, int B)[] edges,
                           double w, double h, (double L, double T, double R, double B) box)
        {
            int n = snap.Length;
            var x = new double[n];
            var y = new double[n];
            var dx = new double[n];
            var dy = new double[n];
            for (int i = 0; i < n; i++) { x[i] = snap[i].X; y[i] = snap[i].Y; }

            // The ideal separation, CAPPED. Plain sqrt(area/n) is the textbook value and is wrong
            // for a small graph in a big window: 30 nodes maximized asks for ~250px between every
            // pair, repulsion overwhelms the springs, and everything ends up flung against the
            // edges with the middle empty.
            double k = Math.Min(Math.Sqrt(w * h / Math.Max(1, n)) * 0.62, 130);
            // GENTLE is the middle ground between "nothing moves" and "the whole screen
            // rearranges". A drop starts nearly cold and runs a handful of passes, so neighbours
            // tidy up around what was just placed and nothing travels far. A full arrange starts
            // hot and runs to convergence, which is what the menu's Arrange is for (2026-08-23).
            double k2 = k * k;
            // ONE MODE. There used to be a second, "gentle", that ran after every drag to let
            // neighbours tidy up. It was reported as broken three times and patched three times -
            // full re-solve, then low temperature, then neighbours-only - before the obvious
            // answer: a drag is the user placing a node, and no automatic movement they did not
            // ask for is acceptable at that moment. Dragging now moves exactly what is dragged,
            // and this solver only ever runs for an Arrange (2026-08-23).
            double temp = Math.Max(w, h) / 12.0;
            int passes = SolvePasses;
            double cx = (box.L + box.R) / 2, cy = (box.T + box.B) / 2;
            var rng = new Random(4242);

            for (int pass = 0; pass < passes && temp > TempFloor; pass++)
            {
                Array.Clear(dx, 0, n);
                Array.Clear(dy, 0, n);

                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        double ddx = x[i] - x[j], ddy = y[i] - y[j];
                        double d = Math.Sqrt(ddx * ddx + ddy * ddy);
                        if (d < 0.01) { ddx = rng.NextDouble() - 0.5; ddy = rng.NextDouble() - 0.5; d = 0.01; }
                        double f = k2 / d, ux = ddx / d, uy = ddy / d;
                        dx[i] += ux * f; dy[i] += uy * f;
                        dx[j] -= ux * f; dy[j] -= uy * f;
                    }

                foreach (var (a, b) in edges)
                {
                    if (a < 0 || b < 0) continue;
                    double ddx = x[a] - x[b], ddy = y[a] - y[b];
                    double d = Math.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < 0.01) continue;
                    double f = d * d / k, ux = ddx / d, uy = ddy / d;
                    dx[a] -= ux * f; dy[a] -= uy * f;
                    dx[b] += ux * f; dy[b] += uy * f;
                }

                for (int i = 0; i < n; i++)
                {
                    dx[i] += (cx - x[i]) * Gravity;
                    dy[i] += (cy - y[i]) * Gravity;
                    double d = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                    if (d > 0.001)
                    {
                        double step = Math.Min(d, temp);
                        x[i] += dx[i] / d * step;
                        y[i] += dy[i] / d * step;
                    }
                    // A FULL arrange solves in roomier space and is fitted afterwards, so a loose
                    // fence is right there - clamping it to the frame is what produced the
                    // ring-around-the-edge shape, because a node pushed outward sticks to the wall
                    // and repulsion cannot pull it back through the clamp.
                    //
                    // A GENTLE settle has no fit afterwards, so for it the box IS the frame.
                    // Without this a nudge could leave a neighbour parked outside the window.
                    x[i] = Math.Max(-w, Math.Min(w * 2, x[i]));
                    y[i] = Math.Max(-h, Math.Min(h * 2, y[i]));
                }
                temp *= Cool;
            }

            // No fit here. The solver runs off the UI thread and cannot ask the canvas how big it
            // is or how large the labels are; Resolve does the fit on the UI thread instead, so
            // there is exactly one place that decides where a layout is placed.
            _solvedX = x;
            _solvedY = y;
        }

        /// <summary>Centres and scales a solved layout into the usable box. The single placement
        /// step: every path that produces coordinates ends here or in EaseTo, both of which use
        /// UsableBox, so nothing can land outside the window.</summary>
        private void FitInto(double[] x, double[] y)
        {
            int n = Math.Min(x.Length, y.Length);
            if (n == 0) return;
            double minX = x[0], maxX = x[0], minY = y[0], maxY = y[0];
            for (int i = 1; i < n; i++)
            {
                if (x[i] < minX) minX = x[i];
                if (x[i] > maxX) maxX = x[i];
                if (y[i] < minY) minY = y[i];
                if (y[i] > maxY) maxY = y[i];
            }
            var (l, t, r, b) = UsableBox();
            double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
            // NO 1.0 CEILING. Refusing to enlarge meant growing the window left the graph at its
            // old size, marooned as a small cluster in the middle of all that space - which is
            // exactly what a fit is supposed to prevent (2026-08-23). It does cap eventually, so
            // a two-node graph does not become two dots at opposite corners.
            double scale = Math.Min(Math.Min((r - l) / spanX, (b - t) / spanY), MaxFitScale);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double tx = (l + r) / 2, ty = (t + b) / 2;
            for (int i = 0; i < n; i++)
            {
                x[i] = tx + (x[i] - cx) * scale;
                y[i] = ty + (y[i] - cy) * scale;
            }
        }

        /// <summary>THE GUARANTEE THAT EVERYTHING IS VISIBLE. Takes whatever positions the nodes
        /// currently hold - solved, arranged, dragged, pinned, any mixture - and centres and
        /// scales them into the canvas. Ignores pinning entirely, because a pin means "keep your
        /// place relative to the others", not "stay off screen".
        ///
        /// Every path that can change the available room ends here, so there is one answer to
        /// "why is a node outside the window" and it is that this did not run.</summary>
        private void FitAllIntoView()
        {
            if (_nodes.Count == 0) return;
            if (AreaW < 40 || AreaH < 40) return;

            var (l, t, r, b) = UsableBox();
            double minX = _nodes.Min(v => v.X), maxX = _nodes.Max(v => v.X);
            double minY = _nodes.Min(v => v.Y), maxY = _nodes.Max(v => v.Y);
            double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
            double availX = Math.Max(1, r - l), availY = Math.Max(1, b - t);
            double scale = Math.Min(Math.Min(availX / spanX, availY / spanY), MaxFitScale);

            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double tx = (l + r) / 2, ty = (t + b) / 2;
            foreach (var v in _nodes)
            {
                v.FromX = v.X; v.FromY = v.Y;
                v.ToX = tx + (v.X - cx) * scale;
                v.ToY = ty + (v.Y - cy) * scale;
            }
            _easeStart = DateTime.UtcNow;
            _ease.Start();
        }

        private void EaseStep()
        {
            double t = (DateTime.UtcNow - _easeStart).TotalMilliseconds / EaseTime.TotalMilliseconds;
            if (t >= 1)
            {
                foreach (var v in _nodes)
                {
                    v.X = v.ToX; v.Y = v.ToY;
                    // A PINNED NODE'S PIN FOLLOWS IT. Anything that eases a pinned node to a new
                    // place - a resize fit, a re-solve - would otherwise leave the pin at the old
                    // coordinates, and the next drag would start the simulation, which writes
                    // X from FixX and teleports the whole layout back to where it used to be.
                    // One rule here covers every mover, so no caller has to remember it.
                    if (v.FixX is not null) { v.FixX = v.X; v.FixY = v.Y; }
                }
                // The FIRST layout arrives here with nothing pinned, so hold has to be applied
                // once the nodes have somewhere to be held. Cheap, and it keeps one definition of
                // what the toggle means rather than a second one for startup.
                if (_hold) ApplyHold();
                Paint();
                _ease.Stop();     // static from here: an idle graph window costs nothing
                return;
            }
            // Ease-in-out: a slow start reads as the graph deciding to move, rather than
            // everything snapping away from its old position the instant you click.
            double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            foreach (var v in _nodes)
            {
                if (ReferenceEquals(v, _drag)) continue;
                v.X = v.FromX + (v.ToX - v.FromX) * e;
                v.Y = v.FromY + (v.ToY - v.FromY) * e;
            }
            Paint();
        }

        /// <summary>Writes positions onto visuals that already exist. No allocation, no measure.</summary>
        /// <summary>Moves everything for one frame, and does it WITHOUT TOUCHING LAYOUT.
        ///
        /// Canvas.SetLeft/SetTop invalidate the arrange pass for every element they touch, and a
        /// Line's X1/Y1 invalidate its measure - so the old version asked WPF to re-lay-out a
        /// hundred-odd elements on every frame of a drag. That is the remaining jumpiness after
        /// the timing was fixed: the maths was smooth and the presentation was not.
        ///
        /// Instead each node's visuals carry a TranslateTransform, which is a RENDER-time
        /// property - writing it never invalidates layout - and all the edges are one Path whose
        /// geometry is rebuilt per frame. One geometry beats forty elements that each want
        /// measuring.</summary>
        private void Paint()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
                foreach (var (a, b) in _edges)
                {
                    if (a.Dot.Visibility != Visibility.Visible || b.Dot.Visibility != Visibility.Visible) continue;
                    ctx.BeginFigure(new Point(a.X, a.Y), false, false);
                    ctx.LineTo(new Point(b.X, b.Y), true, false);
                }
            g.Freeze();   // frozen: WPF can hand it straight to the render thread
            _edgeLayer.Data = g;

            foreach (var v in _nodes)
            {
                v.DotMove.X = v.X - v.R;
                v.DotMove.Y = v.Y - v.R;
                v.LabelMove.X = v.X - v.Label.DesiredSize.Width / 2;
                v.LabelMove.Y = v.Y + v.R + 2;
            }
        }

        // ── Input ────────────────────────────────────────────────────────────────────────

        private Node? NodeAt(Point p) =>
            _canvas.InputHitTest(p) is Ellipse el && el.Tag is Node n ? n : null;

        // ── Selection ────────────────────────────────────────────────────────────────────
        //
        // The view options are worth far more against a chosen few than against one node at a
        // time: "show only what links here" over three notes is a real question, over one it is a
        // narrow one. Click selects, Ctrl+click adds, and dragging on empty space bands a
        // rectangle (2026-08-23).
        private readonly HashSet<Node> _selected = [];
        private Point _bandFrom;
        private bool _banding;
        private readonly System.Windows.Shapes.Rectangle _band = new()
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            StrokeThickness = 1,
            StrokeDashArray = [3, 2],
        };

        private void SetSelected(Node n, bool on)
        {
            if (on) _selected.Add(n); else _selected.Remove(n);
            // The selection ring goes OUTSIDE the fill rather than replacing it, so a node keeps
            // saying which group it belongs to while it is selected.
            n.Dot.Stroke = on ? Res("SelectionBg", Colors.Yellow) : (n.Ghost ? Res("MutedTextBrush", Colors.Gray) : (Brush)n.Dot.Fill);
            n.Dot.StrokeThickness = on ? 3 : (n.Ghost ? 1.4 : 0);
        }

        private void ClearSelection()
        {
            foreach (var n in _selected.ToList()) SetSelected(n, false);
            _selected.Clear();
        }

        /// <summary>What the menu acts on: the selection when there is one, otherwise just the
        /// node under the pointer. So the menu reads the same whether or not anything is selected.
        /// </summary>
        private List<Node> MenuTargets() =>
            _selected.Count > 0 ? [.. _selected]
            : _menuNode != null ? [_menuNode] : [];

        // ── THE LIVE DRAG, on d3-force's model ───────────────────────────────────────────
        //
        // Ported as BEHAVIOUR, not code. d3 holds a dragged node at a fixed position (fx/fy),
        // keeps the simulation warm at a low fixed alpha for as long as the button is down, and
        // drops alpha to zero on release. The result is the thing every attempt here missed:
        //
        //   Motion happens WHILE YOU HOLD THE NODE, where you can see your own hand causing it,
        //   and stops the instant you let go.
        //
        // Every earlier version ran the layout AFTER the drop, which is why the movement always
        // read as random - it happened when the user was no longer doing anything, so nothing on
        // screen explained it. Same forces, same maths; the difference is entirely when it runs.
        //
        // Two more things taken from d3 rather than invented:
        //   - VELOCITY IS CARRIED between ticks and decayed (velocityDecay), instead of a fresh
        //     displacement per pass clamped to a "temperature". Momentum settles; a clamp lurches.
        //   - ALPHA SCALES THE FORCES rather than capping the step, so a cooling simulation eases
        //     off smoothly instead of moving at a fixed rate and then stopping dead.
        // DRIVEN BY THE FRAME CLOCK, not a timer. A DispatcherTimer fires off the dispatcher
        // queue whenever it gets scheduled - the gap between ticks wobbles, and since the
        // integrator advances by a fixed amount per tick, uneven ticks are literally uneven
        // motion. CompositionTarget.Rendering fires exactly once per composition frame, in step
        // with what is actually being drawn, which is the difference between "roughly 30 times a
        // second" and "every frame" (2026-08-23).
        //
        // The step is also SCALED BY REAL ELAPSED TIME, so a dropped frame produces one larger
        // step rather than a stall followed by a lurch.
        private bool _liveOn;
        private TimeSpan _lastFrame;
        private const double LiveAlpha = 0.28;      // d3's drag alphaTarget is 0.3
        private const double VelocityDecay = 0.6;   // d3's default

        // ── THE DRAG IS LOCAL ────────────────────────────────────────────────────────────
        //
        // Only what you grab and what it is JOINED TO may move; everything else is frozen for
        // the duration of the drag. Letting the whole graph respond meant dragging one node in a
        // circle applied the global force model to a shape that is not a force equilibrium, and
        // the entire ring collapsed inward - which is not "the neighbours got out of the way", it
        // is the layout being silently replaced while you hold a node.
        //
        // One hop, because that is the relationship a reader can actually see on screen: the
        // lines leaving the node you are holding are exactly the things that move.
        private const int LiveHops = 1;
        private readonly HashSet<Node> _liveSet = [];

        /// <summary>Exactly the nodes under the cursor right now. The visualizer suspends pinning
        /// for everything EXCEPT these, so a note you have hold of still tracks your hand while
        /// the rest of the graph drifts around it.</summary>
        private readonly HashSet<Node> _carried = [];

        // ── VISUALIZER ───────────────────────────────────────────────────────────────────
        //
        // The graph as something to leave running rather than something to read. Three things
        // together, none of them useful and that is the point:
        //
        //   BREATHING - the ideal link length is modulated by a slow sine, so the whole graph
        //               expands and contracts and never reaches the equilibrium it would
        //               otherwise settle into after a second. This is the squishy motion the
        //               drag used to produce everywhere, given somewhere it belongs.
        //   ROTATION  - the layout turns about the centre of the box. Coordinates are rotated
        //               rather than the canvas, so the labels stay upright instead of going
        //               round with it and ending up upside down.
        //   NO PINS   - "Pin all Notes" is suspended while this runs. Holding everything still
        //               and then asking it to drift is a contradiction, and the pins are not
        //               cleared, only ignored, so switching the visualizer off puts the graph
        //               straight back under whatever the toggle says.
        private bool _visualizer;
        private double _breath;                       // phase of the expand/contract cycle
        private const double SpinPerSec = 0.10;       // radians, about 60s for a full turn
        private const double BreathPerSec = 0.55;     // radians
        private const double BreathDepth = 0.22;      // +/- share of the ideal link length

        /// <summary>The nodes allowed to move during this drag: the carried set plus everything
        /// within LiveHops edges of it.</summary>
        private void BuildLiveSet(List<Node> carried)
        {
            _liveSet.Clear();
            foreach (var v in carried) _liveSet.Add(v);

            var adj = new Dictionary<Node, List<Node>>(_nodes.Count);
            foreach (var (a, b) in _edges)
            {
                if (!adj.TryGetValue(a, out var la)) adj[a] = la = [];
                if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = [];
                la.Add(b); lb.Add(a);
            }

            var frontier = new List<Node>(carried);
            for (int hop = 0; hop < LiveHops && frontier.Count > 0; hop++)
            {
                var next = new List<Node>();
                foreach (var v in frontier)
                    if (adj.TryGetValue(v, out var ns))
                        foreach (var nb in ns)
                            if (_liveSet.Add(nb)) next.Add(nb);
                frontier = next;
            }
        }

        private void SetVisualizer(bool on)
        {
            _visualizer = on;
            _miSpin?.IsChecked = on;
            if (on)
            {
                // FIT TO A CIRCLE FIRST. The ordinary fit fills the box, and a layout that fills a
                // wide box has corners that leave it the moment it turns - the clamp would then
                // drag those nodes along the edge and the spin would look like it was grinding
                // against the sides. Sizing to the inscribed circle means every rotation of the
                // layout is still inside the box.
                FitForSpin();
                _legendFade.Stop();
                DismissLegend();   // nothing to read while it is just running
                StartLive();
            }
            else
            {
                StopLive();
                // Back under whatever the pin toggle says, at wherever the spin left things.
                ApplyHold();
                Paint();
            }
        }

        /// <summary>Scales the layout about its centroid so that its furthest node sits inside the
        /// largest circle the usable box holds. Rotation is then closed: no angle can put anything
        /// outside.</summary>
        private void FitForSpin()
        {
            if (_nodes.Count == 0) return;
            var (l, t, r, b) = UsableBox();
            double cx = (l + r) / 2, cy = (t + b) / 2;
            double radius = Math.Max(20, Math.Min(r - l, b - t) / 2);
            double far = _nodes.Max(v => Math.Sqrt((v.X - cx) * (v.X - cx) + (v.Y - cy) * (v.Y - cy)));
            double scale = far < 1 ? 1 : Math.Min(radius / far, MaxFitScale);
            foreach (var v in _nodes)
            {
                v.X = cx + (v.X - cx) * scale;
                v.Y = cy + (v.Y - cy) * scale;
                v.VX = 0; v.VY = 0;
            }
            Paint();
        }

        private void StartLive()
        {
            if (_liveOn) return;
            _liveOn = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            // Rendering can fire more than once for the same frame time; skipping the duplicate
            // avoids advancing the simulation twice for one painted frame.
            if (e is not RenderingEventArgs r) return;
            if (r.RenderingTime == _lastFrame) return;
            double dt = _lastFrame == TimeSpan.Zero ? 1
                      : (r.RenderingTime - _lastFrame).TotalMilliseconds / 16.67;
            _lastFrame = r.RenderingTime;
            LiveTick(Math.Max(0.2, Math.Min(3, dt)));   // bounded: a long stall must not explode
        }

        private void LiveTick(double dt)
        {
            int n = _nodes.Count;
            if (n == 0) { StopLive(); return; }
            var (bl, bt, br, bb) = UsableBox();
            double w = AreaW, h = AreaH;
            double k = Math.Min(Math.Sqrt(w * h / n) * 0.62, 130);
            if (_visualizer)
            {
                // Moving the target link length is what keeps the graph alive. Push on the
                // springs instead of on the nodes and the motion stays coherent - the whole
                // structure swells and settles rather than individual nodes twitching.
                _breath += BreathPerSec * dt / 60.0;
                k *= 1 + BreathDepth * Math.Sin(_breath);
            }
            double k2 = k * k;
            // The centre is needed for gravity and for the spin, both of which only run in the
            // visualizer; during a plain drag the box is used solely for the clamp at the end.
            double cx = (bl + br) / 2, cy = (bt + bb) / 2;

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = _nodes[i]; var b = _nodes[j];
                    double dx = a.X - b.X, dy = a.Y - b.Y;
                    double d2 = dx * dx + dy * dy;
                    double d = Math.Sqrt(d2);
                    if (d < 0.01) continue;

                    // A DISTANCE FLOOR on the repulsion. The force is k^2/d^2, which goes to
                    // infinity as two nodes approach - so a pair that drifted close would fire
                    // each other across the window in a single frame. That is the visible
                    // "jumping". d3's manyBody has the same guard (distanceMin); here the floor
                    // is the two radii, since nodes closer than that are overlapping anyway.
                    double floor = a.R + b.R;
                    double eff = Math.Max(d, floor);
                    double f = k2 / (eff * eff) * LiveAlpha * dt;
                    a.VX += dx / d * f; a.VY += dy / d * f;
                    b.VX -= dx / d * f; b.VY -= dy / d * f;

                    // COLLISION, d3's forceCollide. Without it nodes settle on top of each other,
                    // the repulsion above builds up under the floor, and the pair eventually
                    // springs apart - which reads as a random jolt. Resolving the overlap
                    // directly, gently, means it never accumulates.
                    double touch = floor + 6;
                    if (d < touch)
                    {
                        double push = (touch - d) / d * 0.5 * dt;
                        a.VX += dx * push; a.VY += dy * push;
                        b.VX -= dx * push; b.VY -= dy * push;
                    }
                }

            foreach (var (a, b) in _edges)
            {
                double dx = a.X - b.X, dy = a.Y - b.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 0.01) continue;
                double f = (d - k) / d * LiveAlpha * 0.35 * dt;
                a.VX -= dx * f; a.VY -= dy * f;
                b.VX += dx * f; b.VY += dy * f;
            }

            foreach (var v in _nodes)
            {
                // A held node is written from its fixed position and has no velocity - d3's
                // fx/fy branch exactly. This is what makes the node track the cursor precisely
                // while everything else reacts to it.
                // A held node is honoured in BOTH modes. Everything else's pin is ignored while
                // the visualizer runs - suspended, not cleared, so turning it off restores
                // whatever "Pin all Notes" was set to.
                bool honourPin = !_visualizer || _carried.Contains(v);
                if (honourPin && v.FixX is double fx && v.FixY is double fy)
                {
                    v.X = fx; v.Y = fy; v.VX = 0; v.VY = 0;
                    continue;
                }
                // OUTSIDE THE NEIGHBOURHOOD: frozen. Forces from the pair loop above may have
                // landed on it, so the velocity is dropped rather than carried - otherwise it
                // would accumulate silently and fire the node off the moment it did become
                // eligible on some later drag. The visualizer moves everything, so it opts out.
                if (!_visualizer && !_liveSet.Contains(v)) { v.VX = 0; v.VY = 0; continue; }
                // GRAVITY ONLY IN THE VISUALIZER. It is a LAYOUT force - it pulls everything
                // toward the middle - so during a drag it is precisely the inward collapse that
                // had to be fixed, and on a one-hop neighbourhood it would dent the ring toward
                // the centre while you pull a node away from it. Left running with nothing else
                // holding the graph together, though, it is what stops the breathing pushing the
                // whole thing off into the corners.
                if (_visualizer)
                {
                    v.VX += (cx - v.X) * Gravity * LiveAlpha * dt;
                    v.VY += (cy - v.Y) * Gravity * LiveAlpha * dt;
                }
                // Decay raised to dt, so the damping is the same per unit of TIME rather than
                // per tick - otherwise a slow frame damps far less than two fast ones and the
                // motion visibly changes character with the frame rate.
                double decay = Math.Pow(VelocityDecay, dt);
                v.VX *= decay;
                v.VY *= decay;
                v.X += v.VX * dt;
                v.Y += v.VY * dt;
                v.X = Math.Max(bl, Math.Min(br, v.X));
                v.Y = Math.Max(bt, Math.Min(bb, v.Y));
            }

            // SPIN LAST, as a rigid turn of the whole layout about the centre - after the forces
            // rather than mixed into them, so it never fights the springs and the shape you are
            // watching is the shape the simulation made. A node you are holding sits it out, or
            // the graph would be rotating out from under your cursor.
            if (_visualizer)
            {
                double a = SpinPerSec * dt / 60.0;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                foreach (var v in _nodes)
                {
                    if (_carried.Contains(v)) continue;
                    double dx = v.X - cx, dy = v.Y - cy;
                    v.X = cx + dx * ca - dy * sa;
                    v.Y = cy + dx * sa + dy * ca;
                }
            }
            Paint();
        }

        /// <summary>Ends the live simulation and clears every velocity, so nothing drifts on
        /// after the button comes up. d3 lets alpha decay to zero instead; stopping dead is the
        /// behaviour asked for here - a dropped arrangement stays exactly as dropped.</summary>
        private void StopLive()
        {
            if (_liveOn) { CompositionTarget.Rendering -= OnFrame; _liveOn = false; }
            // VELOCITIES ONLY. This used to null every FixX/FixY as well, which quietly undid the
            // pinning the rest of the window depends on: DropDraggedNode's whole promise is that a
            // node stays where it was dropped, and an arrangement pins what it places - both were
            // wiped by the next mouse-up. Releasing the button ends the MOTION; it is not a
            // decision to unpin anything, and treating it as one is what made a hand-placed
            // layout impossible to keep.
            foreach (var v in _nodes) { v.VX = 0; v.VY = 0; }
            _liveSet.Clear();
            _carried.Clear();
        }

        private void CanvasDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            var n = NodeAt(p);
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (n == null)
            {
                // Empty space: start a rubber band. A plain click here also clears the selection,
                // which is what every canvas in every app does.
                if (!ctrl) ClearSelection();
                _banding = true;
                _bandFrom = p;
                _band.Visibility = Visibility.Visible;
                _band.Stroke = Res("SelectionBg", Colors.Yellow);
                _band.Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
                PlaceBand(p, p);
                _canvas.CaptureMouse();
                return;
            }

            if (e.ClickCount == 2) { OpenNode(n); return; }

            if (ctrl) SetSelected(n, !_selected.Contains(n));
            else if (!_selected.Contains(n)) { ClearSelection(); SetSelected(n, true); }

            // ARMED ONLY. Nothing is pinned, no simulation starts and no transition is cancelled
            // until the pointer actually moves (BeginDrag, from CanvasMove). Everything this
            // gesture does up to here is selection, which must leave the layout untouched.
            _drag = n;
            _dragFrom = p;
            _dragging = false;
            _canvas.CaptureMouse();
        }

        /// <summary>The moment a press becomes a drag. Fires once, from the first move that clears
        /// the system drag threshold.</summary>
        private void BeginDrag()
        {
            _dragging = true;
            // FIX every node being carried, then run the simulation for as long as the button is
            // held. This is the whole d3 drag idiom: held nodes are pinned to the pointer, the
            // rest respond live.
            foreach (var v in DragSet()) { v.FixX = v.X; v.FixY = v.Y; }
            // Only ONE thing may write positions at a time. Grabbing a node during an Arrange
            // transition abandons the transition where it stands rather than letting two timers
            // fight over every node's coordinates.
            _ease.Stop();
            // Only worth running if something is actually free to react. After a deliberate
            // arrangement every node is pinned, so the simulation would spend a frame confirming
            // that nothing moved; CanvasMove paints the carried nodes itself, so the drag is
            // identical without it.
            _carried.Clear();
            foreach (var v in DragSet()) _carried.Add(v);
            BuildLiveSet(DragSet());
            if (_visualizer) return;   // already running, and it moves everything anyway
            // Worth running only if something in the NEIGHBOURHOOD is actually free to react.
            // With hold on, or on an unconnected node, there is nothing to simulate and
            // CanvasMove paints the carried nodes itself.
            if (_liveSet.Any(v => v.FixX is null)) StartLive();
        }

        /// <summary>The nodes a drag carries: the whole selection when the grabbed node is part
        /// of it, otherwise just the grabbed node.</summary>
        private List<Node> DragSet() =>
            _drag == null ? []
            : _selected.Count > 1 && _selected.Contains(_drag) ? [.. _selected]
            : [_drag];

        private void CanvasMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_banding) { PlaceBand(_bandFrom, p); return; }
            if (_drag == null) return;

            // Below the threshold this is still a click being held, so the layout stays frozen.
            // Windows' own drag distances, so it matches every other drag on the machine rather
            // than inventing a feel.
            if (!_dragging)
            {
                if (Math.Abs(p.X - _dragFrom.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(p.Y - _dragFrom.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                BeginDrag();
            }
            // Moves the FIXED positions, not the live ones: the tick writes X/Y from these, so
            // the carried nodes track the cursor exactly while everything else reacts.
            double ddx = p.X - _drag.X, ddy = p.Y - _drag.Y;
            foreach (var v in DragSet()) { v.FixX = v.X + ddx; v.FixY = v.Y + ddy; }
            // Paint immediately as well, so the node under the cursor never lags the pointer by
            // a tick even if the simulation is a frame behind.
            foreach (var v in DragSet())
            {
                if (v.FixX is double fx) v.X = fx;
                if (v.FixY is double fy) v.Y = fy;
            }
            Paint();
        }

        private void PlaceBand(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            _band.Width = Math.Abs(a.X - b.X);
            _band.Height = Math.Abs(a.Y - b.Y);
            Canvas.SetLeft(_band, x);
            Canvas.SetTop(_band, y);
        }

        private void FinishBand(Point to)
        {
            _banding = false;
            _band.Visibility = Visibility.Collapsed;
            var r = new Rect(new Point(Math.Min(_bandFrom.X, to.X), Math.Min(_bandFrom.Y, to.Y)),
                             new Size(Math.Abs(_bandFrom.X - to.X), Math.Abs(_bandFrom.Y - to.Y)));
            if (r.Width < 3 && r.Height < 3) return;   // a click, not a band
            foreach (var v in _nodes)
                if (v.Dot.Visibility == Visibility.Visible && r.Contains(new Point(v.X, v.Y)))
                    SetSelected(v, true);
        }

        /// <summary>Ends a drag and PINS the node where it was dropped. Without the pin the layout
        /// pulled it straight back to where the forces wanted it, which is the "I move something
        /// and it goes back" that was reported. A pinned node also anchors its neighbours, so
        /// arranging a graph by hand actually holds.</summary>
        private void DropDraggedNode()
        {
            if (_drag == null) return;
            bool moved = _dragging;
            // Captured BEFORE _drag is cleared - DragSet reads _drag, so afterwards it is empty.
            var carried = DragSet();
            _drag = null;
            _dragging = false;
            _canvas.ReleaseMouseCapture();
            _carried.Clear();
            // A press that never moved pinned nothing and started nothing, so there is nothing to
            // unwind - and crucially no StopLive/Paint that could nudge the layout.
            if (!moved) return;
            // The visualizer owns the simulation for as long as it is on; a drop must not stop it.
            if (_visualizer)
            {
                if (!_hold) foreach (var v in carried) { v.FixX = null; v.FixY = null; }
                return;
            }
            // STOPS DEAD. d3 lets alpha decay to zero over a second or so after release; here it
            // ends immediately, because post-release movement is the exact thing that was
            // reported as wrong three times running - it happens when the user is no longer
            // doing anything, so nothing on screen accounts for it.
            //
            // Everything the drag rearranged stays exactly where it ended up.
            StopLive();

            // With HOLD OFF the carried nodes rejoin the simulation instead of staying pinned
            // where they were dropped. Keeping the pin is right when the user has asked for
            // things to stay put, and wrong when they have asked for a live graph - it made
            // "by connection" get progressively stickier with every drag until nothing reacted
            // any more, which is a mode quietly turning into the other mode.
            if (!_hold)
                foreach (var v in carried) { v.FixX = null; v.FixY = null; }
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            // Esc is "show everything" and NOTHING ELSE. It used to fall through to closing the
            // window, which contradicted the menu row that advertises it as Show everything and
            // meant the key for undoing a view could also throw the window away (2026-08-23).
            // This window is meant to stay open beside the notes; it closes by its X, like the
            // SketchPad and the Dictation pad.
            if (e.Key == Key.Escape)
            {
                ClearIsolation();
                ClearSelection();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                foreach (var v in _nodes.Where(v => v.Dot.Visibility == Visibility.Visible)) SetSelected(v, true);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                CopyMenuNodeLink();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.R) { ArrangeForce(); e.Handled = true; return; }
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == 0) { ArrangeCircle(); e.Handled = true; return; }
            if (e.Key == Key.G) { ArrangeGrid(); e.Handled = true; return; }
            if (e.Key == Key.B) { ArrangeByGroup(); e.Handled = true; return; }
            if (e.Key == Key.T) { ArrangeOutward(); e.Handled = true; return; }
            if (e.Key == Key.L) { _miLabels.IsChecked = !_miLabels.IsChecked; ApplyLabelVisibility(); e.Handled = true; return; }
            if (e.Key == Key.P) { SetHold(!_hold); e.Handled = true; return; }
            if (e.Key == Key.V) { SetVisualizer(!_visualizer); e.Handled = true; return; }
            var n = _menuNode ?? _drag;
            if (n == null) return;
            switch (e.Key)
            {
                case Key.Enter: OpenNode(n); e.Handled = true; break;
            }
        }

        private void OpenNode(Node n)
        {
            if (n.Ghost) { _create(n.Title); Close(); return; }
            _open(n.Id);
            Close();
        }

        // ── Node context menu ────────────────────────────────────────────────────────────
        //
        // Built once and retargeted per node, rather than a menu per node: 500 notes would
        // otherwise mean 500 live ContextMenu objects for a feature used one node at a time.

        private ContextMenu _menu = null!;
        private MenuItem _miOpen = null!, _miCopy = null!, _miIsolate = null!, _miDistance = null!;

        private void BuildNodeMenu()
        {
            _menu = new ContextMenu();
            _miOpen = MenuRow("", Str("Str_Ctx_GraphOpen", "Open note"), "Enter", () => { if (_menuNode != null) OpenNode(_menuNode); });
            _miCopy = MenuRow("", Str("Str_Ctx_GraphCopy", "Copy as [[link]]"), "Ctrl+C", CopyMenuNodeLink);
            // NO PIN ROW. Pinning was an internal flag - "may the layout move this node" - and
            // putting it in the menu meant the reader had to know there was a physics simulation
            // with a per-node frozen bit before the row made any sense. It also had no honest
            // behaviour: unpin either rearranged the whole graph, which the word does not suggest,
            // or waited for the next layout run and so appeared to do nothing at all (2026-08-23).
            //
            // What replaced it needs no explaining: a node you drag stays where you drop it, and
            // Arrange is the one command that moves things.
            _miIsolate = MenuRow("", Str("Str_Ctx_GraphIsolate", "Show only what links here"), "F", IsolateMenuNode);
            // "Arrange by distance" belongs HERE, on the note, not in the canvas menu. It lays the
            // graph out in rings by how many links each note is from ONE note - so it is
            // meaningless without a note chosen, and in the canvas menu it had to be titled
            // "Rings by distance from one note" just to explain which note it meant. With a note
            // under the cursor the name is obvious and short (2026-08-23).
            // ArrangeRow, not MenuRow: it takes a DRAWN icon, and this row is an arrange, so it
            // gets the same hub-and-ring miniature the other arrange rows use. On MenuRow it had
            // no icon at all and sat blank beside three rows that had one.
            _miDistance = ArrangeRow(Str("Str_Ctx_GraphDistance", "Arrange by distance"), "T",
                                     IconArrangeTree(), ArrangeOutward);
            _menu.Items.Add(_miOpen);
            _menu.Items.Add(_miCopy);
            _menu.Items.Add(new Separator());
            _menu.Items.Add(_miIsolate);
            _menu.Items.Add(_miDistance);

            // ── The canvas menu: display options, for a right-click on empty space ──────
            // Right-clicking the background used to do nothing at all, which is a dead gesture on
            // the largest target in the window (2026-08-23).
            _canvasMenu = new ContextMenu();
            // ARRANGE, with real alternatives. Force-directed answers "what is the shape of this",
            // but it is bad at "read every title in order" - two questions, two layouts. Circle
            // and grid are deterministic, so they are also the way to get a graph back to
            // something legible after it has been dragged into a mess.
            _miArrange = new MenuItem { Header = Str("Str_Ctx_GraphArrange", "Arrange") };
            _miArrange.Items.Add(ArrangeRow(Str("Str_Ctx_GraphArrangeForce", "By connection"), "R", IconArrangeForce(), ArrangeForce));
            _miArrange.Items.Add(ArrangeRow(Str("Str_Ctx_GraphArrangeCircle", "In a circle"), "C", IconArrangeCircle(), ArrangeCircle));
            _miArrange.Items.Add(ArrangeRow(Str("Str_Ctx_GraphArrangeGrid", "In a grid"), "G", IconArrangeGrid(), ArrangeGrid));
            // No separator. Grouping these into "shapes" and "meanings" was a distinction only I
            // could see; five rows do not need dividing (2026-08-23).
            _miArrange.Items.Add(ArrangeRow(Str("Str_Ctx_GraphArrangeGroups", "By group"), "B", IconArrangeGroups(), ArrangeByGroup));
            _miColor = new MenuItem { Header = Str("Str_Ctx_GraphColor", "Color by group"), IsCheckable = true, IsChecked = true };
            _miColor.Click += (_, _) => ApplyNodeColors();
            _miLabels = new MenuItem { Header = Str("Str_Ctx_GraphLabels", "Show labels"), IsCheckable = true, IsChecked = true, InputGestureText = "L" };
            _miLabels.Click += (_, _) => ApplyLabelVisibility();
            _miGhosts = new MenuItem { Header = Str("Str_Ctx_GraphGhosts", "Show notes not written yet"), IsCheckable = true, IsChecked = true };
            _miGhosts.Click += (_, _) => ApplyGhostVisibility();
            _miHold = new MenuItem
            {
                Header = Str("Str_Ctx_GraphHold", "Pin all Notes"),
                IsCheckable = true,
                IsChecked = _hold,
                InputGestureText = "P",
                ToolTip = Str("Str_TT_GraphHold", "Keep every note where it is put. Off, dragging a note makes the notes it links to move out of the way."),
            };
            _miHold.Click += (_, _) => SetHold(_miHold.IsChecked);
            _miSpin = new MenuItem
            {
                Header = Str("Str_Ctx_GraphSpin", "Visualizer"),
                IsCheckable = true,
                IsChecked = false,
                InputGestureText = "V",
                ToolTip = Str("Str_TT_GraphSpin", "Let the graph drift and turn on its own. Pinning is suspended while it runs."),
            };
            _miSpin.Click += (_, _) => SetVisualizer(_miSpin.IsChecked);
            _miShowAll = MenuRow("", Str("Str_Ctx_GraphShowAll", "Show everything"), "Esc",
                () => { ClearIsolation(); ClearSelection(); });

            _canvasMenu.Items.Add(_miArrange);
            _canvasMenu.Items.Add(_miShowAll);
            _canvasMenu.Items.Add(new Separator());
            _canvasMenu.Items.Add(_miSpin);
            _canvasMenu.Items.Add(_miHold);
            _canvasMenu.Items.Add(_miColor);
            _canvasMenu.Items.Add(_miLabels);
            _canvasMenu.Items.Add(_miGhosts);

            // OPENED BY HAND, and not through the ContextMenu property. Two reasons, both of which
            // cost a round trip on 2026-08-23:
            //
            //   1. A Canvas with a null Background is NOT HIT-TESTABLE. Right-clicking the empty
            //      field never reached the canvas at all, so no menu could open there however it
            //      was wired. The canvas is given a transparent background in its constructor for
            //      exactly this - transparent is hit-testable, null is not.
            //   2. Swapping _canvas.ContextMenu inside ContextMenuOpening is too late; WPF has
            //      already decided which menu it is opening. Choosing here, before anything opens,
            //      is deterministic.
            _canvas.MouseRightButtonUp += (_, e) =>
            {
                var p = e.GetPosition(_canvas);
                _menuNode = NodeAt(p);
                var menu = _menuNode == null ? _canvasMenu : _menu;
                if (_menuNode != null)
                {
                    // RIGHT-CLICK SELECTS, the way it does in every list and canvas: a node that
                    // is not already part of the selection becomes the selection, and a node that
                    // IS part of it leaves the selection alone - so right-clicking one of several
                    // chosen nodes still acts on all of them. Without this the menu could act on
                    // a node with no visible sign of which one (2026-08-23).
                    if (!_selected.Contains(_menuNode))
                    {
                        ClearSelection();
                        SetSelected(_menuNode, true);
                    }
                    _miOpen.Header = _menuNode.Ghost
                        ? Str("Str_Ctx_GraphCreate", "Create this note")
                        : Str("Str_Ctx_GraphOpen", "Open note");
                }
                menu.PlacementTarget = _canvas;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
                e.Handled = true;
            };
        }

        private ContextMenu _canvasMenu = null!;
        private MenuItem _miArrange = null!, _miShowAll = null!;
        private MenuItem _miColor = null!, _miLabels = null!, _miGhosts = null!, _miHold = null!, _miSpin = null!;

        // ── Hold positions ───────────────────────────────────────────────────────────────
        //
        // The one switch that decides whether dragging a node disturbs anything else. It used to
        // be inferred from the arrangement - deliberate layouts held, "by connection" reacted -
        // which was a rule nobody could see and nobody chose.
        //
        //   ON  - every node is pinned. Dragging moves only what you are carrying, in EVERY
        //         arrangement. A circle stays a circle.
        //   OFF - the d3 idiom: held nodes follow the cursor, free neighbours move out of the
        //         way, and a dropped node rejoins the simulation rather than staying put.
        //
        // Picking an arrangement always places nodes regardless: the solver never consults pins,
        // and EaseStep re-syncs them afterwards. So the toggle governs DRAGGING, not layout.
        // Defaults ON, because "what I put somewhere stays there" is the assumption people bring
        // to a canvas, and remembered per app.
        private bool _hold = App.GetSetting("GraphHoldPositions") != "0";

        private void SetHold(bool on)
        {
            _hold = on;
            App.SetSetting("GraphHoldPositions", on ? "1" : "0");
            _miHold?.IsChecked = on;
            ApplyHold();
        }

        /// <summary>Pins every node where it currently sits, or releases them all. Called on every
        /// change of the toggle, so the switch takes effect on the layout already on screen rather
        /// than only on the next arrange.</summary>
        private void ApplyHold()
        {
            foreach (var v in _nodes)
            {
                if (_hold) { v.FixX = v.X; v.FixY = v.Y; }
                else { v.FixX = null; v.FixY = null; }
            }
        }

        /// <summary>Paints every node by its group's sidebar color, or all in the accent. The
        /// colors are the ones already on the group headers, so this needs no new palette and the
        /// sidebar itself is the legend.</summary>
        private void ApplyNodeColors()
        {
            bool byGroup = _miColor.IsChecked;
            foreach (var v in _nodes)
            {
                if (v.Ghost) continue;   // a ghost belongs to no group; it stays an outline
                var b = byGroup ? v.GroupBrush : v.Accent;
                v.Dot.Fill = b;
                // Never overwrite a selection ring - recoloring while something is selected would
                // silently drop the one thing telling you what you had chosen.
                if (!_selected.Contains(v)) v.Dot.Stroke = b;
            }
        }

        private void ApplyLabelVisibility()
        {
            bool on = _miLabels.IsChecked;
            foreach (var v in _nodes)
                if (v.Dot.Visibility == Visibility.Visible)
                    v.Label.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyGhostVisibility()
        {
            bool on = _miGhosts.IsChecked;
            foreach (var v in _nodes.Where(v => v.Ghost))
            {
                v.Dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                v.Label.Visibility = on && _miLabels.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            }
            Paint();   // the edge geometry drops any edge whose endpoints are not both visible
        }

        private static MenuItem ArrangeRow(string header, string gesture, UIElement icon, Action onClick)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture, Icon = icon };
            mi.Click += (_, _) => onClick();
            return mi;
        }

        // ── Arrange icons, DRAWN ─────────────────────────────────────────────────────────
        //
        // Each one is a miniature of the layout it performs, which says more than any stock glyph
        // would. Drawn rather than picked from Segoe MDL2 for the family reason: a glyph is only
        // confirmed by rendering it, and these are shapes nobody has to guess at.
        private static UIElement ArrangeIcon(IEnumerable<Point> dots, IEnumerable<(Point A, Point B)>? links = null)
        {
            var c = new Canvas { Width = 14, Height = 14 };
            var ink = Res("TextBrush", Colors.White);
            if (links != null)
                foreach (var (a, b) in links)
                    c.Children.Add(new Line
                    {
                        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                        Stroke = ink, StrokeThickness = 1, Opacity = 0.65,
                    });
            foreach (var p in dots)
            {
                var d = new Ellipse { Width = 3.6, Height = 3.6, Fill = ink };
                Canvas.SetLeft(d, p.X - 1.8);
                Canvas.SetTop(d, p.Y - 1.8);
                c.Children.Add(d);
            }
            return c;
        }

        /// <summary>Three nodes joined by edges - the shape a force layout makes.</summary>
        private static UIElement IconArrangeForce()
        {
            var a = new Point(3, 4); var b = new Point(11, 2.5); var d = new Point(7, 11.5);
            return ArrangeIcon([a, b, d], [(a, b), (a, d), (b, d)]);
        }

        /// <summary>Six nodes on a ring.</summary>
        private static UIElement IconArrangeCircle()
        {
            var pts = new List<Point>();
            for (int i = 0; i < 6; i++)
            {
                double ang = i / 6.0 * Math.PI * 2 - Math.PI / 2;
                pts.Add(new Point(7 + Math.Cos(ang) * 4.8, 7 + Math.Sin(ang) * 4.8));
            }
            return ArrangeIcon(pts);
        }

        /// <summary>A hub with a ring around it - the arrange-by-distance shape.</summary>
        private static UIElement IconArrangeTree()
        {
            var mid = new Point(7, 7);
            var pts = new List<Point> { mid };
            var links = new List<(Point, Point)>();
            for (int i = 0; i < 5; i++)
            {
                double a = i / 5.0 * Math.PI * 2 - Math.PI / 2;
                var p = new Point(7 + Math.Cos(a) * 5, 7 + Math.Sin(a) * 5);
                pts.Add(p);
                links.Add((mid, p));
            }
            return ArrangeIcon(pts, links);
        }

        /// <summary>Nine nodes in rows and columns.</summary>
        private static UIElement IconArrangeGrid()
        {
            var pts = new List<Point>();
            for (int r = 0; r < 3; r++)
                for (int col = 0; col < 3; col++)
                    pts.Add(new Point(3 + col * 4, 3 + r * 4));
            return ArrangeIcon(pts);
        }

        /// <summary>The force layout, from scratch. Unpins everything first: an arrange that
        /// honored the pins would leave the mess it was asked to clean up.</summary>
        private void ArrangeForce()
        {
            // The solver ignores pins, so this lays out the same either way; ApplyHold decides
            // what happens AFTERWARDS, and EaseStep carries each pin to wherever its node lands.
            if (_visualizer) SetVisualizer(false);   // same reason as EaseTo: one writer at a time
            Resolve();
        }

        /// <summary>Every node on one ring, ordered by group then title, so a notebook reads
        /// around the edge and the links draw as chords across the middle. Deterministic, which
        /// is what makes it a way back from a graph that has been dragged into knots.</summary>
        private void ArrangeCircle()
        {
            var order = _nodes.OrderBy(v => v.Group, StringComparer.OrdinalIgnoreCase)
                              .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase).ToList();
            // Centred in the USABLE box, which is already inset for the labels and the legend.
            var (bl, bt, br, bb) = UsableBox();
            double cx = (bl + br) / 2, cy = (bt + bb) / 2;
            double r = Math.Max(50, Math.Min((br - bl) / 2, (bb - bt) / 2));
            EaseTo(order, (i, n) =>
            {
                double a = i / (double)Math.Max(1, n) * Math.PI * 2 - Math.PI / 2;
                return new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
            });
        }

        /// <summary>Rows and columns, grouped and alphabetical. The layout for scanning titles
        /// rather than for seeing structure.</summary>
        private void ArrangeGrid()
        {
            var order = _nodes.OrderBy(v => v.Group, StringComparer.OrdinalIgnoreCase)
                              .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase).ToList();
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(order.Count * (AreaW / Math.Max(1, AreaH)))));
            int rows = Math.Max(1, (int)Math.Ceiling(order.Count / (double)cols));
            var (bl, bt, br, bb) = UsableBox();
            double stepX = cols <= 1 ? 0 : (br - bl) / (cols - 1);
            double stepY = rows <= 1 ? 0 : (bb - bt) / (rows - 1);
            double x0 = cols <= 1 ? (bl + br) / 2 : bl;
            double y0 = rows <= 1 ? (bt + bb) / 2 : bt;
            EaseTo(order, (i, _) => new Point(x0 + i % cols * stepX, y0 + i / cols * stepY));
        }

        /// <summary>One cluster per group, the clusters themselves laid out on a ring. This is the
        /// layout that answers "how is my notebook actually organised" - and it shows up the notes
        /// that link across group boundaries, which are the interesting ones.</summary>
        private void ArrangeByGroup()
        {
            var groups = _nodes.GroupBy(v => v.Ghost ? "￿" : v.Group, StringComparer.OrdinalIgnoreCase)
                               .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                               .ToList();
            if (groups.Count == 0) return;

            var (bl, bt, br, bb) = UsableBox();
            double cx = (bl + br) / 2, cy = (bt + bb) / 2;
            // One ring of clusters, each cluster its own little ring. A single group must not sit
            // out on the rim of a circle of one - it belongs in the middle.
            double ringR = groups.Count == 1 ? 0 : Math.Max(60, Math.Min((br - bl) / 2, (bb - bt) / 2) * 0.62);
            var placed = new List<Node>();
            var points = new List<Point>();

            for (int gi = 0; gi < groups.Count; gi++)
            {
                double ga = gi / (double)groups.Count * Math.PI * 2 - Math.PI / 2;
                double gx = cx + Math.Cos(ga) * ringR;
                double gy = cy + Math.Sin(ga) * ringR;
                var members = groups[gi].OrderBy(v => v.Title, StringComparer.OrdinalIgnoreCase).ToList();
                // Cluster radius grows with how many are in it, so a big group does not overlap
                // itself and a group of one is a single dot at the cluster's centre.
                double cr = members.Count <= 1 ? 0 : Math.Min(96, 16 + members.Count * 5.5);
                for (int i = 0; i < members.Count; i++)
                {
                    double a = i / (double)members.Count * Math.PI * 2;
                    placed.Add(members[i]);
                    points.Add(new Point(gx + Math.Cos(a) * cr, gy + Math.Sin(a) * cr));
                }
            }
            EaseTo(placed, (i, _) => points[i]);
        }

        /// <summary>Rings outward from one note: its direct links on the first ring, theirs on the
        /// next, and so on. The layout for "what is reachable from here, and how far away is it" -
        /// which is the question a second brain is actually for. Starts from the selection when
        /// there is one, otherwise from the most-linked note.</summary>
        private void ArrangeOutward()
        {
            if (_nodes.Count == 0) return;
            var root = _selected.FirstOrDefault()
                       ?? _menuNode
                       ?? _nodes.OrderByDescending(v => v.Degree).First();

            // Breadth-first, so a node's ring IS its distance from the root in links.
            var depth = new Dictionary<Node, int> { [root] = 0 };
            var queue = new Queue<Node>();
            queue.Enqueue(root);
            var neighbours = new Dictionary<Node, List<Node>>();
            foreach (var (a, b) in _edges)
            {
                if (!neighbours.TryGetValue(a, out var la)) neighbours[a] = la = [];
                if (!neighbours.TryGetValue(b, out var lb)) neighbours[b] = lb = [];
                la.Add(b); lb.Add(a);
            }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!neighbours.TryGetValue(cur, out var adj)) continue;
                foreach (var nx in adj)
                    if (!depth.ContainsKey(nx)) { depth[nx] = depth[cur] + 1; queue.Enqueue(nx); }
            }
            // Anything the root cannot reach goes on an outer ring of its own rather than being
            // dropped - an unreachable note is information too.
            int maxDepth = depth.Count == 0 ? 0 : depth.Values.Max();
            foreach (var v in _nodes) if (!depth.ContainsKey(v)) depth[v] = maxDepth + 1;

            var (bl, bt, br, bb) = UsableBox();
            double cx = (bl + br) / 2, cy = (bt + bb) / 2;
            int rings = Math.Max(1, depth.Values.Max());
            double step = Math.Min((br - bl) / 2, (bb - bt) / 2) / rings;

            var placed = new List<Node>();
            var points = new List<Point>();
            foreach (var ring in depth.GroupBy(kv => kv.Value).OrderBy(g => g.Key))
            {
                var members = ring.Select(kv => kv.Key)
                                  .OrderBy(v => v.Group, StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase).ToList();
                if (ring.Key == 0) { placed.Add(members[0]); points.Add(new Point(cx, cy)); continue; }
                double rr = step * ring.Key;
                for (int i = 0; i < members.Count; i++)
                {
                    double a = i / (double)members.Count * Math.PI * 2 - Math.PI / 2;
                    placed.Add(members[i]);
                    points.Add(new Point(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr));
                }
            }
            EaseTo(placed, (i, _) => points[i]);
        }

        /// <summary>Three clusters - the by-group shape.</summary>
        private static UIElement IconArrangeGroups()
        {
            var pts = new List<Point>();
            foreach (var c in new[] { new Point(4, 4), new Point(10.5, 4.5), new Point(6.5, 10.5) })
            {
                pts.Add(new Point(c.X - 1.4, c.Y - 1.2));
                pts.Add(new Point(c.X + 1.4, c.Y - 1.2));
                pts.Add(new Point(c.X, c.Y + 1.5));
            }
            return ArrangeIcon(pts);
        }


        /// <summary>Sends an explicit arrangement through the same ease the solver uses, and pins
        /// everything: a deliberate arrangement is not a starting guess for the physics to undo.
        /// </summary>
        private void EaseTo(List<Node> order, Func<int, int, Point> place)
        {
            if (order.Count == 0) return;
            // Asking for an arrangement is asking for the graph to hold a shape, which the
            // visualizer is the opposite of - and two writers on the same coordinates fight.
            if (_visualizer) SetVisualizer(false);

            // NORMALISED into the usable box, whatever the arrangement asked for. Circle and grid
            // already span the box, but "by group" and "outward" size themselves from how many
            // nodes and rings there are - so on a big window they came out as a small cluster
            // marooned in the middle (2026-08-23). Fitting here means every arrangement fills the
            // window, and each one only has to get the SHAPE right.
            var xs = new double[order.Count];
            var ys = new double[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                var p = place(i, order.Count);
                xs[i] = p.X; ys[i] = p.Y;
            }
            FitInto(xs, ys);

            for (int i = 0; i < order.Count; i++)
            {
                order[i].FromX = order[i].X;
                order[i].FromY = order[i].Y;
                order[i].ToX = xs[i];
                order[i].ToY = ys[i];
                // Pin what we place WHEN HOLD IS ON. Unpinned, the first tick of the live
                // simulation applies charge, link and gravity forces to a shape that is not a
                // force equilibrium, and a circle collapses into a blob - which is the whole
                // reason the toggle exists. With hold off that reaction is what was asked for.
                order[i].FixX = _hold ? xs[i] : (double?)null;
                order[i].FixY = _hold ? ys[i] : (double?)null;
            }
            _easeStart = DateTime.UtcNow;
            _ease.Start();
        }

        private static string Str(string key, string fallback) =>
            Application.Current?.TryFindResource(key) as string ?? fallback;

        private static MenuItem MenuRow(string glyph, string header, string gesture, Action onClick, string? tip = null)
        {
            var mi = new MenuItem { Header = header, Icon = glyph, InputGestureText = gesture };
            if (tip != null) mi.ToolTip = tip;
            mi.Click += (_, _) => onClick();
            return mi;
        }

        /// <summary>Copies every selected title as wikilinks, one per line - so a rubber band
        /// round a cluster becomes a block of links ready to paste into an index note, which is
        /// the whole reason to select several at once.</summary>
        private void CopyMenuNodeLink()
        {
            var targets = MenuTargets();
            if (targets.Count == 0) return;
            string text = string.Join(Environment.NewLine,
                targets.Select(t => WikiLinks.Wrap(t.Title)));
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.COMException) { /* another app holds the clipboard */ }
        }

        /// <summary>Hides everything that is not this node or a direct neighbour. The graph of a
        /// whole notebook answers "what is the shape of this"; isolating answers "what touches
        /// THIS", which is the question you actually have with a node under the pointer.</summary>
        private void IsolateMenuNode()
        {
            var seeds = MenuTargets();
            if (seeds.Count == 0) { ClearIsolation(); return; }
            var seedSet = new HashSet<Node>(seeds);
            var keep = new HashSet<Node>(seeds);
            foreach (var (a, b) in _edges)
            {
                if (seedSet.Contains(a)) keep.Add(b);
                if (seedSet.Contains(b)) keep.Add(a);
            }
            foreach (var v in _nodes)
            {
                bool on = keep.Contains(v);
                v.Dot.Visibility = v.Label.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            // Edges need no hiding of their own now: they are one geometry, and Paint skips any
            // edge whose endpoints are not both visible.
            Paint();
            _isolated = true;
        }

        private bool _isolated;

        private void ClearIsolation()
        {
            if (!_isolated) return;
            foreach (var v in _nodes) v.Dot.Visibility = v.Label.Visibility = Visibility.Visible;
            _isolated = false;
            ApplyGhostVisibility();   // the ghost toggle still owns whether those are shown
            Paint();
        }

        /// <summary>Node and edge counts, for the caller's status line.</summary>
        private bool _closeFaded;

        /// <summary>Cancel the first close, fade out, then close for real - the shared pattern
        /// from Anim, the same one the SketchPad and every dialog use.</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        // NO MaxWidth/MaxHeight CLAMP. Constraining the window to the work area was an attempt to
        // stop a chromeless window maximizing past the taskbar, and it backfired: WPF applies the
        // maximum during the maximize itself, so snapping and maximizing were both blocked
        // (2026-08-23). WindowChrome already handles the maximized bounds - the SketchPad relies
        // on exactly that and has never needed a clamp. If a maximized window ever does overhang
        // again, the fix is WM_GETMINMAXINFO, not a WPF maximum.

        public string Summary =>
            string.Format(CultureInfo.CurrentCulture, "{0} notes, {1} links", _nodes.Count, _edges.Count);

        public bool IsEmpty => _nodes.Count == 0;
    }
}
