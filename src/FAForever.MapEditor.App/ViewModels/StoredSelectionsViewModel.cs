using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// One stored selection as the chip along the top view shows it (user story U-10): its digit, the
/// name of the group all its units share, and how many of its things are left on the map.
/// </summary>
public sealed partial class StoredSelectionChip(int slot, string? group, int count, Func<int, Task> recall) : ObservableObject
{
    public int Slot => slot;

    /// <summary>
    /// The group's name when all its units lie in one group; null otherwise.
    /// </summary>
    public string? Group => group;

    /// <summary>
    /// How many things are left on the map.
    /// </summary>
    public int Count => count;

    /// <summary>
    /// Whether the selection is what it holds now.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>
    /// <c>3 · Cybran farm · 17</c>, or <c>3 · 17</c> without a group.
    /// </summary>
    public string Text => group is null ? $"{slot} · {count}" : $"{slot} · {group} · {count}";

    /// <summary>
    /// For screen readers and automation: <c>Recall the selection stored as 3</c>.
    /// </summary>
    public string Label => $"Recall the selection stored as {slot}";

    /// <summary>
    /// Whether none of its things is left on the map; the digit still holds them (undo of a delete
    /// brings them back) until it is stored over or cleared.
    /// </summary>
    public bool IsGone => count == 0;

    public string Help => count == 0
        ? $"Its units are gone from the map (undo of the delete brings them back); Alt and {slot} with nothing selected clears it, with a selection stores that"
        : $"{(group is null ? "" : $"{group}: ")}{count} {(count == 1 ? "thing" : "things")}; the digit {slot} recalls it, shift adds it, Alt and the digit stores the selection again, Alt and the digit with nothing selected clears it";

    [RelayCommand]
    private Task Recall() => recall(slot);
}

/// <summary>
/// The selections stored under the digits 1 to 9 for the open map (user story U-10): Alt and a digit
/// store the selection, the digit recalls it (shift adds it), the chips along the top view show
/// them. They are kept with the project in the user's settings, as the units and areas they name
/// (a marker, prop or decal has no name outside a session), and never in the map's files.
/// </summary>
public sealed partial class StoredSelectionsViewModel : ObservableObject
{
    /// <summary>
    /// The most digits there are: 1 to 9.
    /// </summary>
    public const int SlotCount = 9;

    private readonly MainViewModel _model;
    private readonly ILogger _log;
    private readonly SortedDictionary<int, ImmutableArray<ObjectRef>> _slots = [];
    private UserSettings? _settings;
    private string? _folder;

    public StoredSelectionsViewModel(MainViewModel model, ILogger log)
    {
        _model = model;
        _log = log;
    }

    /// <summary>
    /// The chips of the digits that hold something, in order.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<StoredSelectionChip> Chips { get; private set; } = [];

    public bool HasChips => Chips.Count > 0;

    partial void OnChipsChanged(IReadOnlyList<StoredSelectionChip> value) => OnPropertyChanged(nameof(HasChips));

    /// <summary>
    /// The user's settings the selections are kept in; the editor gives them at its start.
    /// </summary>
    public void Load(UserSettings settings)
    {
        _settings = settings;
        Open(_model.Map?.ProjectFolder);
    }

    /// <summary>
    /// Takes the selections of the project that opened (none for a map that is not a project).
    /// </summary>
    public void Open(string? projectFolder)
    {
        _folder = projectFolder;
        _slots.Clear();
        if (_settings is not null && projectFolder is not null && _model.ShownDocument is MapDocument document)
        {
            foreach ((int slot, IReadOnlyList<string> things) in _settings.StoredSelections(projectFolder))
            {
                ImmutableArray<ObjectRef>.Builder kept = ImmutableArray.CreateBuilder<ObjectRef>();
                foreach (string text in things)
                {
                    try
                    {
                        ObjectRef thing = ObjectRef.Parse(text);
                        kept.Add(thing);
                    }
                    catch (FormatException exception)
                    {
                        _log.LogWarning(exception, "A stored selection of {Folder} names {Thing}, which is not a thing of a map", projectFolder, text);
                    }
                }
                if (kept.Count > 0)
                {
                    _slots[slot] = kept.ToImmutable();
                }
            }
        }
        Refresh(_model.ShownDocument);
    }

    /// <summary>
    /// What is stored under a digit; empty when nothing is.
    /// </summary>
    public ImmutableArray<ObjectRef> Of(int slot) => _slots.GetValueOrDefault(slot, []);

    /// <summary>
    /// Stores the selection under a digit, in place of what it held; an empty selection empties it.
    /// </summary>
    /// <returns>How many things are stored.</returns>
    /// <exception cref="ArgumentException">The digit is not 1 to 9.</exception>
    public int Store(int slot)
    {
        Require(slot);
        ImmutableArray<ObjectRef> items = _model.Selection.Items;
        if (items.IsEmpty)
        {
            _slots.Remove(slot);
        }
        else
        {
            _slots[slot] = items;
        }
        Save();
        Refresh(_model.ShownDocument);
        return items.Length;
    }

    /// <summary>
    /// Selects what a digit holds, what no longer exists left out (the digit keeps it, so undo of a
    /// delete brings the selection back to life).
    /// </summary>
    /// <param name="add">Adds to the selection in place of replacing it.</param>
    /// <returns>How many things are selected afterwards.</returns>
    /// <exception cref="ArgumentException">The digit is not 1 to 9, or holds nothing.</exception>
    public int Recall(int slot, bool add = false)
    {
        Require(slot);
        MapDocument document = _model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (!_slots.TryGetValue(slot, out ImmutableArray<ObjectRef> stored))
        {
            throw new ArgumentException($"Nothing is stored as {slot}: select something and press Alt+{slot}");
        }
        // what is gone stays stored: undo of a delete brings it back
        ImmutableArray<ObjectRef> left = [.. stored.Where(document.Exists)];
        _model.Selection.Apply(left, add ? SelectionChange.Add : SelectionChange.Replace);
        return _model.Selection.Count;
    }

    /// <summary>
    /// Makes the chips again for a version of the document: the group's name and how many are left.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        using Performance.Span measure = Performance.Measure("model.stored-selections");
        List<StoredSelectionChip> chips = [];
        foreach ((int slot, ImmutableArray<ObjectRef> things) in _slots)
        {
            int count = 0;
            string? group = null;
            bool same = true;
            foreach (ObjectRef thing in things)
            {
                if (document is null || !document.Exists(thing))
                {
                    continue;
                }
                count++;
                if (thing.Kind == ObjectKind.Unit && document.Find(thing) is PlacedUnit unit && !unit.Group.IsEmpty)
                {
                    string name = unit.Group[^1];
                    if (group is null)
                    {
                        group = name;
                    }
                    else if (group != name)
                    {
                        same = false;
                    }
                }
                else
                {
                    same = false;
                }
            }
            chips.Add(new StoredSelectionChip(slot, same ? group : null, count, digit => _model.RunWithAsync("select.recall", new JsonObject { ["slot"] = digit })));
        }
        if (!chips.Select(chip => (chip.Slot, chip.Text)).SequenceEqual(Chips.Select(chip => (chip.Slot, chip.Text))))
        {
            Chips = chips;
        }
        SelectionChanged();
    }

    /// <summary>
    /// The selection changed: the chip that holds exactly it lights up.
    /// </summary>
    public void SelectionChanged()
    {
        foreach (StoredSelectionChip chip in Chips)
        {
            ImmutableArray<ObjectRef> stored = _slots.GetValueOrDefault(chip.Slot, []);
            chip.IsCurrent = _model.Selection.HasAny && stored.Length == _model.Selection.Count && stored.All(_model.Selection.Contains);
        }
    }

    /// <summary>
    /// What a digit holds, for <c>state</c>: the things as text.
    /// </summary>
    public JsonObject State()
    {
        JsonObject slots = [];
        foreach ((int slot, ImmutableArray<ObjectRef> things) in _slots)
        {
            StoredSelectionChip? chip = Chips.FirstOrDefault(c => c.Slot == slot);
            slots[slot.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["things"] = new JsonArray([.. things.Select(thing => (JsonNode)JsonValue.Create(thing.ToString())!)]),
                ["count"] = chip?.Count ?? 0,
                ["group"] = chip?.Group,
                ["chip"] = chip?.Text,
            };
        }
        return new JsonObject { ["slots"] = slots, ["kept"] = _folder is not null && _settings is not null };
    }

    private static void Require(int slot)
    {
        if (slot is < 1 or > SlotCount)
        {
            throw new ArgumentException($"There is no stored selection {slot}: the digits 1 to {SlotCount}");
        }
    }

    // the units and areas, which a project names the same way on every start
    private void Save()
    {
        // a map saved as a project since it opened keeps them from now on
        _folder = _model.Map?.ProjectFolder;
        if (_settings is null || _folder is null)
        {
            return;
        }
        try
        {
            Dictionary<int, IReadOnlyList<string>> slots = _slots
                .Select(slot => (slot.Key, Things: (IReadOnlyList<string>)[.. slot.Value.Where(thing => thing.Kind is ObjectKind.Unit or ObjectKind.Area).Select(thing => thing.ToString())]))
                .Where(slot => slot.Things.Count > 0)
                .ToDictionary(slot => slot.Key, slot => slot.Things);
            _settings.SetStoredSelections(_folder, slots);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MapProjectException)
        {
            _log.LogWarning(exception, "Could not remember the stored selections of {Folder} in {File}: {Reason}", _folder, _settings.File, exception.Message);
        }
    }
}
