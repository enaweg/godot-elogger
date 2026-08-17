# AGENTS.md

This file provides guidance to Codex and other coding agents working in this repository.

## Overview

eLogger is a [ZLogger](https://github.com/Cysharp/ZLogger) (zero-allocation structured logging) integration for Godot 4.x using C#/.NET. It ships as a Godot editor plugin that depends on the ePlugin framework (also vendored here).

Target runtimes: Godot 4.x, .NET 8+. The dev project itself pins the latest Godot release (see `src/elogger/ELogger.csproj`).

## Commands

Run all commands from `src/elogger/`, the Godot project root where `ELogger.csproj` lives.

### Build

```sh
dotnet build
```

### Run tests

Tests require a Godot binary:

```sh
export GODOT_BIN=/path/to/godot
./addons/gdUnit4/runtest.sh

# Or pass the binary directly:
./addons/gdUnit4/runtest.sh --godot_binary /path/to/godot
```

To run a single test suite, pass it as an extra argument to the script; it is forwarded to the gdUnit4 GDScript runner:

```sh
./addons/gdUnit4/runtest.sh --godot_binary /path/to/godot -s res://Tests/TestLogging.cs
```

Tests live in `src/elogger/Tests/`. gdUnit4 discovers them there via `[gdunit4] settings/test/test_lookup_folder` in `project.godot`. C# test support comes from the `gdUnit4.api` and `gdUnit4.test.adapter` NuGet packages plus the vendored gdUnit4 addon (v6.0.0).

## Architecture

### Two-plugin layout

`src/elogger/addons/` contains two distinct plugins that are always co-deployed:

- **`ePlugin/`** — the plugin lifecycle framework (vendored at v0.5.4, upstream: [godot-epluginframework](https://github.com/enaweg/godot-epluginframework)). It is the dependency; it has no knowledge of ZLogger.
- **`eLogger/`** — the ZLogger-to-Godot bridge. It depends on ePlugin.

Godot 4.4+ generates a `.uid` sidecar file next to each script or resource. Leave these files alone and let Godot manage them.

### ePlugin framework (`addons/ePlugin/`)

ePlugin is a framework that automates Godot plugin install and uninstall tasks: NuGet packages, `.csproj` and solution references, autoload singletons, and hidden source directories.

To create an ePlugin-managed plugin, implement `IEEditorPlugin` on the `EditorPlugin` class and call the extension methods from Godot lifecycle hooks:

```csharp
public override void _EnablePlugin()  { base._EnablePlugin();  this.EnableEPlugin(); }
public override void _DisablePlugin() { this.DisableEPlugin(); base._DisablePlugin(); }
```

`CreateRecipe(IEEditorPluginBuilder builder)` declares what the plugin needs. The framework runs `dotnet add package`, `dotnet sln add`, and similar commands through `IDotnetCli` (version-dispatched as `DotnetCli9` or `DotnetCli10`).

`EGlobal` (an internal singleton) owns all `PluginContext` objects and drives the enable/disable pipeline, including dependency ordering. `EPluginPlugin` is the root Godot node that bootstraps `EGlobal`; it reinitializes itself each `_Process` frame after an assembly reload because Godot does not re-fire `_EnterTree` or `_Ready` in that case.

ePlugin has its own lightweight logging interfaces (`Enaweg.Plugin.Logging.ILogger` and `ILoggerFactory`) that are **not** `Microsoft.Extensions.Logging` types. The framework uses them internally so it can log without a hard MEL dependency.

### eLogger plugin (`addons/eLogger/`)

`ELoggerPlugin.CreateRecipe` registers the `ZLogger` and `ZString` NuGet packages and exposes the `.src` directory, which contains the runtime source files. Because the plugin is enabled in this development project, those packages (`ZLogger` 2.5.10 and `ZString` 2.6.0) appear in `ELogger.csproj`, and the directory exists as `src/` on disk.

Two `IAsyncLogProcessor` implementations handle ZLogger-to-Godot routing:

| Class | Routing |
|---|---|
| `GodotLogProcessor` | Routes all levels to `GD.Print` or `GD.PrintErr` with level prefixes. Simple, with no context. |
| `GodotDebugLogProcessor` | Routes errors and warnings to `GD.PushError` or `GD.PushWarning`, and prints with an optional `GodotObject` instance ID prefix and prettified stack traces. Used inside `ZLoggerGodotDebugLoggerProvider`. |

`ZLoggerGodotDebugLoggerProvider` is the main provider consumers add through `logging.AddZLoggerGodotDebug(...)`. On construction, it also installs a `GodotOSLogger` (a subclass of `Godot.Logger`) to intercept native engine errors and warnings and feed them back into ZLogger.

When `ZLoggerGodotDebugOptions.EPluginIntegration` is `true` (the default), the provider calls `EGlobal.Instance.SwitchLogging(new EPluginLoggerFactory(this))`. This replaces ePlugin's internal `GodotConsoleLogger` with one backed by ZLogger. `EPluginLoggerFactory` and `EPluginLogger` are bridge adapters between ePlugin's `ILoggerFactory`/`ILogger` and ZLogger's `ILoggerProvider`.

### Editor-only guards

All editor-only code—everything in ePlugin and `ELoggerPlugin.cs`—is wrapped in `#if TOOLS`. Runtime game code uses the types in `addons/eLogger/src/` directly.

### Hidden source directory convention

When ePlugin installs a plugin's source directory, it calls `ShowHideHelper.ShowDirectory`, which removes the leading `.` prefix (for example, `.src` becomes `src`) so Godot can see and compile the files. On disable, it reverses this by renaming the directory back to a dot-prefixed name. This is why `ELoggerPlugin.CreateRecipe` refers to `$"{this.GetPluginDirectory()}/.src"`.
