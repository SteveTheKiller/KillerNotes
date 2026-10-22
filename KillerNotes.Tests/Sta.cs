using System;
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
            }) { IsBackground = true, Name = "WPF tests" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            return dispatcher!;
        });

        public static void Run(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
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
