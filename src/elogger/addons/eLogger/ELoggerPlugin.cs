#if TOOLS
using Godot;
using Enaweg.Plugin;

namespace Enaweg.Logger;

[Tool]
public partial class ELoggerPlugin : EditorPlugin, IEEditorPlugin
{
    public void CreateRecipe(IEEditorPluginBuilder builder)
    {
        var srcDirectory = $"{this.GetPluginDirectory()}/.src";

        builder
            .AddNuget("ZLogger")
            .AddNuget("ZString")
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