using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Content to use again (user stories F-10, T-2, M-2, F-9): selection presets placed with the Map
/// mode's Preset tool, height stamps, map templates, and the pickers' stars and recents.
/// </summary>
public sealed partial class MainViewModel
{
    private void InitializeContent()
    {
        SelectionPresetTool presetTool = Tool<SelectionPresetTool>();
        Presets.Register(SelectionPresetTool.Kind(this));
        DocumentChanged += (_, _) =>
        {
            using Performance.Span measure = Performance.Measure("model.content");
            presetTool.Refresh(ShownDocument);
        };
    }
}
