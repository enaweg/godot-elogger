<div align="center">

# eLogger
Zero Allocation Text/Structured Logger ([ZLogger](https://github.com/Cysharp/ZLogger)) integration for Godot

![Godot 4.4](https://img.shields.io/badge/Godot-v4.4-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.5](https://img.shields.io/badge/Godot-v4.5-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.6](https://img.shields.io/badge/Godot-v4.6-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)
![Godot 4.7](https://img.shields.io/badge/Godot-v4.7-202020?logo=godot-engine&logoColor=blue&color=darkgreen&labelColor=202020)

![Dotnet 8](https://img.shields.io/badge/8-02020?logo=dotnet&logoSize=auto&logoColor=purple&color=darkgreen&labelColor=E0E0E0)
![Dotnet 10](https://img.shields.io/badge/10-02020?logo=dotnet&logoSize=auto&logoColor=purple&color=darkgreen&labelColor=E0E0E0)

**NOTE**: This is currently in an experimental state and very much WIP!

</div>

## Features

+ [ZLogger](https://github.com/Cysharp/ZLogger) integration for Godot: zero-allocation structured logging via `Microsoft.Extensions.Logging`, routed into the Godot output panel and error/warning overlays.
+ Captures native engine errors and warnings (script errors, shader errors, `OS` messages) and feeds them back through ZLogger, so everything ends up in one place.
+ Optional integration with [ePlugin](https://github.com/enaweg/godot-epluginframework) internal logging, so plugin lifecycle logs are also routed through ZLogger.
+ Installed and managed via the [ePlugin Framework](https://github.com/enaweg/godot-epluginframework) — enabling the plugin automatically wires up the required NuGet packages (`ZLogger`, `ZString`) and source files.

## Installation

1. Copy `addons/eLogger` and `addons/ePlugin` (eLogger depends on ePlugin) into your project's `addons/` directory.
2. In Godot, open **Project > Project Settings > Plugins** and enable both **ePlugin** and **eLogger**.
3. Enabling the plugin adds the `ZLogger`/`ZString` NuGet packages to your `.csproj` and exposes the plugin's runtime source directory — no manual `dotnet add package` needed.

## Usage

Register the Godot log provider on your `ILoggingBuilder` (for example in a `Microsoft.Extensions.Logging.LoggerFactory` or in your DI setup):

```csharp
using Microsoft.Extensions.Logging;
using Enaweg.Logger;

using var factory = LoggerFactory.Create(logging =>
{
    logging.SetMinimumLevel(LogLevel.Trace);
    logging.AddZLoggerGodotDebug(options =>
    {
        options.PrettyStacktrace = true;   // clean up stack traces on exceptions
        options.EPluginIntegration = true; // route ePlugin's own logs through ZLogger too
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

## Development

All commands run from `src/elogger/` (the Godot project root, where `ELogger.csproj` lives).

**Build:**

```sh
dotnet build
```

**Run tests** (requires a Godot binary):

```sh
export GODOT_BIN=/path/to/godot
./addons/gdUnit4/runtest.sh
```

Tests live in `src/elogger/Tests/` and use [gdUnit4](https://github.com/MikeSchulze/gdUnit4).

### Project layout

`src/elogger/addons/` contains two plugins that are always co-deployed:

- **`ePlugin/`** — the plugin lifecycle framework (vendored, upstream: [godot-epluginframework](https://github.com/enaweg/godot-epluginframework)). Handles NuGet/`.csproj`/autoload wiring for ePlugin-based plugins; has no knowledge of ZLogger.
- **`eLogger/`** — the ZLogger-to-Godot bridge described above. Depends on ePlugin.

See `CLAUDE.md` / `AGENTS.md` for a deeper architecture walkthrough (log routing internals, ePlugin integration points, `.uid` sidecar conventions).

## Contribute

Feel free to contribute with Documentation, Testing, or PRs.

## Commercial Support

Commercial services are available from [Enaweg](https://www.enaweg.at). If you need consulting, implementation
assistance, or tailored development services, please get in touch through their website.

## License

Licensed under the MIT license, see `LICENSE` for more information.
