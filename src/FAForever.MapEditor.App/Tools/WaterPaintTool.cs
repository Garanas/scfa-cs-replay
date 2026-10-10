using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// One of the water's masks, as the water tool offers it.
/// </summary>
public sealed record WaterMaskChoice(WaterMask Mask, string Title, string Help)
{
    public override string ToString() => Title;
}

/// <summary>
/// Paints or erases one of the water's masks (user story W-2): where the water foams, how flat
/// its surface lies, and the depth bias that changes how deep it looks. The masks are half the
/// map's size; the top view shows the chosen one while the tool is active.
/// </summary>
public sealed partial class WaterPaintTool : GridPaintTool
{
    public WaterPaintTool(MainViewModel model) : base(model)
    {
        Painted = Masks[0];
    }

    public override string PresetName => "water";

    public IReadOnlyList<WaterMaskChoice> Masks { get; } = [.. WaterMaps.All.Select(map => new WaterMaskChoice(map.Mask, map.Title, map.Help))];

    /// <summary>
    /// The mask it paints, chosen in the Water panel's list of maps.
    /// </summary>
    [ObservableProperty]
    public partial WaterMaskChoice Painted { get; set; }

    public override PaintTarget? Target => Model.HasMap ? PaintTarget.Water(Painted.Mask) : null;

    /// <summary>
    /// Paints the mask <paramref name="name"/>: <c>foam</c>, <c>flatness</c> or <c>depthBias</c>.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such mask.</exception>
    public void Choose(string name) =>
        Painted = Enum.TryParse(name.Replace(" ", ""), ignoreCase: true, out WaterMask mask)
            ? Masks.First(choice => choice.Mask == mask)
            : throw new ArgumentException($"The water has no mask '{name}': {string.Join(", ", Enum.GetNames<WaterMask>())}");

    partial void OnPaintedChanged(WaterMaskChoice value) => OnTargetChanged();
}
