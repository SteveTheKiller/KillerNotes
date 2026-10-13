#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Threading;

namespace KillerFamily.Deployment
{
    internal static class InstallerExitCodes
    {
        internal const int Success = 0;
        internal const int Failure = 1;
        internal const int AccessDenied = 5;
        internal const int MissingRuntime = 10;
        internal const int DiskFull = 112;
        internal const int Cancelled = 1223;
        internal const int Busy = 1618;
        internal const int SecurityRejected = 1625;

        internal static int FromException(Exception error)
        {
            for (Exception? current = error; current != null; current = current.InnerException)
            {
                if (current is OperationCanceledException) return Cancelled;
                if (current is UnauthorizedAccessException) return AccessDenied;
                if (current is SecurityException) return SecurityRejected;
                int code = current is Win32Exception native ? native.NativeErrorCode :
                    current is IOException ? current.HResult & 0xffff : 0;
                if (code == 39 || code == DiskFull) return DiskFull;
                if (code == AccessDenied || code == Cancelled || code == Busy || code == SecurityRejected)
                    return code;
            }
            return Failure;
        }

        internal static IDisposable Acquire(string product)
        {
            if (string.IsNullOrWhiteSpace(product) || product.IndexOfAny(new[] { '\\', '/' }) >= 0)
                throw new ArgumentException("A product name is required.", nameof(product));
            var mutex = new Mutex(false, @"Global\KillerTools-Install-" + product);
            bool owned;
            try
            {
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
            }
            catch { mutex.Dispose(); throw; }
            if (!owned)
            {
                mutex.Dispose();
                throw new Win32Exception(Busy, "Another installation of this application is in progress.");
            }
            return new Lease(mutex);
        }

        private sealed class Lease : IDisposable
        {
            private Mutex? _mutex;
            internal Lease(Mutex mutex) { _mutex = mutex; }
            public void Dispose()
            {
                var mutex = _mutex;
                if (mutex == null) return;
                _mutex = null;
                try { mutex.ReleaseMutex(); }
                finally { mutex.Dispose(); }
            }
        }
    }
}
