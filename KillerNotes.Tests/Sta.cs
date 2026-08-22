using System;
using System.Threading;

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
        public static void Run(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
