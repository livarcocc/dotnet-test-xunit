using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Testing.Abstractions;
using Xunit.Abstractions;
using VsTestCase = Microsoft.Extensions.Testing.Abstractions.Test;

namespace Xunit.Runner.DotNet
{
    public class DesignTimeExecutionSink : TestMessageSink, IExecutionSink
    {
        readonly IDictionary<ITestCase, VsTestCase> conversions;
        readonly IMessageSinkWithTypes next;
        readonly ITestExecutionSink sink;

        public DesignTimeExecutionSink(ITestExecutionSink sink, IDictionary<ITestCase, VsTestCase> conversions, IMessageSinkWithTypes next)
        {
            this.sink = sink;
            this.conversions = conversions;
            this.next = next;

            ExecutionSummary = new ExecutionSummary();

            TestAssemblyFinishedEvent += HandleTestAssemblyFinished;
            TestFailedEvent += HandleTestFailed;
            TestPassedEvent += HandleTestPassed;
            TestStartingEvent += HandleTestStarting;
            TestSkippedEvent += HandleTestSkipped;
        }

        public ExecutionSummary ExecutionSummary { get; private set; }

        public ManualResetEvent Finished { get; } = new ManualResetEvent(initialState: false);

        void HandleTestStarting(MessageHandlerArgs<ITestStarting> args)
        {
            var test = conversions[args.Message.TestCase];

            sink?.SendTestStarted(test);
        }

        void HandleTestSkipped(MessageHandlerArgs<ITestSkipped> args)
        {
            var test = conversions[args.Message.TestCase];

            sink?.SendTestResult(new TestResult(test) { Outcome = TestOutcome.Skipped });
        }

        void HandleTestFailed(MessageHandlerArgs<ITestFailed> args)
        {
            var testFailed = args.Message;
            var test = conversions[testFailed.TestCase];
            var result = new TestResult(test)
            {
                Outcome = TestOutcome.Failed,
                Duration = TimeSpan.FromSeconds((double)testFailed.ExecutionTime),
                ErrorMessage = string.Join(Environment.NewLine, testFailed.Messages),
                ErrorStackTrace = string.Join(Environment.NewLine, testFailed.StackTraces),
            };

            result.Messages.Add(testFailed.Output);

            sink?.SendTestResult(result);
        }

        void HandleTestPassed(MessageHandlerArgs<ITestPassed> args)
        {
            var testPassed = args.Message;
            var test = conversions[testPassed.TestCase];

            sink?.SendTestResult(new TestResult(test)
            {
                Outcome = TestOutcome.Passed,
                Duration = TimeSpan.FromSeconds((double)testPassed.ExecutionTime),
            });
        }

        void HandleTestAssemblyFinished(MessageHandlerArgs<ITestAssemblyFinished> args)
        {
            var assemblyFinished = args.Message;

            ExecutionSummary = new ExecutionSummary
            {
                Failed = assemblyFinished.TestsFailed,
                Skipped = assemblyFinished.TestsSkipped,
                Time = assemblyFinished.ExecutionTime,
                Total = assemblyFinished.TestsRun
            };

            Finished.Set();
        }

        public override bool OnMessageWithTypes(IMessageSinkMessage message, string[] messageTypes)
        {
            return base.OnMessageWithTypes(message, messageTypes)
                && next.OnMessageWithTypes(message, messageTypes);
        }
    }
}
