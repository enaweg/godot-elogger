#if TOOLS
using Godot;
using Enaweg.Plugin;
using Enaweg.Plugin.Logging;

namespace Enaweg.Logger;

[Tool]
public partial class ELoggerPlugin : EEditorPlugin
{
    protected override ILogger InitializeLogging()
    {
        return new GodotConsoleLogger(PluginSlug);
    }

    internal override void Bootstrap(IEEditorPluginBuilder builder)
    {
        var srcDirectory = $"{PluginDirectory}/.src";

        builder
            .AddNuget("ZLogger")
            .AddDirectory(srcDirectory);
    }
}

#endif