using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Units mode's palettes (user story U-8): the panel's slots follow what the place tool
/// places, and what the place commands place is noted as placed last.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The unit palettes: the chosen one's slots, the units placed last.
    /// </summary>
    public UnitPalettesViewModel UnitPalettes { get; }

    private void InitializeUnitPalettes()
    {
        UnitPlaceTool place = Tool<UnitPlaceTool>();
        // the slot that holds what the tool places is the chosen one
        place.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UnitPlaceTool.Unit) or nameof(UnitPlaceTool.Preset))
            {
                UnitPalettes.FollowTool(place.Unit?.Key, place.Preset);
            }
        };
        UnitPalettes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UnitPalettesViewModel.Slots))
            {
                UnitPalettes.FollowTool(place.Unit?.Key, place.Preset);
            }
        };
        // what the place commands placed is offered again: a unit by its id, a preset by its name
        Commands.Completed += done =>
        {
            if (!done.Command.Id.StartsWith("units.", StringComparison.Ordinal))
            {
                return;
            }
            if (done.Command.Id == "units.placePreset")
            {
                if ((done.Arguments["name"]?.GetValue<string>() ?? place.Preset) is string preset)
                {
                    UnitPalettes.NotePlaced(LastPlacedItem.PresetKey(preset));
                }
            }
            else if (done.Command.Id.StartsWith("units.add", StringComparison.Ordinal) && done.Command.Id != "units.addGroup")
            {
                string? blueprint = done.Result is JsonObject result && result["blueprint"] is JsonValue value && value.TryGetValue(out string? id) ? id
                    : done.Arguments["blueprint"]?.GetValue<string>() ?? place.Unit?.Key;
                if (blueprint is not null)
                {
                    UnitPalettes.NotePlaced(LastPlacedItem.UnitKey(blueprint));
                }
            }
        };
    }
}
