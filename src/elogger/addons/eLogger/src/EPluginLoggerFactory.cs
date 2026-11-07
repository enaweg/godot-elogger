using System;
using Microsoft.Extensions.Logging;
#if TOOLS
using System.Collections.Generic;
// ePlugin's internals are editor-only, so the namespace does not exist in an exported game build.
using Enaweg.Plugin.Internal;
#endif
using ILogger = Enaweg.Plugin.Logging.ILogger;
using ILoggerFactory = Enaweg.Plugin.Logging.ILoggerFactory;

namespace Enaweg.Logger;

public sealed class EPluginLoggerFactory : ILoggerFactory
{
    private readonly Func<string, ILogger> _createLogger;

    public EPluginLoggerFactory(ILoggerProvider loggerProvider)
    {
        _createLogger = category => new EPluginLogger(loggerProvider.CreateLogger(category));
    }

    /// <summary>
    /// Creates loggers that write through <paramref name="loggerAccessor" />, which maps a category onto a logger of
    /// the application. The accessor runs for every message (see <see cref="EPluginLogger" />), so it must not throw.
    /// </summary>
    internal EPluginLoggerFactory(Func<string, Microsoft.Extensions.Logging.ILogger> loggerAccessor)
    {
        _createLogger = category => new EPluginLogger(category, loggerAccessor);
    }

    public ILogger CreateLogger(string category) => _createLogger(category);
}

#if TOOLS
/// <summary>
/// Where ePlugin's logging is switched: ePlugin's <see cref="EGlobal" /> in the editor, a substitute in tests.
/// </summary>
internal interface IEPluginLoggingHost
{
    bool IsValid { get; }

    void SwitchLogging(ILoggerFactory loggerFactory);
}

/// <summary>
/// Hands ePlugin's logging to providers and back.
/// <para>
/// ePlugin has a single logger factory for the whole editor and keeps the loggers it created from it. A provider that
/// took it over and is then disposed would leave ePlugin with loggers of a disposed provider, and every logger ePlugin
/// creates afterwards would throw <see cref="System.ObjectDisposedException" />. So every provider that takes over is
/// remembered in order, and handing back returns ePlugin to the provider that had it before - or, when none is left,
/// to the Godot console logging ePlugin starts with.
/// </para>
/// </summary>
internal sealed class EPluginLoggingSwitch
{
    /// <summary>The switch for ePlugin's <see cref="EGlobal" />. Tests replace it and restore it afterwards.</summary>
    internal static EPluginLoggingSwitch Global { get; set; } = new(new EGlobalLoggingHost());

    readonly IEPluginLoggingHost host;
    readonly List<ILoggerFactory> owners = new();

    internal EPluginLoggingSwitch(IEPluginLoggingHost host)
    {
        this.host = host;
    }

    /// <summary>
    /// Switches ePlugin's logging to <paramref name="factory" />. Returns <c>false</c>, and switches nothing, when
    /// ePlugin is not running. When ePlugin throws, <paramref name="factory" /> still counts as the owner: ePlugin may
    /// have switched part of its logging already, so handing back must restore it.
    /// </summary>
    internal bool TakeOver(ILoggerFactory factory)
    {
        lock (owners)
        {
            // ePlugin only exists while the editor is running its plugins. In a game run from the editor, which still
            // compiles with TOOLS defined, EGlobal has no plugin context, so there is nothing to switch.
            if (!host.IsValid)
            {
                return false;
            }

            owners.Add(factory);
            host.SwitchLogging(factory);
            return true;
        }
    }

    /// <summary>
    /// Gives up <paramref name="factory" />'s hold on ePlugin's logging. When it is the factory ePlugin currently logs
    /// through, ePlugin switches to the previous owner, or to its own default logging when there is none.
    /// </summary>
    internal void HandBack(ILoggerFactory factory)
    {
        lock (owners)
        {
            var index = owners.LastIndexOf(factory);
            if (index < 0)
            {
                return;
            }

            var isCurrent = index == owners.Count - 1;
            owners.RemoveAt(index);

            // After an assembly reload ePlugin starts over with its default logging; then there is nothing to restore.
            if (!isCurrent || !host.IsValid)
            {
                return;
            }

            host.SwitchLogging(owners.Count > 0 ? owners[^1] : CreateDefaultFactory());
        }
    }

    /// <summary>The logging ePlugin is initialized with (see <c>EPluginPlugin</c>): its Godot console logger.</summary>
    internal static ILoggerFactory CreateDefaultFactory() =>
        new Enaweg.Plugin.Logging.GenericLoggerFactory(category => new Enaweg.Plugin.Logging.GodotConsoleLogger(category));

    sealed class EGlobalLoggingHost : IEPluginLoggingHost
    {
        public bool IsValid => EGlobal.Instance.IsValid();

        public void SwitchLogging(ILoggerFactory loggerFactory) => EGlobal.Instance.SwitchLogging(loggerFactory);
    }
}
#endif
