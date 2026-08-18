using System;
using System.Collections.Generic;
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
        var provider = new RecordingLoggerProvider();
        var sut = new EPluginLoggerFactory(provider);

        var result = sut.CreateLogger("MyCategory");

        AssertThat(provider.RequestedCategories).ContainsExactly("MyCategory");
        AssertObject(result).IsInstanceOf<EPluginLogger>();
    }

    [TestCase]
    public void CreateLogger_CalledTwice_QueriesProviderEachTime()
    {
        var provider = new RecordingLoggerProvider();
        var sut = new EPluginLoggerFactory(provider);

        sut.CreateLogger("First");
        sut.CreateLogger("Second");

        AssertThat(provider.RequestedCategories).ContainsExactly("First", "Second");
    }

    sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> RequestedCategories { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            RequestedCategories.Add(categoryName);
            return new NoOpLogger();
        }

        public void Dispose()
        {
        }
    }

    sealed class NoOpLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
