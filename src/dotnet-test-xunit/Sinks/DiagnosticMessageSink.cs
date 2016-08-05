using System;

namespace Xunit.Runner.DotNet
{
    public class DiagnosticMessageSink : TestMessageSink
    {
        public DiagnosticMessageSink(object consoleLock, string assemblyDisplayName, bool showDiagnostics, bool noColor)
        {
            if (showDiagnostics)
                DiagnosticMessageEvent += args =>
                {
                    lock (consoleLock)
                    {
                        if (!noColor)
                            Console.ForegroundColor = ConsoleColor.Yellow;

                        Console.WriteLine("   {0}: {1}", assemblyDisplayName, args.Message.Message);

                        if (!noColor)
                            Console.ForegroundColor = ConsoleColor.Gray;
                    }
                };
        }
    }
}
