using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
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

    /// <summary>
    /// What the open map uses that neither the workspace nor the map's own files have (user story
    /// F-8): no tiles, a list of the paths, each leading to its problem.
    /// </summary>
    Missing,

    /// <summary>
    /// The assets the map maker starred (user story F-9), in every map of the workspace.
    /// </summary>
    Starred,

    /// <summary>
    /// The assets chosen last, the newest first (user story F-9).
    /// </summary>
    Recent,
}

/// <summary>
/// A tile of the shelf: an asset, a prop's family of variants (<see cref="AssetEntry.Family"/>), or
/// a decal set (its textures named alike with a suffix each, <see cref="DecalSets"/>).
/// </summary>
/// <param name="set">The decal set the tile stands for; null for any other tile.</param>
public sealed partial class AssetTile(AssetEntry entry, IReadOnlyList<AssetEntry> members, DecalSetAssets? set = null) : ObservableObject
{
    /// <summary>
    /// The decal set it stands for: placed as one decal with all its textures; null for another tile.
    /// </summary>
    public DecalSetAssets? Set => set;

    public bool IsSet => set is not null;
    /// <summary>
    /// The asset it shows: the first of its family.
    /// </summary>
    public AssetEntry Entry => entry;

    /// <summary>
    /// The variants it stands for, itself first; one for an asset without a family.
    /// </summary>
    public IReadOnlyList<AssetEntry> Members => members;

    public bool IsFamily => members.Count > 1;

    public string Title => IsFamily ? Path.GetFileName(entry.Family) : entry.Name;

    /// <summary>
    /// What it is beside its name: the number of variants, a unit's id, a texture's category.
    /// </summary>
    public string Detail => set is not null ? $"set of {set.Count}"
        : IsFamily ? $"{members.Count} variants"
        : entry.Kind == AssetKind.Unit ? (entry.TechLevel is null ? entry.Code : $"{AssetSearch.TechTitle(entry.TechLevel)} · {entry.Code}")
        : entry.Category;

    /// <summary>
    /// The picture to show; with <see cref="Model"/>, null for a glyph of its kind.
    /// </summary>
    public string? Image => entry.Image;

    /// <summary>
    /// The mesh to draw as its picture: a prop's or unit's.
    /// </summary>
    public AssetModel? Model => entry.Model;

    public bool HasImage => entry.Image is not null || entry.Model is not null;

    /// <summary>
    /// A unit's icon as the game's build menu shows it (user story U-1), a PNG; null for none.
    /// </summary>
    public string? Icon => entry.Icon;

    /// <summary>
    /// The picture of the unit's layer the game draws behind its icon.
    /// </summary>
    public string? IconBackground => entry.IconBackground;

    public bool HasIcon => entry.Icon is not null;

    /// <summary>
    /// The icon of its kind (<c>Styles/Icons.axaml</c>), shown when it has no picture.
    /// </summary>
    public string Glyph => AssetsViewModel.IconOf(entry.Kind);

    /// <summary>
    /// Whether its tile has a line of mass and energy: a unit's cost, a prop's reclaim.
    /// </summary>
    public bool ShowsEconomy => entry.Kind is AssetKind.Unit or AssetKind.Prop;

    /// <summary>
    /// Its mass and energy in a short line, mass first: <c>5.2k · 47.4k</c> (in full in
    /// <see cref="EconomyHelp"/>); empty for the other kinds.
    /// </summary>
    public string Economy => !ShowsEconomy ? ""
        : entry.Mass == 0 && entry.Energy == 0 ? (entry.Kind == AssetKind.Unit ? "No cost" : "No reclaim")
        : $"{AssetSearch.Short(entry.Mass)} · {AssetSearch.Short(entry.Energy)}";

    /// <summary>
    /// Its mass and energy in full: what its tile's tool tip and a screen reader say.
    /// </summary>
    public string EconomyHelp => !ShowsEconomy ? ""
        : entry.Kind == AssetKind.Unit
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Costs {entry.Mass:#,0} mass and {entry.Energy:#,0} energy")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Reclaim: {entry.Mass:#,0} mass and {entry.Energy:#,0} energy");

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
    /// Whether it (one of its variants) is starred (user story F-9).
    /// </summary>
    [ObservableProperty]
    public partial bool IsStarred { get; set; }

    /// <summary>
    /// What a screen reader says of it.
    /// </summary>
    public string Label => $"{Title}, {Detail}" + (ShowsEconomy ? $", {EconomyHelp}" : "") + (Uses > 0 ? $", {Uses} on the map" : "");
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
    private MapDocument? _document;
    private string _cubemaps = "";

    public AssetsViewModel(ILogger log)
    {
        _log = log;
        Favourites.Changed += OnFavouritesChanged;
    }

    /// <summary>
    /// The workspace's assets, read when it is ready (<see cref="LoadAsync"/>).
    /// </summary>
    public AssetCatalog Catalog { get; private set; } = AssetCatalog.Empty;

    /// <summary>
    /// Whether the workspace's assets have been read (they may be none); automation waits for it.
    /// </summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProps), nameof(HasFamilies), nameof(FamiliesHelp), nameof(GroupsTitle), nameof(CanSort))]
    public partial AssetKind Kind { get; set; } = AssetKind.Prop;

    /// <summary>
    /// Whether the shelf shows props, which come in families.
    /// </summary>
    public bool IsProps => Kind == AssetKind.Prop;

    /// <summary>
    /// Whether the shown kind groups its assets into one tile: a prop's variants, a decal set's textures.
    /// </summary>
    public bool HasFamilies => Kind is AssetKind.Prop or AssetKind.Decal;

    /// <summary>
    /// What the Variants chip does for the shown kind.
    /// </summary>
    public string FamiliesHelp => Kind == AssetKind.Decal
        ? "A decal set's textures (_albedo, _normals, _spec, _mask) as one tile, placed as one decal"
        : "A prop's sizes and variants as one tile";

    /// <summary>
    /// What the groups are: the biomes, or the units' factions.
    /// </summary>
    public string GroupsTitle => Kind switch { AssetKind.Unit => "Faction", AssetKind.Brush or AssetKind.Cubemap => "Folder", _ => "Biome" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInMap), nameof(IsNarrow), nameof(IsEverything), nameof(IsMissingScope), nameof(IsStarredScope), nameof(IsRecentScope))]
    public partial AssetScope Scope { get; set; } = AssetScope.Narrow;

    public bool IsMissingScope => Scope == AssetScope.Missing;

    /// <summary>
    /// Where each asset of the workspace comes from (user story F-8): the game's or FAF's
    /// archives, or the workspace's own; read after the catalogue.
    /// </summary>
    public AssetOrigins Origins { get; private set; } = AssetOrigins.Empty;

    /// <summary>
    /// The origin the shelf shows only (<see cref="AssetSource.Short"/>: <c>Game</c>, <c>FAF</c>,
    /// <c>Workspace</c>, <c>Unknown</c>), or null for all.
    /// </summary>
    [ObservableProperty]
    public partial string? Source { get; set; }

    /// <summary>
    /// The origins of the kind's assets in the scope and search, with their counts.
    /// </summary>
    public IReadOnlyList<AssetChoice> Sources { get; private set; } = [];

    /// <summary>
    /// What the open map uses that the workspace and the map's own files lack, of every kind, most used first.
    /// </summary>
    public IReadOnlyList<MissingAsset> AllMissing { get; private set; } = [];

    /// <summary>
    /// The missing assets of the kind, which the Missing scope lists.
    /// </summary>
    public IReadOnlyList<MissingAsset> Missing { get; private set; } = [];

    public int MissingCount => AllMissing.Count;

    /// <summary>
    /// Where the chosen asset comes from, in words.
    /// </summary>
    public string? ShownSource => Shown is AssetEntry shown ? Origins.Of(shown).Title : null;

    /// <summary>
    /// Raised when the person asks to see a missing asset's problem (the Problems tab).
    /// </summary>
    public event Action<MissingAsset>? ShowProblemRequested;

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
    /// How the tiles are ordered: as listed, or the most mass or energy first (units and props).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortIndex), nameof(IsSorted), nameof(SortHelp))]
    public partial AssetSort Sort { get; set; }

    /// <summary>
    /// Whether the tiles are in another order than listed (the sort button is on).
    /// </summary>
    public bool IsSorted => Sort != AssetSort.Listed;

    /// <summary>
    /// What the sort button says: the order now, and what a click does.
    /// </summary>
    public string SortHelp => $"Order: {AssetSearch.SortTitles[(int)Sort].ToLowerInvariant()}. Click for the next: as listed, the most mass first, the most energy first (a unit's build cost, a prop's reclaim)";

    /// <summary>
    /// The shelf's sort button: the next order.
    /// </summary>
    [RelayCommand]
    private void NextSort() => Sort = (AssetSort)(((int)Sort + 1) % AssetSearch.SortTitles.Count);

    /// <summary>
    /// <see cref="Sort"/> as the shelf's combo box chooses it (<see cref="AssetSearch.SortTitles"/>).
    /// </summary>
    public int SortIndex
    {
        get => (int)Sort;
        set => Sort = Enum.IsDefined((AssetSort)value) ? (AssetSort)value : AssetSort.Listed;
    }

    public IReadOnlyList<string> SortTitles => AssetSearch.SortTitles;

    /// <summary>
    /// Whether the shown kind has mass and energy to sort by: units and props.
    /// </summary>
    public bool CanSort => Kind is AssetKind.Unit or AssetKind.Prop;

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
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsFamilySelected), nameof(IsSetSelected), nameof(MembersTitle), nameof(Shown), nameof(ShownUses), nameof(ShownGlyph), nameof(ShownStarred), nameof(ShownStarText), nameof(ShownSet), nameof(SetText))]
    public partial AssetTile? Selected { get; private set; }

    /// <summary>
    /// Whether choosing a decal set's tile chooses the whole set (until one of its textures is
    /// chosen in the inspector, which chooses that one alone).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownSet), nameof(SetText))]
    public partial bool WholeSet { get; private set; }

    /// <summary>
    /// The decal set chosen as a whole: what the Decals mode's place tool places; null otherwise.
    /// </summary>
    public DecalSetAssets? ShownSet => WholeSet ? Selected?.Set : null;

    public bool IsSetSelected => Selected?.IsSet == true;

    /// <summary>
    /// The heading over the tile's members in the inspector.
    /// </summary>
    public string MembersTitle => IsSetSelected ? "Textures of the set" : "Variants";

    /// <summary>
    /// What the inspector says of a chosen decal set: its parts, and whether the whole set or one
    /// texture is chosen.
    /// </summary>
    public string SetText => Selected?.Set is not DecalSetAssets set ? ""
        : WholeSet ? $"The whole set is chosen: {set.Summary}. It is placed as one decal and written as one decal per texture; its masks feed decal-masks layers."
        : $"One texture of the set is chosen; the set has {set.Summary}.";

    private bool _choosingTile;

    /// <summary>
    /// Chooses the whole decal set of the chosen tile again, after one of its textures was chosen.
    /// </summary>
    [RelayCommand]
    private void ChooseWholeSet()
    {
        if (Selected?.IsSet == true)
        {
            _choosingTile = true;
            SelectedMember = Selected.Entry;
            _choosingTile = false;
            WholeSet = true;
        }
    }

    /// <summary>
    /// The variant of the selected family the inspector shows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown), nameof(ShownDetail), nameof(ShownUses), nameof(ShownImage), nameof(ShownModel), nameof(ShownNormalImage), nameof(HasShownImage), nameof(HasShownNormalImage), nameof(ShownGlyph), nameof(ShownUse), nameof(ShownKeyLabel), nameof(ShownIcon), nameof(HasShownIcon), nameof(ShownIconBackground), nameof(ShownStarred), nameof(ShownStarText))]
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

    public AssetModel? ShownModel => Shown?.Model;

    public bool HasShownImage => Shown?.Image is not null || Shown?.Model is not null;

    public string? ShownNormalImage => Shown?.NormalImage is string normals && normals != Shown.Image ? normals : null;

    public bool HasShownNormalImage => ShownNormalImage is not null;

    /// <summary>
    /// The shown unit's icon as the game's build menu shows it (user story U-1); null for none.
    /// </summary>
    public string? ShownIcon => Shown?.Icon;

    public string? ShownIconBackground => Shown?.IconBackground;

    public bool HasShownIcon => ShownIcon is not null;

    public string ShownGlyph => IconOf(Shown?.Kind ?? Kind);

    /// <summary>
    /// What the shown asset is for: a brush is a shape of the brush tools, a prop what the Props
    /// mode places or paints, a texture a stratum's.
    /// </summary>
    public string ShownUse => Shown?.Kind switch
    {
        AssetKind.Prop => "Choosing it in the Props mode makes it the prop the Place tool places, or adds it to the palette the Paint tool paints; drag it onto a view to place it there.",
        AssetKind.Texture => "A stratum's texture: choose it for a layer in the Paint mode's layer textures, or add a stratum with it.",
        AssetKind.Brush => "Choosing it makes it the shape of the active brush tool: Sculpt in the Terrain mode, Strata in the Paint mode, whose options turn it.",
        AssetKind.Decal => "Choosing it makes it the decal the Place tool of the Decals mode places (in that mode the tool becomes active); drag it onto a view to place it there.",
        AssetKind.Unit => "Choosing it makes it what the Units mode's Place tool places: click on the map, or drag the tile onto it.",
        AssetKind.Cubemap => "A cube map: choose it in the Lighting mode's Sky as the sky around the map or what units reflect, or in the Water mode as what the water reflects.",
        _ => "",
    };

    /// <summary>
    /// Why the shelf is empty, or empty when it is not.
    /// </summary>
    public string EmptyText => Tiles.Count > 0 || (Scope == AssetScope.Missing && Missing.Count > 0) ? ""
        : Scope == AssetScope.Missing ? $"The map uses no {Noun(Kind, 2).ToLowerInvariant()} that the workspace and the map's own files lack."
        : Kind == AssetKind.Brush && !Catalog.Entries.Any(entry => entry.Kind == AssetKind.Brush)
            ? "No brushes yet: put greyscale PNGs in the workspace's brushes folder (Modes, Assets, Show the brushes folder), in folders as you like."
        : Kind == AssetKind.Brush && Scope == AssetScope.InMap ? "A map holds no brushes: choose The workspace's."
        : Catalog.Entries.Count == 0 ? "The workspace has no converted assets."
        : Scope == AssetScope.InMap ? "The map uses none of these. Choose Map's biomes or Everything."
        : Scope == AssetScope.Starred ? "None starred yet: choose an asset and star it in the inspector, or in the picker."
        : Scope == AssetScope.Recent ? "None chosen lately: the assets chosen on the shelf or in the picker show here, the newest first."
        : "Nothing matches. Search in Everything, or clear the biome and category.";

    public bool IsEmpty => EmptyText.Length > 0;

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

    public string NarrowTitle => Kind switch { AssetKind.Unit => "Civilian", AssetKind.Brush => "The workspace's", AssetKind.Cubemap => "Sky and reflections", _ => "Map's biomes" };

    /// <summary>
    /// Which assets <see cref="AssetScope.Narrow"/> shows, said in full.
    /// </summary>
    public string NarrowHelp => Kind == AssetKind.Unit ? "The civilian units"
        : Kind == AssetKind.Cubemap ? "The cube maps of the sky and of what units reflect"
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
        OnPropertyChanged(nameof(Catalog));
        IsLoaded = true;
        _log.LogInformation("Read {Assets} assets of the workspace in {Milliseconds} ms", catalog.Entries.Count, Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        FindMissing();
        Rebuild();

        // where each comes from: the archives' lists of files, after the tiles show
        long origins = Stopwatch.GetTimestamp();
        AssetOrigins read = await Task.Run(() =>
        {
            using Performance.Span measure = Performance.Measure("assets.origins");
            return AssetOrigins.Read(workspace);
        });
        if (read.Count == 0)
        {
            _log.LogWarning("Cannot tell where the assets come from: the archives of {Folder}'s manifest are missing or unreadable", workspace.AssetsFolder);
        }
        else
        {
            _log.LogInformation("Read where the assets come from: {Files} files of the archives in {Milliseconds} ms", read.Count, Math.Round(Stopwatch.GetElapsedTime(origins).TotalMilliseconds));
        }
        UseOrigins(read);
    }

    /// <summary>
    /// Where the assets come from; for tests and the reading above.
    /// </summary>
    public void UseOrigins(AssetOrigins origins)
    {
        Origins = origins;
        Rebuild();
        OnPropertyChanged(nameof(ShownSource));
    }

    /// <summary>
    /// Shows these assets; for tests and the reading above.
    /// </summary>
    public void Use(AssetCatalog catalog)
    {
        Catalog = catalog;
        OnPropertyChanged(nameof(Catalog));
        IsLoaded = true;
        FindMissing();
        Rebuild();
    }

    /// <summary>
    /// Reads one asset again from its files (user story F-8), the rest of the catalogue kept: its
    /// blueprint's facts, its pictures. The tiles and the inspector show it as it is now.
    /// </summary>
    /// <param name="key">Any name a map uses for it (a game path, a unit's id).</param>
    /// <returns>The asset as it is now; null when its files are gone (it leaves the shelf).</returns>
    /// <exception cref="ArgumentException">The workspace has no such asset.</exception>
    public AssetEntry? ReloadOne(string key, string assetsFolder)
    {
        using Performance.Span measure = Performance.Measure("assets.reload-one");
        AssetEntry old = Catalog.Find(key) ?? throw new ArgumentException($"The workspace has no asset '{key}'");
        (AssetCatalog catalog, AssetEntry? fresh) = Catalog.Reload(key, assetsFolder);
        foreach (string file in new[] { old.File, old.Image, old.Icon, old.NormalImage, old.Model?.Mesh, fresh?.File, fresh?.Image, fresh?.Icon, fresh?.Model?.Mesh }.OfType<string>())
        {
            Controls.AssetThumbnail.Cache.Forget(file);
        }
        bool shown = Shown is AssetEntry entry && entry.Key == old.Key;
        Catalog = catalog;
        OnPropertyChanged(nameof(Catalog));
        FindMissing();
        Rebuild();
        if (shown && fresh is not null)
        {
            Select(fresh.Key);
        }
        _log.LogInformation("Read the asset {Key} again: {Result}", old.Key, fresh is null ? "its files are gone" : "updated");
        return fresh;
    }

    /// <summary>
    /// Finds what the map uses and the workspace lacks again (span <c>assets.missing</c>).
    /// </summary>
    private void FindMissing()
    {
        using Performance.Span measure = Performance.Measure("assets.missing");
        AllMissing = _document is null ? [] : MissingAssets.Find(_document, Catalog);
        OnPropertyChanged(nameof(AllMissing));
        OnPropertyChanged(nameof(MissingCount));
    }

    /// <summary>
    /// Counts what the open map uses; the tiles say it, and the scopes follow it.
    /// </summary>
    public void UseMap(MapDocument? document)
    {
        using Performance.Span measure = Performance.Measure("assets.uses");
        IReadOnlyDictionary<string, int> uses = document is null ? new Dictionary<string, int>() : AssetUse.Count(document);
        bool sameMap = ReferenceEquals(document, _document) || (document is not null && _document is not null && document.Files == _document.Files);
        _document = document;
        // the cube maps the map names (L-1) count as uses; whether they are missing is checked when they change
        string cubemaps = document is null ? "" : string.Join('|', MissingAssets.CubemapsOf(document));
        bool cubemapsChanged = cubemaps != _cubemaps;
        _cubemaps = cubemaps;
        if (sameMap && uses.Count == _uses.Count && uses.All(use => _uses.TryGetValue(use.Key, out int count) && count == use.Value))
        {
            if (cubemapsChanged)
            {
                FindMissing();
                if (Scope == AssetScope.Missing)
                {
                    Rebuild();
                }
            }
            return;
        }
        IReadOnlyList<string> biomes = AssetUse.Biomes(uses);
        bool scopeChanged = Scope is AssetScope.InMap or AssetScope.Missing || !biomes.SequenceEqual(_biomes);
        _uses = uses;
        _biomes = biomes;
        FindMissing();
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
        if (KindOfMode(mode) is AssetKind kind)
        {
            Kind = kind;
        }
    }

    /// <summary>
    /// The kind of asset a mode works with: brushes in Terrain, textures in Paint, props, decals,
    /// units; null for the other modes.
    /// </summary>
    public static AssetKind? KindOfMode(string? mode) => mode switch
    {
        "terrain" => AssetKind.Brush,
        "paint" => AssetKind.Texture,
        "props" => AssetKind.Prop,
        "decals" => AssetKind.Decal,
        "units" => AssetKind.Unit,
        "lighting" => AssetKind.Cubemap,
        _ => null,
    };

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
        _choosingTile = true;
        Selected = tile;
        SelectedMember = tile?.Entry;
        _choosingTile = false;
        WholeSet = tile?.IsSet == true;
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
    private void ChooseSource(AssetChoice choice) => Source = choice.IsOn ? null : choice.Value;

    [RelayCommand]
    private void ShowMissingProblem(MissingAsset missing) => ShowProblemRequested?.Invoke(missing);

    [RelayCommand]
    private void ChooseTile(AssetTile tile)
    {
        Select(tile);
        NoteUsed(tile.Entry.Key);
    }

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

    partial void OnSourceChanged(string? value) => Rebuild();

    partial void OnFamiliesChanged(bool value) => Rebuild();

    partial void OnSortChanged(AssetSort value) => Rebuild();

    partial void OnColumnsChanged(int value) => MakeRows();

    partial void OnSelectedChanged(AssetTile? value)
    {
        OnPropertyChanged(nameof(ShownUse));
        OnPropertyChanged(nameof(ShownSource));
    }

    partial void OnSelectedMemberChanged(AssetEntry? value)
    {
        // a texture of a set chosen in the inspector is placed alone
        if (!_choosingTile)
        {
            WholeSet = false;
        }
        OnPropertyChanged(nameof(ShownUse));
        OnPropertyChanged(nameof(ShownSource));
    }

    /// <summary>
    /// Filters the catalogue again: the kinds and their counts in the scope and search, the groups
    /// and categories of the kind, and the tiles; the selection stays if it is still shown.
    /// </summary>
    private void Rebuild()
    {
        if (_rebuilding)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("assets.filter");
        string[] words = AssetSearch.Words(Query);
        IEnumerable<AssetEntry> InScope(AssetKind kind) =>
            Catalog.Entries.Where(entry => entry.Kind == kind && IsInScope(entry) && AssetSearch.Matches(entry, words));

        Kinds = Scope == AssetScope.Missing
            ? [.. Enum.GetValues<AssetKind>().Select(kind => new AssetChoice(kind.ToString(), KindTitle(kind), AllMissing.Count(missing => missing.Kind == kind), kind == Kind))]
            : [.. Enum.GetValues<AssetKind>().Select(kind => new AssetChoice(kind.ToString(), KindTitle(kind), InScope(kind).Count(), kind == Kind))];
        Missing = [.. AllMissing.Where(missing => missing.Kind == Kind && AssetSearch.Words(Query).All(word => missing.Path.Contains(word, StringComparison.OrdinalIgnoreCase)))];

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

        Sources = Origins.Count == 0 && Kind != AssetKind.Brush ? [] : [.. ofKind
            .Where(entry => (Group is null || entry.Group == Group) && (Category is null || entry.Category == Category))
            .GroupBy(entry => Origins.Of(entry).Short)
            .OrderByDescending(source => source.Count())
            .Select(source => new AssetChoice(source.Key, source.Key, source.Count(), source.Key == Source))];
        IEnumerable<AssetEntry> shown = AssetSearch.Sorted(InFavouritesOrder(ofKind).Where(entry => (Group is null || entry.Group == Group) && (Category is null || entry.Category == Category)
            && (Source is null || Origins.Of(entry).Short == Source)), CanSort ? Sort : AssetSort.Listed);
        AssetEntry? selected = Shown;
        List<AssetTile> tiles;
        if (Kind == AssetKind.Prop && Families)
        {
            tiles = [.. shown.GroupBy(entry => (entry.Group, entry.Category, entry.Family)).Select(family => new AssetTile(family.First(), [.. family]))];
        }
        else if (Kind == AssetKind.Decal && Families)
        {
            // a decal set's textures share a tile, its albedo first (D-1)
            Dictionary<string, DecalSetAssets> sets = DecalSets.Of(Catalog.Entries).ToDictionary(set => set.Name, StringComparer.Ordinal);
            tiles = [.. shown.GroupBy(entry => DecalSets.PartOf(entry.Key).Name, StringComparer.Ordinal).Select(family =>
            {
                AssetEntry[] members = [.. family.OrderBy(entry => PartOrder(DecalSets.PartOf(entry.Key).Part))];
                return new AssetTile(members[0], members, sets.GetValueOrDefault(family.Key));
            })];
        }
        else
        {
            tiles = [.. shown.Select(entry => new AssetTile(entry, [entry]))];
        }
        foreach (AssetTile tile in tiles)
        {
            tile.Uses = UsesOf(tile.Members);
            tile.IsStarred = IsStarred(tile);
        }
        Tiles = tiles;
        MakeRows();

        OnPropertyChanged(nameof(Kinds));
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Sources));
        OnPropertyChanged(nameof(Missing));
        OnPropertyChanged(nameof(Tiles));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(InMapCount));
        OnPropertyChanged(nameof(NarrowTitle));
        OnPropertyChanged(nameof(NarrowHelp));
        OnPropertyChanged(nameof(StarredCount));
        OnPropertyChanged(nameof(RecentCount));

        // by its key: read again, the catalogue holds other objects for the same assets
        AssetEntry? current = selected is null ? null : Catalog.Find(selected.Key);
        AssetTile? again = current is null ? null : TileOf(current);
        bool whole = WholeSet;
        Select(again);
        if (again is not null)
        {
            _choosingTile = true;
            SelectedMember = current;
            _choosingTile = false;
            WholeSet = whole && again.IsSet;
        }
    }

    /// <summary>
    /// The order of a decal set's textures on its tile: the albedo first, which its picture shows.
    /// </summary>
    private static int PartOrder(DecalSetPart part) => part switch
    {
        DecalSetPart.Albedo => 0,
        DecalSetPart.Normals => 1,
        DecalSetPart.Specular => 2,
        DecalSetPart.Mask => 3,
        _ => 4,
    };

    private void MakeRows()
    {
        int columns = Math.Max(1, Columns);
        Rows = [.. Tiles.Chunk(columns).Select(row => new AssetRow(row))];
        OnPropertyChanged(nameof(Rows));
    }

    private bool IsInScope(AssetEntry entry) => Scope switch
    {
        AssetScope.InMap => entry.Keys.Any(_uses.ContainsKey),
        AssetScope.Missing => false,
        AssetScope.Starred or AssetScope.Recent => IsInFavourites(entry),
        AssetScope.Narrow => entry.Kind switch
        {
            AssetKind.Unit => entry.IsCivilian,
            AssetKind.Brush => true,
            AssetKind.Cubemap => entry.Category is "sky" or "reflection",
            _ => _biomes.Count == 0 || _biomes.Contains(entry.Group),
        },
        _ => true,
    };

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
        (AssetKind.Cubemap, true) => "Cube map",
        (AssetKind.Cubemap, false) => "Cube maps",
        (_, true) => "Texture",
        _ => "Textures",
    };

    public static string IconOf(AssetKind kind) => kind switch
    {
        AssetKind.Prop => "Icon.Props",
        AssetKind.Unit => "Icon.Units",
        AssetKind.Decal => "Icon.Decals",
        AssetKind.Brush => "Icon.Terrain",
        AssetKind.Cubemap => "Icon.Lighting",
        _ => "Icon.Paint",
    };
}
