﻿using System;
using System.Collections.Generic;
using Microsoft.Extensions.Testing.Abstractions;
using Xunit.Abstractions;
using VsTestCase = Microsoft.Extensions.Testing.Abstractions.Test;

namespace Xunit.Runner.DotNet
{
    public class DesignTimeExecutionVisitor : TestMessageVisitor<ITestAssemblyFinished>, IExecutionVisitor
    {
        private readonly ITestExecutionSink sink;
        private readonly IDictionary<ITestCase, VsTestCase> conversions;
        private readonly IMessageSink next;

        public DesignTimeExecutionVisitor(ITestExecutionSink sink, IDictionary<ITestCase, VsTestCase> conversions, IMessageSink next)
        {
            this.sink = sink;
            this.conversions = conversions;
            this.next = next;

            ExecutionSummary = new ExecutionSummary();
        }

        public ExecutionSummary ExecutionSummary { get; private set; }

        protected override bool Visit(ITestStarting testStarting)
        {
            var test = conversions[testStarting.TestCase];

            sink?.SendTestStarted(test);

            return true;
        }

        protected override bool Visit(ITestSkipped testSkipped)
        {
            var test = conversions[testSkipped.TestCase];

            sink?.SendTestResult(new TestResult(test) { Outcome = TestOutcome.Skipped });

            return true;
        }

        protected override bool Visit(ITestFailed testFailed)
        {
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

            return true;
        }

        protected override bool Visit(ITestPassed testPassed)
        {
            var test = conversions[testPassed.TestCase];

            sink?.SendTestResult(new TestResult(test)
            {
                Outcome = TestOutcome.Passed,
                Duration = TimeSpan.FromSeconds((double)testPassed.ExecutionTime),
            });

            return true;
        }

        protected override bool Visit(ITestAssemblyFinished assemblyFinished)
        {
            var result = base.Visit(assemblyFinished);

            ExecutionSummary = new ExecutionSummary
            {
                Failed = assemblyFinished.TestsFailed,
                Skipped = assemblyFinished.TestsSkipped,
                Time = assemblyFinished.ExecutionTime,
                Total = assemblyFinished.TestsRun
            };

            return result;
        }

        public override bool OnMessage(IMessageSinkMessage message)
        {
            return
                base.OnMessage(message) &&
                next.OnMessage(message);
        }
    }
}
