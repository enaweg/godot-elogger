using System;
using System.Linq;
using GdUnit4;
using Microsoft.Extensions.Logging;
using ZLogger;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class GodotLogProcessorTest
{
    [TestCase]
    public void Post_RoutesEachLogLevelToTheExpectedGodotOutput()
    {
        var marker = Guid.NewGuid().ToString("N");

        var output = Capture(logger =>
        {
            logger.ZLogTrace($"trace-{marker}");
            logger.ZLogDebug($"debug-{marker}");
            logger.ZLogInformation($"info-{marker}");
            logger.ZLogWarning($"warn-{marker}");
            logger.ZLogError($"error-{marker}");
            logger.ZLogCritical($"critical-{marker}");
        }, marker);

        AssertThat(output).ContainsExactlyInAnyOrder(
            new GodotOutput($"TRACE: trace-{marker}", false, null),
            new GodotOutput($"DEBUG: debug-{marker}", false, null),
            new GodotOutput($"info-{marker}", false, null),
            new GodotOutput($"WARN: warn-{marker}", false, null),
            new GodotOutput($"error-{marker}", true, null),
            new GodotOutput($"CRITICAL: critical-{marker}", true, null));
    }

    [TestCase]
    public void Post_WithNoneLevel_ProducesNoOutput()
    {
        var marker = Guid.NewGuid().ToString("N");

        var output = Capture(logger => logger.Log(LogLevel.None, $"none-{marker}"), marker);

        AssertThat(output).IsEmpty();
    }

    [TestCase]
    public void Post_WithAnUnknownLevel_PrintsThePlainMessage()
    {
        var marker = Guid.NewGuid().ToString("N");

        // An out-of-range level must never take down the caller's log statement.
        var output = Capture(logger => logger.Log((LogLevel)42, $"unknown-{marker}"), marker);

        AssertThat(output).ContainsExactly(new GodotOutput($"unknown-{marker}", false, null));
    }

    static GodotOutput[] Capture(Action<ILogger> log, string marker)
    {
        var output = GodotOutputCapture.During(() =>
        {
            using var factory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddZLoggerLogProcessor(new GodotLogProcessor(new ZLoggerOptions()));
            });
            log(factory.CreateLogger("Test"));
        });

        return output.Where(o => o.Text.Contains(marker)).ToArray();
    }
}
