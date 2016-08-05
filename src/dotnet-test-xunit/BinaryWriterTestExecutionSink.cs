using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Testing.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Xunit.Runner.DotNet
{
    public class BinaryWriterTestExecutionSink : BinaryWriterTestSink, ITestExecutionSink
    {
        readonly ConcurrentDictionary<string, TestState> runningTests;

        public BinaryWriterTestExecutionSink(BinaryWriter binaryWriter) : base(binaryWriter)
        {
            runningTests = new ConcurrentDictionary<string, TestState>();
        }

        public void SendTestStarted(Test test)
        {
            Guard.ArgumentNotNull(nameof(test), test);

            if (test.FullyQualifiedName != null)
                runningTests.TryAdd(test.FullyQualifiedName, new TestState() { StartTime = DateTimeOffset.Now, });

            BinaryWriter.Write(JsonConvert.SerializeObject(new Message
            {
                MessageType = "TestExecution.TestStarted",
                Payload = JToken.FromObject(test),
            }));
        }

        public void SendTestResult(TestResult testResult)
        {
            Guard.ArgumentNotNull(nameof(testResult), testResult);

            if (testResult.StartTime == default(DateTimeOffset) && testResult.Test.FullyQualifiedName != null)
            {
                TestState state;
                runningTests.TryRemove(testResult.Test.FullyQualifiedName, out state);

                testResult.StartTime = state.StartTime;
            }

            if (testResult.EndTime == default(DateTimeOffset))
                testResult.EndTime = DateTimeOffset.Now;

            BinaryWriter.Write(JsonConvert.SerializeObject(new Message
            {
                MessageType = "TestExecution.TestResult",
                Payload = JToken.FromObject(testResult),
            }));
        }

        class TestState
        {
            public DateTimeOffset StartTime { get; set; }
        }
    }
}
