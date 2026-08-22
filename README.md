<div align="center">

# eLogger

**Zero-allocation text and structured logging with [ZLogger](https://github.com/Cysharp/ZLogger) for [Godot](https://godotengine.org/).**

[![CI](https://github.com/enaweg/godot-elogger/actions/workflows/ci-pr.yml/badge.svg)](https://github.com/enaweg/godot-elogger/actions/workflows/ci-pr.yml)
![Godot 4.4](https://img.shields.io/badge/Godot-v4.4-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.5](https://img.shields.io/badge/Godot-v4.5-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.6](https://img.shields.io/badge/Godot-v4.6-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.7.2](https://img.shields.io/badge/Godot-v4.7.2-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Dotnet 8](https://img.shields.io/badge/8-02020?logo=dotnet&logoSize=auto&logoColor=purple&color=darkgreen&labelColor=E0E0E0)
![Dotnet 10](https://img.shields.io/badge/10-02020?logo=dotnet&logoSize=auto&logoColor=purple&color=darkgreen&labelColor=E0E0E0)

**NOTE**: This project is experimental and still a work in progress.

</div>

## Requirements

The current CI-tested configuration uses:

+ [Godot 4.7.2 .NET](https://godotengine.org/download/archive/4.7.2-stable/)
+ [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

The project targets `net8.0`.

## Installation

1. Download the latest [eLogger release](https://github.com/enaweg/godot-elogger/releases) and [ePlugin release](https://github.com/enaweg/godot-epluginframework/releases).
2. Extract `addons/eLogger` and `addons/ePlugin` into your Godot project's `addons` directory. eLogger depends on ePlugin.
3. Open the project in the Godot .NET editor and enable **ePlugin** and **eLogger** under **Project > Project Settings > Plugins**.

Enabling eLogger adds the required `ZLogger` and `ZString` NuGet packages to the project and exposes the plugin's runtime source directory. No manual `dotnet add package` command is needed.

## Features

+ [ZLogger](https://github.com/Cysharp/ZLogger) integration for Godot: zero-allocation structured logging through `Microsoft.Extensions.Logging`.
+ Routes log messages to Godot's output panel and error/warning overlays.
+ Captures native engine errors and warnings, including script errors, shader errors, and `OS` messages, and feeds them back through ZLogger.
+ Optional integration with [ePlugin](https://github.com/enaweg/godot-epluginframework) logging, so plugin lifecycle messages can also be handled by ZLogger.
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

## Testing

The current CI configuration builds and tests pull requests with Godot 4.7.2 and .NET 8.

To build and run the tests locally:

```bash
cd src/elogger
dotnet build "ELogger.sln" --configuration Debug
dotnet test "ELogger.sln" --configuration Debug --settings .runsettings
```

Tests use [gdUnit4](https://github.com/MikeSchulze/gdUnit4). A Godot .NET executable must be available through `GODOT_BIN` when running the gdUnit4 test runner:

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
