using System;
using System.Collections.Generic;
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
        var recorder = new RecordingLogger();
        var sut = new EPluginLogger(recorder);

        sut.Log("hello world");

        AssertThat(recorder.Entries).HasSize(1);
        AssertThat(recorder.Entries[0].Level).IsEqual(LogLevel.Information);
        AssertThat(recorder.Entries[0].Message).IsEqual("hello world");
    }

    [TestCase]
    public void Warn_ForwardsMessageAtWarningLevel()
    {
        var recorder = new RecordingLogger();
        var sut = new EPluginLogger(recorder);

        sut.Warn("careful");

        AssertThat(recorder.Entries).HasSize(1);
        AssertThat(recorder.Entries[0].Level).IsEqual(LogLevel.Warning);
        AssertThat(recorder.Entries[0].Message).IsEqual("careful");
    }

    [TestCase]
    public void Error_ForwardsMessageAtErrorLevel()
    {
        var recorder = new RecordingLogger();
        var sut = new EPluginLogger(recorder);

        sut.Error("boom");

        AssertThat(recorder.Entries).HasSize(1);
        AssertThat(recorder.Entries[0].Level).IsEqual(LogLevel.Error);
        AssertThat(recorder.Entries[0].Message).IsEqual("boom");
    }

    sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
