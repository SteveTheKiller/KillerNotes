using System;
using System.Windows;
using System.Windows.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public class WindowMinimumPolicyTests
    {
        [Theory]
        [InlineData(720)]
        [InlineData(1800)]
        public void QueuedPreviewUpdateCannotUndoStartupWorkAreaFitting(double preferred) => Sta.Run(() =>
        {
            var window = new Window { Width = 640, MinWidth = 640 };
            var policy = new WindowMinimumPolicy();
            policy.Queue(window, () => preferred, () => 640);
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.ApplicationIdle);
            Dispatcher.PushFrame(frame);
            Assert.Equal(640, window.MinWidth);
            Assert.Equal(640, window.Width);
        });
    }
}
