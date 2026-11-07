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

    [TestCase]
    public void ToResPath_MapsFilesInsideTheProject()
    {
        var root = "/home/dev/game/";

        AssertThat(DiagnosticsHelper.ToResPath("/home/dev/game/Scripts/Player.cs", root))
            .IsEqual("res://Scripts/Player.cs");
        AssertThat(DiagnosticsHelper.ToResPath("/home/dev/game/Scripts/Player.cs", "/home/dev/game"))
            .IsEqual("res://Scripts/Player.cs");
    }

    [TestCase]
    public void ToResPath_WithAnEmptyProjectRoot_KeepsThePath()
    {
        // GlobalizePath("res://") can be empty in an exported game; string.Replace used to throw on it.
        AssertThat(DiagnosticsHelper.ToResPath("/build/src/Scripts/Player.cs", ""))
            .IsEqual("/build/src/Scripts/Player.cs");
    }

    [TestCase]
    public void ToResPath_OnlyMapsALeadingMatch()
    {
        AssertThat(DiagnosticsHelper.ToResPath("/home/dev/other/home/dev/game/Player.cs", "/home/dev/game/"))
            .IsEqual("/home/dev/other/home/dev/game/Player.cs");
        AssertThat(DiagnosticsHelper.ToResPath("/home/dev/game2/Player.cs", "/home/dev/game/"))
            .IsEqual("/home/dev/game2/Player.cs");
    }

    [TestCase]
    public void CleanupException_PrintsTypeMessageAndCleanedUpFrames()
    {
        var exception = Catch(() => ThrowFrom("outer"));

        var result = DiagnosticsHelper.CleanupException(exception);

        AssertThat(result).StartsWith("System.InvalidOperationException: outer");
        AssertThat(result).Contains($"void {typeof(DiagnosticsHelperTest).FullName}.{nameof(ThrowFrom)}(string message)");
        AssertThat(result).NotContains("   at ");
    }

    [TestCase]
    public void CleanupException_IncludesInnerExceptions()
    {
        var exception = Catch(() =>
        {
            try
            {
                ThrowFrom("inner");
            }
            catch (Exception inner)
            {
                throw new ArgumentException("outer", inner);
            }
        });

        var result = DiagnosticsHelper.CleanupException(exception);

        AssertThat(result).StartsWith("System.ArgumentException: outer");
        AssertThat(result).Contains("---> System.InvalidOperationException: inner");
        AssertThat(result).Contains($"{nameof(ThrowFrom)}(string message)");
    }

    [TestCase]
    public void CleanupException_IncludesEveryExceptionOfAnAggregate()
    {
        var exception = new AggregateException(Catch(() => ThrowFrom("first")), Catch(() => ThrowFrom("second")));

        var result = DiagnosticsHelper.CleanupException(exception);

        AssertThat(result).StartsWith("System.AggregateException: ");
        AssertThat(result).Contains("---> System.InvalidOperationException: first");
        AssertThat(result).Contains("---> System.InvalidOperationException: second");
    }

    [TestCase]
    public void CleanupException_ShowsAsyncMethodsAsWrittenWithoutRuntimePlumbing()
    {
        var exception = Catch(() => RunOnThreadPool(OuterAsync));

        var result = DiagnosticsHelper.CleanupException(exception);

        // The runtime's own trace names these methods; the state machines they compile to are an implementation detail.
        AssertThat(result).Contains($"{typeof(DiagnosticsHelperTest).FullName}.{nameof(InnerAsync)}(string message)");
        AssertThat(result).Contains($"{typeof(DiagnosticsHelperTest).FullName}.{nameof(OuterAsync)}()");
        AssertThat(result).NotContains("MoveNext");
        AssertThat(result).NotContains("d__");
        // [StackTraceHidden] frames that rethrow across the await.
        AssertThat(result).NotContains("ExceptionDispatchInfo");
        AssertThat(result).NotContains("TaskAwaiter");
    }

    [TestCase]
    public void CleanupException_ShowsGenericAsyncMethodsAsWritten()
    {
        var exception = Catch(() => RunOnThreadPool(() => GenericAsync(1)));

        var result = DiagnosticsHelper.CleanupException(exception);

        AssertThat(result).Contains($"{nameof(GenericAsync)}<T>(T value)");
        AssertThat(result).NotContains("MoveNext");
    }

    [TestCase]
    public void CleanupException_ShowsIteratorsAsWritten()
    {
        var exception = Catch(() =>
        {
            foreach (var _ in Iterator())
            {
            }
        });

        var result = DiagnosticsHelper.CleanupException(exception);

        AssertThat(result).Contains($"{typeof(DiagnosticsHelperTest).FullName}.{nameof(Iterator)}()");
        AssertThat(result).NotContains("MoveNext");
    }

    [TestCase]
    public void SelectTrace_KeepsTheCleanedUpTrace()
    {
        AssertThat(DiagnosticsHelper.SelectTrace("cleaned\n", 0, "   at raw")).IsEqual("cleaned\n");
        // Some frames without metadata are normal under the JIT (native frames); they do not discard a good trace.
        AssertThat(DiagnosticsHelper.SelectTrace("cleaned\n", 2, "   at raw")).IsEqual("cleaned\n");
    }

    [TestCase]
    public void SelectTrace_WhenNoFrameHadMetadata_FallsBackToTheRuntimeTrace()
    {
        // Under NativeAOT every frame lacks method metadata; an empty trace would hide where the exception came from.
        AssertThat(DiagnosticsHelper.SelectTrace("", 3, "   at Game.Player.Jump()"))
            .IsEqual("   at Game.Player.Jump()" + Environment.NewLine);
    }

    [TestCase]
    public void SelectTrace_WhenFramesWereOnlyFilteredOut_StaysEmpty()
    {
        // Every frame resolved but was logging plumbing: the raw trace would only bring that plumbing back.
        AssertThat(DiagnosticsHelper.SelectTrace("", 0, "   at ZLogger.Something()")).IsEqual("");
        AssertThat(DiagnosticsHelper.SelectTrace("", 3, null)).IsEqual("");
    }

    /// <summary>
    /// Blocks on an async method without deadlocking. The test thread is Godot's main thread, whose
    /// synchronization context only runs continuations while the main loop turns - which it does not while blocked.
    /// </summary>
    static void RunOnThreadPool(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task OuterAsync()
    {
        await InnerAsync("async boom");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task InnerAsync(string message)
    {
        await Task.Yield();
        throw new InvalidOperationException(message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<T> GenericAsync<T>(T value)
    {
        await Task.Yield();
        throw new InvalidOperationException($"generic {value}");
    }

    static IEnumerable<int> Iterator()
    {
        yield return 1;
        throw new InvalidOperationException("iterator boom");
    }

    static Exception Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            return e;
        }

        throw new InvalidOperationException("the action did not throw");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowFrom(string message) => throw new InvalidOperationException(message);

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
