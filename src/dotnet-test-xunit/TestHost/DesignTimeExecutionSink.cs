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

            Execution.TestAssemblyFinishedEvent += HandleTestAssemblyFinished;
            Execution.TestFailedEvent += HandleTestFailed;
            Execution.TestPassedEvent += HandleTestPassed;
            Execution.TestStartingEvent += HandleTestStarting;
            Execution.TestSkippedEvent += HandleTestSkipped;
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
            var testSkipped = args.Message;
            var test = conversions[testSkipped.TestCase];
            var result = new TestResult(test) { Outcome = TestOutcome.Skipped };

            result.Messages.Add($"Reason: {testSkipped.Reason}");

            if (!string.IsNullOrWhiteSpace(testSkipped.Output))
                result.Messages.Add(testSkipped.Output);

            sink?.SendTestResult(result);
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

            if (!string.IsNullOrWhiteSpace(testFailed.Output))
                result.Messages.Add(testFailed.Output);

            sink?.SendTestResult(result);
        }

        void HandleTestPassed(MessageHandlerArgs<ITestPassed> args)
        {
            var testPassed = args.Message;
            var test = conversions[testPassed.TestCase];
            var result = new TestResult(test)
            {
                Outcome = TestOutcome.Passed,
                Duration = TimeSpan.FromSeconds((double)testPassed.ExecutionTime),
            };

            if (!string.IsNullOrWhiteSpace(testPassed.Output))
                result.Messages.Add(testPassed.Output);

            sink?.SendTestResult(result);
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

        public override bool OnMessageWithTypes(IMessageSinkMessage message, HashSet<string> messageTypes)
        {
            return base.OnMessageWithTypes(message, messageTypes)
                && next.OnMessageWithTypes(message, messageTypes);
        }
    }
}
