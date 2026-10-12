using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Decals mode's state that is the editor's and not the map's (user story D-2): the decal
/// groups as the panel lists them, and which groups are hidden. A hidden group's decals are not
/// drawn in the 3D view and cannot be picked; hiding is an editor setting, never saved or exported.
/// </summary>
/// <remarks>
/// The groups themselves are the document's (<see cref="MapDocument.DecalGroups"/>), changed
/// through the commands <c>decals.addGroup</c>, <c>decals.renameGroup</c> and the like. Another map
/// shows every group again.
/// </remarks>
public sealed partial class DecalsViewModel : ObservableObject
{
    private static readonly HashSet<int> NoIds = [];

    private readonly Func<string, JsonObject, Task> _run;
    private readonly Dictionary<int, DecalGroupItem> _items = [];
    private HashSet<int> _hiddenGroups = [];
    private ImmutableGroups _groupsOf;

    /// <param name="run">Runs a command with its arguments, for the panel's fields.</param>
    public DecalsViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
    }

    /// <summary>
    /// The map's decal groups, in its order.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups))]
    public partial IReadOnlyList<DecalGroupItem> Groups { get; private set; } = [];

    public bool HasGroups => Groups.Count > 0;

    /// <summary>
    /// The ids of the groups hidden in the views.
    /// </summary>
    public IReadOnlySet<int> HiddenGroups => _hiddenGroups;

    /// <summary>
    /// The ids of the decals of the hidden groups: not drawn in 3D, not picked.
    /// </summary>
    public IReadOnlySet<int> HiddenDecals { get; private set; } = NoIds;

    /// <summary>
    /// Raised when the decals hidden change: the 3D view draws again.
    /// </summary>
    public event EventHandler? HiddenChanged;

    /// <summary>
    /// Whether a thing is a decal of a hidden group.
    /// </summary>
    public bool IsHidden(ObjectRef thing) => thing.Kind == ObjectKind.Decal && HiddenDecals.Contains(thing.Id);

    /// <summary>
    /// Whether a thing may be picked: everything but the decals of hidden groups.
    /// </summary>
    public bool IsShown(ObjectRef thing) => !IsHidden(thing);

    /// <summary>
    /// Shows the groups as they are in a version of the document, and which decals they hide.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        ImmutableGroups groups = new ImmutableGroups(document?.DecalGroups ?? []);
        if (groups == _groupsOf)
        {
            return;
        }
        _groupsOf = groups;
        using Performance.Span measure = Performance.Measure("model.decal-groups");
        List<DecalGroupItem> items = [];
        foreach (DecalGroup group in groups.Value)
        {
            if (!_items.TryGetValue(group.Id, out DecalGroupItem? item))
            {
                item = new DecalGroupItem(group.Id, this);
                _items[group.Id] = item;
            }
            item.Show(group.Name, group.DecalIds.Length, !_hiddenGroups.Contains(group.Id));
            items.Add(item);
        }
        foreach (int gone in _items.Keys.Except(items.Select(item => item.Id)).ToArray())
        {
            _items.Remove(gone);
        }
        _hiddenGroups.IntersectWith(items.Select(item => item.Id));
        if (!items.SequenceEqual(Groups))
        {
            Groups = items;
        }
        UpdateHidden(groups.Value);
    }

    /// <summary>
    /// Shows or hides a group's decals in the views.
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such group.</exception>
    public void SetShown(MapDocument document, int group, bool shown)
    {
        if (document.FindDecalGroup(group) is null)
        {
            throw new ArgumentException($"The map has no decal group {group}");
        }
        bool changed = shown ? _hiddenGroups.Remove(group) : _hiddenGroups.Add(group);
        if (_items.TryGetValue(group, out DecalGroupItem? item))
        {
            item.Show(item.Name, item.Count, shown);
        }
        if (changed)
        {
            UpdateHidden(document.DecalGroups);
        }
    }

    /// <summary>
    /// Shows every group again and forgets the groups: another map was opened.
    /// </summary>
    public void Clear()
    {
        _hiddenGroups = [];
        _items.Clear();
        _groupsOf = default;
        Groups = [];
        if (HiddenDecals.Count > 0)
        {
            HiddenDecals = NoIds;
            HiddenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal Task Run(string id, JsonObject arguments) => _run(id, arguments);

    /// <summary>
    /// The panel's button: selects the group's decals (<c>decals.selectGroup</c>).
    /// </summary>
    [RelayCommand]
    private Task SelectGroup(DecalGroupItem group) => _run("decals.selectGroup", new JsonObject { ["group"] = group.Id });

    /// <summary>
    /// The panel's button: adds the selected decals to the group (<c>decals.addToGroup</c>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedDecals))]
    private Task AddToGroup(DecalGroupItem group) => _run("decals.addToGroup", new JsonObject { ["group"] = group.Id });

    /// <summary>
    /// The panel's button: takes the selected decals out of the group (<c>decals.removeFromGroup</c>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedDecals))]
    private Task RemoveFromGroup(DecalGroupItem group) => _run("decals.removeFromGroup", new JsonObject { ["group"] = group.Id });

    /// <summary>
    /// Whether a decal is selected: Add and Take out act on the selected decals, so they wait for one.
    /// </summary>
    public bool HasSelectedDecals { get; private set; }

    /// <summary>
    /// What the disabled Add and Take out say; null while they are enabled.
    /// </summary>
    public string? SelectionTip => HasSelectedDecals ? null : "Select decals on the map first";

    /// <summary>
    /// The selection changed: whether it holds a decal.
    /// </summary>
    public void SelectionChanged(bool anyDecal)
    {
        if (anyDecal == HasSelectedDecals)
        {
            return;
        }
        HasSelectedDecals = anyDecal;
        OnPropertyChanged(nameof(HasSelectedDecals));
        OnPropertyChanged(nameof(SelectionTip));
        AddToGroupCommand.NotifyCanExecuteChanged();
        RemoveFromGroupCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The panel's cross: removes the group, keeping its decals (<c>decals.deleteGroup</c>).
    /// </summary>
    [RelayCommand]
    private Task DeleteGroup(DecalGroupItem group) => _run("decals.deleteGroup", new JsonObject { ["group"] = group.Id });

    private void UpdateHidden(IEnumerable<DecalGroup> groups)
    {
        HashSet<int> hidden = [.. groups.Where(group => _hiddenGroups.Contains(group.Id)).SelectMany(group => group.DecalIds)];
        if (hidden.SetEquals(HiddenDecals))
        {
            return;
        }
        HiddenDecals = hidden.Count == 0 ? NoIds : hidden;
        HiddenChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A version of the document's groups, compared by the array they are held in.
    /// </summary>
    private readonly record struct ImmutableGroups(System.Collections.Immutable.ImmutableArray<DecalGroup> Value);
}

/// <summary>
/// A decal group in the Decals panel: its name (typed to rename it), how many decals it holds, and
/// whether it shows.
/// </summary>
public sealed partial class DecalGroupItem : ObservableObject
{
    private readonly DecalsViewModel _owner;
    private string _name = "";
    private bool _isShown = true;

    internal DecalGroupItem(int id, DecalsViewModel owner)
    {
        Id = id;
        _owner = owner;
    }

    public int Id { get; }

    /// <summary>
    /// How many decals it holds.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial int Count { get; private set; }

    /// <summary>
    /// <c>12 decals</c>.
    /// </summary>
    public string Detail => string.Create(CultureInfo.InvariantCulture, $"{Count} decal{(Count == 1 ? "" : "s")}");

    /// <summary>
    /// Its name; a name typed renames it (<c>decals.renameGroup</c>).
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (value == _name)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                // the binding wrote the text: show the name again
                string shown = _name;
                _name = value;
                OnPropertyChanged();
                _name = shown;
                OnPropertyChanged();
                return;
            }
            _ = _owner.Run("decals.renameGroup", new JsonObject { ["group"] = Id, ["name"] = value });
        }
    }

    /// <summary>
    /// Whether its decals show in the views; changing it runs <c>decals.showGroup</c>.
    /// </summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (value != _isShown)
            {
                _ = _owner.Run("decals.showGroup", new JsonObject { ["group"] = Id, ["on"] = value });
            }
        }
    }

    internal void Show(string name, int count, bool shown)
    {
        if (_name != name)
        {
            _name = name;
            OnPropertyChanged(nameof(Name));
        }
        Count = count;
        if (_isShown != shown)
        {
            _isShown = shown;
            OnPropertyChanged(nameof(IsShown));
        }
    }
}

/// <summary>
/// The Decals panel's groups (user story D-2): each shown or hidden, renamed, filled from the
/// selection, emptied, selected or removed; a new one from the selection.
/// </summary>
public sealed record DecalGroupsSection() : PanelSection("Decal groups");
