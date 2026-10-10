using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A group of an army the place tool places into, as its combo box shows it.
/// </summary>
/// <param name="Path"><c>INITIAL/Wrecks</c>; empty for the army's root group.</param>
public sealed record GroupChoice(string Path, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// Places units for an army (user story U-2), or a units preset (U-4): a click places the chosen
/// unit at the mouse (one step of undo, through <c>units.add</c>), into the chosen army's chosen
/// group, named with a free <c>UNIT_n</c>. A structure turns in quarter turns and snaps to the
/// build grid; a mobile unit turns freely. The unit comes from the unit picker (Choose...), a tile
/// chosen on the shelf, or a tile dropped on a view, which places it where it is dropped. Q and E
/// turn what is placed next: a structure or a preset a quarter turn, a mobile unit 15 degrees
/// (with shift a quarter turn).
/// </summary>
public sealed partial class UnitPlaceTool(MainViewModel model) : ObservableObject, IMapTool, IAssetDropTarget
{
    /// <summary>
    /// The armies of the map, for the combo box.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Armies { get; private set; } = [];

    /// <summary>
    /// The army it places for.
    /// </summary>
    [ObservableProperty]
    public partial string? Army { get; set; }

    /// <summary>
    /// The groups of <see cref="Army"/>, for the combo box.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<GroupChoice> Groups { get; private set; } = [];

    /// <summary>
    /// The group it places into; <c>INITIAL</c>, whose units the game creates at the start, unless
    /// another is chosen.
    /// </summary>
    [ObservableProperty]
    public partial GroupChoice? ChosenGroup { get; set; }

    /// <summary>
    /// The group's path (<see cref="ChosenGroup"/>), <c>INITIAL</c> when none is chosen.
    /// </summary>
    public string Group => ChosenGroup?.Path ?? UnitGroupEdits.InitialGroup;

    /// <summary>
    /// The unit it places, from the picker or the shelf; null until one is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitTitle), nameof(HasUnit))]
    public partial AssetEntry? Unit { get; set; }

    public bool HasUnit => Unit is not null;

    /// <summary>
    /// The units preset it places instead of a unit; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitTitle), nameof(HasPreset))]
    public partial string? Preset { get; set; }

    public bool HasPreset => Preset is not null;

    /// <summary>
    /// What a click places, as the options bar says it.
    /// </summary>
    public string UnitTitle => Preset is string preset ? $"Preset {preset}"
        : Unit is AssetEntry unit ? $"{unit.Name} ({unit.Key})"
        : "No unit chosen";

    /// <summary>
    /// Whether what it places are wrecks (user story U-5): they go into the army's <c>WRECKAGE</c>
    /// group, whose units the game turns into wrecks at the start, in place of the chosen group.
    /// </summary>
    [ObservableProperty]
    public partial bool AsWrecks { get; set; }

    /// <summary>
    /// Which way what it places next faces, in degrees clockwise from +x in the top view.
    /// </summary>
    [ObservableProperty]
    public partial double Heading { get; set; }

    public double CursorRadius => 0;

    public string Help => "Click to place; Q and E turn; drop a unit from the shelf to place it there";

    /// <summary>
    /// Its settings for automation's <c>state</c>.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["unit"] = Unit?.Key,
        ["preset"] = Preset,
        ["army"] = Army,
        ["group"] = Group,
        ["heading"] = Heading,
        ["asWrecks"] = AsWrecks,
        ["structure"] = Unit?.IsStructure,
        ["footprint"] = Unit?.Footprint is (double x, double z) ? new JsonArray(x, z) : null,
    };

    partial void OnUnitChanged(AssetEntry? value)
    {
        if (value is not null)
        {
            Preset = null;
        }
    }

    partial void OnArmyChanged(string? value) => RefreshGroups(model.ShownDocument);

    public void Press(float x, float z)
    {
        if (model.ShownDocument is null)
        {
            return;
        }
        if (Preset is string preset)
        {
            _ = model.RunWithAsync("units.placePreset", new JsonObject { ["name"] = preset, ["x"] = x, ["z"] = z });
        }
        else if (Unit is not null)
        {
            _ = model.RunWithAsync("units.add", new JsonObject { ["x"] = x, ["z"] = z });
        }
        else
        {
            model.Status = "Choose a unit to place: Units, Choose a unit, or a unit on the asset shelf";
        }
    }

    public void Drag(float x, float z)
    {
    }

    public void Release()
    {
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt) || key is not ("Q" or "E"))
        {
            return false;
        }
        double step = Preset is not null || Unit?.IsStructure == true || modifiers.HasFlag(ToolModifiers.Shift) ? 90 : 15;
        double heading = (Heading + (key == "E" ? step : -step)) % 360;
        Heading = heading < 0 ? heading + 360 : heading;
        model.Status = $"Places facing {Heading:0}°";
        return true;
    }

    public bool Accepts(AssetEntry entry) => entry.Kind == AssetKind.Unit;

    public void Drop(AssetEntry entry, float x, float z)
    {
        Unit = entry;
        _ = model.RunWithAsync("units.add", new JsonObject { ["blueprint"] = entry.Key, ["x"] = x, ["z"] = z });
    }

    /// <summary>
    /// Shows the armies and groups of a version of the document; an army it no longer has makes
    /// way for the civilians (<c>NEUTRAL_CIVILIAN</c>), the first extra army, or the first army.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        ImmutableArray<Army> armies = document?.Armies ?? [];
        if (!Armies.SequenceEqual(armies.Select(army => army.Name)))
        {
            Armies = [.. armies.Select(army => army.Name)];
        }
        if (document is not null && (Army is null || document.FindArmy(Army) is null))
        {
            Army = document.FindArmy("NEUTRAL_CIVILIAN")?.Name
                ?? document.Scenario.ExtraArmies.FirstOrDefault(name => document.FindArmy(name) is not null)
                ?? armies.FirstOrDefault()?.Name;
        }
        RefreshGroups(document);
    }

    /// <summary>
    /// Chooses where it places: an army and one of its groups.
    /// </summary>
    public void PlaceInto(string army, string group)
    {
        Army = army;
        if (Groups.All(choice => choice.Path != group))
        {
            RefreshGroups(model.ShownDocument);
        }
        ChosenGroup = Groups.FirstOrDefault(choice => choice.Path == group) ?? ChosenGroup;
    }

    private void RefreshGroups(MapDocument? document)
    {
        UnitGroup? root = Army is null ? null : document?.FindArmy(Army)?.Units;
        List<GroupChoice> choices = [];
        void Add(UnitGroup group, ImmutableArray<string> path)
        {
            string text = UnitGroupEdits.PathText(path);
            choices.Add(new GroupChoice(text, path.IsEmpty ? $"{group.Name} (the root group)" : text));
            foreach (UnitGroup child in group.Groups)
            {
                Add(child, path.Add(child.Name));
            }
        }
        if (root is not null)
        {
            Add(root, []);
        }
        if (!choices.Contains(new GroupChoice(UnitGroupEdits.InitialGroup, UnitGroupEdits.InitialGroup)))
        {
            // placing makes it
            choices.Insert(Math.Min(1, choices.Count), new GroupChoice(UnitGroupEdits.InitialGroup, UnitGroupEdits.InitialGroup));
        }
        if (!choices.SequenceEqual(Groups))
        {
            string group = Group;
            Groups = choices;
            ChosenGroup = choices.FirstOrDefault(choice => choice.Path == group) ?? choices.First(choice => choice.Path == UnitGroupEdits.InitialGroup);
        }
        else if (ChosenGroup is null)
        {
            ChosenGroup = choices.First(choice => choice.Path == UnitGroupEdits.InitialGroup);
        }
    }
}
