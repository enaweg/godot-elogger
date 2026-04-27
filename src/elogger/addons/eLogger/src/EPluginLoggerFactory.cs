using Microsoft.Extensions.Logging;
using ILogger = Enaweg.Plugin.Logging.ILogger;
using ILoggerFactory = Enaweg.Plugin.Logging.ILoggerFactory;

namespace Enaweg.Logger;

public sealed class EPluginLoggerFactory : ILoggerFactory
{
    private readonly ILoggerProvider _loggerProvider;

    public EPluginLoggerFactory(ILoggerProvider loggerProvider)
    {
        _loggerProvider = loggerProvider;
    }

    public ILogger CreateLogger(string category)
    {
        var logger = _loggerProvider.CreateLogger(category);
        return new EPluginLogger(logger);
    }
}