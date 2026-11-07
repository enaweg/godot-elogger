using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GdUnit4;
using Godot;
using Microsoft.Extensions.Logging;
using ZLogger;
using static GdUnit4.Assertions;
using ErrorType = Godot.Logger.ErrorType;

namespace Enaweg.Logger.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class GodotDebugLogProcessorTest
{
    [TestCase]
    public void Post_RoutesEachLogLevelToTheExpectedGodotOutput()
    {
        var marker = Guid.NewGuid().ToString("N");

        var output = Capture(new ZLoggerGodotDebugOptions(), logger =>
        {
            logger.ZLogTrace($"trace-{marker}");
            logger.ZLogDebug($"debug-{marker}");
            logger.ZLogInformation($"info-{marker}");
            logger.ZLogWarning($"warn-{marker}");
            logger.ZLogError($"error-{marker}");
            logger.ZLogCritical($"critical-{marker}");
        }, marker);

        // Warnings and errors are pushed so they show up in the editor's Debugger dock; the rest is printed.
        AssertThat(output).ContainsExactlyInAnyOrder(
            new GodotOutput($"trace-{marker}", false, null),
            new GodotOutput($"debug-{marker}", false, null),
            new GodotOutput($"info-{marker}", false, null),
            new GodotOutput($"warn-{marker}", true, ErrorType.Warning),
            new GodotOutput($"error-{marker}", true, ErrorType.Error),
            new GodotOutput($"critical-{marker}", true, ErrorType.Error));
    }

    [TestCase]
    public void Post_WithAGodotObjectContext_PrefixesTheInstanceId()
    {
        var marker = Guid.NewGuid().ToString("N");
        var context = new RefCounted();

        var output = Capture(new ZLoggerGodotDebugOptions(),
            logger => logger.ZLogInformation($"with-context-{marker}", context), marker);

        AssertThat(output.Select(o => o.Text))
            .ContainsExactly($"(#{context.GetInstanceId()}) with-context-{marker}");
    }

    [TestCase]
    public void Post_WithAFreedGodotObjectContext_LogsWithoutThrowing()
    {
        var marker = Guid.NewGuid().ToString("N");
        var context = new Node();
        context.Free();

        var output = Capture(new ZLoggerGodotDebugOptions(),
            logger => logger.ZLogInformation($"freed-context-{marker}", context), marker);

        AssertThat(output.Select(o => o.Text)).ContainsExactly($"(#freed) freed-context-{marker}");
    }

    [TestCase]
    public void Post_WithANonGodotContext_DoesNotPrefix()
    {
        var marker = Guid.NewGuid().ToString("N");

        var output = Capture(new ZLoggerGodotDebugOptions(),
            logger => logger.ZLogInformation($"plain-context-{marker}", "not a GodotObject"), marker);

        AssertThat(output.Select(o => o.Text)).ContainsExactly($"plain-context-{marker}");
    }

    [TestCase]
    public void Post_WithPrettyStacktrace_AppendsTheCleanedUpTrace()
    {
        var marker = Guid.NewGuid().ToString("N");
        var exception = ThrowAndCatch();

        var output = Capture(new ZLoggerGodotDebugOptions { PrettyStacktrace = true },
            logger => logger.ZLogError(exception, $"failed-{marker}"), marker);

        var text = output.Single().Text;
        AssertThat(text).StartsWith($"failed-{marker}");
        AssertThat(text).Contains("System.InvalidOperationException: boom");
        AssertThat(text).Contains($"void {typeof(GodotDebugLogProcessorTest).FullName}.{nameof(ThrowingFrame)}()");
        AssertThat(text).EndsWith("---");
    }

    [TestCase]
    public void Post_WithPrettyStacktrace_DoesNotAlsoPrintTheRawTrace()
    {
        var marker = Guid.NewGuid().ToString("N");
        var exception = ThrowAndCatch();

        var output = Capture(new ZLoggerGodotDebugOptions { PrettyStacktrace = true },
            logger => logger.ZLogError(exception, $"failed-{marker}"), marker);

        // The plain-text formatter appends exception.ToString(), whose frames start with "   at ".
        var text = output.Single().Text;
        AssertThat(text).NotContains("   at ");
        AssertThat(Regex.Matches(text, "System.InvalidOperationException: boom").Count).IsEqual(1);
    }

    [TestCase]
    public void Post_WithPrettyStacktraceAndAJsonFormatter_PrintsTheExceptionOnce()
    {
        var marker = Guid.NewGuid().ToString("N");
        var exception = ThrowAndCatch();
        var options = new ZLoggerGodotDebugOptions { PrettyStacktrace = true };
        options.UseJsonFormatter();

        var output = Capture(options, logger => logger.ZLogError(exception, $"failed-{marker}"), marker);

        // The JSON formatter writes the exception into its own structure; the cleaned-up trace must not follow it.
        var text = output.Single().Text;
        AssertThat(text).StartsWith("{");
        AssertThat(Regex.Matches(text, "boom").Count).IsEqual(1);
        AssertThat(text).NotContains($"void {typeof(GodotDebugLogProcessorTest).FullName}.{nameof(ThrowingFrame)}()");
        AssertThat(text).NotContains("---");
    }

    [TestCase]
    public void Post_WithoutPrettyStacktrace_LeavesTheFormatterTraceAlone()
    {
        var marker = Guid.NewGuid().ToString("N");
        var exception = ThrowAndCatch();

        var output = Capture(new ZLoggerGodotDebugOptions { PrettyStacktrace = false },
            logger => logger.ZLogError(exception, $"failed-{marker}"), marker);

        var text = output.Single().Text;
        AssertThat(text).NotContains("---");
        AssertThat(text).Contains("System.InvalidOperationException: boom");
        AssertThat(text).Contains("   at ");
    }

    [TestCase]
    public void Post_WithPrettyStacktraceButNoException_LeavesTheMessageAlone()
    {
        var marker = Guid.NewGuid().ToString("N");

        var output = Capture(new ZLoggerGodotDebugOptions { PrettyStacktrace = true },
            logger => logger.ZLogError($"no-exception-{marker}"), marker);

        AssertThat(output.Select(o => o.Text)).ContainsExactly($"no-exception-{marker}");
    }

    static GodotOutput[] Capture(ZLoggerGodotDebugOptions options, Action<ILogger> log, string marker)
    {
        var output = GodotOutputCapture.During(() =>
        {
            using var factory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddZLoggerLogProcessor(new GodotDebugLogProcessor(options));
            });
            log(factory.CreateLogger("Test"));
        });

        return output.Where(o => o.Text.Contains(marker)).ToArray();
    }

    static Exception ThrowAndCatch()
    {
        try
        {
            ThrowingFrame();
            throw new InvalidOperationException("unreachable");
        }
        catch (Exception e)
        {
            return e;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowingFrame() => throw new InvalidOperationException("boom");
}
