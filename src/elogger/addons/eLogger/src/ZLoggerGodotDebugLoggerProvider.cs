using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZLogger;
using ZLogger.Formatters;
using Environment = System.Environment;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Enaweg.Logger;

public sealed class ZLoggerGodotDebugOptions : ZLoggerOptions
{
    /// <summary>
    /// Print logged exceptions with cleaned-up stack traces that link to <c>res://</c> paths. The plain-text
    /// formatter's exception output is replaced by this, so a custom exception formatter set through
    /// <c>UsePlainTextFormatter</c> only applies when this is <c>false</c>.
    /// </summary>
    public bool PrettyStacktrace { get; set; } = true;
    public bool EPluginIntegration { get; set; } = true;

    /// <summary>
    /// Root of the categories that intercepted engine messages are logged under. Godot reports an error type with
    /// every diagnostic, and each type gets its own category below this root, so with the default prefix messages
    /// arrive as "Godot.Engine", "Godot.Script", "Godot.Shader" and "Godot.Output". Standard category filtering
    /// then applies to one kind or to all of them:
    /// <code>
    /// logging.AddFilter("Godot.Output", LogLevel.None);   // drop the GD.Print mirror
    /// logging.AddFilter("Godot.Shader", LogLevel.None);   // drop shader diagnostics
    /// logging.AddFilter("Godot", LogLevel.Warning);       // quieten every engine message
    /// </code>
    /// Change the prefix if "Godot" would collide with categories the application already uses.
    /// </summary>
    public string EngineCategoryPrefix { get; set; } = GodotOSLogger.DefaultCategoryPrefix;
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
            return new ZLoggerGodotDebugLoggerProvider(options, serviceProvider);
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

        // The plain-text formatter appends exception.ToString(), so the trace would be printed twice: raw by the
        // formatter, then cleaned up by Post. Leave the exception to Post. Every CreateFormatter call returns a new
        // instance, so this does not affect other processors built from the same options.
        if (options.PrettyStacktrace && formatter is PlainTextZLoggerFormatter plainText)
        {
            plainText.SetExceptionFormatter(static (_, _) => { });
        }
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

            if (log.LogInfo.Exception is { } exception && options.PrettyStacktrace)
            {
                msg =
                    $"{msg}{Environment.NewLine}{DiagnosticsHelper.CleanupException(exception)}{Environment.NewLine}---";
            }

            if (context is not null)
            {
                // A context can be logged after its native object was freed, e.g. from an async continuation after
                // QueueFree. GetInstanceId would throw ObjectDisposedException then, and the logging pipeline would
                // re-throw it into the code that logged.
                msg = GodotObject.IsInstanceValid(context)
                    ? $"(#{context.GetInstanceId()}) {msg}"
                    : $"(#freed) {msg}";
            }

            using var _ = GodotLogGuard.Enter();
            switch (log.LogInfo.LogLevel)
            {
                case LogLevel.Error or LogLevel.Critical:
                    GD.PushError(msg);
                    break;
                case LogLevel.Warning:
                    GD.PushWarning(msg);
                    break;
                default:
                    GD.Print(msg);
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

internal sealed partial class GodotOSLogger : Godot.Logger
{
    internal const string DefaultCategoryPrefix = "Godot";

    readonly Func<string, ILogger> loggerAccessor;

    // Built once: resolving a category per message would allocate on a path that runs for every GD.Print.
    readonly string engineCategory;
    readonly string scriptCategory;
    readonly string shaderCategory;
    readonly string outputCategory;

    public GodotOSLogger(ILogger logger, string categoryPrefix = DefaultCategoryPrefix)
        : this(_ => logger, categoryPrefix)
    {
    }

    public GodotOSLogger(Func<string, ILogger> loggerAccessor, string categoryPrefix = DefaultCategoryPrefix)
    {
        this.loggerAccessor = loggerAccessor;
        engineCategory = $"{categoryPrefix}.Engine";
        scriptCategory = $"{categoryPrefix}.Script";
        shaderCategory = $"{categoryPrefix}.Shader";
        outputCategory = $"{categoryPrefix}.Output";
    }

    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Array<ScriptBacktrace> scriptBacktraces)
    {
        base._LogError(function, file, line, code, rationale, editorNotify, errorType, scriptBacktraces);
        if (GodotLogGuard.IsWriting)
        {
            return;
        }

        // Godot puts the failing expression in "code" and the optional explanatory message in "rationale".
        // GD.PushError/GD.PushWarning and the ERR_*_MSG macros leave "rationale" empty, so preferring it
        // unconditionally logged blank entries. Mirror the engine's own Logger::log_error and fall back to "code".
        var details = string.IsNullOrEmpty(rationale) ? code : rationale;

        // Godot classifies every diagnostic, so keep that classification in the category rather than flattening
        // engine, script and shader problems into one bucket.
        var category = errorType switch
        {
            (int)ErrorType.Script => scriptCategory,
            (int)ErrorType.Shader => shaderCategory,
            _ => engineCategory
        };

        var logger = loggerAccessor(category);
        if (errorType == (int)ErrorType.Warning)
        {
            logger.ZLogWarning($"{details}", null, function, file, line);
        }
        else
        {
            // Anything Godot adds to the enum later is still a diagnostic; report it rather than drop it.
            logger.ZLogError($"{details}", null, function, file, line);
        }
    }

    public override void _LogMessage(string message, bool error)
    {
        base._LogMessage(message, error);
        if (GodotLogGuard.IsWriting)
        {
            return;
        }

        // Godot hands over printed text with its line break attached. Sinks end every entry themselves, so keeping it
        // would add a blank line after each mirrored GD.Print. Reassigned rather than copied into a new local so the
        // structured parameter keeps its "message" name. TrimEnd returns the same instance when there is nothing to trim.
        message = message.TrimEnd('\r', '\n');

        var logger = loggerAccessor(outputCategory);
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
    readonly ConcurrentDictionary<string, ILogger> engineLoggers = new();
    readonly ZLoggerOptions options;
    readonly GodotDebugLogProcessor processor;
    readonly GodotOSLogger godotLogger;
    IExternalScopeProvider? scopeProvider;
    int isDisposed;
#if TOOLS
    readonly EPluginLoggingSwitch? ePluginLogging;
    readonly EPluginLoggerFactory? ePluginLoggerFactory;
#endif

    public ZLoggerGodotDebugLoggerProvider(ZLoggerGodotDebugOptions options) : this(options, null)
    {
    }

    public ZLoggerGodotDebugLoggerProvider(ZLoggerGodotDebugOptions options, IServiceProvider? serviceProvider)
    {
        this.options = options;
        this.processor = new GodotDebugLogProcessor(options);

        var loggerAccessor = CreateEngineLoggerAccessor(serviceProvider);
        godotLogger = new GodotOSLogger(loggerAccessor, options.EngineCategoryPrefix);
        OS.AddLogger(godotLogger);

        if (options.EPluginIntegration)
        {
#if TOOLS
            // ePlugin only exists while the editor is running its plugins; in a game build the integration is
            // compiled out, and in a game run from the editor TakeOver finds no ePlugin and switches nothing.
            // Like engine messages, ePlugin's go through the application's factory, so they reach every sink and
            // honour its filters rather than only being printed by this provider.
            var ePluginFactory = new EPluginLoggerFactory(loggerAccessor);
            var ePluginSwitch = EPluginLoggingSwitch.Global;
            if (ePluginSwitch.TakeOver(ePluginFactory))
            {
                ePluginLogging = ePluginSwitch;
                ePluginLoggerFactory = ePluginFactory;
            }
#endif
        }
    }

    /// <summary>
    /// Builds the accessor that maps a category of engine messages, or of ePlugin's messages, onto the logger it is
    /// written to.
    /// <para>
    /// These messages belong in every sink the application configured, not just this provider's, so loggers are
    /// taken from the application's <see cref="ILoggerFactory" /> when one is reachable. That resolution has to
    /// happen lazily: the factory depends on every <see cref="ILoggerProvider" />, so it cannot be resolved while
    /// this provider is still being constructed. When the provider is built by hand there is no service provider
    /// and it falls back to logging through itself.
    /// </para>
    /// <para>
    /// Loggers are cached per category. The accessor runs for every intercepted message, including every
    /// GD.Print, so resolving one each time would allocate on a hot path in a zero-allocation logging library.
    /// </para>
    /// </summary>
    internal Func<string, ILogger> CreateEngineLoggerAccessor(IServiceProvider? serviceProvider)
    {
        if (serviceProvider is null)
        {
            // Hoisted so the cache lookup below does not allocate a delegate per message.
            Func<string, ILogger> fallbackFactory = CreateEngineFallbackLogger;
            return category => engineLoggers.GetOrAdd(category, fallbackFactory);
        }

        return category =>
        {
            if (engineLoggers.TryGetValue(category, out var cached))
            {
                return cached;
            }

            try
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(category);
                return engineLoggers.GetOrAdd(category, logger);
            }
            catch (Exception)
            {
                // An engine message can arrive before the factory is ready. Capturing it must never fail, so fall
                // back to this provider without caching - the next message retries the full factory.
                return CreateEngineFallbackLogger(category);
            }
        };
    }

    /// <summary>
    /// Logger used when the application's factory cannot be reached. Engine callbacks run on Godot's side of the
    /// stack, so this must never throw: once the provider is disposed, CreateLogger would raise
    /// <see cref="ObjectDisposedException" /> into the engine, so drop the message instead.
    /// </summary>
    ILogger CreateEngineFallbackLogger(string category)
    {
        return Volatile.Read(ref isDisposed) != 0 ? NullLogger.Instance : CreateLogger(category);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

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
            HandBackEPluginLogging();
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
            HandBackEPluginLogging();
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

    /// <summary>
    /// Returns ePlugin's logging to whoever had it before this provider took it over. Otherwise ePlugin keeps loggers
    /// of a disposed provider, and every logger it creates afterwards throws <see cref="ObjectDisposedException" />.
    /// </summary>
    void HandBackEPluginLogging()
    {
#if TOOLS
        if (ePluginLogging is null || ePluginLoggerFactory is null)
        {
            return;
        }

        try
        {
            ePluginLogging.HandBack(ePluginLoggerFactory);
        }
        catch (Exception e)
        {
            // Disposal must not fail because ePlugin could not switch its logging.
            GD.PushWarning($"eLogger could not hand ePlugin's logging back: {e.Message}");
        }
#endif
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        this.scopeProvider = scopeProvider;
    }
}
