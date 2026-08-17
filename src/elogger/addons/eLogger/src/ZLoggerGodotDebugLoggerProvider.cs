using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Enaweg.Plugin.Internal;
using Godot;
using Godot.Collections;
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
    [ThreadStatic] static bool isWritingToGodot;

    internal static bool IsWritingToGodot => isWritingToGodot;

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
                var stacktrace = new StackTrace(log.LogInfo.Exception, true);
                msg =
                    $"{msg}{Environment.NewLine}{DiagnosticsHelper.CleanupStackTrace(stacktrace)}{Environment.NewLine}---";
            }

            var wasWritingToGodot = isWritingToGodot;
            isWritingToGodot = true;
            try
            {
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
                isWritingToGodot = wasWritingToGodot;
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

internal sealed partial class GodotOSLogger(ILogger logger) : Godot.Logger
{
    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Array<ScriptBacktrace> scriptBacktraces)
    {
        base._LogError(function, file, line, code, rationale, editorNotify, errorType, scriptBacktraces);
        if (GodotDebugLogProcessor.IsWritingToGodot)
        {
            return;
        }

        switch (errorType)
        {
            case (int)ErrorType.Error:
                logger.ZLogError($"{rationale}", null, function, file, line);
                break;
            case (int)ErrorType.Script:
                logger.ZLogError($"{rationale}", null, function, file, line);
                break;
            case (int)ErrorType.Shader:
                logger.ZLogError($"{rationale}", null, function, file, line);
                break;
            case (int)ErrorType.Warning:
                logger.ZLogWarning($"{rationale}", null, function, file, line);
                break;
        }
    }

    public override void _LogMessage(string message, bool error)
    {
        base._LogMessage(message, error);
        if (GodotDebugLogProcessor.IsWritingToGodot)
        {
            return;
        }

        if (error)
        {
            logger.ZLogError($"{message}");
        }
        else
        {
            logger.ZLogInformation($"{message}");
        }
    }
}

[ProviderAlias("ZLoggerGodotDebug")]
public class ZLoggerGodotDebugLoggerProvider : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
{
    readonly ZLoggerOptions options;
    readonly GodotDebugLogProcessor processor;
    readonly GodotOSLogger godotLogger;
    IExternalScopeProvider? scopeProvider;
    int isDisposed;

    public ZLoggerGodotDebugLoggerProvider(ZLoggerGodotDebugOptions options)
    {
        this.options = options;
        this.processor = new GodotDebugLogProcessor(options);

        godotLogger = new GodotOSLogger(CreateLogger("OSLogger"));
        OS.AddLogger(godotLogger);

        if (options.EPluginIntegration)
        {
#if TOOLS
            EGlobal.Instance.SwitchLogging(new EPluginLoggerFactory(this));
#endif
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ZLoggerLogger(categoryName, processor, options, options.IncludeScopes ? scopeProvider : null);
    }

    public void Dispose()
    {
        if (!TryBeginDispose())
        {
            return;
        }

        try
        {
            RemoveGodotLogger();
        }
        finally
        {
            processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose())
        {
            return;
        }

        try
        {
            RemoveGodotLogger();
        }
        finally
        {
            await processor.DisposeAsync().ConfigureAwait(false);
        }
    }

    bool TryBeginDispose()
    {
        return Interlocked.Exchange(ref isDisposed, 1) == 0;
    }

    void RemoveGodotLogger()
    {
        try
        {
            OS.RemoveLogger(godotLogger);
        }
        finally
        {
            godotLogger.Dispose();
        }
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        this.scopeProvider = scopeProvider;
    }
}
