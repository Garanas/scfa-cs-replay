using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The picker (concept C2, user stories F-9 and U-1): a slot, tool or command asks the map maker
/// for one asset of a kind (<see cref="PickAsync"/>), who searches and filters the workspace's
/// assets of that kind and chooses one, or cancels. It has filters and a selection of its own:
/// the shelf's stay as they are.
/// </summary>
/// <remarks>
/// The groups are the biomes, a unit's factions or the brushes' folders; the categories a prop's
/// folder, a decal's or texture's kind, or for units what they are (land, air, naval, structure,
/// and civilian, which is any of those); units have a tech level besides. Each choice counts what
/// it would show with the other filters as they are.
/// </remarks>
/// <param name="catalog">The workspace's assets as they are now: the shelf's catalogue.</param>
public sealed partial class AssetPickerViewModel(Func<AssetCatalog> catalog) : ObservableObject
{
    /// <summary>
    /// The value of the category that shows the civilian units, whatever they are.
    /// </summary>
    public const string Civilian = "civilian";

    private TaskCompletionSource<string?>? _answer;
    private bool _rebuilding;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>
    /// The kind of asset asked for.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnits), nameof(GroupsTitle), nameof(CategoriesTitle), nameof(SearchHint))]
    public partial AssetKind Kind { get; private set; } = AssetKind.Prop;

    /// <summary>
    /// What the picker asks, as its heading: <c>Choose a unit</c>, or the slot's own question.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    /// <summary>
    /// Words that must each be in the name, code, group or category.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = "";

    /// <summary>
    /// The biome, faction or folder shown; null for all.
    /// </summary>
    [ObservableProperty]
    public partial string? Group { get; set; }

    /// <summary>
    /// The category shown (<c>trees</c>, <c>air</c>, <see cref="Civilian"/>); null for all.
    /// </summary>
    [ObservableProperty]
    public partial string? Category { get; set; }

    /// <summary>
    /// The units' tech level shown (<c>1</c> to <c>4</c>, <c>none</c>); null for all.
    /// </summary>
    [ObservableProperty]
    public partial string? Tech { get; set; }

    /// <summary>
    /// The shelf, whose stars and recents the picker offers and notes (user story F-9); null in
    /// tests of the picker alone.
    /// </summary>
    public AssetsViewModel? Shelf { get; set; }

    /// <summary>
    /// Whether only the starred (<c>starred</c>) or the recent (<c>recent</c>) assets show; null for all.
    /// </summary>
    [ObservableProperty]
    public partial string? Show { get; set; }

    /// <summary>
    /// The choices of <see cref="Show"/>, with how many each shows.
    /// </summary>
    public IReadOnlyList<AssetChoice> Shows { get; private set; } = [];

    /// <summary>
    /// Whether the selected asset is starred.
    /// </summary>
    public bool SelectedStarred => Selected is AssetTile tile && Shelf?.Favourites.IsStarred(tile.Entry.Key) == true;

    /// <summary>
    /// What the star button says.
    /// </summary>
    public string StarText => SelectedStarred ? "Starred" : "Star";

    /// <summary>
    /// How many tiles fit in a row, as the view measures it.
    /// </summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 6;

    /// <summary>
    /// The tile the keyboard is on, which Enter chooses; null when nothing matches.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectionText), nameof(SelectedStarred), nameof(StarText))]
    public partial AssetTile? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

    /// <summary>
    /// The selected asset in a line, beside the buttons: its name and what it is.
    /// </summary>
    public string SelectionText => Selected is AssetTile tile
        ? $"{tile.Entry.Name} · {Describe(tile.Entry)}"
        : "Nothing chosen";

    public bool IsUnits => Kind == AssetKind.Unit;

    public string GroupsTitle => Kind switch { AssetKind.Unit => "Faction", AssetKind.Brush or AssetKind.Cubemap => "Folder", _ => "Biome" };

    public string CategoriesTitle => Kind == AssetKind.Unit ? "Kind" : "Category";

    public string SearchHint => Kind == AssetKind.Unit ? "Search: name or id" : "Search: name, biome, category";

    public double TileWidth => ThumbnailSize + 16;

    public double ThumbnailSize => 80;

    public IReadOnlyList<AssetChoice> Groups { get; private set; } = [];

    public IReadOnlyList<AssetChoice> Categories { get; private set; } = [];

    public IReadOnlyList<AssetChoice> Techs { get; private set; } = [];

    /// <summary>
    /// The tiles the filters leave, in order: one per asset (a prop's variants each have their own,
    /// since a slot takes one of them).
    /// </summary>
    public IReadOnlyList<AssetTile> Tiles { get; private set; } = [];

    /// <summary>
    /// <see cref="Tiles"/> in rows of <see cref="Columns"/>.
    /// </summary>
    public IReadOnlyList<AssetRow> Rows { get; private set; } = [];

    public bool IsEmpty => Tiles.Count == 0;

    public string EmptyText => Tiles.Count > 0 ? ""
        : !catalog().Entries.Any(entry => entry.Kind == Kind) ? $"The workspace has no {AssetsViewModel.Noun(Kind, 2).ToLowerInvariant()}."
        : "Nothing matches: change the search, or clear a filter.";

    /// <summary>
    /// What the picker shows, in a line: <c>12 of 340 units</c>.
    /// </summary>
    public string Summary
    {
        get
        {
            int all = catalog().Entries.Count(entry => entry.Kind == Kind);
            string noun = AssetsViewModel.Noun(Kind, all).ToLowerInvariant();
            return Tiles.Count == all ? $"{all} {noun}" : $"{Tiles.Count} of {all} {noun}";
        }
    }

    /// <summary>
    /// Raised when the picker opens, for the view to put the keyboard in its search field.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Asks for one asset of <paramref name="kind"/>: the picker opens with its filters cleared,
    /// on <paramref name="current"/> when that is given (the asset a slot holds). A picker that is
    /// open already is cancelled first.
    /// </summary>
    /// <returns>The chosen asset's key (<see cref="AssetEntry.Key"/>), or null when cancelled.</returns>
    public Task<string?> PickAsync(AssetKind kind, string? title = null, string? current = null)
    {
        _answer?.TrySetResult(null);
        TaskCompletionSource<string?> answer = _answer = new TaskCompletionSource<string?>();
        _rebuilding = true;
        Kind = kind;
        Title = title ?? $"Choose {Article(kind)} {AssetsViewModel.Noun(kind, 1).ToLowerInvariant()}";
        Query = "";
        Group = null;
        Category = null;
        Tech = null;
        Show = null;
        _rebuilding = false;
        Rebuild();
        if (current is not null && catalog().Find(current) is AssetEntry entry && Tiles.FirstOrDefault(tile => tile.Entry == entry) is AssetTile tile)
        {
            Select(tile);
        }
        IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
        return answer.Task;
    }

    /// <summary>
    /// Chooses the selected asset, and closes; nothing happens while none is selected.
    /// </summary>
    [RelayCommand]
    public void Choose()
    {
        if (Selected is AssetTile tile)
        {
            Close(tile.Entry.Key);
        }
    }

    /// <summary>
    /// Chooses a tile, as a double click does.
    /// </summary>
    public void Choose(AssetTile tile)
    {
        Select(tile);
        Choose();
    }

    /// <summary>
    /// Closes without an asset.
    /// </summary>
    [RelayCommand]
    public void Cancel() => Close(null);

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
    /// The index of the selected tile in <see cref="Tiles"/>, or -1.
    /// </summary>
    public int SelectedIndex => Selected is null ? -1 : IndexOf(Selected);

    [RelayCommand]
    private void ChooseTile(AssetTile tile) => Select(tile);

    [RelayCommand]
    private void ChooseGroup(AssetChoice choice) => Group = choice.IsOn ? null : choice.Value;

    [RelayCommand]
    private void ChooseCategory(AssetChoice choice) => Category = choice.IsOn ? null : choice.Value;

    [RelayCommand]
    private void ChooseTech(AssetChoice choice) => Tech = choice.IsOn ? null : choice.Value;

    [RelayCommand]
    private void ChooseShow(AssetChoice choice) => Show = choice.IsOn ? null : choice.Value;

    /// <summary>
    /// Stars the selected asset, or takes its star away (user story F-9).
    /// </summary>
    [RelayCommand]
    public void ToggleStar()
    {
        if (Selected is AssetTile tile && Shelf is AssetsViewModel shelf)
        {
            tile.IsStarred = shelf.SetStar(tile.Entry.Key, !shelf.Favourites.IsStarred(tile.Entry.Key));
            OnPropertyChanged(nameof(SelectedStarred));
            OnPropertyChanged(nameof(StarText));
            if (Show == Starred)
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// The value of <see cref="Show"/> that shows the starred assets.
    /// </summary>
    public const string Starred = "starred";

    /// <summary>
    /// The value of <see cref="Show"/> that shows the assets chosen last.
    /// </summary>
    public const string Recent = "recent";

    partial void OnShowChanged(string? value) => Rebuild();

    partial void OnQueryChanged(string value) => Rebuild();

    partial void OnGroupChanged(string? value) => Rebuild();

    partial void OnCategoryChanged(string? value) => Rebuild();

    partial void OnTechChanged(string? value) => Rebuild();

    partial void OnColumnsChanged(int value) => MakeRows();

    private void Close(string? key)
    {
        IsOpen = false;
        if (key is not null)
        {
            Shelf?.NoteUsed(key);
        }
        TaskCompletionSource<string?>? answer = _answer;
        _answer = null;
        answer?.TrySetResult(key);
    }

    /// <summary>
    /// Filters the catalogue again: the choices of each filter, counted with the others, and the
    /// tiles. The selection stays on its asset if that is still shown, else the first tile.
    /// </summary>
    private void Rebuild()
    {
        if (_rebuilding)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("assets.pick-filter");
        string[] words = AssetSearch.Words(Query);
        AssetFavourites? favourites = Shelf?.Favourites;
        List<AssetEntry> matching = [.. catalog().Entries.Where(entry => entry.Kind == Kind && AssetSearch.Matches(entry, words))];
        Shows = favourites is null ? [] :
        [
            new AssetChoice(Starred, "Starred", matching.Count(entry => favourites.IsStarred(entry.Key)), Show == Starred),
            new AssetChoice(Recent, "Recent", matching.Count(entry => favourites.RecentIndex(entry.Key) >= 0), Show == Recent),
        ];
        List<AssetEntry> ofKind = favourites is null || Show is null ? matching
            : Show == Starred ? [.. matching.Where(entry => favourites.IsStarred(entry.Key))]
            : [.. matching.Where(entry => favourites.RecentIndex(entry.Key) >= 0).OrderBy(entry => favourites.RecentIndex(entry.Key))];
        bool InGroup(AssetEntry entry) => Group is null || entry.Group == Group;
        bool InCategory(AssetEntry entry) => Category is null || IsOfCategory(entry, Category);
        bool InTech(AssetEntry entry) => Tech is null || TechOf(entry) == Tech;

        Groups = [.. ofKind
            .Where(entry => InCategory(entry) && InTech(entry))
            .GroupBy(entry => entry.Group)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AssetChoice(group.Key, AssetsViewModel.Title(group.Key), group.Count(), group.Key == Group))];
        List<AssetEntry> forCategories = [.. ofKind.Where(entry => InGroup(entry) && InTech(entry))];
        List<AssetChoice> categories = [.. forCategories
            .GroupBy(entry => entry.Category)
            .OrderByDescending(category => category.Count())
            .Select(category => new AssetChoice(category.Key, category.Key, category.Count(), category.Key == Category))];
        if (Kind == AssetKind.Unit && forCategories.Count(entry => entry.IsCivilian) is int civilians and > 0)
        {
            categories.Add(new AssetChoice(Civilian, Civilian, civilians, Category == Civilian));
        }
        Categories = categories;
        Techs = Kind != AssetKind.Unit ? [] : [.. ofKind
            .Where(entry => InGroup(entry) && InCategory(entry))
            .GroupBy(TechOf)
            .OrderBy(tech => tech.Key, StringComparer.Ordinal)
            .Select(tech => new AssetChoice(tech.Key, AssetSearch.TechTitle(tech.First().TechLevel), tech.Count(), tech.Key == Tech))];

        AssetEntry? selected = Selected?.Entry;
        Tiles = [.. ofKind.Where(entry => InGroup(entry) && InCategory(entry) && InTech(entry))
            .Select(entry => new AssetTile(entry, [entry]) { IsStarred = favourites?.IsStarred(entry.Key) == true })];
        MakeRows();

        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Techs));
        OnPropertyChanged(nameof(Shows));
        OnPropertyChanged(nameof(Tiles));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(Summary));

        Select(Tiles.FirstOrDefault(tile => tile.Entry == selected) ?? Tiles.FirstOrDefault());
    }

    private void MakeRows()
    {
        Rows = [.. Tiles.Chunk(Math.Max(1, Columns)).Select(row => new AssetRow(row))];
        OnPropertyChanged(nameof(Rows));
    }

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

    private static bool IsOfCategory(AssetEntry entry, string category) =>
        category == Civilian && entry.Kind == AssetKind.Unit ? entry.IsCivilian : entry.Category == category;

    /// <summary>
    /// The value of a unit's tech level among the filters: <c>1</c> to <c>4</c>, or <c>none</c>.
    /// </summary>
    private static string TechOf(AssetEntry entry) => entry.TechLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";

    /// <summary>
    /// What an asset is, in a few words: <c>T2 · land · UEF</c>, <c>trees · evergreen</c>.
    /// </summary>
    public static string Describe(AssetEntry entry) => entry.Kind == AssetKind.Unit
        ? string.Join(" · ", new[] { entry.TechLevel is null ? null : AssetSearch.TechTitle(entry.TechLevel), entry.Category, entry.Group, entry.IsCivilian ? Civilian : null, entry.Code }.OfType<string>())
        : $"{entry.Category} · {AssetsViewModel.Title(entry.Group)}";

    private static string Article(AssetKind kind) => kind == AssetKind.Unit ? "a" : AssetsViewModel.Noun(kind, 1)[0] is 'A' or 'E' or 'I' or 'O' or 'U' ? "an" : "a";
}
