using System;
using System.Linq;
using System.Runtime.CompilerServices;
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
        AssertThat(text).Contains($"void {typeof(GodotDebugLogProcessorTest).FullName}.{nameof(ThrowingFrame)}()");
        AssertThat(text).EndsWith("---");
    }

    [TestCase]
    public void Post_WithoutPrettyStacktrace_LeavesTheMessageAlone()
    {
        var marker = Guid.NewGuid().ToString("N");
        var exception = ThrowAndCatch();

        var output = Capture(new ZLoggerGodotDebugOptions { PrettyStacktrace = false },
            logger => logger.ZLogError(exception, $"failed-{marker}"), marker);

        AssertThat(output.Single().Text).NotContains("---");
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
