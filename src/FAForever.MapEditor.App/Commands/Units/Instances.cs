using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The group a unit preset was placed as knows the preset (user story U-9): these commands work on
/// that link. Left out, the army and group are those chosen in the Units mode's tree; the group
/// must have been placed from a preset. The link is never used to change the map.
/// </summary>
internal static class InstanceCommands
{
    /// <summary>
    /// The instance of the group named, or chosen in the tree.
    /// </summary>
    /// <exception cref="ArgumentException">No group, or one that was not placed from a preset.</exception>
    public static PlacedInstance Of(MapDocument document, ArmiesViewModel armies, string? army, string? group)
    {
        string name = army ?? armies.Chosen?.Army
            ?? throw new ArgumentException("Choose a group placed from a preset in the Units mode's tree, or give the arguments army and group");
        ImmutableArray<string> path = army is not null ? UnitGroupEdits.ParsePath(group) : armies.Chosen!.Value.Path;
        UnitCommands.RequireArmy(document, name);
        UnitGroup found = document.FindGroup(name, path) ?? throw new ArgumentException($"The army {name} has no group {UnitGroupEdits.PathText(path)}");
        return found.Instance is PresetInstance instance
            ? new PlacedInstance(name, path, found, instance)
            : throw new ArgumentException($"The group {(path.IsEmpty ? name : $"{UnitGroupEdits.PathText(path)} of {name}")} was not placed from a preset");
    }

    public static string Describe(PlacedInstance instance) => $"{UnitGroupEdits.PathText(instance.Path)} of {instance.Army} (the preset {instance.Instance.Preset})";
}

/// <summary>
/// Selects the units of a group placed from a unit preset, with those of the groups in it (user
/// story U-9): the instance as a whole, as a double click on one of its units does.
/// </summary>
/// <param name="Group">Its path (<c>INITIAL/Cybran farm</c>).</param>
/// <returns>How many units are selected.</returns>
[EditorCommand("units.selectInstance", "Select the instance", Story = "U-9", Needs = Needs.Map)]
public sealed record SelectUnitInstance(string? Army = null, string? Group = null) : IRequest<int>;

public sealed class SelectUnitInstanceHandler(MainViewModel model, ArmiesViewModel armies, SelectionViewModel selection) : IRequestHandler<SelectUnitInstance, int>
{
    public Task<int> Handle(SelectUnitInstance request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        PlacedInstance instance = InstanceCommands.Of(document, armies, request.Army, request.Group);
        ObjectRef[] units = [.. document.UnitsIn(instance.Army, instance.Path).Select(unit => ObjectRef.Unit(unit.Unit.Name))];
        selection.Set(units);
        model.Status = $"Selected the {MainViewModel.Units(units.Length)} of the instance {InstanceCommands.Describe(instance)}";
        return Task.FromResult(units.Length);
    }
}

/// <summary>
/// Saves the layout of a group placed from a unit preset back into the preset (user story U-9),
/// one step of undo: the group's units (with those of the groups in it) as Save as a preset keeps
/// them, written over the preset's file (which keeps its role, wrecks and source; a file that is
/// gone is made again), and the instance is current afterwards. The file is overwritten, so a
/// person is asked first; without one, <paramref name="Confirm"/> must be true.
/// </summary>
/// <param name="Group">Its path (<c>INITIAL/Cybran farm</c>).</param>
/// <param name="Confirm">Yes to overwriting the preset's file, when nobody can be asked.</param>
/// <returns>The preset's name and how many units it holds now.</returns>
[UndoStep]
[EditorCommand("units.saveInstance", "Save the instance back to its preset", Story = "U-9")]
public sealed record SaveUnitInstance(string? Army = null, string? Group = null, bool Confirm = false) : IRequest<JsonObject>;

public sealed class SaveUnitInstanceHandler(MainViewModel model, ArmiesViewModel armies, PresetsViewModel presets, IEditorDialogs dialogs, CurrentEdit edit) : IRequestHandler<SaveUnitInstance, JsonObject>
{
    public async Task<JsonObject> Handle(SaveUnitInstance request, CancellationToken cancellationToken)
    {
        PresetLibrary library = presets.Library ?? throw new InvalidOperationException("Presets need a workspace");
        PlacedInstance instance = InstanceCommands.Of(edit.Transaction.Document, armies, request.Army, request.Group);
        int units = edit.Transaction.Document.UnitsIn(instance.Army, instance.Path).Count();
        if (!request.Confirm && !await dialogs.ConfirmAsync("Save back to the preset",
            $"Write the layout of {UnitGroupEdits.PathText(instance.Path)} of {instance.Army} ({MainViewModel.Units(units)}) into the preset {instance.Instance.Preset}? Its file is overwritten; the other instances of the preset on this map are then out of date.",
            "Save back"))
        {
            throw new OperationCanceledException("Saving back was cancelled");
        }
        Preset? saved = null;
        string before = instance.Instance.Fingerprint;
        byte[]? fileBefore = null;
        try
        {
            string file = library.FileOf(UnitPresets.Kind, instance.Instance.Preset);
            fileBefore = File.Exists(file) ? File.ReadAllBytes(file) : null;
            UnitCommands.Apply(edit, d => d.SaveInstance(instance.Army, instance.Path, library, out saved));
            model.SavedBack.Note(instance.Army, instance.Path, instance.Instance.Preset, before, UnitPresets.Fingerprint(saved!)!, fileBefore, File.ReadAllBytes(library.FileOf(UnitPresets.Kind, saved!.Name)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            model.Logger("Presets").LogError(exception, "Saving the instance {Group} of {Army} back into the preset {Preset} failed: {Reason}",
                UnitGroupEdits.PathText(instance.Path), instance.Army, instance.Instance.Preset, exception.Message);
            throw new InvalidOperationException($"The preset {instance.Instance.Preset} cannot be written: {exception.Message}", exception);
        }
        presets.Refresh();
        model.Logger("Presets").LogInformation("Saved the instance {Group} of {Army} back into the preset {Preset} ({Units} units)",
            UnitGroupEdits.PathText(instance.Path), instance.Army, saved!.Name, units);
        model.Status = $"Saved {UnitGroupEdits.PathText(instance.Path)} of {instance.Army} back into the preset {saved.Name} ({MainViewModel.Units(units)})";
        return new JsonObject { ["preset"] = saved.Name, ["units"] = units, ["fingerprint"] = UnitPresets.Fingerprint(saved) };
    }
}

/// <summary>
/// Shows a unit preset in the Units mode's panel (user story U-9): the Units mode, its Unit presets
/// list scrolled to the preset and the preset highlighted. Left out, the preset of the group chosen
/// in the tree.
/// </summary>
/// <returns>The preset's name, and whether the workspace has it.</returns>
[EditorCommand("units.showPreset", "Show the preset", Story = "U-9")]
public sealed record ShowUnitPreset(string? Name = null) : IRequest<JsonObject>;

public sealed class ShowUnitPresetHandler(MainViewModel model, ArmiesViewModel armies, PresetsViewModel presets) : IRequestHandler<ShowUnitPreset, JsonObject>
{
    public Task<JsonObject> Handle(ShowUnitPreset request, CancellationToken cancellationToken)
    {
        PresetLibrary library = presets.Library ?? throw new InvalidOperationException("Presets need a workspace");
        string name = request.Name
            ?? armies.ChosenGroup?.Instance?.Preset
            ?? throw new ArgumentException("Choose a group placed from a preset in the Units mode's tree, or give the preset's name as the argument name");
        bool has = library.Find(UnitPresets.Kind, name) is not null;
        model.SelectMode("units");
        if (has && presets.Shown.All(preset => !string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            // the filter hides it: show them all
            presets.Filter = PresetFilter.All;
        }
        presets.Highlighted = name;
        model.Status = has ? $"The preset {name} is highlighted in Unit presets" : $"The workspace has no unit preset {name}: the list cannot show it";
        return Task.FromResult(new JsonObject { ["name"] = name, ["found"] = has });
    }
}

/// <summary>
/// Forgets which unit preset a group was placed from (user story U-9), one step of undo; the units
/// stay as they are. Afterwards the group is an ordinary group.
/// </summary>
/// <param name="Group">Its path (<c>INITIAL/Cybran farm</c>).</param>
[UndoStep]
[EditorCommand("units.detachInstance", "Detach the instance from its preset", Story = "U-9")]
public sealed record DetachUnitInstance(string? Army = null, string? Group = null) : IRequest<JsonObject>;

public sealed class DetachUnitInstanceHandler(MainViewModel model, ArmiesViewModel armies, CurrentEdit edit) : IRequestHandler<DetachUnitInstance, JsonObject>
{
    public Task<JsonObject> Handle(DetachUnitInstance request, CancellationToken cancellationToken)
    {
        PlacedInstance instance = InstanceCommands.Of(edit.Transaction.Document, armies, request.Army, request.Group);
        UnitCommands.Apply(edit, d => d.Detach(instance.Army, instance.Path));
        armies.ChooseWhenShown(instance.Army, instance.Path);
        model.Status = $"Detached {UnitGroupEdits.PathText(instance.Path)} of {instance.Army} from the preset {instance.Instance.Preset}";
        return Task.FromResult(new JsonObject { ["army"] = instance.Army, ["group"] = UnitGroupEdits.PathText(instance.Path), ["preset"] = instance.Instance.Preset });
    }
}
