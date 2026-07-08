# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

eLogger is a [ZLogger](https://github.com/Cysharp/ZLogger) (zero-allocation structured logging) integration for Godot 4.x using C#/.NET. It ships as a Godot editor plugin that depends on the ePlugin framework (also vendored here).

Target runtimes: Godot 4.x, .NET 8+. The dev project itself pins the latest godot release (see `src/elogger/ELogger.csproj`).

## Commands

All commands run from `src/elogger/` (the Godot project root where `ELogger.csproj` lives).

**Build:**
```sh
dotnet build
```

**Run tests** (requires Godot binary):
```sh
export GODOT_BIN=/path/to/godot
./addons/gdUnit4/runtest.sh
# or pass binary directly:
./addons/gdUnit4/runtest.sh --godot_binary /path/to/godot
```

**Run a single test suite** (pass as extra arg to the script, forwarded to the gdUnit4 GDScript runner):
```sh
./addons/gdUnit4/runtest.sh --godot_binary /path/to/godot -s res://Tests/TestLogging.cs
```

Tests live in `src/elogger/Tests/` — gdUnit4 discovers them there via `[gdunit4] settings/test/test_lookup_folder` in `project.godot`. C# test support comes from the `gdUnit4.api` / `gdUnit4.test.adapter` NuGet packages plus the vendored gdUnit4 addon (v6.0.0).

## Architecture

### Two-plugin layout

`src/elogger/addons/` contains two distinct plugins that are always co-deployed:

- **`ePlugin/`** — the plugin lifecycle framework (vendored at v0.5.4, upstream: [godot-epluginframework](https://github.com/enaweg/godot-epluginframework)). It is the dependency; it has no knowledge of ZLogger.
- **`eLogger/`** — the ZLogger-to-Godot bridge. It depends on ePlugin.

Godot 4.4+ generates a `.uid` sidecar file next to each script/resource; leave them alone and let Godot manage them.

### ePlugin framework (`addons/ePlugin/`)

ePlugin is a framework that automates Godot plugin install/uninstall: NuGet packages, `.csproj`/solution references, autoload singletons, and hidden source directories.

To create an ePlugin-managed plugin, implement `IEEditorPlugin` on your `EditorPlugin` class and call the extension methods from Godot lifecycle hooks:

```csharp
public override void _EnablePlugin()  { base._EnablePlugin();  this.EnableEPlugin(); }
public override void _DisablePlugin() { this.DisableEPlugin(); base._DisablePlugin(); }
```

`CreateRecipe(IEEditorPluginBuilder builder)` declares what the plugin needs. The framework runs `dotnet add package`, `dotnet sln add`, etc., via `IDotnetCli` (version-dispatched: `DotnetCli9` vs `DotnetCli10`).

`EGlobal` (internal singleton) owns all `PluginContext` objects and drives the enable/disable pipeline, including dependency ordering. `EPluginPlugin` is the root Godot node that bootstraps `EGlobal`; it re-initializes itself each `_Process` frame after an assembly reload because Godot doesn't re-fire `_EnterTree`/`_Ready` in that case.

ePlugin has its own lightweight logging interfaces (`Enaweg.Plugin.Logging.ILogger` / `ILoggerFactory`) that are **not** `Microsoft.Extensions.Logging` types. These are used internally by the framework so it can log without taking a hard MEL dependency.

### eLogger plugin (`addons/eLogger/`)

`ELoggerPlugin.CreateRecipe` registers the `ZLogger` and `ZString` NuGet packages and exposes the `.src` directory (which contains the runtime source files). Because the plugin is enabled in this dev project, those packages (`ZLogger` 2.5.10, `ZString` 2.6.0) appear in `ELogger.csproj` and the directory exists as `src/` on disk.

**ZLogger → Godot routing** is handled by two `IAsyncLogProcessor` implementations:

| Class | Routing |
|---|---|
| `GodotLogProcessor` | Routes all levels to `GD.Print` / `GD.PrintErr` with level prefixes. Simple, no context. |
| `GodotDebugLogProcessor` | Routes errors/warnings to `GD.PushError`/`GD.PushWarning`, prints with optional `GodotObject` instance ID prefix and prettified stack traces. Used inside `ZLoggerGodotDebugLoggerProvider`. |

`ZLoggerGodotDebugLoggerProvider` is the main provider consumers add via `logging.AddZLoggerGodotDebug(...)`. On construction it also installs a `GodotOSLogger` (subclassing `Godot.Logger`) to intercept native engine errors/warnings and feed them back into ZLogger.

**ePlugin integration**: when `ZLoggerGodotDebugOptions.EPluginIntegration` is `true` (the default), the provider calls `EGlobal.Instance.SwitchLogging(new EPluginLoggerFactory(this))`, replacing ePlugin's internal `GodotConsoleLogger` with one backed by ZLogger. `EPluginLoggerFactory` and `EPluginLogger` are the bridge adapters between ePlugin's `ILoggerFactory`/`ILogger` and ZLogger's `ILoggerProvider`.

### Key design note — `#if TOOLS` guards

All editor-only code (everything in ePlugin, `ELoggerPlugin.cs`) is wrapped in `#if TOOLS`. Runtime game code only uses the types in `addons/eLogger/src/` directly.

### Hidden source directory convention

When ePlugin installs a plugin's source directory it calls `ShowHideHelper.ShowDirectory` which removes the leading `.` prefix (e.g. `.src` → `src`) so Godot can see and compile the files. On disable it reverses this by renaming back to a dot-prefixed name. This is why `ELoggerPlugin.CreateRecipe` refers to `$"{this.GetPluginDirectory()}/.src"`.
