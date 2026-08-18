using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
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
        var capture = new CapturingLogger();
        OS.AddLogger(capture);
        try
        {
            using var factory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddZLoggerLogProcessor(new GodotLogProcessor(new ZLoggerOptions()));
            });

            var logger = factory.CreateLogger("Test");
            logger.ZLogTrace($"trace-{marker}");
            logger.ZLogDebug($"debug-{marker}");
            logger.ZLogInformation($"info-{marker}");
            logger.ZLogWarning($"warn-{marker}");
            logger.ZLogError($"error-{marker}");
            logger.ZLogCritical($"critical-{marker}");
        }
        finally
        {
            OS.RemoveLogger(capture);
        }

        var entries = capture.EntriesContaining(marker);

        AssertThat(entries).ContainsExactlyInAnyOrder(
            (Message: $"TRACE: trace-{marker}", Error: false),
            (Message: $"DEBUG: debug-{marker}", Error: false),
            (Message: $"info-{marker}", Error: false),
            (Message: $"WARN: warn-{marker}", Error: false),
            (Message: $"error-{marker}", Error: true),
            (Message: $"CRITICAL: critical-{marker}", Error: true));
    }

    [TestCase]
    public void Post_WithNoneLevel_ProducesNoOutput()
    {
        var marker = Guid.NewGuid().ToString("N");
        var capture = new CapturingLogger();
        OS.AddLogger(capture);
        try
        {
            using var factory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddZLoggerLogProcessor(new GodotLogProcessor(new ZLoggerOptions()));
            });

            var logger = factory.CreateLogger("Test");
            logger.Log(LogLevel.None, $"none-{marker}");
        }
        finally
        {
            OS.RemoveLogger(capture);
        }

        AssertThat(capture.EntriesContaining(marker)).IsEmpty();
    }

    sealed partial class CapturingLogger : Godot.Logger
    {
        readonly List<(string Message, bool Error)> entries = new();
        readonly object gate = new();

        public override void _LogMessage(string message, bool error)
        {
            lock (gate)
            {
                entries.Add((message.TrimEnd('\r', '\n'), error));
            }
        }

        public List<(string Message, bool Error)> EntriesContaining(string marker)
        {
            lock (gate)
            {
                return entries.FindAll(e => e.Message.Contains(marker));
            }
        }
    }
}
