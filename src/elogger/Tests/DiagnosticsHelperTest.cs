using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GdUnit4;
using Microsoft.Extensions.Logging;
using ZLogger;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
[RequireGodotRuntime]
public class DiagnosticsHelperTest
{
    [TestCase]
    public void CleanupStackTrace_IncludesCallingMethodAndParameters()
    {
        var result = CaptureStackTraceFrom(42, "hello");

        AssertThat(result).Contains(nameof(CleanupStackTrace_IncludesCallingMethodAndParameters));
        AssertThat(result).Contains("string Enaweg.Logger.Tests.DiagnosticsHelperTest.CaptureStackTraceFrom(int value, string text)");
    }

    [TestCase]
    public void CleanupStackTrace_OnEmptyStackTrace_ReturnsEmptyString()
    {
        // An exception that was never thrown carries no captured frames.
        var emptyTrace = new StackTrace(new Exception(), false);

        var result = DiagnosticsHelper.CleanupStackTrace(emptyTrace);

        AssertThat(result).IsEqual(string.Empty);
    }

    [TestCase]
    public void CleanupStackTrace_ExcludesMicrosoftLoggingAndZLoggerFrames()
    {
        var capture = new StackCapturingLoggerProvider();
        using (var factory = LoggerFactory.Create(logging => logging.AddProvider(capture)))
        {
            factory.CreateLogger("Test").ZLogInformation($"capture");
        }

        // The trace was taken inside the logger, so it runs through ZLogger and Microsoft.Extensions.Logging.
        var raw = capture.StackTrace!.ToString();
        AssertThat(raw).Contains("Microsoft.Extensions.Logging.");
        AssertThat(raw).Contains("ZLogger.");

        var result = DiagnosticsHelper.CleanupStackTrace(capture.StackTrace);

        AssertThat(result).Contains(nameof(CleanupStackTrace_ExcludesMicrosoftLoggingAndZLoggerFrames));
        AssertThat(result).NotContains("Microsoft.Extensions.Logging.");
        AssertThat(result).NotContains("ZLogger.");
    }

    [TestCase]
    public void CleanupStackTrace_PrintsTypesInCSharpNotation()
    {
        var result = CaptureWithTypes(new List<int>(), Array.Empty<int>(), (1, "a"), Task.FromResult(""),
            new Dictionary<string, List<int>>());

        AssertThat(result).Contains("System.Collections.Generic.List<int> list");
        AssertThat(result).Contains("int[] numbers");
        AssertThat(result).Contains("(int, string) pair");
        AssertThat(result).Contains("Task<string> task");
        AssertThat(result).Contains("System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>> map");
        AssertThat(result).NotContains("`");
    }

    [TestCase]
    public void CleanupStackTrace_SeparatesGenericMethodArguments()
    {
        var result = CaptureGeneric<string, object>();

        AssertThat(result).Contains($"{nameof(CaptureGeneric)}<TFirst, TSecond>()");
    }

    [TestCase]
    public void CleanupStackTrace_PrintsConstructorsWithoutReturnTypeOrDoubleDot()
    {
        var result = new StackCapturingObject().Result;

        AssertThat(result).Contains($"{typeof(StackCapturingObject).FullName}.ctor()");
        AssertThat(result).NotContains("..ctor");
        AssertThat(result).NotContains($"void {typeof(StackCapturingObject).FullName}");
    }

    [TestCase]
    public void CleanupStackTrace_LinksSourceLocationsAsResPaths()
    {
        var result = CaptureStackTraceFrom(1, "x");

        // Debug builds ship portable PDBs, so frames from this project resolve to a file inside res://.
        AssertThat(Regex.IsMatch(result, @"\(at res://Tests/DiagnosticsHelperTest\.cs:\d+\)")).IsTrue();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string CaptureStackTraceFrom(int value, string text) =>
        DiagnosticsHelper.CleanupStackTrace(new StackTrace(true));

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string CaptureWithTypes(List<int> list, int[] numbers, (int, string) pair, Task<string> task,
        Dictionary<string, List<int>> map) =>
        DiagnosticsHelper.CleanupStackTrace(new StackTrace(true));

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string CaptureGeneric<TFirst, TSecond>() =>
        DiagnosticsHelper.CleanupStackTrace(new StackTrace(true));

    sealed class StackCapturingObject
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackCapturingObject()
        {
            Result = DiagnosticsHelper.CleanupStackTrace(new StackTrace(true));
        }

        public string Result { get; }
    }

    sealed class StackCapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public StackTrace? StackTrace { get; private set; }

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            StackTrace = new StackTrace(true);
        }

        public void Dispose()
        {
        }
    }
}
