using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Paints or erases a mask for tools (user story K-1): where rivers go, where forests grow, what
/// to erode. The mask is chosen in the Masks mode's panel (<see cref="MasksViewModel"/>), and of
/// its stack the painted layer chosen in the inspector; the views show it while the tool is active.
/// </summary>
public sealed partial class MaskPaintTool : GridPaintTool
{
    public MaskPaintTool(MainViewModel model) : base(model)
    {
        Strength = 0.5;
    }

    public override string PresetName => "masks";

    public override PaintTarget? Target => Model.Masks.Chosen is string name && Model.ShownDocument?.Masks.ContainsKey(name) == true ? PaintTarget.Mask(name) : null;

    public override LayerTarget? Stack => Model.Masks.Chosen is string name ? LayerTarget.Mask(name) : null;

    /// <summary>
    /// The panel chose another mask, or the chosen one was renamed or removed.
    /// </summary>
    internal void OnMaskChanged() => OnTargetChanged();
}
