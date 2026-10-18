using System;
using System.Linq;
using GdUnit4;
using Godot;
using Microsoft.Extensions.Logging;
using ZLogger;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class EngineErrorRoutingTest
{
    [TestCase]
    public void EngineError_ReachesOtherSinksInTheSameFactory()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        using (var factory = CreateFactory(sink))
        {
            // Force the provider (and with it the OS logger) to be created.
            factory.CreateLogger("warmup");

            GD.PushError($"engine-error-{marker}");
        }

        AssertThat(sink.Entries.Any(e => e.Message.Contains($"engine-error-{marker}")
                                         && e.Level == LogLevel.Error
                                         && e.Category == "Godot.Engine")).IsTrue();
    }

    [TestCase]
    public void EngineWarning_ReachesOtherSinksInTheSameFactory()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        using (var factory = CreateFactory(sink))
        {
            factory.CreateLogger("warmup");

            GD.PushWarning($"engine-warning-{marker}");
        }

        AssertThat(sink.Entries.Any(e => e.Message.Contains($"engine-warning-{marker}")
                                         && e.Level == LogLevel.Warning
                                         && e.Category == "Godot.Engine")).IsTrue();
    }

    [TestCase]
    public void EnginePrint_ReachesOtherSinksUnderTheOutputCategory()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        using (var factory = CreateFactory(sink))
        {
            factory.CreateLogger("warmup");

            GD.Print($"engine-print-{marker}");
            GD.PrintErr($"engine-printerr-{marker}");
        }

        AssertThat(sink.Entries.Any(e => e.Message.Contains($"engine-print-{marker}")
                                         && e.Level == LogLevel.Information
                                         && e.Category == "Godot.Output")).IsTrue();
        AssertThat(sink.Entries.Any(e => e.Message.Contains($"engine-printerr-{marker}")
                                         && e.Level == LogLevel.Error
                                         && e.Category == "Godot.Output")).IsTrue();
    }

    [TestCase]
    public void EngineCategoryPrefix_ConfiguredThroughTheBuilder_IsUsed()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        using (var factory = CreateFactory(sink, o => o.EngineCategoryPrefix = "MyGame.Godot"))
        {
            factory.CreateLogger("warmup");

            GD.PushError($"engine-error-{marker}");
        }

        AssertThat(sink.Entries.Where(e => e.Message.Contains($"engine-error-{marker}")).Select(e => e.Category))
            .ContainsExactly("MyGame.Godot.Engine");
    }

    [TestCase]
    public void ApplicationLog_IsNotEchoedBackThroughTheOsLogger()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        using (var factory = CreateFactory(sink))
        {
            var logger = factory.CreateLogger("app");
            logger.ZLogInformation($"app-info-{marker}");
            logger.ZLogWarning($"app-warning-{marker}");
            logger.ZLogError($"app-error-{marker}");
        }

        // The Godot processor prints or pushes these, which the OS logger sees again. The guard must stop them
        // from being re-logged, otherwise every application log lands in every sink twice.
        AssertThat(sink.Entries.Count(e => e.Message.Contains($"app-info-{marker}"))).IsEqual(1);
        AssertThat(sink.Entries.Count(e => e.Message.Contains($"app-warning-{marker}"))).IsEqual(1);
        AssertThat(sink.Entries.Count(e => e.Message.Contains($"app-error-{marker}"))).IsEqual(1);
    }

    [TestCase]
    public void GodotLogProcessorOutput_IsNotEchoedBackThroughTheOsLogger()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        using var factory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddZLoggerLogProcessor(new GodotLogProcessor(new ZLoggerOptions()));
            logging.AddZLoggerLogProcessor(sink);
        });
        var osLogger = new GodotOSLogger(factory.CreateLogger);
        OS.AddLogger(osLogger);
        try
        {
            var logger = factory.CreateLogger("app");
            logger.ZLogInformation($"app-info-{marker}");
            logger.ZLogError($"app-error-{marker}");
        }
        finally
        {
            OS.RemoveLogger(osLogger);
        }

        AssertThat(sink.Entries.Count(e => e.Message.Contains($"app-info-{marker}"))).IsEqual(1);
        AssertThat(sink.Entries.Count(e => e.Message.Contains($"app-error-{marker}"))).IsEqual(1);
    }

    [TestCase]
    public void EngineOutputCanBeFilteredOutWithoutLosingEngineErrors()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        using (var factory = CreateFactory(sink, filter: logging => logging.AddFilter("Godot.Output", LogLevel.None)))
        {
            factory.CreateLogger("warmup");

            GD.Print($"engine-print-{marker}");
            GD.PushError($"engine-error-{marker}");
        }

        // Splitting engine messages by category is what makes the GD.Print mirror filterable on its own.
        AssertThat(sink.Entries.Any(e => e.Message.Contains($"engine-error-{marker}"))).IsTrue();
        AssertThat(sink.Entries.Any(e => e.Message.Contains($"engine-print-{marker}"))).IsFalse();
    }

    static ILoggerFactory CreateFactory(MemorySink sink, Action<ZLoggerGodotDebugOptions>? configure = null,
        Action<ILoggingBuilder>? filter = null) =>
        LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            filter?.Invoke(logging);
            logging.AddZLoggerGodotDebug(o =>
            {
                o.EPluginIntegration = false;
                configure?.Invoke(o);
            });
            logging.AddZLoggerLogProcessor(sink);
        });
}
