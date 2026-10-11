using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses a unit palette (user story U-8): its ten slots show in the Units panel, and the keys 1
/// to 9 and 0 pick them while the place tool is active. Its control is the panel's combo box.
/// </summary>
/// <param name="Name">The palette's name (<c>UEF T1 base</c>), ignoring case.</param>
/// <returns>The palette and its slots, as <c>state.units.palette</c> has them.</returns>
[EditorCommand("units.palette", "Choose a palette", Story = "U-8")]
public sealed record ChooseUnitPalette(string Name) : IRequest<JsonObject>;

public sealed class ChooseUnitPaletteHandler(MainViewModel model, UnitPalettesViewModel palettes) : IRequestHandler<ChooseUnitPalette, JsonObject>
{
    public Task<JsonObject> Handle(ChooseUnitPalette request, CancellationToken cancellationToken)
    {
        palettes.Choose(request.Name);
        model.Status = $"Palette {palettes.Chosen}: 1 to 9 and 0 pick a slot while the place tool is active";
        return Task.FromResult(palettes.Describe());
    }
}

/// <summary>
/// Chooses the palette after the chosen one, round the list (user story U-8): Tab while the place
/// tool is active; <paramref name="Back"/> the one before (Shift+Tab).
/// </summary>
/// <returns>The palette chosen and its slots; <c>chosen</c> null when the workspace has none.</returns>
[EditorCommand("units.nextPalette", "Next palette", Story = "U-8")]
public sealed record NextUnitPalette(bool Back = false) : IRequest<JsonObject>;

public sealed class NextUnitPaletteHandler(MainViewModel model, UnitPalettesViewModel palettes) : IRequestHandler<NextUnitPalette, JsonObject>
{
    public Task<JsonObject> Handle(NextUnitPalette request, CancellationToken cancellationToken)
    {
        model.Status = palettes.Next(request.Back ? -1 : 1) is string palette
            ? $"Palette {palette}: 1 to 9 and 0 pick a slot while the place tool is active"
            : "The workspace has no palettes yet: Units, New palette makes one";
        return Task.FromResult(palettes.Describe());
    }
}

/// <summary>
/// Picks a slot of the chosen palette (user story U-8): its unit becomes what the place tool
/// places (as <c>units.pick</c> chooses it), or its preset (as <c>presets.apply</c> does), and the
/// place tool is chosen. What the keys 1 to 9 and 0 do while the place tool is active, and a click
/// on a slot. An empty slot picks nothing and says so.
/// </summary>
/// <param name="N">The slot as its key is labelled: 1 to 9, and 0 (or 10) for the tenth.</param>
/// <returns>The slot's index (<c>slot</c>), its unit or preset, and whether something was picked.</returns>
[EditorCommand("units.slot", "Pick a palette slot", Story = "U-8", Needs = Needs.Map)]
public sealed record PickUnitPaletteSlot(int N) : IRequest<JsonObject>;

public sealed class PickUnitPaletteSlotHandler(MainViewModel model, UnitPalettesViewModel palettes) : IRequestHandler<PickUnitPaletteSlot, JsonObject>
{
    public async Task<JsonObject> Handle(PickUnitPaletteSlot request, CancellationToken cancellationToken)
    {
        int index = UnitPalettes.IndexOf(request.N);
        if (palettes.Chosen is null)
        {
            throw new InvalidOperationException("The workspace has no palettes yet: Units, New palette makes one");
        }
        PaletteSlotItem slot = palettes.Slots[index];
        string? picked = await palettes.PickSlotAsync(index);
        if (picked is null)
        {
            model.Status = slot.IsMissing
                ? $"Slot {slot.Key} of {palettes.Chosen} names {slot.Title}, which the workspace lacks: clear it or choose another unit"
                : $"Slot {slot.Key} of {palettes.Chosen} is empty: drop a unit from the shelf or a preset into it";
        }
        else
        {
            model.SelectTool("units.place");
            model.Status = $"Slot {slot.Key}: click to place {picked}; Q and E turn it";
        }
        return new JsonObject
        {
            ["slot"] = index,
            ["key"] = slot.Key,
            ["unit"] = slot.Slot.IsUnit ? slot.Slot.Unit : null,
            ["preset"] = slot.Slot.IsPreset ? slot.Slot.Preset : null,
            ["picked"] = picked is not null,
        };
    }
}

/// <summary>
/// Puts a unit or a units preset into a slot of a palette, or empties it (user story U-8): what
/// dropping a tile of the shelf or a preset of the list onto a slot does, and a slot's context
/// menu. Without a unit, a preset or <paramref name="Clear"/>, the picker asks for a unit (not
/// without a person: give <c>unit</c>). The palette's file is written at once.
/// </summary>
/// <param name="Slot">1 to 9, and 0 (or 10) for the tenth.</param>
/// <param name="Palette">Left out, the chosen palette.</param>
/// <param name="Unit">A unit's id the workspace has (<c>ueb0101</c>).</param>
/// <param name="Preset">A units preset's name (<c>UEF farm</c>).</param>
/// <param name="Clear">Empties the slot.</param>
/// <returns>The palette, the slot and what it holds now.</returns>
[EditorCommand("units.setSlot", "Put in a palette slot", Story = "U-8")]
public sealed record SetUnitPaletteSlot(int Slot, string? Palette = null, string? Unit = null, string? Preset = null, bool Clear = false) : IRequest<JsonObject>;

public sealed class SetUnitPaletteSlotHandler(MainViewModel model, UnitPalettesViewModel palettes, PresetsViewModel presets, AssetsViewModel assets, IEditorDialogs dialogs)
    : IRequestHandler<SetUnitPaletteSlot, JsonObject>
{
    public async Task<JsonObject> Handle(SetUnitPaletteSlot request, CancellationToken cancellationToken)
    {
        int index = UnitPalettes.IndexOf(request.Slot);
        if (request.Unit is not null && request.Preset is not null)
        {
            throw new ArgumentException("A slot holds a unit or a preset, not both");
        }
        UnitPaletteSlot slot;
        if (request.Clear)
        {
            slot = UnitPaletteSlot.Empty;
        }
        else if (request.Preset is string preset)
        {
            PresetLibrary library = presets.Library ?? throw new InvalidOperationException("Palettes need a workspace");
            Preset found = library.Find(PresetKinds.Units, preset) ?? throw new ArgumentException($"There is no units preset '{preset}'");
            slot = UnitPaletteSlot.OfPreset(found.Name);
        }
        else
        {
            string? key = request.Unit;
            if (key is null)
            {
                string current = palettes.Slots[index].Slot.Unit ?? "";
                key = await dialogs.PickAssetAsync(AssetKind.Unit, $"Choose a unit for slot {UnitPalettes.KeyLabel(index)}", current.Length == 0 ? null : current);
                if (key is null)
                {
                    model.Status = "Choosing a unit for the slot cancelled";
                    return new JsonObject { ["cancelled"] = true };
                }
            }
            AssetEntry entry = assets.Find(key) ?? throw new ArgumentException($"The workspace has no unit '{key}'");
            if (entry.Kind != AssetKind.Unit)
            {
                throw new ArgumentException($"'{key}' is no unit");
            }
            slot = UnitPaletteSlot.OfUnit(entry.Key);
        }
        string palette = palettes.SetSlot(request.Palette, index, slot);
        model.Status = slot.IsEmpty ? $"Cleared slot {UnitPalettes.KeyLabel(index)} of {palette}"
            : slot.IsUnit ? $"Slot {UnitPalettes.KeyLabel(index)} of {palette}: {assets.Find(slot.Unit!)?.Name ?? slot.Unit} ({slot.Unit})"
            : $"Slot {UnitPalettes.KeyLabel(index)} of {palette}: the preset {slot.Preset}";
        return new JsonObject
        {
            ["palette"] = palette,
            ["slot"] = index,
            ["key"] = UnitPalettes.KeyLabel(index),
            ["unit"] = slot.Unit,
            ["preset"] = slot.Preset,
        };
    }
}

/// <summary>
/// Makes a new, empty palette in the workspace and chooses it (user story U-8). Without a name it
/// asks for one (not without a person: give <c>name</c>); a name taken gets a number.
/// </summary>
/// <returns>The palette's name.</returns>
[EditorCommand("units.newPalette", "New palette...", Story = "U-8")]
public sealed record NewUnitPalette(string? Name = null) : IRequest<JsonObject>;

public sealed class NewUnitPaletteHandler(MainViewModel model, UnitPalettesViewModel palettes, IEditorDialogs dialogs) : IRequestHandler<NewUnitPalette, JsonObject>
{
    public async Task<JsonObject> Handle(NewUnitPalette request, CancellationToken cancellationToken)
    {
        string? name = request.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (await dialogs.AskTextAsync("New palette", "A palette holds ten slots, each a unit or a units preset, which the keys 1 to 9 and 0 pick while the place tool is active. It is saved in the workspace's presets.",
                "Name of the palette", "", ["Make the palette"]) is not (_, string text))
            {
                model.Status = "Making a palette cancelled";
                return new JsonObject { ["cancelled"] = true };
            }
            name = text;
        }
        string made = palettes.New(name);
        model.Status = $"Made the palette {made}: drop units from the shelf or presets into its slots";
        return new JsonObject { ["name"] = made };
    }
}

/// <summary>
/// Renames a palette (user story U-8): the chosen one unless named. Without the new name it asks
/// for one (not without a person: give <c>to</c>).
/// </summary>
/// <param name="Name">Left out, the chosen palette.</param>
/// <returns>The old and the new name.</returns>
[EditorCommand("units.renamePalette", "Rename the palette...", Story = "U-8")]
public sealed record RenameUnitPalette(string? To = null, string? Name = null) : IRequest<JsonObject>;

public sealed class RenameUnitPaletteHandler(MainViewModel model, UnitPalettesViewModel palettes, IEditorDialogs dialogs) : IRequestHandler<RenameUnitPalette, JsonObject>
{
    public async Task<JsonObject> Handle(RenameUnitPalette request, CancellationToken cancellationToken)
    {
        string name = request.Name ?? palettes.Chosen ?? throw new InvalidOperationException("The workspace has no palettes yet: Units, New palette makes one");
        string? to = request.To;
        if (string.IsNullOrWhiteSpace(to))
        {
            if (await dialogs.AskTextAsync("Rename the palette", $"The palette {name} and its file get the new name.", "New name", name, ["Rename"]) is not (_, string text))
            {
                model.Status = "Renaming the palette cancelled";
                return new JsonObject { ["cancelled"] = true };
            }
            to = text;
        }
        string renamed = palettes.Rename(name, to);
        model.Status = $"Renamed the palette {name} to {renamed}";
        return new JsonObject { ["from"] = name, ["name"] = renamed };
    }
}

/// <summary>
/// Deletes a palette's file from the workspace (user story U-8): the chosen one unless named.
/// </summary>
/// <returns>The name of the palette deleted.</returns>
[EditorCommand("units.deletePalette", "Delete the palette", Story = "U-8")]
public sealed record DeleteUnitPalette(string? Name = null) : IRequest<JsonObject>;

public sealed class DeleteUnitPaletteHandler(MainViewModel model, UnitPalettesViewModel palettes) : IRequestHandler<DeleteUnitPalette, JsonObject>
{
    public Task<JsonObject> Handle(DeleteUnitPalette request, CancellationToken cancellationToken)
    {
        string deleted = palettes.Delete(request.Name);
        model.Status = $"Deleted the palette {deleted}";
        return Task.FromResult(new JsonObject { ["name"] = deleted });
    }
}
