using System;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    // Steve, 2026-11-21: bringing the window back from the tray takes forever. This times what the tray icon does
    // (ShowFromTray after a hide) until the window has drawn twice, on Trash Polka and on Dark, three alternating
    // passes each, and counts the ink work done in the restore. Set KN_INK_SHOTS to a folder to keep the numbers.
    [Collection("Application resources")]
    public sealed class TrayRestoreTimingTests
    {
        [Fact]
        public void ComingBackFromTheTrayDrawsWithinASecond() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            MarkdownAuthoringTests.Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            if (!window.IsVisible) window.Show();
            var report = new StringBuilder();
            double worst = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var theme in new[] { Theme.TrashPolka, Theme.Dark })
                {
                    Application.Current.Resources.MergedDictionaries[0] = ThemeHarness.Create(theme, theme == Theme.TrashPolka ? Accent.Red : Accent.Blue);
                    Settle(window, 1500);
                    window.Hide();
                    Settle(window, 800);
                    int composes = ThemeMotif.ComposeCalls, raster = ThemeMotif.RasterPaints, vector = ThemeMotif.VectorPaints;
                    double composeMs = ThemeMotif.ComposeMs;
                    var clock = Stopwatch.StartNew();
                    window.ShowFromTray();
                    double shown = clock.Elapsed.TotalMilliseconds;
                    WaitForFrames(window, 2);
                    double drawn = clock.Elapsed.TotalMilliseconds;
                    if (theme == Theme.TrashPolka) worst = Math.Max(worst, drawn);
                    report.AppendLine($"pass {pass} {theme}: show {shown:0} ms, drawn {drawn:0} ms, composes {ThemeMotif.ComposeCalls - composes} "
                        + $"({ThemeMotif.ComposeMs - composeMs:0} ms), raster {ThemeMotif.RasterPaints - raster}, vector {ThemeMotif.VectorPaints - vector}");
                }
            }
            string? shots = Environment.GetEnvironmentVariable("KN_INK_SHOTS");
            if (shots != null) System.IO.File.WriteAllText(System.IO.Path.Combine(shots, "restore.txt"), report.ToString());
            Assert.True(worst < 1000, "Tray restore too slow:\n" + report);
        });

        // The global show shortcut (QuickNote.cs): Windows posts WM_HOTKEY to the hidden shortcut window, and the
        // window comes back from the tray the way the icon brings it back.
        [Fact]
        public void TheShowShortcutBringsTheWindowBackFromTheTray() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            MarkdownAuthoringTests.Ready(window);
            if (!window.IsVisible) window.Show();
            var register = typeof(KillerNotes.Shell.MainWindow).GetMethod("RegisterShowHotkey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            register.Invoke(window, ["Win+Alt+O"]);
            var source = (System.Windows.Interop.HwndSource)typeof(KillerNotes.Shell.MainWindow)
                .GetField("_hotkeySource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            int id = (int)typeof(KillerNotes.Shell.MainWindow)
                .GetField("ShowHotkeyId", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            try
            {
                window.Hide();
                Settle(window, 300);
                Assert.False(window.IsVisible);
                SendMessage(source.Handle, 0x0312, new IntPtr(id), IntPtr.Zero);
                Settle(window, 300);
                Assert.True(window.IsVisible);
            }
            finally { register.Invoke(window, [null]); }
        });

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        private static void WaitForFrames(Window window, int frames)
        {
            int seen = 0;
            var frame = new DispatcherFrame();
            void OnRendering(object? sender, EventArgs e) { if (++seen >= frames) frame.Continue = false; }
            CompositionTarget.Rendering += OnRendering;
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            guard.Tick += (_, _) => { guard.Stop(); frame.Continue = false; };
            guard.Start();
            Dispatcher.PushFrame(frame);
            guard.Stop();
            CompositionTarget.Rendering -= OnRendering;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        private static void Settle(Window window, int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }
}
