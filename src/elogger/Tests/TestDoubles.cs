using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace Enaweg.Logger.Tests;

internal readonly record struct RecordedLog(string Category, LogLevel Level, string Message);

/// <summary>
/// In-memory <see cref="ILoggerFactory" /> / <see cref="ILoggerProvider" /> that records every entry with the
/// category of the logger it was written through, and every category a logger was requested for.
/// </summary>
internal sealed class LogRecorder : ILoggerFactory, ILoggerProvider
{
    readonly List<RecordedLog> entries = new();
    readonly List<string> requestedCategories = new();

    public IReadOnlyList<RecordedLog> Entries
    {
        get
        {
            lock (entries) return entries.ToArray();
        }
    }

    public IReadOnlyList<string> RequestedCategories
    {
        get
        {
            lock (requestedCategories) return requestedCategories.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        lock (requestedCategories) requestedCategories.Add(categoryName);
        return new RecordingLogger(this, categoryName);
    }

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public void Dispose()
    {
    }

    sealed class RecordingLogger : ILogger
    {
        readonly LogRecorder owner;
        readonly string category;

        public RecordingLogger(LogRecorder owner, string category)
        {
            this.owner = owner;
            this.category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner.entries) owner.entries.Add(new RecordedLog(category, logLevel, formatter(state, exception)));
        }
    }
}

internal readonly record struct SinkEntry(
    string Category,
    LogLevel Level,
    string Message,
    string? MemberName,
    string? FilePath,
    int LineNumber);

/// <summary>ZLogger processor that keeps every formatted entry, for asserting on what reached a sink.</summary>
internal sealed class MemorySink : IAsyncLogProcessor
{
    readonly IZLoggerFormatter formatter = new ZLoggerOptions().CreateFormatter();
    readonly List<SinkEntry> entries = new();

    public IReadOnlyList<SinkEntry> Entries
    {
        get
        {
            lock (entries) return entries.ToArray();
        }
    }

    public ValueTask DisposeAsync() => default;

    public void Post(IZLoggerEntry log)
    {
        try
        {
            var writer = new ArrayBufferWriter<byte>();
            formatter.FormatLogEntry(writer, log);
            var info = log.LogInfo;
            var entry = new SinkEntry(info.Category.Name, info.LogLevel, Encoding.UTF8.GetString(writer.WrittenSpan),
                info.MemberName, info.FilePath, info.LineNumber);
            lock (entries) entries.Add(entry);
        }
        finally
        {
            log.Return();
        }
    }
}

/// <summary>What a <see cref="GodotOutputCapture" /> saw: printed output has no error type.</summary>
internal readonly record struct GodotOutput(string Text, bool IsError, Godot.Logger.ErrorType? ErrorType);

/// <summary>
/// Godot logger that records engine output - prints through <c>_LogMessage</c>, pushed errors and warnings
/// through <c>_LogError</c> - while an action runs.
/// </summary>
internal sealed partial class GodotOutputCapture : Godot.Logger
{
    readonly List<GodotOutput> entries = new();

    public static IReadOnlyList<GodotOutput> During(Action action)
    {
        var capture = new GodotOutputCapture();
        OS.AddLogger(capture);
        try
        {
            action();
        }
        finally
        {
            OS.RemoveLogger(capture);
        }

        lock (capture.entries) return capture.entries.ToArray();
    }

    public override void _LogMessage(string message, bool error)
    {
        lock (entries) entries.Add(new GodotOutput(message.TrimEnd('\r', '\n'), error, null));
    }

    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Array<ScriptBacktrace> scriptBacktraces)
    {
        var text = string.IsNullOrEmpty(rationale) ? code : rationale;
        lock (entries) entries.Add(new GodotOutput(text, true, (ErrorType)errorType));
    }
}
