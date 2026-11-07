using GdUnit4;
using Microsoft.Extensions.Logging;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
public class EPluginLoggerTest
{
    [TestCase]
    public void Log_ForwardsMessageAtInformationLevel()
    {
        var recorder = new LogRecorder();
        var sut = new EPluginLogger(recorder.CreateLogger("ePlugin"));

        sut.Log("hello world");

        AssertThat(recorder.Entries).ContainsExactly(new RecordedLog("ePlugin", LogLevel.Information, "hello world"));
    }

    [TestCase]
    public void Warn_ForwardsMessageAtWarningLevel()
    {
        var recorder = new LogRecorder();
        var sut = new EPluginLogger(recorder.CreateLogger("ePlugin"));

        sut.Warn("careful");

        AssertThat(recorder.Entries).ContainsExactly(new RecordedLog("ePlugin", LogLevel.Warning, "careful"));
    }

    [TestCase]
    public void Error_ForwardsMessageAtErrorLevel()
    {
        var recorder = new LogRecorder();
        var sut = new EPluginLogger(recorder.CreateLogger("ePlugin"));

        sut.Error("boom");

        AssertThat(recorder.Entries).ContainsExactly(new RecordedLog("ePlugin", LogLevel.Error, "boom"));
    }

    [TestCase]
    public void Log_KeepsMessageTemplateSyntaxLiteral()
    {
        var recorder = new LogRecorder();
        var sut = new EPluginLogger(recorder.CreateLogger("ePlugin"));

        // ePlugin messages are finished text (paths, dotnet output); braces must not be read as placeholders.
        sut.Log("installed {Package} into {0} at C:\\{dir}");

        AssertThat(recorder.Entries[0].Message).IsEqual("installed {Package} into {0} at C:\\{dir}");
    }

    [TestCase]
    public void WithALoggerAccessor_ResolvesTheLoggerForEveryMessage()
    {
        var recorder = new LogRecorder();
        var requests = 0;
        var sut = new EPluginLogger("ePlugin", category =>
        {
            requests++;
            return recorder.CreateLogger(category);
        });

        // ePlugin creates its loggers before the application's factory is ready, so nothing is resolved up front.
        AssertThat(requests).IsEqual(0);

        sut.Log("first");
        sut.Error("second");

        AssertThat(requests).IsEqual(2);
        AssertThat(recorder.Entries).ContainsExactly(
            new RecordedLog("ePlugin", LogLevel.Information, "first"),
            new RecordedLog("ePlugin", LogLevel.Error, "second"));
    }
}
