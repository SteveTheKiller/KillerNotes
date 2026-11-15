using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Runs a test body on an STA thread.
    ///
    /// xunit runs facts on MTA threads, and the WPF text stack will not have it: building a
    /// FlowDocument is tolerable, but TextRange.Save/Load with DataFormats.XamlPackage goes
    /// through the packaging and clipboard-adjacent plumbing, which throws outside STA. Every
    /// test that touches MarkdownConvert or a XamlPackage blob therefore wraps its body here.
    ///
    /// Deliberately a plain thread rather than the Xunit.StaFact package: one small helper is
    /// cheaper than another dependency on a net48 project that already pins transitive versions
    /// by hand, and it keeps the test project's package list matching the other apps'.
    ///
    /// The exception is rethrown rather than wrapped, so a failing assert still reports as that
    /// assert instead of as a thread failure.
    /// </summary>
    internal static class Sta
    {
        private static readonly Lazy<Dispatcher> SharedDispatcher = new(() =>
        {
            Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
                // Release this apartment's COM wrappers here, while the thread can still
                // answer the finalizer, rather than in thread teardown, where it cannot.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Marshal.CleanupUnusedObjectsInCurrentContext();
            }) { IsBackground = true, Name = "WPF tests" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            sharedThread = thread;
            return dispatcher!;
        });

        private static Thread? sharedThread;

        /// <summary>
        /// Ends the Application thread once the last test has run. <see cref="SuiteFramework"/>
        /// calls this before xunit reports the assembly finished, which is the last point
        /// before the runner unloads the test AppDomain.
        ///
        /// Left running, the thread is torn down by the unload itself. WPF still holds COM
        /// objects in this thread's apartment, and the unload hands them to the finalizer, which
        /// has to call into this apartment to release them while the dying thread waits on the
        /// finalizer. Neither moves, so the test host never exits. Shutting the dispatcher down
        /// from a DomainUnload handler is too late: that handler runs on the finalizer thread
        /// after WPF's own unload listeners have started dismantling the dispatcher's window.
        /// </summary>
        internal static void Shutdown()
        {
            if (!SharedDispatcher.IsValueCreated) return;
            SharedDispatcher.Value.InvokeShutdown();
            sharedThread?.Join();
        }

        public static void Run(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    // Binding and text-service shutdown must run before this apartment exits.
                    var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
                    if (dispatcher != null && !dispatcher.HasShutdownStarted) dispatcher.InvokeShutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        // Application outlives a test, so its dispatcher must remain available afterward.
        public static void RunApplication(Action body)
        {
            Exception? failure = null;
            SharedDispatcher.Value.Invoke(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            if (failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
