using System;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace Enaweg.Logger;

public sealed class EPluginLogger : global::Enaweg.Plugin.Logging.ILogger
{
    private readonly Func<ILogger> _logger;

    public EPluginLogger(ILogger logger)
    {
        _logger = () => logger;
    }

    /// <summary>
    /// Resolves the logger through <paramref name="loggerAccessor" /> for every message instead of once. ePlugin keeps
    /// its loggers for as long as the editor runs, and it creates them while the application's logger factory may still
    /// be under construction; resolving late lets the messages reach that factory once it is ready.
    /// </summary>
    internal EPluginLogger(string category, Func<string, ILogger> loggerAccessor)
    {
        _logger = () => loggerAccessor(category);
    }

    public void Log(string message)
    {
        _logger().ZLogInformation($"{message}");
    }

    public void Warn(string message)
    {
        _logger().ZLogWarning($"{message}");
    }

    public void Error(string message)
    {
        _logger().ZLogError($"{message}");
    }
}
