using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;

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
        // a unit dragged in the Units mode's tree while selected takes the selected units along (U-3)
        Armies.SelectedUnits = () => [.. Selection.Items.Where(thing => thing.Kind == ObjectKind.Unit).Select(thing => thing.Name!)];
        DocumentChanged += (_, _) =>
        {
            using Performance.Span measure = Performance.Measure("model.content");
            presetTool.Refresh(ShownDocument);
        };
    }
}
