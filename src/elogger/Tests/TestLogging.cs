using Microsoft.Extensions.Logging;
using ZLogger;

namespace Enaweg.Logger;

public sealed class TestLogging
{
    public TestLogging()
    {
        using var factory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);

            // Add ZLogger provider to ILoggingBuilder
            logging.AddZLoggerConsole();

            // Output Structured Logging, setup options
            // logging.AddZLoggerConsole(options => options.UseJsonFormatter());

            // Output to the godot console
            var options = new ZLoggerOptions();
            logging.AddZLoggerLogProcessor(new GodotLogProcessor(options));
        });

        var logger = factory.CreateLogger("Program");

        var name = "John";
        var age = 33;

        // Use **Z**Log method and string interpolation to log message
        logger.ZLogInformation($"Hello my name is {name}, {age} years old.");
    }
}