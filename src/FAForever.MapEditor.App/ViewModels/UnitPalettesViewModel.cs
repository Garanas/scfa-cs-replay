using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;
using FAForever.MapEditor.Project.Workspaces.Serialized;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// One slot of the chosen palette as the Units panel draws it (user story U-8): its key, the unit
/// (with the game's icon) or the units preset it holds, or nothing.
/// </summary>
public sealed partial class PaletteSlotItem(int index, UnitPaletteSlot slot, AssetEntry? entry, Preset? preset) : ObservableObject
{
    /// <summary>
    /// 0 to 9.
    /// </summary>
    public int Index => index;

    /// <summary>
    /// The key that picks it: <c>1</c> to <c>9</c>, <c>0</c>.
    /// </summary>
    public string Key => UnitPalettes.KeyLabel(index);

    /// <summary>
    /// The slot as the palette's file holds it.
    /// </summary>
    public UnitPaletteSlot Slot => slot;

    /// <summary>
    /// The unit it holds, from the workspace's assets; null for a preset, an empty slot or a unit
    /// the workspace lacks (<see cref="IsMissing"/>).
    /// </summary>
    public AssetEntry? Entry => entry;

    /// <summary>
    /// The units preset it holds; null for a unit, an empty slot or a preset the workspace lacks.
    /// </summary>
    public Preset? Preset => preset;

    public bool IsEmpty => slot.IsEmpty;

    public bool IsUnit => slot.IsUnit;

    public bool IsPreset => slot.IsPreset;

    /// <summary>
    /// Whether the slot names a unit or preset the workspace does not have.
    /// </summary>
    public bool IsMissing => slot.IsUnit && entry is null || slot.IsPreset && preset is null;

    /// <summary>
    /// The unit's name, the preset's name, or what to do with an empty slot.
    /// </summary>
    public string Title => slot.IsUnit ? entry?.Name ?? slot.Unit! : slot.IsPreset ? slot.Preset! : "drop here";

    /// <summary>
    /// How many units the preset places, as the presets list says it; null for a unit or an empty slot.
    /// </summary>
    public string? Detail => preset is not null ? Controls.PresetDetailConverter.Detail(preset) : null;

    public string? Icon => entry?.Icon;

    public string? IconBackground => entry?.IconBackground;

    public bool HasIcon => entry?.Icon is not null;

    /// <summary>
    /// The unit's mesh, drawn when it has no icon.
    /// </summary>
    public AssetModel? Model => entry?.Model;

    public bool ShowsModel => entry is not null && entry.Icon is null && entry.Model is not null;

    /// <summary>
    /// Whether the slot holds what the place tool places now.
    /// </summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    /// <summary>
    /// What a screen reader says of the slot's button.
    /// </summary>
    public string Label => $"Palette slot {Key}: {(IsEmpty ? "empty" : IsUnit ? $"{Title} ({slot.Unit})" : $"preset {Title}")}{(IsMissing ? ", not in the workspace" : "")}";

    /// <summary>
    /// The tool tip: what it holds and how to change it.
    /// </summary>
    public string Help => IsEmpty
        ? $"Press {Key} to pick this slot once it holds something. Drop a unit from the shelf here, or right-click to choose one"
        : IsMissing ? $"{Title}: the workspace has no such {(IsUnit ? "unit" : "preset")}. Right-click to clear the slot or choose a unit"
        : $"{Title}{(Detail is null ? "" : $", {Detail}")}. Press {Key} or click to place it; right-click to change or clear the slot";
}

/// <summary>
/// A unit or preset placed last (user story U-8), offered again as a chip under the palette.
/// </summary>
public sealed class LastPlacedItem(string key, AssetEntry? entry, Preset? preset, bool qualify = false)
{
    private const string UnitPrefix = "unit:";
    private const string PresetPrefix = "preset:";

    /// <summary>
    /// What is remembered, as the settings hold it: <c>unit:ueb0101</c> or <c>preset:UEF farm</c>.
    /// </summary>
    public string Key => key;

    public static string UnitKey(string id) => UnitPrefix + id.Trim().ToLowerInvariant();

    public static string PresetKey(string name) => PresetPrefix + name.Trim();

    public bool IsPreset => key.StartsWith(PresetPrefix, StringComparison.Ordinal);

    public bool IsUnit => key.StartsWith(UnitPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The unit's id or the preset's name.
    /// </summary>
    public string Name => IsPreset ? key[PresetPrefix.Length..] : IsUnit ? key[UnitPrefix.Length..] : key;

    public AssetEntry? Entry => entry;

    public string Title => IsUnit ? entry?.Name ?? Name : Name;

    /// <summary>
    /// What the chip reads: the title, with the faction after it when another chip has the same
    /// title (an Aeon and a UEF Power Generator).
    /// </summary>
    public string ChipText => qualify && entry is not null ? $"{entry.Name} ({entry.Group})" : Title;

    /// <summary>
    /// The tool tip: the full name, the unit's id and how to place it again.
    /// </summary>
    public string Help => IsUnit ? $"{ChipText} ({Name}): click to place it again" : $"The preset {Name}: click to place it again";

    public string? Icon => entry?.Icon;

    public string? IconBackground => entry?.IconBackground;

    public bool HasIcon => entry?.Icon is not null;

    public bool IsMissing => IsUnit && entry is null || IsPreset && preset is null;

    public string Label => IsPreset ? $"Place the preset {Name} again" : $"Place {Title} again";
}

/// <summary>
/// The Units mode's palettes (user story U-8): named sets of ten slots, each a unit or a units
/// preset, kept as presets of kind <see cref="PresetKinds.Palettes"/> in the workspace beside the
/// other presets (<see cref="UnitPalettes"/>). The chosen palette's slots show in the Units panel;
/// the keys 1 to 9 and 0 pick a slot while the place tool is active, Tab goes to the next palette.
/// A slot picked chooses its unit (<c>units.pick</c>) or preset (<c>presets.apply</c>) for the
/// place tool. The units and presets placed last are offered again (<see cref="LastPlaced"/>),
/// kept in the user's settings with the palette chosen last.
/// </summary>
public sealed partial class UnitPalettesViewModel : ObservableObject
{
    /// <summary>
    /// How many units and presets placed last are offered again.
    /// </summary>
    public const int LastPlacedCount = 6;

    private readonly PresetsViewModel _presets;
    private readonly AssetsViewModel _assets;
    private readonly ILogger _log;
    private readonly Func<string, JsonObject, Task> _run;
    private readonly List<string> _lastPlaced = [];
    private bool _loading;

    /// <param name="run">Runs a command with arguments, as a button does.</param>
    public UnitPalettesViewModel(PresetsViewModel presets, AssetsViewModel assets, ILogger log, Func<string, JsonObject, Task> run)
    {
        _presets = presets;
        _assets = assets;
        _log = log;
        _run = run;
        presets.LibraryChanged += files =>
        {
            if (files is null || files.Any(file => file.Contains(PresetKinds.Palettes, StringComparison.OrdinalIgnoreCase) || file.Contains(PresetKinds.Units, StringComparison.OrdinalIgnoreCase)))
            {
                Refresh();
            }
        };
        assets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetsViewModel.Catalog))
            {
                RebuildSlots();
                RebuildLastPlaced();
            }
        };
        Slots = EmptySlots();
    }

    /// <summary>
    /// The user's settings the chosen palette and the units placed last are kept in; null without
    /// them (tests), when nothing is remembered.
    /// </summary>
    public UserSettings? Settings { get; private set; }

    /// <summary>
    /// Takes the palette chosen last and the units placed last from the user's settings.
    /// </summary>
    public void Load(UserSettings settings)
    {
        Settings = settings;
        UnitsSettingsDto units = settings.Units;
        _lastPlaced.Clear();
        _lastPlaced.AddRange((units.LastPlaced ?? []).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).Take(LastPlacedCount));
        _remembered = units.Palette;
        RebuildLastPlaced();
        Refresh();
    }

    // the palette the settings name, chosen once the library lists it
    private string? _remembered;

    /// <summary>
    /// Whether palettes can be read and written: the workspace is ready.
    /// </summary>
    public bool HasLibrary => _presets.Library is not null;

    /// <summary>
    /// The palettes' names, in order.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPalettes))]
    public partial IReadOnlyList<string> Names { get; private set; } = [];

    public bool HasPalettes => Names.Count > 0;

    /// <summary>
    /// The chosen palette's name; null while the workspace has none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosen))]
    public partial string? Chosen { get; set; }

    public bool HasChosen => Chosen is not null;

    /// <summary>
    /// The chosen palette's ten slots (empty ones while there is no palette).
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<PaletteSlotItem> Slots { get; private set; }

    /// <summary>
    /// The slot that holds what the place tool places now, 0 to 9; -1 for none.
    /// </summary>
    [ObservableProperty]
    public partial int ChosenSlot { get; private set; } = -1;

    /// <summary>
    /// The units and presets placed last, the newest first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastPlaced))]
    public partial IReadOnlyList<LastPlacedItem> LastPlaced { get; private set; } = [];

    public bool HasLastPlaced => LastPlaced.Count > 0;

    /// <summary>
    /// The chosen palette's slots as its file holds them.
    /// </summary>
    public UnitPaletteSettings ChosenSettings { get; private set; } = UnitPalettes.Empty;

    partial void OnChosenChanged(string? value)
    {
        if (_loading)
        {
            return;
        }
        RebuildSlots();
        Remember();
    }

    /// <summary>
    /// Reads the workspace's palettes again; the chosen one stays chosen when it is still there,
    /// else the one remembered, else the first.
    /// </summary>
    public void Refresh()
    {
        List<string> names = [];
        if (_presets.Library is PresetLibrary library)
        {
            List<string> unreadable = [];
            try
            {
                names = [.. library.List(UnitPalettes.Kind, unreadable).Select(preset => preset.Name)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(exception, "Cannot read the palettes in {Folder}: {Reason}", library.Folder, exception.Message);
            }
            foreach (string file in unreadable)
            {
                _log.LogWarning("A palette cannot be read: {File}", file);
            }
        }
        // nothing chosen or remembered: a T1 base (the examples' first useful one), else the first by name
        string? wanted = Chosen is string chosen && names.Contains(chosen, StringComparer.OrdinalIgnoreCase) ? chosen
            : _remembered is string remembered && names.Contains(remembered, StringComparer.OrdinalIgnoreCase) ? remembered
            : names.FirstOrDefault(name => name.EndsWith("T1 base", StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault();
        _loading = true;
        try
        {
            if (!names.SequenceEqual(Names))
            {
                Names = names;
            }
            Chosen = wanted is null ? null : names.First(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _loading = false;
        }
        OnPropertyChanged(nameof(HasLibrary));
        RebuildSlots();
    }

    /// <summary>
    /// Chooses a palette by name.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such palette.</exception>
    public void Choose(string name)
    {
        Chosen = Names.FirstOrDefault(each => string.Equals(each, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(Names.Count == 0 ? "The workspace has no palettes yet: Units, New palette makes one" : $"There is no palette '{name}': {string.Join(", ", Names)}");
    }

    /// <summary>
    /// Chooses the palette after (or, with a negative step, before) the chosen one, round the list.
    /// </summary>
    /// <returns>The palette chosen, or null when there are none.</returns>
    public string? Next(int step)
    {
        if (Names.Count == 0)
        {
            return null;
        }
        int index = Chosen is null ? -1 : Names.ToList().FindIndex(name => name == Chosen);
        int next = ((index + Math.Sign(step)) % Names.Count + Names.Count) % Names.Count;
        Chosen = Names[next];
        return Chosen;
    }

    /// <summary>
    /// Picks a slot: its unit becomes what the place tool places (<c>units.pick</c>), or its preset
    /// (<c>presets.apply</c>); the caller chooses the place tool. An empty slot picks nothing.
    /// </summary>
    /// <param name="index">0 to 9.</param>
    /// <returns>What was picked, as the status line says it; null for an empty or missing slot.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Not a slot.</exception>
    public async Task<string?> PickSlotAsync(int index)
    {
        if (index < 0 || index >= UnitPalettes.SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "A palette has slots 0 to 9");
        }
        PaletteSlotItem slot = Slots[index];
        if (slot.IsEmpty || slot.IsMissing)
        {
            return null;
        }
        if (slot.IsUnit)
        {
            await _run("units.pick", new JsonObject { ["key"] = slot.Slot.Unit });
            return $"{slot.Title} ({slot.Slot.Unit})";
        }
        await _run("presets.apply", new JsonObject { ["kind"] = PresetKinds.Units, ["name"] = slot.Slot.Preset });
        return $"the preset {slot.Title}";
    }

    /// <summary>
    /// Marks the slot that holds what the place tool places (a unit's id, or a preset's name): the
    /// panel highlights it.
    /// </summary>
    public void FollowTool(string? unit, string? preset)
    {
        int chosen = -1;
        for (int i = 0; i < Slots.Count; i++)
        {
            UnitPaletteSlot slot = Slots[i].Slot;
            bool holds = preset is not null ? slot.IsPreset && string.Equals(slot.Preset, preset, StringComparison.OrdinalIgnoreCase)
                : unit is not null && slot.IsUnit && string.Equals(slot.Unit, unit, StringComparison.OrdinalIgnoreCase);
            if (holds && chosen < 0)
            {
                chosen = i;
            }
            Slots[i].IsChosen = holds && chosen == i;
        }
        ChosenSlot = chosen;
    }

    /// <summary>
    /// Writes one slot of a palette, and shows it when the palette is the chosen one.
    /// </summary>
    /// <param name="name">The palette; null for the chosen one.</param>
    /// <param name="index">0 to 9.</param>
    /// <returns>The palette's name.</returns>
    /// <exception cref="InvalidOperationException">No workspace, or no palette to write into.</exception>
    /// <exception cref="ArgumentException">There is no such palette.</exception>
    public string SetSlot(string? name, int index, UnitPaletteSlot slot)
    {
        PresetLibrary library = _presets.Library ?? throw new InvalidOperationException("Palettes need a workspace");
        string palette = name?.Trim() ?? Chosen ?? throw new InvalidOperationException("The workspace has no palettes yet: Units, New palette makes one");
        Preset preset = library.Find(UnitPalettes.Kind, palette) ?? throw new ArgumentException($"There is no palette '{palette}': {string.Join(", ", Names)}");
        UnitPaletteSettings settings = UnitPalettes.SetSlot(UnitPalettes.Read(preset), index, slot);
        library.Save(UnitPalettes.ToPreset(preset.Name, settings));
        _log.LogInformation("Palette {Palette}, slot {Slot}: {What}", preset.Name, UnitPalettes.KeyLabel(index), slot.IsEmpty ? "cleared" : slot.IsUnit ? slot.Unit : $"preset {slot.Preset}");
        Refresh();
        return preset.Name;
    }

    /// <summary>
    /// Makes a new, empty palette and chooses it.
    /// </summary>
    /// <param name="name">Null or empty for <c>Palette</c>, made unique.</param>
    /// <returns>Its name.</returns>
    /// <exception cref="InvalidOperationException">No workspace.</exception>
    public string New(string? name)
    {
        PresetLibrary library = _presets.Library ?? throw new InvalidOperationException("Palettes need a workspace");
        string wanted = string.IsNullOrWhiteSpace(name) ? "Palette" : name.Trim();
        string made = library.FreeName(UnitPalettes.Kind, wanted);
        library.Save(UnitPalettes.ToPreset(made, UnitPalettes.Empty));
        _log.LogInformation("Made the palette {Palette}", made);
        Chosen = null;
        _remembered = made;
        Refresh();
        Remember();
        return made;
    }

    /// <summary>
    /// Renames a palette: a new file under the new name, the old one deleted.
    /// </summary>
    /// <param name="name">Null for the chosen one.</param>
    /// <returns>The new name as written.</returns>
    /// <exception cref="InvalidOperationException">No workspace, or no palette.</exception>
    /// <exception cref="ArgumentException">There is no such palette, no new name, or the new name is taken.</exception>
    public string Rename(string? name, string to)
    {
        PresetLibrary library = _presets.Library ?? throw new InvalidOperationException("Palettes need a workspace");
        string palette = name?.Trim() ?? Chosen ?? throw new InvalidOperationException("The workspace has no palettes yet");
        if (string.IsNullOrWhiteSpace(to))
        {
            throw new ArgumentException("Give the palette's new name", nameof(to));
        }
        Preset preset = library.Find(UnitPalettes.Kind, palette) ?? throw new ArgumentException($"There is no palette '{palette}': {string.Join(", ", Names)}");
        string renamed = to.Trim();
        if (!string.Equals(renamed, preset.Name, StringComparison.OrdinalIgnoreCase) && library.Find(UnitPalettes.Kind, renamed) is not null)
        {
            throw new ArgumentException($"There is a palette named '{renamed}' already");
        }
        if (renamed == preset.Name)
        {
            return renamed;
        }
        if (!string.Equals(renamed, preset.Name, StringComparison.OrdinalIgnoreCase))
        {
            library.Delete(UnitPalettes.Kind, preset.Name);
        }
        library.Save(UnitPalettes.ToPreset(renamed, UnitPalettes.Read(preset)));
        _log.LogInformation("Renamed the palette {Palette} to {Name}", preset.Name, renamed);
        if (Chosen == preset.Name)
        {
            Chosen = null;
            _remembered = renamed;
        }
        Refresh();
        Remember();
        return renamed;
    }

    /// <summary>
    /// Deletes a palette.
    /// </summary>
    /// <param name="name">Null for the chosen one.</param>
    /// <returns>The name of the palette deleted.</returns>
    /// <exception cref="InvalidOperationException">No workspace, or no palette.</exception>
    /// <exception cref="ArgumentException">There is no such palette.</exception>
    public string Delete(string? name)
    {
        PresetLibrary library = _presets.Library ?? throw new InvalidOperationException("Palettes need a workspace");
        string palette = name?.Trim() ?? Chosen ?? throw new InvalidOperationException("The workspace has no palettes yet");
        Preset preset = library.Find(UnitPalettes.Kind, palette) ?? throw new ArgumentException($"There is no palette '{palette}': {string.Join(", ", Names)}");
        library.Delete(UnitPalettes.Kind, preset.Name);
        _log.LogInformation("Deleted the palette {Palette}", preset.Name);
        Refresh();
        Remember();
        return preset.Name;
    }

    /// <summary>
    /// Notes a unit or preset placed (<see cref="LastPlacedItem.UnitKey"/>, <see cref="LastPlacedItem.PresetKey"/>):
    /// it goes first among the last placed, once, and the user's settings keep the list.
    /// </summary>
    public void NotePlaced(string key)
    {
        if (_lastPlaced.Count > 0 && _lastPlaced[0] == key)
        {
            return;
        }
        _lastPlaced.Remove(key);
        _lastPlaced.Insert(0, key);
        while (_lastPlaced.Count > LastPlacedCount)
        {
            _lastPlaced.RemoveAt(_lastPlaced.Count - 1);
        }
        RebuildLastPlaced();
        Remember();
    }

    /// <summary>
    /// Chooses a unit or preset placed last for the place tool, as a slot does.
    /// </summary>
    public async Task PickLastPlacedAsync(LastPlacedItem item)
    {
        await (item.IsPreset
            ? _run("presets.apply", new JsonObject { ["kind"] = PresetKinds.Units, ["name"] = item.Name })
            : _run("units.pick", new JsonObject { ["key"] = item.Name }));
        ChoosePlaceTool?.Invoke();
    }

    /// <summary>
    /// Chooses the Place tool of the Units mode; the editor sets it. A chip and a slot both choose it.
    /// </summary>
    public Action? ChoosePlaceTool { get; set; }

    /// <summary>
    /// Writes the palette chosen and the list placed last into the user's settings.
    /// </summary>
    private void Remember()
    {
        if (Settings is not UserSettings settings)
        {
            return;
        }
        try
        {
            settings.SetUnits(new UnitsSettingsDto { Palette = Chosen, LastPlaced = _lastPlaced.Count == 0 ? null : [.. _lastPlaced] });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MapProjectException)
        {
            _log.LogWarning(exception, "Could not remember the palette in {File}: {Reason}", settings.File, exception.Message);
        }
    }

    private static IReadOnlyList<PaletteSlotItem> EmptySlots() =>
        [.. Enumerable.Range(0, UnitPalettes.SlotCount).Select(i => new PaletteSlotItem(i, UnitPaletteSlot.Empty, null, null))];

    private void RebuildSlots()
    {
        UnitPaletteSettings settings = UnitPalettes.Empty;
        if (Chosen is string chosen && _presets.Library is PresetLibrary library)
        {
            try
            {
                settings = library.Find(UnitPalettes.Kind, chosen) is Preset preset ? UnitPalettes.Read(preset) : UnitPalettes.Empty;
            }
            catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
            {
                _log.LogError(exception, "The palette {Palette} cannot be read: {Reason}", chosen, exception.Message);
            }
        }
        ChosenSettings = settings;
        int[] chosenSlots = [.. Slots.Where(slot => slot.IsChosen).Select(slot => slot.Index)];
        List<PaletteSlotItem> slots = [];
        for (int i = 0; i < UnitPalettes.SlotCount; i++)
        {
            UnitPaletteSlot slot = settings.Slots[i];
            AssetEntry? entry = slot.IsUnit ? _assets.Find(slot.Unit!) is { Kind: AssetKind.Unit } unit ? unit : null : null;
            Preset? preset = slot.IsPreset ? FindPreset(slot.Preset!) : null;
            slots.Add(new PaletteSlotItem(i, slot, entry, preset) { IsChosen = chosenSlots.Contains(i) && !slot.IsEmpty });
        }
        Slots = slots;
        ChosenSlot = slots.FirstOrDefault(slot => slot.IsChosen)?.Index ?? -1;
    }

    private Preset? FindPreset(string name)
    {
        try
        {
            return _presets.Library?.Find(PresetKinds.Units, name);
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.LogWarning(exception, "The preset {Preset} of a palette slot cannot be read: {Reason}", name, exception.Message);
            return null;
        }
    }

    private void RebuildLastPlaced()
    {
        List<(string Key, AssetEntry? Entry, Preset? Preset)> found = [.. _lastPlaced.Select(key =>
        {
            LastPlacedItem bare = new LastPlacedItem(key, null, null);
            AssetEntry? entry = bare.IsUnit && _assets.Find(bare.Name) is { Kind: AssetKind.Unit } unit ? unit : null;
            Preset? preset = bare.IsPreset ? FindPreset(bare.Name) : null;
            return (key, entry, preset);
        })];
        // two chips with one name say their faction
        HashSet<string> twice = [.. found.Where(item => item.Entry is not null).GroupBy(item => item.Entry!.Name).Where(g => g.Count() > 1).Select(g => g.Key)];
        LastPlaced = [.. found.Select(item => new LastPlacedItem(item.Key, item.Entry, item.Preset, item.Entry is not null && twice.Contains(item.Entry.Name)))];
    }

    /// <summary>
    /// The chosen palette's folder, made when it is missing.
    /// </summary>
    public string Folder => _presets.FolderOf(UnitPalettes.Kind);

    /// <summary>
    /// The unit the workspace has under a key (an id, a tile's key); null for anything else.
    /// </summary>
    public AssetEntry? FindUnit(string key) => _assets.Find(key) is { Kind: AssetKind.Unit } unit ? unit : null;

    /// <summary>
    /// Runs a command with arguments, as the panel's buttons and drops do.
    /// </summary>
    public Task Run(string id, JsonObject arguments) => _run(id, arguments);

    /// <summary>
    /// The palettes for automation's <c>state.units.palette</c>.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["names"] = new JsonArray([.. Names.Select(name => (JsonNode)JsonValue.Create(name))]),
        ["chosen"] = Chosen,
        ["chosenSlot"] = ChosenSlot < 0 ? null : ChosenSlot,
        ["slots"] = new JsonArray([.. Slots.Select(slot => (JsonNode)new JsonObject
        {
            ["key"] = slot.Key,
            ["unit"] = slot.Slot.IsUnit ? slot.Slot.Unit : null,
            ["preset"] = slot.Slot.IsPreset ? slot.Slot.Preset : null,
            ["title"] = slot.IsEmpty ? null : slot.Title,
            ["missing"] = slot.IsMissing,
            ["chosen"] = slot.IsChosen,
        })]),
        ["lastPlaced"] = new JsonArray([.. LastPlaced.Select(item => (JsonNode)JsonValue.Create(item.Key))]),
    };

    /// <summary>
    /// A slot's button: picks it.
    /// </summary>
    [RelayCommand]
    private Task PickSlot(PaletteSlotItem slot) => _run("units.slot", new JsonObject { ["n"] = slot.Index + 1 });

    /// <summary>
    /// A slot's context menu: clears it.
    /// </summary>
    [RelayCommand]
    private Task ClearSlot(PaletteSlotItem slot) => _run("units.setSlot", new JsonObject { ["slot"] = slot.Index + 1, ["clear"] = true });

    /// <summary>
    /// A slot's context menu: chooses a unit for it in the picker.
    /// </summary>
    [RelayCommand]
    private Task ChooseUnitForSlot(PaletteSlotItem slot) => _run("units.setSlot", new JsonObject { ["slot"] = slot.Index + 1 });

    /// <summary>
    /// A chip of the last placed: chooses it again.
    /// </summary>
    [RelayCommand]
    private Task PickLastPlaced(LastPlacedItem item) => PickLastPlacedAsync(item);

    [RelayCommand]
    private Task NewPalette() => _run("units.newPalette", []);

    [RelayCommand]
    private Task RenamePalette() => _run("units.renamePalette", []);

    [RelayCommand]
    private Task DeletePalette() => _run("units.deletePalette", []);

    [RelayCommand]
    private Task ShowFolder() => _run("presets.showFolder", new JsonObject { ["kind"] = UnitPalettes.Kind });
}
