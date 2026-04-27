using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Enaweg.Plugin.Internal;
using Godot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;
using Environment = System.Environment;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Enaweg.Logger;

public sealed class ZLoggerGodotDebugOptions : ZLoggerOptions
{
    public bool PrettyStacktrace { get; set; } = true;
    public bool EPluginIntegration { get; set; } = true;
}

public static class ZLoggerGodotExtensions
{
    public static ILoggingBuilder AddZLoggerGodotDebug(this ILoggingBuilder builder) =>
        builder.AddZLoggerGodotDebug(_ => { });

    public static ILoggingBuilder AddZLoggerGodotDebug(this ILoggingBuilder builder,
        Action<ZLoggerGodotDebugOptions> configure)
    {
        builder.Services.AddSingleton<ILoggerProvider, ZLoggerGodotDebugLoggerProvider>(serviceProvider =>
        {
            var options = new ZLoggerGodotDebugOptions();
            configure(options);
            return new ZLoggerGodotDebugLoggerProvider(options);
        });
        return builder;
    }
}

public class GodotDebugLogProcessor : IAsyncLogProcessor
{
    [ThreadStatic] static ArrayBufferWriter<byte>? bufferWriter;

    readonly ZLoggerGodotDebugOptions options;
    readonly IZLoggerFormatter formatter;

    public GodotDebugLogProcessor(ZLoggerGodotDebugOptions options)
    {
        this.options = options;
        formatter = options.CreateFormatter();
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }

    public void Post(IZLoggerEntry log)
    {
        try
        {
            var context = log.LogInfo.Context as GodotObject;
            var msg = FormatToString(log, formatter);

            if (log.LogInfo.Exception is not null && options.PrettyStacktrace)
            {
                var stacktrace = new StackTrace(5, true);
                msg =
                    $"{msg}{Environment.NewLine}{DiagnosticsHelper.CleanupStackTrace(stacktrace)}{Environment.NewLine}---";
            }

            switch (log.LogInfo.LogLevel)
            {
                case LogLevel.Error or LogLevel.Critical:
                    GD.PushError(context is not null ? $"(#{context.GetInstanceId()}) {msg}" : msg);
                    break;
                case LogLevel.Warning:
                    GD.PushWarning(context is not null ? $"(#{context.GetInstanceId()}) {msg}" : msg);
                    break;
                default:
                    GD.Print(context is not null ? $"(#{context.GetInstanceId()}) {msg}" : msg);
                    break;
            }
        }
        finally
        {
            log.Return();
        }
    }

    static string FormatToString(IZLoggerEntry entry, IZLoggerFormatter formatter)
    {
        bufferWriter ??= new ArrayBufferWriter<byte>();
        bufferWriter.Clear();

        formatter.FormatLogEntry(bufferWriter, entry);
        return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
    }
}

[ProviderAlias("ZLoggerGodotDebug")]
public class ZLoggerGodotDebugLoggerProvider : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
{
    readonly ZLoggerOptions options;
    readonly GodotDebugLogProcessor processor;
    IExternalScopeProvider? scopeProvider;

    public ZLoggerGodotDebugLoggerProvider(ZLoggerGodotDebugOptions options)
    {
        this.options = options;
        this.processor = new GodotDebugLogProcessor(options);

        if (options.EPluginIntegration)
        {
#if TOOLS
            
            EGlobal.Instance.Initialize(EGlobal.Instance, new EPluginLoggerFactory(this));
#endif
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ZLoggerLogger(categoryName, processor, options, options.IncludeScopes ? scopeProvider : null);
    }

    public void Dispose()
    {
        processor.DisposeAsync().AsTask().Wait();
    }

    public async ValueTask DisposeAsync()
    {
        await processor.DisposeAsync().ConfigureAwait(false);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        this.scopeProvider = scopeProvider;
    }
}