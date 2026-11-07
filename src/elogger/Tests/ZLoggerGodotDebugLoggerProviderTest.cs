using System;
using System.Linq;
using GdUnit4;
using Godot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZLogger;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class ZLoggerGodotDebugLoggerProviderTest
{
    [TestCase]
    public void CreateLogger_BeforeDispose_Succeeds()
    {
        using var provider = CreateProvider();

        AssertThat(provider.CreateLogger("before-dispose")).IsNotNull();
    }

    [TestCase]
    public void CreateLogger_AfterDispose_Throws()
    {
        var provider = CreateProvider();
        provider.Dispose();

        AssertThrown(() => provider.CreateLogger("after-dispose"))
            .IsInstanceOf<ObjectDisposedException>();
    }

    [TestCase]
    public void CreateLogger_AfterDisposeAsync_Throws()
    {
        var provider = CreateProvider();
        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();

        AssertThrown(() => provider.CreateLogger("after-dispose"))
            .IsInstanceOf<ObjectDisposedException>();
    }

    [TestCase]
    public void Dispose_CalledRepeatedly_DoesNotThrow()
    {
        var provider = CreateProvider();

        // A DI container and user code can both dispose the provider; only the first call may tear down.
        provider.Dispose();
        provider.Dispose();
        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestCase]
    public void Dispose_RemovesTheEngineLoggerFromGodot()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sink = new MemorySink();

        var factory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddZLoggerGodotDebug(o => o.EPluginIntegration = false);
            logging.AddZLoggerLogProcessor(sink);
        });
        factory.CreateLogger("warmup");
        GD.PushError($"before-dispose-{marker}");
        factory.Dispose();

        // The engine logger caches its category loggers, so if it stayed registered with Godot this would still
        // reach the sink - and every disposed provider would keep logging for the rest of the process.
        GD.PushError($"after-dispose-{marker}");

        AssertThat(sink.Entries.Any(e => e.Message.Contains($"before-dispose-{marker}"))).IsTrue();
        AssertThat(sink.Entries.Any(e => e.Message.Contains($"after-dispose-{marker}"))).IsFalse();
    }

    [TestCase]
    public void EngineLoggerAccessor_WithoutServiceProvider_ReusesOneLogger()
    {
        using var provider = CreateProvider();

        var accessor = provider.CreateEngineLoggerAccessor(null);

        // The accessor runs per engine message; it must not allocate a logger each time.
        AssertBool(ReferenceEquals(accessor("Godot.Engine"), accessor("Godot.Engine"))).IsTrue();
        // Different categories must not share a logger.
        AssertBool(ReferenceEquals(accessor("Godot.Engine"), accessor("Godot.Shader"))).IsFalse();
    }

    [TestCase]
    public void EngineLoggerAccessor_WithServiceProvider_UsesTheApplicationFactoryOncePerCategory()
    {
        using var provider = CreateProvider();
        var factory = new LogRecorder();
        var accessor = provider.CreateEngineLoggerAccessor(new FlakyServiceProvider(factory, failures: 0));

        var first = accessor("Godot.Engine");
        var second = accessor("Godot.Engine");
        accessor("Godot.Shader");

        AssertBool(ReferenceEquals(first, second)).IsTrue();
        AssertThat(factory.RequestedCategories).ContainsExactly("Godot.Engine", "Godot.Shader");
    }

    [TestCase]
    public void EngineLoggerAccessor_WhenTheFactoryIsNotReady_FallsBackAndRetriesLater()
    {
        using var provider = CreateProvider();
        var factory = new LogRecorder();
        var services = new FlakyServiceProvider(factory, failures: 1);
        var accessor = provider.CreateEngineLoggerAccessor(services);

        // The factory depends on every provider, so an engine message during start-up cannot reach it yet.
        var early = accessor("Godot.Engine");
        AssertThat(early).IsNotNull();
        AssertThat(factory.RequestedCategories).IsEmpty();

        // The fallback must not be cached, or engine messages would never reach the other sinks.
        var later = accessor("Godot.Engine");
        accessor("Godot.Engine");

        AssertBool(ReferenceEquals(early, later)).IsFalse();
        AssertThat(factory.RequestedCategories).ContainsExactly("Godot.Engine");
        AssertThat(services.FactoryRequests).IsEqual(2);
    }

    [TestCase]
    public void EngineLoggerAccessor_AfterDispose_DoesNotThrowIntoTheEngine()
    {
        var provider = CreateProvider();
        var accessor = provider.CreateEngineLoggerAccessor(null);
        provider.Dispose();

        // An engine error racing with disposal must not push ObjectDisposedException into a Godot callback.
        // This throws, and so fails the test, if the accessor is not disposal-safe.
        AssertBool(ReferenceEquals(accessor("Godot.Engine"), NullLogger.Instance)).IsTrue();
    }

    [TestCase]
    public void EngineLogger_AfterDispose_SwallowsMessagesInsteadOfThrowing()
    {
        var provider = CreateProvider();
        var sut = new GodotOSLogger(provider.CreateEngineLoggerAccessor(null));
        provider.Dispose();

        // Throws out of the Godot callback, and so fails the test, if the fallback is not disposal-safe.
        sut._LogMessage("after dispose", true);
    }

#if TOOLS
    [TestCase]
    public void Dispose_WithEPluginIntegration_HandsEPluginLoggingBack()
    {
        var host = new FakeEPluginLoggingHost();
        WithEPluginLoggingSwitch(new EPluginLoggingSwitch(host), () =>
        {
            var provider = new ZLoggerGodotDebugLoggerProvider(new ZLoggerGodotDebugOptions());
            AssertObject(host.Current).IsInstanceOf<EPluginLoggerFactory>();

            provider.Dispose();

            // ePlugin must not keep a factory whose CreateLogger throws ObjectDisposedException.
            AssertObject(host.Current).IsInstanceOf<Enaweg.Plugin.Logging.GenericLoggerFactory>();
            host.Current!.CreateLogger("after dispose").Log("still logging");
        });
    }

    [TestCase]
    public void Constructor_WhenEPluginFailsToSwitch_StillLogsAndLeavesEPluginOnItsDefault()
    {
        var marker = Guid.NewGuid().ToString("N");
        var host = new FakeEPluginLoggingHost { FailNextSwitch = true };
        var sink = new MemorySink();
        WithEPluginLoggingSwitch(new EPluginLoggingSwitch(host), () =>
        {
            // The integration is optional: a failure in ePlugin must not fail the application's logging setup.
            var factory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddZLoggerGodotDebug();
                logging.AddZLoggerLogProcessor(sink);
            });
            AssertObject(host.Current).IsInstanceOf<Enaweg.Plugin.Logging.GenericLoggerFactory>();

            // The engine logger is installed regardless.
            factory.CreateLogger("warmup");
            GD.PushError($"engine-{marker}");
            var switches = host.Switches.Count;
            factory.Dispose();

            // Nothing was taken over, so disposing does not switch ePlugin again.
            AssertThat(host.Switches.Count).IsEqual(switches);
        });

        AssertThat(sink.Entries.Any(e => e.Message.Contains($"engine-{marker}"))).IsTrue();
    }

    [TestCase]
    public void DisposeAsync_WithEPluginIntegration_HandsEPluginLoggingBack()
    {
        var host = new FakeEPluginLoggingHost();
        WithEPluginLoggingSwitch(new EPluginLoggingSwitch(host), () =>
        {
            var provider = new ZLoggerGodotDebugLoggerProvider(new ZLoggerGodotDebugOptions());

            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();

            AssertObject(host.Current).IsInstanceOf<Enaweg.Plugin.Logging.GenericLoggerFactory>();
        });
    }

    [TestCase]
    public void Dispose_OfAnEarlierProvider_KeepsEPluginOnTheLaterOne()
    {
        var host = new FakeEPluginLoggingHost();
        WithEPluginLoggingSwitch(new EPluginLoggingSwitch(host), () =>
        {
            var first = new ZLoggerGodotDebugLoggerProvider(new ZLoggerGodotDebugOptions());
            using var second = new ZLoggerGodotDebugLoggerProvider(new ZLoggerGodotDebugOptions());
            var current = host.Current!;

            first.Dispose();

            AssertObject(host.Current).IsSame(current);
        });
    }

    [TestCase]
    public void Provider_WithoutEPluginIntegration_LeavesEPluginAlone()
    {
        var host = new FakeEPluginLoggingHost();
        WithEPluginLoggingSwitch(new EPluginLoggingSwitch(host), () =>
        {
            CreateProvider().Dispose();

            AssertThat(host.Switches).IsEmpty();
        });
    }

    [TestCase]
    public void EPluginMessages_ReachOtherSinksAndHonourFilters()
    {
        var marker = Guid.NewGuid().ToString("N");
        var host = new FakeEPluginLoggingHost();
        var sink = new MemorySink();
        WithEPluginLoggingSwitch(new EPluginLoggingSwitch(host), () =>
        {
            using var factory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddFilter("Enaweg.Plugin.Noisy", LogLevel.Warning);
                logging.AddZLoggerGodotDebug();
                logging.AddZLoggerLogProcessor(sink);
            });

            // ePlugin got this logger while the factory was still being built.
            host.Logger!.Log($"framework-{marker}");
            var noisy = host.Current!.CreateLogger("Enaweg.Plugin.Noisy");
            noisy.Log($"noisy-info-{marker}");
            noisy.Warn($"noisy-warning-{marker}");
        });

        AssertThat(sink.Entries.Where(e => e.Message.Contains(marker)).Select(e => (e.Category, e.Message)))
            .ContainsExactly(
                ("Enaweg.Plugin.EPluginPlugin", $"framework-{marker}"),
                ("Enaweg.Plugin.Noisy", $"noisy-warning-{marker}"));
    }

    static void WithEPluginLoggingSwitch(EPluginLoggingSwitch ePluginLogging, Action action)
    {
        var global = EPluginLoggingSwitch.Global;
        EPluginLoggingSwitch.Global = ePluginLogging;
        try
        {
            action();
        }
        finally
        {
            EPluginLoggingSwitch.Global = global;
        }
    }
#endif

    static ZLoggerGodotDebugLoggerProvider CreateProvider() =>
        new(new ZLoggerGodotDebugOptions { EPluginIntegration = false });

    /// <summary>Hands out the logger factory, but throws for the first <c>failures</c> requests.</summary>
    sealed class FlakyServiceProvider : IServiceProvider
    {
        readonly ILoggerFactory factory;
        int failures;

        public FlakyServiceProvider(ILoggerFactory factory, int failures)
        {
            this.factory = factory;
            this.failures = failures;
        }

        public int FactoryRequests { get; private set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType != typeof(ILoggerFactory))
            {
                return null;
            }

            FactoryRequests++;
            if (failures > 0)
            {
                failures--;
                throw new InvalidOperationException("Logger factory is still being constructed.");
            }

            return factory;
        }
    }
}
