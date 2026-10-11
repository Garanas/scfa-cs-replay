using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Units mode's tree of armies and their groups (user story U-3).
/// </summary>
public sealed record UnitGroupsSection() : PanelSection("Armies and groups");

/// <summary>
/// The Units mode's inspector: the army or group chosen in the tree (user stories U-3, U-4, M-4),
/// <c>Views/MapPanels/UnitGroupPanel</c>.
/// </summary>
public sealed record UnitGroupDetailsSection() : PanelSection("Army or group");

/// <summary>
/// An army or one of its groups in the Units mode's tree, with how many units it holds (with the
/// groups in it).
/// </summary>
/// <param name="Path">Below the army's root group; empty for the army itself (its root group).</param>
/// <param name="Units">The units directly in it, shown in the tree after its groups (user story U-3).</param>
public sealed partial class GroupNode(string army, ImmutableArray<string> path, int count, IReadOnlyList<GroupNode> children, IReadOnlyList<UnitNode>? units = null) : ObservableObject
{
    public string Army => army;

    public ImmutableArray<string> Path => path;

    /// <summary>
    /// <c>INITIAL/Wrecks</c>; empty for the army's root group.
    /// </summary>
    public string PathText => UnitGroupEdits.PathText(path);

    public bool IsArmy => path.IsEmpty;

    public int Count => count;

    public IReadOnlyList<GroupNode> Children => children;

    /// <summary>
    /// The units directly in it.
    /// </summary>
    public IReadOnlyList<UnitNode> Units => units ?? [];

    /// <summary>
    /// What the tree shows under it: its groups, then its units.
    /// </summary>
    public IReadOnlyList<object> Items => Units.Count == 0 ? children : [.. children, .. Units];

    /// <summary>
    /// What the tree shows: <c>INITIAL (12)</c>, or the army's name with its units.
    /// </summary>
    public string Label => $"{(path.IsEmpty ? army : path[^1])} ({count})";

    /// <summary>
    /// What a screen reader says: the army and the group.
    /// </summary>
    public string Description => path.IsEmpty ? $"Army {army}, {MainViewModel.Units(count)}" : $"Group {PathText} of {army}, {MainViewModel.Units(count)}";

    /// <summary>
    /// Whether the tree shows its children; kept when the tree is made again.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// What names it in the tree, whatever version of the document: the army and the path.
    /// </summary>
    public string Key => $"{army}:{PathText}";

    /// <summary>
    /// The node and every node below it.
    /// </summary>
    public IEnumerable<GroupNode> SelfAndBelow() => children.SelectMany(child => child.SelfAndBelow()).Prepend(this);
}

/// <summary>
/// An army in the Map mode's list: its name, whether players take it, its team and its spawn.
/// </summary>
public sealed record ArmyRow(string Name, bool Playable, string? Team, bool HasSpawn, int Units, string Factions)
{
    /// <summary>
    /// What the list shows under the name.
    /// </summary>
    public string Summary => string.Join(", ", new[]
    {
        Playable ? "playable" : "extra",
        Team is null ? null : $"team {Team}",
        Playable && !HasSpawn ? "no spawn" : null,
        Factions.Length == 0 ? null : Factions,
        MainViewModel.Units(Units),
    }.OfType<string>());
}

/// <summary>
/// How the chosen army stands towards another (user story M-4); choosing a stance runs <c>map.setAlliance</c>.
/// </summary>
public sealed partial class AllianceRow(string army, string other, string stance, Func<string, JsonObject, Task> run) : ObservableObject
{
    /// <summary>
    /// The choices: <see cref="None"/>, then the stances of the save file.
    /// </summary>
    public static IReadOnlyList<string> Choices { get; } = [None, .. ArmyEdits.Stances];

    public const string None = "None";

    public string Other => other;

    public string Stance
    {
        get => stance;
        set
        {
            if (value is not null && value != stance)
            {
                stance = value;
                OnPropertyChanged();
                _ = run("map.setAlliance", new JsonObject { ["army"] = army, ["other"] = other, ["stance"] = value == None ? null : value });
            }
        }
    }
}

/// <summary>
/// The armies of the open map and their trees of unit groups (user stories M-4, U-3): the Units
/// mode's tree with its chosen group, the Map mode's list of armies with the chosen army's
/// settings. Every change goes through a command; this part only shows the document and runs them.
/// </summary>
public sealed partial class ArmiesViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);
    private MapDocument? _shown;
    private string _treeKey = "";
    private bool _showing;
    private ImmutableArray<ImmutableSortedDictionary<string, string>> _alliances = [];

    /// <param name="run">Runs a command with arguments, as a button does.</param>
    public ArmiesViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
    }

    /// <summary>
    /// The armies' names, in the map's order.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Names { get; private set; } = [];

    /// <summary>
    /// The Units mode's tree: each army with its groups.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<GroupNode> Tree { get; private set; } = [];

    /// <summary>
    /// The army or group chosen in the tree: where the panel's buttons work, and where the place
    /// tool places.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenGroup), nameof(CanChangeChosenGroup), nameof(ChosenGroupTitle), nameof(ChosenGroupUnits), nameof(IsArmyChosen), nameof(ChosenGroupKind))]
    public partial GroupNode? ChosenGroup { get; set; }

    public bool HasChosenGroup => ChosenGroup is not null;

    /// <summary>
    /// Whether the chosen group can be renamed and removed: it is no army's root group.
    /// </summary>
    public bool CanChangeChosenGroup => ChosenGroup is { IsArmy: false };

    /// <summary>
    /// Whether an army (its root group) is chosen in the tree: the inspector shows its fields.
    /// </summary>
    public bool IsArmyChosen => ChosenGroup is { IsArmy: true };

    /// <summary>
    /// The chosen group's new name (Rename).
    /// </summary>
    [ObservableProperty]
    public partial string GroupName { get; set; } = "";

    /// <summary>
    /// The name the next group added to the chosen army or group gets; left empty, <c>GROUP_1</c>.
    /// </summary>
    [ObservableProperty]
    public partial string NewGroupName { get; set; } = "";

    /// <summary>
    /// The inspector's heading for the chosen army or group: the group's name, or the army's.
    /// </summary>
    public string ChosenGroupTitle => ChosenGroup is not GroupNode node ? "" : node.IsArmy ? node.Army : node.Path[^1];

    /// <summary>
    /// What is chosen in the tree, over the inspector's heading: <c>Army</c> or <c>Group</c>.
    /// </summary>
    public string ChosenGroupKind => ChosenGroup is not GroupNode node ? "" : node.IsArmy ? "Army" : "Group";

    /// <summary>
    /// How many units the chosen army or group holds, with the groups in it.
    /// </summary>
    public string ChosenGroupUnits => ChosenGroup is GroupNode node ? MainViewModel.Units(node.Count) : "";

    /// <summary>
    /// The Map mode's list of armies.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ArmyRow> Rows { get; private set; } = [];

    /// <summary>
    /// The army chosen in the Map mode's list, whose settings show below it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenArmy))]
    public partial ArmyRow? ChosenArmy { get; set; }

    public bool HasChosenArmy => ChosenArmy is not null;

    /// <summary>
    /// The chosen army's new name; Enter renames it.
    /// </summary>
    [ObservableProperty]
    public partial string ArmyName { get; set; } = "";

    /// <summary>
    /// The chosen army's team; Enter puts it in that team, empty in none.
    /// </summary>
    [ObservableProperty]
    public partial string Team { get; set; } = "";

    /// <summary>
    /// How the chosen army stands towards each other army.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<AllianceRow> Alliances { get; private set; } = [];

    private bool _playable;
    private readonly bool[] _factions = new bool[4];

    /// <summary>
    /// Whether players take the chosen army; changing it runs <c>map.setArmy</c>.
    /// </summary>
    public bool Playable
    {
        get => _playable;
        set
        {
            if (SetProperty(ref _playable, value) && !_showing && ChosenArmy is ArmyRow army)
            {
                _ = _run("map.setArmy", new JsonObject { ["army"] = army.Name, ["playable"] = value });
            }
        }
    }

    public bool Uef { get => _factions[0]; set => SetFaction(0, value); }

    public bool Aeon { get => _factions[1]; set => SetFaction(1, value); }

    public bool Cybran { get => _factions[2]; set => SetFaction(2, value); }

    public bool Seraphim { get => _factions[3]; set => SetFaction(3, value); }

    private static readonly string[] FactionProperties = [nameof(Uef), nameof(Aeon), nameof(Cybran), nameof(Seraphim)];

    private void SetFaction(int index, bool value)
    {
        if (_factions[index] == value)
        {
            return;
        }
        _factions[index] = value;
        OnPropertyChanged(FactionProperties[index]);
        if (!_showing && ChosenArmy is ArmyRow army)
        {
            // none chosen is no limit, as all four is
            _ = _run("map.setArmy", new JsonObject
            {
                ["army"] = army.Name,
                ["factions"] = new JsonArray([.. ArmyEdits.Factions.Where((_, i) => _factions[i]).Select(f => (JsonNode)JsonValue.Create(f))]),
            });
        }
    }

    /// <summary>
    /// Shows the armies of a version of the document; the tree is made again only when its armies,
    /// groups or counts changed (not when a unit only moves).
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        if (document is not null && _shown is not null && document.Armies == _shown.Armies && document.Scenario == _shown.Scenario && document.Markers.Spawns == _shown.Markers.Spawns)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("model.armies");
        _shown = document;
        _showing = true;
        try
        {
            ImmutableArray<Army> armies = document?.Armies ?? [];
            if (!Names.SequenceEqual(armies.Select(army => army.Name)))
            {
                Names = [.. armies.Select(army => army.Name)];
            }
            RefreshTree(armies);
            RefreshRows(document);
        }
        finally
        {
            _showing = false;
        }
        if (_chooseGroup is (string army, ImmutableArray<string> path))
        {
            Choose(army, path);
        }
        if (_chooseArmy is string chosen)
        {
            ChosenArmy = Rows.FirstOrDefault(row => row.Name == chosen) ?? ChosenArmy;
        }
        _chooseGroup = null;
        _chooseArmy = null;
    }

    private (string Army, ImmutableArray<string> Path)? _chooseGroup;
    private string? _chooseArmy;

    /// <summary>
    /// Chooses a group in the tree once the document that has it shows: after the command that
    /// made or renamed it.
    /// </summary>
    public void ChooseWhenShown(string army, ImmutableArray<string> path) => _chooseGroup = (army, path);

    /// <summary>
    /// Chooses an army in the Map mode's list once the document that has it shows.
    /// </summary>
    public void ChooseArmyWhenShown(string army) => _chooseArmy = army;

    private void RefreshTree(ImmutableArray<Army> armies)
    {
        foreach (GroupNode node in Tree.SelectMany(node => node.SelfAndBelow()))
        {
            if (node.IsExpanded)
            {
                _expanded.Add(node.Key);
            }
            else
            {
                _expanded.Remove(node.Key);
            }
        }
        GroupNode Node(string army, ImmutableArray<string> path, UnitGroup group) => new GroupNode(army, path, UnitGroupEdits.CountUnits(group),
            [.. group.Groups.Select(child => Node(army, path.Add(child.Name), child))],
            [.. group.Units.Select(unit => new UnitNode(army, path, unit.Name, unit.Blueprint))])
        {
            IsExpanded = _expanded.Contains($"{army}:{UnitGroupEdits.PathText(path)}"),
        };
        GroupNode[] tree = [.. armies.Select(army => Node(army.Name, [], army.Units))];
        string key = string.Join("|", tree.SelectMany(node => node.SelfAndBelow()).Select(node => $"{node.Key}={node.Count}:{string.Join(",", node.Units.Select(unit => unit.Name))}"));
        if (key == _treeKey)
        {
            return;
        }
        string? chosen = ChosenGroup?.Key;
        _treeKey = key;
        Tree = tree;
        ChosenGroup = chosen is null ? null : tree.SelectMany(node => node.SelfAndBelow()).FirstOrDefault(node => node.Key == chosen);
    }

    private void RefreshRows(MapDocument? document)
    {
        if (document is null)
        {
            Rows = [];
            ChosenArmy = null;
            return;
        }
        ScenarioInfo s = document.Scenario;
        ArmyRow[] rows = [.. document.Armies.Select(army => new ArmyRow(
            army.Name,
            s.PlayableArmies.Contains(army.Name),
            document.TeamOf(army.Name),
            document.Markers.Spawns.Any(spawn => spawn.Name == army.Name),
            UnitGroupEdits.CountUnits(army.Units),
            s.ArmyFactions.TryGetValue(army.Name, out ImmutableArray<string> factions) ? string.Join(" ", factions) : ""))];
        if (rows.SequenceEqual(Rows) && document.Armies.Select(army => army.Alliances).SequenceEqual(_alliances))
        {
            return;
        }
        _alliances = [.. document.Armies.Select(army => army.Alliances)];
        string? chosen = ChosenArmy?.Name;
        Rows = rows;
        ChosenArmy = rows.FirstOrDefault(row => row.Name == chosen);
        ShowChosenArmy();
    }

    partial void OnChosenArmyChanged(ArmyRow? value)
    {
        bool showing = _showing;
        _showing = true;
        try
        {
            ShowChosenArmy();
        }
        finally
        {
            _showing = showing;
        }
    }

    partial void OnChosenGroupChanged(GroupNode? value)
    {
        FollowChosenGroup(value);
        if (value is not null && !value.IsArmy)
        {
            GroupName = value.Path[^1];
        }
        // the chosen army or group's army is the army whose fields the inspector shows (one view
        // for the Units mode's tree and the Map mode's list)
        if (value is not null && ChosenArmy?.Name != value.Army && Rows.FirstOrDefault(row => row.Name == value.Army) is ArmyRow row)
        {
            ChosenArmy = row;
        }
    }

    /// <summary>
    /// The chosen army's settings, as the document has them.
    /// </summary>
    private void ShowChosenArmy()
    {
        ArmyRow? row = ChosenArmy;
        Army? army = row is null ? null : _shown?.FindArmy(row.Name);
        ArmyName = row?.Name ?? "";
        Team = row?.Team ?? "";
        Playable = row?.Playable ?? false;
        ImmutableArray<string> limits = row is not null && _shown!.Scenario.ArmyFactions.TryGetValue(row.Name, out ImmutableArray<string> f) ? f : [];
        for (int i = 0; i < 4; i++)
        {
            SetFaction(i, limits.Contains(ArmyEdits.Factions[i]));
        }
        Alliances = army is null
            ? []
            : [.. _shown!.Armies.Where(other => other.Name != army.Name)
                .Select(other => new AllianceRow(army.Name, other.Name, army.Alliances.GetValueOrDefault(other.Name) ?? AllianceRow.None, _run))];
    }

    #region The buttons of the panels

    [RelayCommand]
    private async Task AddGroup()
    {
        await _run("units.addGroup", new JsonObject
        {
            ["army"] = ChosenGroup?.Army,
            ["parent"] = ChosenGroup?.PathText,
            ["name"] = NewGroupName.Trim().Length == 0 ? null : NewGroupName.Trim(),
        });
        NewGroupName = "";
    }

    [RelayCommand]
    private Task SaveGroupAsPreset() => ChosenGroup is GroupNode node
        ? _run("units.saveGroupAsPreset", new JsonObject { ["army"] = node.Army, ["group"] = node.PathText })
        : Task.CompletedTask;

    [RelayCommand]
    private Task RenameGroup() => ChosenGroup is { IsArmy: false } node
        ? _run("units.renameGroup", new JsonObject { ["army"] = node.Army, ["group"] = node.PathText, ["name"] = GroupName.Trim() })
        : Task.CompletedTask;

    [RelayCommand]
    private Task RemoveGroup() => ChosenGroup is { IsArmy: false } node
        ? _run("units.removeGroup", new JsonObject { ["army"] = node.Army, ["group"] = node.PathText, ["keepUnits"] = true })
        : Task.CompletedTask;

    [RelayCommand]
    private Task SelectGroupUnits() => ChosenGroup is GroupNode node
        ? _run("units.selectGroup", new JsonObject { ["army"] = node.Army, ["group"] = node.PathText })
        : Task.CompletedTask;

    [RelayCommand]
    private Task MoveSelectionHere() => ChosenGroup is GroupNode node
        ? _run("units.moveToGroup", new JsonObject { ["army"] = node.Army, ["group"] = node.PathText })
        : Task.CompletedTask;

    [RelayCommand]
    private Task AddArmy() => _run("map.addArmy", new JsonObject());

    [RelayCommand]
    private Task AddExtraArmy() => _run("map.addArmy", new JsonObject { ["extra"] = true });

    [RelayCommand]
    private Task RemoveArmy() => ChosenArmy is ArmyRow army ? _run("map.removeArmy", new JsonObject { ["army"] = army.Name }) : Task.CompletedTask;

    [RelayCommand]
    private Task RenameArmy() => ChosenArmy is ArmyRow army && ArmyName.Trim() != army.Name
        ? _run("map.renameArmy", new JsonObject { ["army"] = army.Name, ["name"] = ArmyName.Trim() })
        : Task.CompletedTask;

    [RelayCommand]
    private Task SetTeam() => ChosenArmy is ArmyRow army && Team.Trim() != (army.Team ?? "")
        ? _run("map.setArmy", new JsonObject { ["army"] = army.Name, ["team"] = Team.Trim() })
        : Task.CompletedTask;

    #endregion

    /// <summary>
    /// The chosen group's army and path, for the commands that work there when not told where.
    /// </summary>
    public (string Army, ImmutableArray<string> Path)? Chosen => ChosenGroup is GroupNode node ? (node.Army, node.Path) : null;

    /// <summary>
    /// Chooses an army or group of the tree, showing it: its army and the groups above it open.
    /// </summary>
    /// <returns>Whether the tree has it.</returns>
    public bool Choose(string army, IReadOnlyList<string> path)
    {
        string key = $"{army}:{UnitGroupEdits.PathText(path)}";
        GroupNode? node = Tree.SelectMany(n => n.SelfAndBelow()).FirstOrDefault(n => n.Key == key);
        if (node is null)
        {
            return false;
        }
        foreach (GroupNode above in Tree.SelectMany(n => n.SelfAndBelow()).Where(n => n.Army == army && n.Path.Length < path.Count && path.Take(n.Path.Length).SequenceEqual(n.Path)))
        {
            above.IsExpanded = true;
        }
        ChosenGroup = node;
        return true;
    }

    /// <summary>
    /// The armies for automation's <c>state</c>: name, playable, team, factions, alliances, spawn, units.
    /// </summary>
    public JsonArray DescribeArmies() => new JsonArray([.. Rows.Select(row => (JsonNode)new JsonObject
    {
        ["name"] = row.Name,
        ["playable"] = row.Playable,
        ["team"] = row.Team,
        ["factions"] = row.Factions,
        ["spawn"] = row.HasSpawn,
        ["units"] = row.Units,
        ["alliances"] = _shown?.FindArmy(row.Name) is Army army
            ? new JsonObject([.. army.Alliances.Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)JsonValue.Create(entry.Value)))])
            : null,
        ["chosen"] = row.Name == ChosenArmy?.Name,
    })]);

    /// <summary>
    /// The tree for automation's <c>state</c>: each army and group with its units, the first 200,
    /// and the chosen one.
    /// </summary>
    public JsonObject DescribeGroups() => new JsonObject
    {
        ["chosen"] = ChosenGroup is GroupNode node ? new JsonObject { ["army"] = node.Army, ["group"] = node.PathText } : null,
        ["groups"] = new JsonArray([.. Tree.SelectMany(n => n.SelfAndBelow()).Take(200).Select(n => (JsonNode)new JsonObject
        {
            ["army"] = n.Army,
            ["group"] = n.PathText,
            ["units"] = n.Count,
        })]),
    };
}
