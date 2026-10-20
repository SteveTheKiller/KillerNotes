using System;
using System.Windows;
using System.Windows.Threading;

namespace KillerNotes.Services
{
    internal sealed class WindowMinimumPolicy
    {
        private bool _queued;
        public void Queue(Window window, Func<double> preferred, Func<double> available)
        {
            if (_queued) return;
            if (Math.Abs(window.MinWidth - Math.Min(preferred(), available())) < .5) return;
            _queued = true;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _queued = false;
                double minimum = Math.Max(0, Math.Min(preferred(), available()));
                if (Math.Abs(window.MinWidth - minimum) >= .5) window.MinWidth = minimum;
            }), DispatcherPriority.Background);
        }
    }
}
