#if TOOLS
using Godot;
using Enaweg.Plugin;

namespace Enaweg.Logger;

[Tool]
public partial class ELoggerPlugin : EditorPlugin, IEEditorPlugin
{
    /// <summary>
    /// The ZLogger version the plugin installs: a NuGet floating version, so the newest 2.6.x patch release is used
    /// but a new minor or major release, which eLogger has not been tested against, is not.
    /// </summary>
    internal const string ZLoggerVersion = "2.6.*";

    /// <summary>
    /// The ZString version the plugin installs: a NuGet floating version, so the newest 2.6.x patch release is used
    /// but a new minor or major release, which eLogger has not been tested against, is not.
    /// </summary>
    internal const string ZStringVersion = "2.6.*";

    public void CreateRecipe(IEEditorPluginBuilder builder)
    {
        var srcDirectory = $"{this.GetPluginDirectory()}/.src";

        builder
            .AddNuget("ZLogger", ZLoggerVersion)
            .AddNuget("ZString", ZStringVersion)
            .AddDirectory(srcDirectory);
    }

    public override void _EnablePlugin()
    {
        base._EnablePlugin();
        this.EnableEPlugin();
    }

    public override void _DisablePlugin()
    {
        this.DisableEPlugin();
        base._DisablePlugin();
    }
}

#endif