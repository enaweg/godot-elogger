using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace Enaweg.Logger;

public sealed class GodotLogProcessor : IAsyncLogProcessor
{
    [ThreadStatic] static ArrayBufferWriter<byte>? bufferWriter;

    readonly IZLoggerFormatter formatter;


    public GodotLogProcessor(ZLoggerOptions options)
    {
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
            var msg = FormatToString(log, formatter);

            switch (log.LogInfo.LogLevel)
            {
                case LogLevel.Trace:
                    GD.Print($"TRACE: {msg}");
                    break;
                case LogLevel.Debug:
                    GD.Print($"DEBUG: {msg}");
                    break;
                case LogLevel.Information:
                    GD.Print(msg);
                    break;
                case LogLevel.Warning:
                    GD.Print($"WARN: {msg}");
                    break;
                case LogLevel.Error:
                    GD.PrintErr(msg);
                    break;
                case LogLevel.Critical:
                    GD.PrintErr($"CRITICAL: {msg}");
                    break;
                case LogLevel.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
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