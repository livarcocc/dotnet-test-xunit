using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.DotNet.InternalAbstractions;
using Microsoft.DotNet.ProjectModel;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Testing.Abstractions;
using Newtonsoft.Json;
using Xunit.Abstractions;
using ISourceInformationProvider = Xunit.Abstractions.ISourceInformationProvider;
using VsTestCase = Microsoft.Extensions.Testing.Abstractions.Test;

namespace Xunit.Runner.DotNet
{
    public class Program : IDisposable
    {
#pragma warning disable 0649
        volatile bool cancel;
#pragma warning restore 0649
        readonly ConcurrentDictionary<string, ExecutionSummary> completionMessages = new ConcurrentDictionary<string, ExecutionSummary>();
        bool failed;
        IRunnerLogger logger;
        IMessageSinkWithTypes reporterMessageHandler;
        ITestDiscoverySink testDiscoverySink;
        ITestExecutionSink testExecutionSink;
        private Socket socket;

#if NETCOREAPP1_0
        private const string compileTarget = ".NET Core";
#else
        private const string compileTarget = "Desktop .NET";
#endif

        public static int Main(string[] args)
        {
            using (var program = new Program())
                return program.Run(args);
        }

        public int Run(string[] args)
        {
            try
            {
                var reporters = GetAvailableRunnerReporters();

                if (args.Length == 0 || args.Any(arg => arg == "-?" || arg == "-help"))
                {
                    PrintHeader();
                    PrintUsage(reporters);
                    return 2;
                }

#if !NETCOREAPP1_0
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
#endif

                var commandLine = CommandLine.Parse(reporters, args);

#if !NETCOREAPP1_0
                if (commandLine.Debug)
                    Debugger.Launch();
#else
                if (commandLine.Debug)
                {
                    Console.WriteLine("Debug support is not available in .NET Core.");
                    return 2;
                }
#endif

                logger = new ConsoleRunnerLogger(!commandLine.NoColor);
                reporterMessageHandler = MessageSinkWithTypesAdapter.Wrap(commandLine.Reporter.CreateMessageHandler(logger));

                if (!commandLine.NoLogo)
                    PrintHeader();

                var testsToRun = commandLine.DesignTimeTestUniqueNames;
                if (commandLine.Port.HasValue)
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    var ipEndPoint = new IPEndPoint(IPAddress.Loopback, commandLine.Port.Value);

                    socket.Connect(ipEndPoint);
                    var networkStream = new NetworkStream(socket);

                    UseTestSinksWithSockets(networkStream);

                    if (commandLine.WaitCommand)
                    {
                        var reader = new BinaryReader(networkStream);
                        testExecutionSink.SendWaitingCommand();

                        var rawMessage = reader.ReadString();
                        var message = JsonConvert.DeserializeObject<Message>(rawMessage);

                        testsToRun = message.Payload.ToObject<RunTestsMessage>().Tests;
                    }
                }
                else
                {
                    UseTestSinksWithStandardOutputStreams();
                }

                var failCount = RunProject(commandLine.Project, commandLine.ParallelizeAssemblies, commandLine.ParallelizeTestCollections,
                                           commandLine.MaxParallelThreads, commandLine.DiagnosticMessages, commandLine.NoColor,
                                           commandLine.AppDomains, commandLine.FailSkips, commandLine.DesignTime, commandLine.List,
                                           testsToRun);

                if (commandLine.Wait)
                    WaitForInput();

                return failCount > 0 ? 1 : 0;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("error: {0}", ex.Message);
                return 3;
            }
            catch (BadImageFormatException ex)
            {
                Console.WriteLine("{0}", ex.Message);
                return 4;
            }
            finally
            {
                Console.ResetColor();
            }
        }

        public void Dispose()
        {
            socket?.Dispose();
        }

        private void UseTestSinksWithStandardOutputStreams()
        {
            testDiscoverySink = new StreamingTestDiscoverySink(Console.OpenStandardOutput());
            testExecutionSink = new StreamingTestExecutionSink(Console.OpenStandardOutput());
        }

        private void UseTestSinksWithSockets(NetworkStream networkStream)
        {
            var binaryWriter = new BinaryWriter(networkStream);
            testDiscoverySink = new BinaryWriterTestDiscoverySink(binaryWriter);
            testExecutionSink = new BinaryWriterTestExecutionSink(binaryWriter);
        }

        private static void WaitForInput()
        {
            Console.WriteLine();

            Console.Write("Press ENTER to continue...");
            Console.ReadLine();

            Console.WriteLine();
        }

        // This is a temporary workaround.
        private static string GetProjectPathFromDllPath(string dllPath)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(dllPath));
            while (directory != directory.Root && directory.EnumerateFiles().All(f => f.Name != "project.json"))
            {
                directory = directory.Parent;
            }

            var projectFile = directory.EnumerateFiles().FirstOrDefault(f => f.Name == "project.json");

            return projectFile?.FullName;
        }

#if !NETCOREAPP1_0
        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;

            if (ex != null)
                Console.WriteLine(ex.ToString());
            else
                Console.WriteLine("Error of unknown type thrown in application domain");

            Environment.Exit(1);
        }
#endif

        static List<IRunnerReporter> GetAvailableRunnerReporters()
        {
            var result = new List<IRunnerReporter>();
            var dependencyModel = DependencyContext.Load(typeof(Program).GetTypeInfo().Assembly);

            foreach (var assemblyName in dependencyModel.GetRuntimeAssemblyNames(RuntimeEnvironment.GetRuntimeIdentifier()))
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    foreach (var type in assembly.DefinedTypes)
                    {
#pragma warning disable CS0618
                        if (type == null || type.IsAbstract || type == typeof(DefaultRunnerReporter).GetTypeInfo() || type == typeof(DefaultRunnerReporterWithTypes).GetTypeInfo() || type.ImplementedInterfaces.All(i => i != typeof(IRunnerReporter)))
                            continue;
#pragma warning restore CS0618

                        var ctor = type.DeclaredConstructors.FirstOrDefault(c => c.GetParameters().Length == 0);
                        if (ctor == null)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Type {type.FullName} in assembly {assembly} appears to be a runner reporter, but does not have an empty constructor.");
                            Console.ResetColor();
                            continue;
                        }

                        result.Add((IRunnerReporter)ctor.Invoke(new object[0]));
                    }
                }
                catch
                {
                    continue;
                }
            }

            return result;
        }

        void PrintHeader() =>
            Console.WriteLine($"xUnit.net .NET CLI test runner ({IntPtr.Size * 8}-bit {compileTarget} {RuntimeEnvironment.GetRuntimeIdentifier()})");

        static void PrintUsage(IReadOnlyList<IRunnerReporter> reporters)
        {
            Console.WriteLine("Copyright (C) 2016 Outercurve Foundation.");
            Console.WriteLine();
            Console.WriteLine("usage: dotnet-test-xunit [configFile.json] [options] [reporter] [resultFormat filename [...]]");
            Console.WriteLine();
            Console.WriteLine("Valid options:");
            Console.WriteLine("  -nologo                : do not show the header message");
            Console.WriteLine("  -nocolor               : do not output results with colors");
#if !NETCOREAPP1_0
            Console.WriteLine("  -appdomain option      : override default app domain support");
            Console.WriteLine("                         :   on  - turn on app domains (when available)");
            Console.WriteLine("                         :   off - turn off app domains");
#endif
            Console.WriteLine("  -failskips             : convert skipped tests into failures");
            Console.WriteLine("  -parallel option       : set parallelization based on option");
            Console.WriteLine("                         :   none        - turn off all parallelization");
            Console.WriteLine("                         :   collections - only parallelize collections");
            Console.WriteLine("                         :   assemblies  - only parallelize assemblies");
            Console.WriteLine("                         :   all         - parallelize collections and assemblies");
            Console.WriteLine("  -maxthreads count      : maximum thread count for collection parallelization");
            Console.WriteLine("                         :   default   - run with default (1 thread per CPU thread)");
            Console.WriteLine("                         :   unlimited - run with unbounded thread count");
            Console.WriteLine("                         :   (number)  - limit task thread pool size to 'count'");
#if !NETCOREAPP1_0
            Console.WriteLine("  -noshadow              : do not shadow copy assemblies");
#endif
            Console.WriteLine("  -wait                  : wait for input after completion");
            Console.WriteLine("  -diagnostics           : enable diagnostics messages for all test assemblies");
#if !NETCOREAPP1_0
            Console.WriteLine("  -debug                 : launch the debugger to debug the tests");
#endif
            Console.WriteLine("  -trait \"name=value\"    : only run tests with matching name/value traits");
            Console.WriteLine("                         : if specified more than once, acts as an OR operation");
            Console.WriteLine("  -notrait \"name=value\"  : do not run tests with matching name/value traits");
            Console.WriteLine("                         : if specified more than once, acts as an AND operation");
            Console.WriteLine("  -method \"name\"         : run a given test method (should be fully specified;");
            Console.WriteLine("                         : i.e., 'MyNamespace.MyClass.MyTestMethod')");
            Console.WriteLine("                         : if specified more than once, acts as an OR operation");
            Console.WriteLine("  -class \"name\"          : run all methods in a given test class (should be fully");
            Console.WriteLine("                         : specified; i.e., 'MyNamespace.MyClass')");
            Console.WriteLine("                         : if specified more than once, acts as an OR operation");
            Console.WriteLine("  -namespace \"name\"      : run all methods in a given namespace (i.e.,");
            Console.WriteLine("                         : 'MyNamespace.MySubNamespace')");
            Console.WriteLine("                         : if specified more than once, acts as an OR operation");
            Console.WriteLine("  -noautoreporters       : do not allow reporters to be auto-enabled by environment");
            Console.WriteLine("                         : (for example, auto-detecting TeamCity or AppVeyor)");
            Console.WriteLine();

            var switchableReporters = reporters.Where(r => !string.IsNullOrWhiteSpace(r.RunnerSwitch)).ToList();
            if (switchableReporters.Count > 0)
            {
                Console.WriteLine("Reporters: (optional, choose only one)");

                foreach (var reporter in switchableReporters.OrderBy(r => r.RunnerSwitch))
                    Console.WriteLine("  -{0} : {1}", reporter.RunnerSwitch.ToLowerInvariant().PadRight(21), reporter.Description);

                Console.WriteLine();
            }

            Console.WriteLine("Result formats: (optional, choose one or more)");

            foreach (var transform in TransformFactory.AvailableTransforms)
                Console.WriteLine("  {0} : {1}",
                                  string.Format("-{0} <filename>", transform.CommandLine).PadRight(22).Substring(0, 22),
                                  transform.Description);
        }

        int RunProject(XunitProject project,
                       bool? parallelizeAssemblies,
                       bool? parallelizeTestCollections,
                       int? maxThreadCount,
                       bool diagnosticMessages,
                       bool noColor,
                       AppDomainSupport? appDomains,
                       bool failSkips,
                       bool designTime,
                       bool list,
                       IReadOnlyList<string> designTimeFullyQualifiedNames)
        {
            XElement assembliesElement = null;
            var xmlTransformers = TransformFactory.GetXmlTransformers(project);
            var needsXml = xmlTransformers.Count > 0;
            var consoleLock = new object();

            if (!parallelizeAssemblies.HasValue)
                parallelizeAssemblies = project.All(assembly =>
                                                    {
                                                        var assm = (XunitProjectAssembly2)assembly;

                                                        return assm.ConfigFilename != null ?
                                                            assm.Configuration.ParallelizeAssemblyOrDefault :
                                                            assm.ConfigurationStream.ParallelizeAssemblyOrDefault;
                                                    });

            if (needsXml)
                assembliesElement = new XElement("assemblies");

            var originalWorkingFolder = Directory.GetCurrentDirectory();

            using (AssemblyHelper.SubscribeResolve())
            {
                var clockTime = Stopwatch.StartNew();

                if (parallelizeAssemblies.GetValueOrDefault())
                {
                    var tasks = project.Assemblies.Select(assembly => TaskRun(() =>
                        ExecuteAssembly(
                            consoleLock,
                            (XunitProjectAssembly2)assembly,
                            needsXml,
                            parallelizeTestCollections,
                            maxThreadCount,
                            diagnosticMessages,
                            noColor,
                            appDomains,
                            failSkips,
                            project.Filters,
                            designTime,
                            list,
                            designTimeFullyQualifiedNames)));

                    var results = Task.WhenAll(tasks).GetAwaiter().GetResult();
                    foreach (var assemblyElement in results.Where(result => result != null))
                        assembliesElement.Add(assemblyElement);
                }
                else
                {
                    foreach (var assembly in project.Assemblies)
                    {
                        var assemblyElement = ExecuteAssembly(
                            consoleLock,
                            (XunitProjectAssembly2)assembly,
                            needsXml,
                            parallelizeTestCollections,
                            maxThreadCount,
                            diagnosticMessages,
                            noColor,
                            appDomains,
                            failSkips,
                            project.Filters,
                            designTime,
                            list,
                            designTimeFullyQualifiedNames);

                        if (assemblyElement != null)
                            assembliesElement.Add(assemblyElement);
                    }
                }

                SendTestCompletedIfNecessary(designTime, list);

                clockTime.Stop();

                if (completionMessages.Count > 0)
                    reporterMessageHandler.OnMessage(new TestExecutionSummary(clockTime.Elapsed, completionMessages.OrderBy(kvp => kvp.Key).ToList()));
            }

            Directory.SetCurrentDirectory(originalWorkingFolder);

            foreach (var transformer in xmlTransformers)
                transformer(assembliesElement);

            return failed ? 1 : completionMessages.Values.Sum(summary => summary.Failed);
        }

        private void SendTestCompletedIfNecessary(bool designTime, bool list)
        {
            if (!designTime)
                return;

            if (list)
                testDiscoverySink.SendTestCompleted();
            else
                testExecutionSink.SendTestCompleted();
        }

        XElement ExecuteAssembly(object consoleLock,
                                 XunitProjectAssembly2 assembly,
                                 bool needsXml,
                                 bool? parallelizeTestCollections,
                                 int? maxThreadCount,
                                 bool diagnosticMessages,
                                 bool noColor,
                                 AppDomainSupport? appDomain,
                                 bool failSkips,
                                 XunitFilters filters,
                                 bool designTime,
                                 bool listTestCases,
                                 IReadOnlyList<string> designTimeFullyQualifiedNames)
        {
            if (cancel)
                return null;

            var assemblyElement = needsXml ? new XElement("assembly") : null;

            try
            {
                // if we had a config file use it
                var config = assembly.ConfigFilename != null ? assembly.Configuration : assembly.ConfigurationStream;

                // Turn off pre-enumeration of theories when we're not running in Visual Studio
                if (!designTime)
                    config.PreEnumerateTheories = false;
                if (appDomain.HasValue)
                    config.AppDomain = appDomain.GetValueOrDefault();
                if (diagnosticMessages)
                    config.DiagnosticMessages = true;

                var discoveryOptions = TestFrameworkOptions.ForDiscovery(config);
                var executionOptions = TestFrameworkOptions.ForExecution(config);
                if (maxThreadCount.HasValue)
                    executionOptions.SetMaxParallelThreads(maxThreadCount);
                if (parallelizeTestCollections.HasValue)
                    executionOptions.SetDisableParallelization(!parallelizeTestCollections.GetValueOrDefault());

                var assemblyDisplayName = Path.GetFileNameWithoutExtension(assembly.AssemblyFilename);
                var diagnosticMessageSink = new DiagnosticMessageSink(consoleLock, assemblyDisplayName, config.DiagnosticMessagesOrDefault, noColor);
                var appDomainSupport = config.AppDomainOrDefault;
                var shadowCopy = config.ShadowCopyOrDefault;
                var longRunningSeconds = config.LongRunningTestSecondsOrDefault;
                var sourceInformationProvider = GetSourceInformationProviderAdapater(assembly);

                using (var controller = new XunitFrontController(appDomainSupport, assembly.AssemblyFilename, assembly.ConfigFilename, shadowCopy, diagnosticMessageSink: MessageSinkAdapter.Wrap(diagnosticMessageSink), sourceInformationProvider: sourceInformationProvider))
                using (var discoverySink = new TestDiscoverySink())
                {
                    var includeSourceInformation = designTime && listTestCases;

                    // Discover & filter the tests
                    reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryStarting(assembly, controller.CanUseAppDomains && appDomainSupport != AppDomainSupport.Denied, shadowCopy, discoveryOptions));

                    controller.Find(includeSourceInformation: includeSourceInformation, messageSink: discoverySink, discoveryOptions: discoveryOptions);
                    discoverySink.Finished.WaitOne();

                    IDictionary<ITestCase, VsTestCase> vsTestCases = null;
                    if (designTime)
                        vsTestCases = DesignTimeTestConverter.Convert(discoverySink.TestCases);

                    if (listTestCases)
                    {
                        lock (consoleLock)
                        {
                            if (designTime)
                            {
                                foreach (var testcase in vsTestCases.Values)
                                {
                                    testDiscoverySink?.SendTestFound(testcase);

                                    Console.WriteLine(testcase.FullyQualifiedName);
                                }
                            }
                            else
                            {
                                foreach (var testcase in discoverySink.TestCases)
                                    Console.WriteLine(testcase.DisplayName);
                            }
                        }

                        return assemblyElement;
                    }

                    IExecutionSink resultsSink;

                    if (designTime)
                        resultsSink = new DesignTimeExecutionSink(testExecutionSink, vsTestCases, reporterMessageHandler);
                    else
                        resultsSink = new DelegatingExecutionSummarySink(reporterMessageHandler, () => cancel, (path, summary) => completionMessages.TryAdd(path, summary));

                    if (assemblyElement != null)
                        resultsSink = new DelegatingXmlCreationSink(resultsSink, assemblyElement);
                    if (longRunningSeconds > 0)
                        resultsSink = new DelegatingLongRunningTestDetectionSink(resultsSink, TimeSpan.FromSeconds(longRunningSeconds), diagnosticMessageSink);
                    if (failSkips)
                        resultsSink = new DelegatingFailSkipSink(resultsSink);

                    IList<ITestCase> filteredTestCases;
                    var testCasesDiscovered = discoverySink.TestCases.Count;
                    if (!designTime || designTimeFullyQualifiedNames.Count == 0)
                        filteredTestCases = discoverySink.TestCases.Where(filters.Filter).ToList();
                    else
                        filteredTestCases = vsTestCases.Where(t => designTimeFullyQualifiedNames.Contains(t.Value.FullyQualifiedName)).Select(t => t.Key).ToList();
                    var testCasesToRun = filteredTestCases.Count;

                    reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryFinished(assembly, discoveryOptions, testCasesDiscovered, testCasesToRun));

                    if (filteredTestCases.Count == 0)
                        completionMessages.TryAdd(Path.GetFileName(assembly.AssemblyFilename), new ExecutionSummary());
                    else
                    {
                        reporterMessageHandler.OnMessage(new TestAssemblyExecutionStarting(assembly, executionOptions));

                        controller.RunTests(filteredTestCases, resultsSink, executionOptions);
                        resultsSink.Finished.WaitOne();

                        reporterMessageHandler.OnMessage(new TestAssemblyExecutionFinished(assembly, executionOptions, resultsSink.ExecutionSummary));
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;

                var e = ex;
                while (e != null)
                {
                    Console.WriteLine("{0}: {1}", e.GetType().FullName, e.Message);
                    e = e.InnerException;
                }
            }

            return assemblyElement;
        }

        private static ISourceInformationProvider GetSourceInformationProviderAdapater(XunitProjectAssembly assembly)
        {
            var directoryPath = Path.GetDirectoryName(assembly.AssemblyFilename);
            var assemblyName = Path.GetFileNameWithoutExtension(assembly.AssemblyFilename);
            var pdbPath = Path.Combine(directoryPath, assemblyName + FileNameSuffixes.DotNet.ProgramDatabase);

            return File.Exists(pdbPath)
                ? new SourceInformationProviderAdapater(new SourceInformationProvider(pdbPath))
                : null;
        }

        static Task<T> TaskRun<T>(Func<T> function)
        {
            var tcs = new TaskCompletionSource<T>();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    tcs.SetResult(function());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }
    }
}
