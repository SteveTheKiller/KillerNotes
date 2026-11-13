using System;
using System.Linq;
using System.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class AudioBootstrapTests
    {
        [Fact]
        public void ConcurrentFirstUsesWaitForTheCodecToFinishLoading()
        {
            var domain = AppDomain.CreateDomain("Audio startup", null, new AppDomainSetup
            {
                ApplicationBase = AppContext.BaseDirectory,
                ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
            });
            try
            {
                var probe = (AudioStartupProbe)domain.CreateInstanceAndUnwrap(typeof(AudioStartupProbe).Assembly.FullName, typeof(AudioStartupProbe).FullName);
                bool[] results = probe.Run();
                Assert.Contains(true, results);
                Assert.All(results, Assert.True);
            }
            finally { AppDomain.Unload(domain); }
        }
    }

    public sealed class AudioStartupProbe : MarshalByRefObject
    {
        public bool[] Run()
        {
            var results = new bool[16];
            using var ready = new CountdownEvent(results.Length);
            using var start = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, results.Length).Select(i => new Thread(() =>
            {
                ready.Signal();
                start.Wait();
                results[i] = FlacCodec.Available;
            })).ToArray();
            foreach (var thread in threads) thread.Start();
            ready.Wait();
            start.Set();
            foreach (var thread in threads) thread.Join();
            return results;
        }
    }
}
