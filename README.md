<div align="center">

# eLogger

**Zero-allocation text and structured logging with [ZLogger](https://github.com/Cysharp/ZLogger) for [Godot](https://godotengine.org/).**

[![CI](https://github.com/enaweg/godot-elogger/actions/workflows/ci-pr.yml/badge.svg)](https://github.com/enaweg/godot-elogger/actions/workflows/ci-pr.yml)
![Godot 4.4](https://img.shields.io/badge/Godot-v4.4-202020?logo=godot-engine&logoColor=blue&color=lightcoral&labelColor=202020)
![Godot 4.5](https://img.shields.io/badge/Godot-v4.5-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.6](https://img.shields.io/badge/Godot-v4.6-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.7](https://img.shields.io/badge/Godot-v4.7-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)

![Dotnet 8](https://img.shields.io/badge/8-02020?logo=dotnet&logoSize=auto&logoColor=purple&color=darkgreen&labelColor=E0E0E0)
![Dotnet 10](https://img.shields.io/badge/10-02020?logo=dotnet&logoSize=auto&logoColor=purple&color=darkgreen&labelColor=E0E0E0)

![ZLogger 2.5](https://img.shields.io/badge/ZLogger-v2.5-202020?color=darkgreen&labelColor=202020)

**NOTE**: This project is experimental and still a work in progress.

</div>

## About ZLogger

[ZLogger](https://github.com/Cysharp/ZLogger) is a zero-allocation text and structured logger for .NET by
[Cysharp](https://github.com/Cysharp), built on top of `Microsoft.Extensions.Logging`. Rather than formatting a
message into intermediate `string` objects, it writes interpolated log calls straight into pooled UTF-8 buffers,
so logging from a hot path — a `_Process` frame, a physics step — adds no garbage-collector pressure. Because it is
an ordinary `Microsoft.Extensions.Logging` provider, the familiar `ILogger<T>`, category, `SetMinimumLevel` and
`AddFilter` model still applies, and it can run alongside other logging providers.

Its main features:

+ **Zero-allocation formatting.** The `ZLog*` methods (`ZLogInformation`, `ZLogError`, …) are built on C#
  interpolated string handlers and [Utf8StringInterpolation](https://github.com/Cysharp/Utf8StringInterpolation),
  writing values directly as UTF-8. A call below the enabled log level is skipped before its arguments are ever
  formatted, so disabled `Trace`/`Debug` logging costs almost nothing.
+ **Text and structured logging from a single call.** The interpolation holes double as named fields:
  `logger.ZLogInformation($"Player {id} spawned at {position}")` prints as readable text, and serializes `id` and
  `position` as properties when a JSON formatter is configured — no separate message template and argument array.
+ **Pluggable formatters.** Plain text with custom prefix and suffix templates (`SetPrefixFormatter` /
  `SetSuffixFormatter`), `System.Text.Json` output via `options.UseJsonFormatter()`, or a completely custom
  `IZLoggerFormatter` through `options.UseFormatter(...)`. JSON output is configurable down to property names
  (`KeyNameMutator`), scope and property inclusion, and how exceptions are emitted.
+ **Asynchronous writing.** Entries are handed to an `IAsyncLogProcessor` and flushed on a background thread
  through a bounded buffer (`BackgroundBufferCapacity`), keeping I/O off the calling thread — the game loop, in a
  Godot project. This is also the extension point eLogger implements to route entries into Godot.
+ **Built-in sinks.** `AddZLoggerConsole`, `AddZLoggerFile`, `AddZLoggerRollingFile` (rotating by interval or size),
  `AddZLoggerStream`, `AddZLoggerInMemory`, and `AddZLoggerLogProcessor` for custom processors.
+ **Source-generated log methods.** `[ZLoggerMessage]` turns a `partial` method declaration into a strongly typed,
  allocation-free logging method with a fixed message template and `EventId`.
+ **Rich log entries.** Scopes (`IncludeScopes`), event IDs, caller and thread information (`CaptureThreadInfo`),
  timestamps from an injectable `TimeProvider`, and an arbitrary per-entry context object — the last is what
  eLogger reads to recognize the emitting `GodotObject`.

eLogger's job is to plug this pipeline into the engine: it supplies the Godot sink, mirrors native engine
diagnostics back into ZLogger, and leaves formatter, filter and sink configuration to ZLogger itself.

## Requirements

The current CI-tested configuration uses:

+ [Godot 4.7.2 .NET](https://godotengine.org/download/archive/4.7.2-stable/)
+ [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

The project targets `net8.0`. Godot **4.5** is the minimum supported version: engine message interception
subclasses `Godot.Logger`, which was first exposed to scripting in 4.5.

## Installation

1. Download the latest [eLogger release](https://github.com/enaweg/godot-elogger/releases) and [ePlugin release](https://github.com/enaweg/godot-epluginframework/releases).
2. Extract `addons/eLogger` and `addons/ePlugin` into your Godot project's `addons` directory. eLogger depends on ePlugin.
3. Open the project in the Godot .NET editor and enable **ePlugin** and **eLogger** under **Project > Project Settings > Plugins**.

Enabling eLogger adds the required `ZLogger` and `ZString` NuGet packages to the project and exposes the plugin's runtime source directory. No manual `dotnet add package` command is needed. A welcome page with first steps is shown once after eLogger is enabled, and new eLogger releases are offered in the ePlugin Manager.

## Features

+ [ZLogger](https://github.com/Cysharp/ZLogger) integration for Godot: zero-allocation structured logging through `Microsoft.Extensions.Logging`.
+ Routes log messages to Godot's output panel and error/warning overlays — `GD.Print` for informational levels, `GD.PushWarning` and `GD.PushError` for warnings and errors, so they show up in the editor's Debugger dock.
+ Captures native engine diagnostics — engine errors and warnings, script errors, shader errors, and everything printed through `GD.Print` / `GD.PrintErr` — and feeds them back through ZLogger, so engine output reaches the file, JSON, or network sinks you configured rather than only the editor console.
+ Logs intercepted engine messages under a category per diagnostic type (`Godot.Engine`, `Godot.Script`, `Godot.Shader`, `Godot.Output`), so standard `AddFilter` rules can silence or level-limit one kind or all of them. The prefix is configurable.
+ Prefixes a message with the emitting object's instance ID when the log entry carries a `GodotObject` as its ZLogger context.
+ Cleans up exception stack traces: ZLogger and `Microsoft.Extensions.Logging` frames are dropped, types are printed in C# notation, and source locations are rewritten to `res://` paths with line numbers.
+ Configured through `ZLoggerGodotDebugOptions`, which derives from `ZLoggerOptions` — custom formatters, JSON output, timestamps, and `IncludeScopes` all work as they do in plain ZLogger. The provider implements `ISupportExternalScope`, so `BeginScope` state flows through. It is registered under the `ZLoggerGodotDebug` provider alias for configuration-driven filtering.
+ Guards against double logging: output the plugin itself writes to Godot is not re-captured by the engine interceptor and logged a second time.
+ Optional integration with [ePlugin](https://github.com/enaweg/godot-epluginframework) logging (on by default), so plugin lifecycle messages are handled by ZLogger too. It is skipped automatically outside the editor, where ePlugin is not running.
+ Uses the [ePlugin Framework](https://github.com/enaweg/godot-epluginframework) to manage NuGet packages and runtime source files when the plugin is enabled.

## Examples

Register the Godot provider on an `ILoggingBuilder`, for example in a `Microsoft.Extensions.Logging.LoggerFactory` or your dependency-injection setup:

```csharp
using Microsoft.Extensions.Logging;
using Enaweg.Logger;

using var factory = LoggerFactory.Create(logging =>
{
    logging.SetMinimumLevel(LogLevel.Trace);
    logging.AddZLoggerGodotDebug(options =>
    {
        options.PrettyStacktrace = true;   // clean up exception stack traces
        options.EPluginIntegration = true; // route ePlugin logs through ZLogger
    });
});

var logger = factory.CreateLogger("MyGame");
logger.ZLogInformation($"Player spawned at {position}");
```

Log levels are routed as follows:

| Level | Destination |
|---|---|
| `Trace` / `Debug` / `Information` | `GD.Print` |
| `Warning` | `GD.PushWarning` |
| `Error` / `Critical` | `GD.PushError` |

Messages captured from the engine are logged through the application's `ILoggerFactory`, so they reach every sink
you configured. Each kind gets its own category, based on the error type Godot reports:

| Godot error type | Category | Level |
|---|---|---|
| `Error` | `Godot.Engine` | `Error` |
| `Warning` | `Godot.Engine` | `Warning` |
| `Script` | `Godot.Script` | `Error` |
| `Shader` | `Godot.Shader` | `Error` |
| engine output (`GD.Print`, `GD.PrintErr`) | `Godot.Output` | `Information` / `Error` |

Standard category filtering then applies to one kind, or to all of them through the shared root:

```csharp
logging.AddFilter("Godot.Output", LogLevel.None);    // drop the GD.Print mirror
logging.AddFilter("Godot.Shader", LogLevel.None);    // ignore shader diagnostics
logging.AddFilter("Godot", LogLevel.Warning);        // quieten every engine message
```

Set `options.EngineCategoryPrefix` if `Godot` would collide with categories your application already uses.

## Testing

The current CI configuration builds and tests pull requests with Godot 4.7.2 and .NET 8.

Tests use [gdUnit4](https://github.com/MikeSchulze/gdUnit4), which launches Godot to host them, so a Godot .NET
executable must be available through `GODOT_BIN` for either runner below.

To build and run the tests locally:

```bash
cd src/elogger
export GODOT_BIN=/path/to/godot
dotnet build "eLogger.sln" --configuration Debug
dotnet test "eLogger.sln" --configuration Debug --settings .runsettings
```

The gdUnit4 shell runner works as well:

```bash
export GODOT_BIN=/path/to/godot
./addons/gdUnit4/runtest.sh
```

See the [CI workflow](https://github.com/enaweg/godot-elogger/blob/main/.github/workflows/ci-pr.yml) for the complete headless test setup.

### Project layout

`src/elogger/addons/` contains two plugins that are always co-deployed:

- **`ePlugin/`** — the plugin lifecycle framework (vendored, upstream: [godot-epluginframework](https://github.com/enaweg/godot-epluginframework)). It manages plugin dependencies, NuGet packages, project references, autoloads, and source directories.
- **`eLogger/`** — the ZLogger-to-Godot bridge described above. It depends on ePlugin.

## Contribute

Feel free to contribute with documentation, testing, or pull requests.

## Commercial Support

Commercial services are available from [Enaweg](https://www.enaweg.at). If you need consulting, implementation
assistance, or tailored development services, please get in touch through their website.

## License

Licensed under the [MIT license](LICENSE).
