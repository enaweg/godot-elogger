#if TOOLS
using Godot;
using Enaweg.Plugin;

namespace Enaweg.Logger;

[Tool]
public partial class ELoggerPlugin : EditorPlugin, IEEditorPlugin
{
    public EditorPlugin GodotPlugin => this;

    public void Bootstrap(IEEditorPluginBuilder builder)
    {
        var srcDirectory = $"{this.GetPluginDirectory()}/.src";

        builder
            .AddNuget("ZLogger")
            .AddNuget("ZString")
            .AddDirectory(srcDirectory);
    }

    public void Reinitialize()
    {
    }
}

#endif