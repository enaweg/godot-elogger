using Microsoft.Extensions.Logging;
using ZLogger;

namespace Enaweg.Logger;

public sealed class EPluginLogger : global::Enaweg.Plugin.Logging.ILogger
{
    private readonly ILogger _logger;

    public EPluginLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void Log(string message)
    {
        _logger.ZLogInformation($"{message}");
    }

    public void Warn(string message)
    {
        _logger.ZLogWarning($"{message}");
    }

    public void Error(string message)
    {
        _logger.ZLogError($"{message}");
    }
}