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
}
