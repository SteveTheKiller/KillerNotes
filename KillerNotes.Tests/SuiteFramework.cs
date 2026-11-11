using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("KillerNotes.Tests.SuiteFramework", "KillerNotes.Tests")]

namespace KillerNotes.Tests
{
    /// <summary>
    /// The stock xunit framework with one addition: once every test has run, and before the
    /// runner hears the assembly is finished and unloads the test AppDomain, it ends the shared
    /// WPF thread. See <see cref="Sta.Shutdown"/> for why that cannot wait for the unload.
    /// </summary>
    public sealed class SuiteFramework : XunitTestFramework
    {
        public SuiteFramework(IMessageSink messageSink) : base(messageSink) { }

        protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
            new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

        private sealed class Executor : XunitTestFrameworkExecutor
        {
            public Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
                : base(assemblyName, sourceInformationProvider, diagnosticMessageSink) { }

            protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
            {
                using var runner = new AssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
                await runner.RunAsync();
            }
        }

        private sealed class AssemblyRunner : XunitTestAssemblyRunner
        {
            public AssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
                : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions) { }

            protected override async Task BeforeTestAssemblyFinishedAsync()
            {
                await base.BeforeTestAssemblyFinishedAsync();
                Sta.Shutdown();
            }
        }
    }
}
