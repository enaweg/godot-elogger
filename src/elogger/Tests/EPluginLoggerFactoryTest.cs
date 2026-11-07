using System;
using GdUnit4;
using Microsoft.Extensions.Logging;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
public class EPluginLoggerFactoryTest
{
    [TestCase]
    public void CreateLogger_PassesCategoryToProviderAndWrapsResultInEPluginLogger()
    {
        var provider = new LogRecorder();
        var sut = new EPluginLoggerFactory(provider);

        var result = sut.CreateLogger("MyCategory");

        AssertThat(provider.RequestedCategories).ContainsExactly("MyCategory");
        AssertObject(result).IsInstanceOf<EPluginLogger>();
    }

    [TestCase]
    public void CreateLogger_CalledTwice_QueriesProviderEachTime()
    {
        var provider = new LogRecorder();
        var sut = new EPluginLoggerFactory(provider);

        sut.CreateLogger("First");
        sut.CreateLogger("Second");

        AssertThat(provider.RequestedCategories).ContainsExactly("First", "Second");
    }

    [TestCase]
    public void CreateLogger_ReturnsALoggerThatWritesThroughTheProvidersLogger()
    {
        var provider = new LogRecorder();
        var sut = new EPluginLoggerFactory(provider);

        sut.CreateLogger("First").Warn("from first");
        sut.CreateLogger("Second").Error("from second");

        AssertThat(provider.Entries).ContainsExactly(
            new RecordedLog("First", LogLevel.Warning, "from first"),
            new RecordedLog("Second", LogLevel.Error, "from second"));
    }

    [TestCase]
    public void WithALoggerAccessor_CreateLogger_ResolvesNothingUntilAMessageIsLogged()
    {
        var recorder = new LogRecorder();
        var sut = new EPluginLoggerFactory(recorder.CreateLogger);

        var logger = sut.CreateLogger("MyCategory");
        AssertThat(recorder.RequestedCategories).IsEmpty();

        logger.Warn("careful");

        AssertThat(recorder.Entries).ContainsExactly(new RecordedLog("MyCategory", LogLevel.Warning, "careful"));
    }
}

#if TOOLS
[TestSuite]
public class EPluginLoggingSwitchTest
{
    [TestCase]
    public void TakeOver_WhenEPluginIsNotRunning_SwitchesNothing()
    {
        var host = new FakeEPluginLoggingHost { IsValid = false };
        var sut = new EPluginLoggingSwitch(host);
        var factory = new EPluginLoggerFactory(new LogRecorder());

        AssertBool(sut.TakeOver(factory)).IsFalse();
        sut.HandBack(factory);

        AssertThat(host.Switches).IsEmpty();
    }

    [TestCase]
    public void TakeOver_SwitchesEPluginToTheFactory()
    {
        var host = new FakeEPluginLoggingHost();
        var sut = new EPluginLoggingSwitch(host);
        var factory = new EPluginLoggerFactory(new LogRecorder());

        AssertBool(sut.TakeOver(factory)).IsTrue();

        AssertObject(host.Current).IsSame(factory);
    }

    [TestCase]
    public void HandBack_OfTheOnlyOwner_RestoresEPluginsDefaultLogging()
    {
        var host = new FakeEPluginLoggingHost();
        var sut = new EPluginLoggingSwitch(host);
        var factory = new EPluginLoggerFactory(new LogRecorder());
        sut.TakeOver(factory);

        sut.HandBack(factory);

        AssertObject(host.Current).IsInstanceOf<Enaweg.Plugin.Logging.GenericLoggerFactory>();
        AssertObject(host.Current!.CreateLogger("after")).IsInstanceOf<Enaweg.Plugin.Logging.GodotConsoleLogger>();
    }

    [TestCase]
    public void HandBack_OfTheCurrentOwner_RestoresThePreviousOwner()
    {
        var host = new FakeEPluginLoggingHost();
        var sut = new EPluginLoggingSwitch(host);
        var first = new EPluginLoggerFactory(new LogRecorder());
        var second = new EPluginLoggerFactory(new LogRecorder());
        sut.TakeOver(first);
        sut.TakeOver(second);

        sut.HandBack(second);

        AssertObject(host.Current).IsSame(first);
    }

    [TestCase]
    public void HandBack_OfAnEarlierOwner_KeepsTheCurrentOne()
    {
        var host = new FakeEPluginLoggingHost();
        var sut = new EPluginLoggingSwitch(host);
        var first = new EPluginLoggerFactory(new LogRecorder());
        var second = new EPluginLoggerFactory(new LogRecorder());
        sut.TakeOver(first);
        sut.TakeOver(second);

        sut.HandBack(first);
        AssertThat(host.Switches.Count).IsEqual(2);
        AssertObject(host.Current).IsSame(second);

        // With the earlier owner gone, the last one hands back to ePlugin's default rather than to a disposed provider.
        sut.HandBack(second);
        AssertObject(host.Current).IsInstanceOf<Enaweg.Plugin.Logging.GenericLoggerFactory>();
    }

    [TestCase]
    public void HandBack_OfAFactoryWithoutAHold_SwitchesNothing()
    {
        var host = new FakeEPluginLoggingHost();
        var sut = new EPluginLoggingSwitch(host);
        var factory = new EPluginLoggerFactory(new LogRecorder());
        sut.TakeOver(factory);
        sut.HandBack(factory);

        sut.HandBack(factory);
        sut.HandBack(new EPluginLoggerFactory(new LogRecorder()));

        AssertThat(host.Switches.Count).IsEqual(2);
    }

    [TestCase]
    public void TakeOver_WhenEPluginThrows_StillHandsBack()
    {
        var host = new FakeEPluginLoggingHost { FailWith = new InvalidOperationException("dotnet is missing") };
        var sut = new EPluginLoggingSwitch(host);
        var factory = new EPluginLoggerFactory(new LogRecorder());

        AssertThrown(() => sut.TakeOver(factory)).IsInstanceOf<InvalidOperationException>();
        host.FailWith = null;

        // ePlugin may already log through the factory, so it must be restored like any other owner's.
        sut.HandBack(factory);
        AssertObject(host.Current).IsInstanceOf<Enaweg.Plugin.Logging.GenericLoggerFactory>();
    }

    [TestCase]
    public void HandBack_AfterEPluginStopped_SwitchesNothing()
    {
        var host = new FakeEPluginLoggingHost();
        var sut = new EPluginLoggingSwitch(host);
        var factory = new EPluginLoggerFactory(new LogRecorder());
        sut.TakeOver(factory);

        // An assembly reload resets ePlugin, which then starts over with its default logging.
        host.IsValid = false;
        sut.HandBack(factory);

        AssertThat(host.Switches.Count).IsEqual(1);
    }
}
#endif
