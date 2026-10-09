using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Which assets the shelf shows of a kind.
/// </summary>
public enum AssetScope
{
    /// <summary>
    /// What the open map uses.
    /// </summary>
    InMap,

    /// <summary>
    /// The biomes the map uses (props, decals, textures), or the civilian units: what a map maker
    /// chooses from most.
    /// </summary>
    Narrow,

    /// <summary>
    /// Every asset of the kind.
    /// </summary>
    Everything,
}

/// <summary>
/// A tile of the shelf: an asset, or a prop's family of variants (<see cref="AssetEntry.Family"/>).
/// </summary>
public sealed partial class AssetTile(AssetEntry entry, IReadOnlyList<AssetEntry> members) : ObservableObject
{
    /// <summary>
    /// The asset it shows: the first of its family.
    /// </summary>
    public AssetEntry Entry => entry;

    /// <summary>
    /// The variants it stands for, itself first; one for an asset without a family.
    /// </summary>
    public IReadOnlyList<AssetEntry> Members => members;

    public bool IsFamily => members.Count > 1;

    public string Title => IsFamily ? entry.Family : entry.Name;

    /// <summary>
    /// What it is beside its name: the number of variants, a unit's id, a texture's category.
    /// </summary>
    public string Detail => IsFamily ? $"{members.Count} variants"
        : entry.Kind == AssetKind.Unit ? entry.Code
        : entry.Category;

    /// <summary>
    /// The picture to show, or null for a glyph of its kind.
    /// </summary>
    public string? Image => entry.Image;

    public bool HasImage => entry.Image is not null;

    /// <summary>
    /// The icon of its kind (<c>Styles/Icons.axaml</c>), shown when it has no picture.
    /// </summary>
    public string Glyph => AssetsViewModel.IconOf(entry.Kind);

    /// <summary>
    /// How often the map uses it (its family together).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsed), nameof(Label))]
    public partial int Uses { get; set; }

    public bool IsUsed => Uses > 0;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// What a screen reader says of it.
    /// </summary>
    public string Label => $"{Title}, {Detail}" + (Uses > 0 ? $", {Uses} on the map" : "");
}

/// <summary>
/// A row of tiles: the shelf lists rows, so that only those in sight are made.
/// </summary>
public sealed record AssetRow(IReadOnlyList<AssetTile> Tiles);

/// <summary>
/// A choice of a filter with how many it shows: a kind, a biome or faction, a category.
/// </summary>
/// <param name="Value">What it filters on; null for all.</param>
public sealed record AssetChoice(string? Value, string Title, int Count, bool IsOn)
{
    public string Text => $"{Title} {Count}";
}

/// <summary>
/// The asset shelf (concept C1, user stories F-8 and F-9): the workspace's props, units, decals,
/// stratum textures and brushes to look through, by kind (which follows the mode), scope (what the map
/// uses, its biomes, everything), search, biome or faction, and category; a prop's variants as one
/// tile. What is selected shows in the inspector.
/// </summary>
public sealed partial class AssetsViewModel : ObservableObject
{
    private readonly ILogger _log;
    private IReadOnlyDictionary<string, int> _uses = new Dictionary<string, int>();
    private IReadOnlyList<string> _biomes = [];
    private bool _rebuilding;

    public AssetsViewModel(ILogger log)
    {
        _log = log;
    }

    /// <summary>
    /// The workspace's assets, read when it is ready (<see cref="LoadAsync"/>).
    /// </summary>
    public AssetCatalog Catalog { get; private set; } = AssetCatalog.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProps), nameof(GroupsTitle))]
    public partial AssetKind Kind { get; set; } = AssetKind.Prop;

    /// <summary>
    /// Whether the shelf shows props, which come in families.
    /// </summary>
    public bool IsProps => Kind == AssetKind.Prop;

    /// <summary>
    /// What the groups are: the biomes, or the units' factions.
    /// </summary>
    public string GroupsTitle => Kind switch { AssetKind.Unit => "Faction", AssetKind.Brush => "Folder", _ => "Biome" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInMap), nameof(IsNarrow), nameof(IsEverything))]
    public partial AssetScope Scope { get; set; } = AssetScope.Narrow;

    public bool IsInMap => Scope == AssetScope.InMap;

    public bool IsNarrow => Scope == AssetScope.Narrow;

    public bool IsEverything => Scope == AssetScope.Everything;

    /// <summary>
    /// Words that must each be in the name, code, group or category.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = "";

    /// <summary>
    /// The biome (or a unit's faction) shown; null for all of the scope.
    /// </summary>
    [ObservableProperty]
    public partial string? Group { get; set; }

    /// <summary>
    /// The category shown (trees, structure, splats, ...); null for all.
    /// </summary>
    [ObservableProperty]
    public partial string? Category { get; set; }

    /// <summary>
    /// Whether a prop's variants share one tile.
    /// </summary>
    [ObservableProperty]
    public partial bool Families { get; set; } = true;

    /// <summary>
    /// How many tiles fit in a row, as the view measures it.
    /// </summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 6;

    /// <summary>
    /// The size of the tiles: 0 small, 1 medium, 2 large.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileWidth), nameof(ThumbnailSize), nameof(TileSizeHelp))]
    public partial int TileSize { get; set; } = 1;

    public string TileSizeHelp => $"Tiles: {(TileSize switch { 0 => "small", 2 => "large", _ => "medium" })}. Click for another size, or control and the wheel over the tiles";

    public double TileWidth => ThumbnailSize + 16;

    public double ThumbnailSize => TileSize switch { 0 => 56, 2 => 112, _ => 80 };

    /// <summary>
    /// The tile chosen, shown in the inspector; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsFamilySelected), nameof(Shown), nameof(ShownUses), nameof(ShownGlyph))]
    public partial AssetTile? Selected { get; private set; }

    /// <summary>
    /// The variant of the selected family the inspector shows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown), nameof(ShownDetail), nameof(ShownUses), nameof(ShownImage), nameof(ShownNormalImage), nameof(HasShownImage), nameof(HasShownNormalImage), nameof(ShownGlyph), nameof(ShownPlanned), nameof(ShownKeyLabel))]
    public partial AssetEntry? SelectedMember { get; set; }

    public bool HasSelection => Selected is not null;

    public bool IsFamilySelected => Selected?.IsFamily == true;

    /// <summary>
    /// The asset the inspector shows: the chosen variant, else the tile's.
    /// </summary>
    public AssetEntry? Shown => SelectedMember ?? Selected?.Entry;

    /// <summary>
    /// What the shown asset is, in a line: <c>Prop · Evergreen · trees</c>.
    /// </summary>
    public string ShownDetail => Shown is AssetEntry shown
        ? $"{Noun(shown.Kind, 1)} · {Title(shown.Group)} · {shown.Category}"
        : "";

    public string ShownUses => Shown is AssetEntry shown && shown.Kind != AssetKind.Brush
        ? (shown.Keys.Sum(key => _uses.GetValueOrDefault(key)) switch { 0 => "Not used by this map", 1 => "Used once by this map", int n => $"Used {n} times by this map" })
            + (IsFamilySelected && Selected!.Uses > 0 ? $"; its family {Selected.Uses} times" : "")
        : "";

    /// <summary>
    /// Where the shown asset's name points: the game's files, or the workspace's for a brush.
    /// </summary>
    public string ShownKeyLabel => Shown?.Kind == AssetKind.Brush ? "In the workspace" : "In the game";

    public string? ShownImage => Shown?.Image;

    public bool HasShownImage => Shown?.Image is not null;

    public string? ShownNormalImage => Shown?.NormalImage is string normals && normals != Shown.Image ? normals : null;

    public bool HasShownNormalImage => ShownNormalImage is not null;

    public string ShownGlyph => IconOf(Shown?.Kind ?? Kind);

    /// <summary>
    /// What the shown asset is for, not built yet: its user story.
    /// </summary>
    public string ShownPlanned => Shown?.Kind switch
    {
        AssetKind.Prop => "Placing it on the map, and adding it to a palette: user stories P-1 and P-2",
        AssetKind.Unit => "Placing it for an army: user story U-2",
        AssetKind.Decal => "Placing it on the terrain: user story D-1",
        AssetKind.Texture => "Using it for a stratum: user story S-2",
        _ => "",
    };

    /// <summary>
    /// Why the shelf is empty, or empty when it is not.
    /// </summary>
    public string EmptyText => Tiles.Count > 0 ? ""
        : Kind == AssetKind.Brush && !Catalog.Entries.Any(entry => entry.Kind == AssetKind.Brush)
            ? "No brushes yet: put greyscale PNGs in the workspace's brushes folder (Modes, Assets, Show the brushes folder), in folders as you like."
        : Kind == AssetKind.Brush && Scope == AssetScope.InMap ? "A map holds no brushes: choose The workspace's."
    /// <summary>
    /// What the shown asset is for today, beside what is planned: a brush is a shape of the brush tools.
    /// </summary>
    public string ShownUse => Shown?.Kind == AssetKind.Brush
        ? "Choosing it makes it the shape of the active brush tool: Sculpt in the Terrain mode, Strata in the Paint mode, whose options turn it."
        : "";

        : Catalog.Entries.Count == 0 ? "The workspace has no converted assets."
        : Scope == AssetScope.InMap ? "The map uses none of these. Choose Map's biomes or Everything."
        : "Nothing matches. Search in Everything, or clear the biome and category.";

    public bool IsEmpty => Tiles.Count == 0;

    public IReadOnlyList<AssetChoice> Kinds { get; private set; } = [];

    public IReadOnlyList<AssetChoice> Groups { get; private set; } = [];

    public IReadOnlyList<AssetChoice> Categories { get; private set; } = [];

    /// <summary>
    /// The tiles the filters leave, in order.
    /// </summary>
    public IReadOnlyList<AssetTile> Tiles { get; private set; } = [];

    /// <summary>
    /// <see cref="Tiles"/> in rows of <see cref="Columns"/>.
    /// </summary>
    public IReadOnlyList<AssetRow> Rows { get; private set; } = [];

    /// <summary>
    /// How many assets of the kind the open map uses.
    /// </summary>
    public int InMapCount => _uses.Keys.Select(Catalog.Find).Where(entry => entry?.Kind == Kind).Distinct().Count();

    public string NarrowTitle => Kind switch { AssetKind.Unit => "Civilian", AssetKind.Brush => "The workspace's", _ => "Map's biomes" };

    /// <summary>
    /// Which assets <see cref="AssetScope.Narrow"/> shows, said in full.
    /// </summary>
    public string NarrowHelp => Kind == AssetKind.Unit ? "The civilian units"
        : Kind == AssetKind.Brush ? "Every brush of the workspace's brushes folder"
        : _biomes.Count == 0 ? "Every biome: the map uses none yet"
        : $"The biomes the map uses: {string.Join(", ", _biomes.Select(Title))}";

    /// <summary>
    /// What the shelf shows, in a line: <c>112 props in 38 families</c>.
    /// </summary>
    public string Summary
    {
        get
        {
            int assets = Tiles.Sum(tile => tile.Members.Count);
            string noun = Noun(Kind, assets).ToLowerInvariant();
            if (Catalog.Entries.Count == 0)
            {
                return "No assets: the workspace has none converted";
            }
            return Tiles.Count == assets ? $"{assets} {noun}" : $"{assets} {noun} in {Tiles.Count} families";
        }
    }

    /// <summary>
    /// Raised when the shelf is to take the keyboard: its search field.
    /// </summary>
    public event EventHandler? FocusRequested;

    /// <summary>
    /// Reads the workspace's assets on a pool thread, then shows them.
    /// </summary>
    public async Task LoadAsync(Workspace workspace)
    {
        long start = Stopwatch.GetTimestamp();
        AssetCatalog catalog = await Task.Run(() =>
        {
            using Performance.Span measure = Performance.Measure("assets.load");
            return AssetCatalog.Load(workspace);
        });
        Catalog = catalog;
        _log.LogInformation("Read {Assets} assets of the workspace in {Milliseconds} ms", catalog.Entries.Count, Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        Rebuild();
    }

    /// <summary>
    /// Shows these assets; for tests and the reading above.
    /// </summary>
    public void Use(AssetCatalog catalog)
    {
        Catalog = catalog;
        Rebuild();
    }

    /// <summary>
    /// Counts what the open map uses; the tiles say it, and the scopes follow it.
    /// </summary>
    public void UseMap(MapDocument? document)
    {
        using Performance.Span measure = Performance.Measure("assets.uses");
        IReadOnlyDictionary<string, int> uses = document is null ? new Dictionary<string, int>() : AssetUse.Count(document);
        if (uses.Count == _uses.Count && uses.All(use => _uses.TryGetValue(use.Key, out int count) && count == use.Value))
        {
            return;
        }
        IReadOnlyList<string> biomes = AssetUse.Biomes(uses);
        bool scopeChanged = Scope == AssetScope.InMap || !biomes.SequenceEqual(_biomes);
        _uses = uses;
        _biomes = biomes;
        if (scopeChanged)
        {
            Rebuild();
        }
        else
        {
            foreach (AssetTile tile in Tiles)
            {
                tile.Uses = UsesOf(tile.Members);
            }
            OnPropertyChanged(nameof(InMapCount));
            OnPropertyChanged(nameof(ShownUses));
        }
    }

    /// <summary>
    /// The kind of the mode the map maker chose: the shelf follows Terrain (brushes), Paint, Props,
    /// Decals and Units.
    /// </summary>
    public void FollowMode(string mode)
    {
        AssetKind? kind = mode switch
        {
            "terrain" => AssetKind.Brush,
            "paint" => AssetKind.Texture,
            "props" => AssetKind.Prop,
            "decals" => AssetKind.Decal,
            "units" => AssetKind.Unit,
            _ => null,
        };
        if (kind is AssetKind k)
        {
            Kind = k;
        }
    }

    /// <summary>
    /// Selects the asset a map names by <paramref name="key"/> (a game path, a unit's id): its kind
    /// shows, and filters that would hide it are cleared.
    /// </summary>
    /// <exception cref="ArgumentException">The workspace has no such asset.</exception>
    public void Select(string key)
    {
        AssetEntry entry = Catalog.Find(key) ?? throw new ArgumentException($"The workspace has no asset '{key}'");
        _rebuilding = true;
        Kind = entry.Kind;
        _rebuilding = false;
        Rebuild();
        if (TileOf(entry) is null)
        {
            _rebuilding = true;
            Scope = AssetScope.Everything;
            Query = "";
            Group = null;
            Category = null;
            _rebuilding = false;
            Rebuild();
        }
        AssetTile tile = TileOf(entry)!;
        Select(tile);
        SelectedMember = entry;
    }

    /// <summary>
    /// Selects a tile, or nothing.
    /// </summary>
    public void Select(AssetTile? tile)
    {
        if (Selected is AssetTile old)
        {
            old.IsSelected = false;
        }
        Selected = tile;
        SelectedMember = tile?.Entry;
        if (tile is not null)
        {
            tile.IsSelected = true;
        }
    }

    /// <summary>
    /// Moves the selection through the tiles as the arrow keys do: across, or a row up or down.
    /// </summary>
    public void Move(int across, int down)
    {
        if (Tiles.Count == 0)
        {
            return;
        }
        int at = Selected is null ? -1 : IndexOf(Selected);
        int next = at < 0 ? 0 : Math.Clamp(at + across + down * Math.Max(1, Columns), 0, Tiles.Count - 1);
        Select(Tiles[next]);
    }

    /// <summary>
    /// Shows the shelf's search field to type in.
    /// </summary>
    public void Focus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ChooseKind(AssetChoice choice)
    {
        if (Enum.TryParse(choice.Value, out AssetKind kind))
        {
            Kind = kind;
        }
    }

    [RelayCommand]
    private void ChooseScope(AssetScope scope) => Scope = scope;

    [RelayCommand]
    private void ChooseGroup(AssetChoice choice) => Group = choice.IsOn ? null : choice.Value;

    [RelayCommand]
    private void ChooseCategory(AssetChoice choice) => Category = choice.IsOn ? null : choice.Value;

    [RelayCommand]
    private void ChooseTile(AssetTile tile) => Select(tile);

    [RelayCommand]
    private void ClearSelection() => Select((AssetTile?)null);

    [RelayCommand]
    private void ToggleFamilies() => Families = !Families;

    /// <summary>
    /// The next size of the tiles: small, medium, large, then small again.
    /// </summary>
    [RelayCommand]
    private void NextTileSize() => TileSize = (TileSize + 1) % 3;

    partial void OnKindChanged(AssetKind value)
    {
        // a biome or category of one kind means nothing to another
        bool rebuilding = _rebuilding;
        _rebuilding = true;
        Group = null;
        Category = null;
        _rebuilding = rebuilding;
        Rebuild();
    }

    partial void OnScopeChanged(AssetScope value) => Rebuild();

    partial void OnQueryChanged(string value) => Rebuild();

    partial void OnGroupChanged(string? value) => Rebuild();

    partial void OnCategoryChanged(string? value) => Rebuild();

    partial void OnFamiliesChanged(bool value) => Rebuild();

    partial void OnColumnsChanged(int value) => MakeRows();

    /// <summary>
    /// Filters the catalogue again: the kinds and their counts in the scope and search, the groups
    /// and categories of the kind, and the tiles; the selection stays if it is still shown.
    /// </summary>
    private void Rebuild()
    {
        if (_rebuilding)
    partial void OnSelectedChanged(AssetTile? value) => OnPropertyChanged(nameof(ShownUse));

    partial void OnSelectedMemberChanged(AssetEntry? value) => OnPropertyChanged(nameof(ShownUse));

        {
            return;
        }
        using Performance.Span measure = Performance.Measure("assets.filter");
        string[] words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<AssetEntry> InScope(AssetKind kind) =>
            Catalog.Entries.Where(entry => entry.Kind == kind && IsInScope(entry) && Matches(entry, words));

        Kinds = [.. Enum.GetValues<AssetKind>().Select(kind => new AssetChoice(kind.ToString(), KindTitle(kind), InScope(kind).Count(), kind == Kind))];

        List<AssetEntry> ofKind = [.. InScope(Kind)];
        Groups = [.. ofKind
            .Where(entry => Category is null || entry.Category == Category)
            .GroupBy(entry => entry.Group)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AssetChoice(group.Key, Title(group.Key), group.Count(), group.Key == Group))];
        Categories = [.. ofKind
            .Where(entry => Group is null || entry.Group == Group)
            .GroupBy(entry => entry.Category)
            .OrderByDescending(category => category.Count())
            .Select(category => new AssetChoice(category.Key, category.Key, category.Count(), category.Key == Category))];

        IEnumerable<AssetEntry> shown = ofKind.Where(entry => (Group is null || entry.Group == Group) && (Category is null || entry.Category == Category));
        AssetEntry? selected = Shown;
        List<AssetTile> tiles = Kind == AssetKind.Prop && Families
            ? [.. shown.GroupBy(entry => (entry.Group, entry.Category, entry.Family)).Select(family => new AssetTile(family.First(), [.. family]))]
            : [.. shown.Select(entry => new AssetTile(entry, [entry]))];
        foreach (AssetTile tile in tiles)
        {
            tile.Uses = UsesOf(tile.Members);
        }
        Tiles = tiles;
        MakeRows();

        OnPropertyChanged(nameof(Kinds));
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Tiles));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(InMapCount));
        OnPropertyChanged(nameof(NarrowTitle));
        OnPropertyChanged(nameof(NarrowHelp));

        // by its key: read again, the catalogue holds other objects for the same assets
        AssetEntry? current = selected is null ? null : Catalog.Find(selected.Key);
        AssetTile? again = current is null ? null : TileOf(current);
        Select(again);
        if (again is not null)
        {
            SelectedMember = current;
        }
    }

    private void MakeRows()
    {
        int columns = Math.Max(1, Columns);
        Rows = [.. Tiles.Chunk(columns).Select(row => new AssetRow(row))];
        OnPropertyChanged(nameof(Rows));
    }

    private bool IsInScope(AssetEntry entry) => Scope switch
    {
        AssetScope.InMap => entry.Keys.Any(_uses.ContainsKey),
        AssetScope.Narrow => entry.Kind switch
        {
            AssetKind.Unit => entry.IsCivilian,
            AssetKind.Brush => true,
            _ => _biomes.Count == 0 || _biomes.Contains(entry.Group),
        },
        _ => true,
    };

    private static bool Matches(AssetEntry entry, string[] words) => words.All(word =>
        entry.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
        || entry.Code.Contains(word, StringComparison.OrdinalIgnoreCase)
        || entry.Group.Contains(word, StringComparison.OrdinalIgnoreCase)
        || entry.Category.Contains(word, StringComparison.OrdinalIgnoreCase));

    private int UsesOf(IReadOnlyList<AssetEntry> members) =>
        members.Sum(member => member.Keys.Sum(key => _uses.GetValueOrDefault(key)));

    private AssetTile? TileOf(AssetEntry entry) => Tiles.FirstOrDefault(tile => tile.Members.Contains(entry));

    private int IndexOf(AssetTile tile)
    {
        for (int i = 0; i < Tiles.Count; i++)
        {
            if (Tiles[i] == tile)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// A biome's folder as a title: <c>red barrens</c> is <c>Red barrens</c>.
    /// </summary>
    public static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    public static string KindTitle(AssetKind kind) => Noun(kind, 2);

    /// <summary>
    /// The kind's name for <paramref name="count"/> of it: <c>Prop</c>, <c>Brushes</c>.
    /// </summary>
    public static string Noun(AssetKind kind, int count) => (kind, count == 1) switch
    {
        (AssetKind.Prop, true) => "Prop",
        (AssetKind.Prop, false) => "Props",
        (AssetKind.Unit, true) => "Unit",
        (AssetKind.Unit, false) => "Units",
        (AssetKind.Decal, true) => "Decal",
        (AssetKind.Decal, false) => "Decals",
        (AssetKind.Brush, true) => "Brush",
        (AssetKind.Brush, false) => "Brushes",
        (_, true) => "Texture",
        _ => "Textures",
    };

    public static string IconOf(AssetKind kind) => kind switch
    {
        AssetKind.Prop => "Icon.Props",
        AssetKind.Unit => "Icon.Units",
        AssetKind.Decal => "Icon.Decals",
        AssetKind.Brush => "Icon.Terrain",
        _ => "Icon.Paint",
    };
}
