using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Features.Players.Matches;

/// <summary>
/// The filters of the Matches tab as the address holds them, every value already normalised to a
/// token the tab knows (an unknown or missing value is the default). Two filters are equal when
/// they would ask the API the same thing, so a tab can tell whether a navigation changed anything.
/// </summary>
public sealed record MatchFilters(string Period, string Mode, string Faction, string Result, string Map, string With, string Sort, int Page)
{
    /// <summary>The filters of an address without parameters.</summary>
    public static MatchFilters Default { get; } = MatchUrl.Read(_ => null);

    /// <summary>The same filters on the first page in the default order: what the charts depend on.</summary>
    public MatchFilters ForCharts() => this with { Sort = MatchUrl.DefaultSort, Page = 1 };

    /// <summary>The query for the API; the period becomes a start moment counted back from <paramref name="now"/>.</summary>
    public MatchQuery ToQuery(DateTimeOffset now) => new()
    {
        Since = Periods.Since(Period, now),
        Mode = Mode,
        Faction = MatchUrl.FactionOf(Faction),
        Result = Result switch
        {
            MatchUrl.ResultWin => MatchResult.Victory,
            MatchUrl.ResultLoss => MatchResult.Defeat,
            _ => null,
        },
        Map = Map.Length > 0 ? Map : null,
        WithPlayer = With.Length > 0 ? With : null,
        Sort = MatchUrl.SortOf(Sort),
        Page = Page,
    };
}

/// <summary>The query parameters of the Matches tab: reading them, and the names a Reset clears.</summary>
public static class MatchUrl
{
    public const string DefaultSort = "newest";

    public const string ResultWin = "win";

    public const string ResultLoss = "loss";

    /// <summary>The parameters of the filter row; Reset removes them all (and <c>page</c> with them).</summary>
    public static readonly IReadOnlyList<string> Parameters = ["period", "mode", "faction", "result", "map", "with", "sort"];

    /// <summary>The choices of the order select.</summary>
    public static readonly IReadOnlyList<ChoiceOption> Sorts =
    [
        new(DefaultSort, "Newest first"),
        new("oldest", "Oldest first"),
        new("longest", "Longest first"),
        new("shortest", "Shortest first"),
    ];

    private static readonly string[] Modes = [.. PlayerFilters.Modes.Select(option => option.Token)];

    /// <summary>The filters of an address; <paramref name="get"/> returns the raw value of a parameter or null.</summary>
    public static MatchFilters Read(Func<string, string?> get)
    {
        string? mode = Token(get("mode"));
        string? faction = Token(get("faction"));
        string? result = Token(get("result"));
        string? sort = Token(get("sort"));
        return new MatchFilters(
            Periods.Normalize(Token(get("period"))),
            mode is not null && Modes.Contains(mode) ? mode : "all",
            faction is not null && FactionOf(faction) is not null ? faction : "any",
            result is ResultWin or ResultLoss ? result : "any",
            get("map")?.Trim() ?? "",
            get("with")?.Trim() ?? "",
            sort is not null && Sorts.Any(option => option.Token == sort) ? sort : DefaultSort,
            int.TryParse(get("page"), out int page) && page > 1 ? page : 1);
    }

    /// <summary>The faction a token names (<c>uef</c>, <c>cybran</c>, <c>aeon</c>, <c>seraphim</c>), or null.</summary>
    public static Faction? FactionOf(string? token) => token switch
    {
        "uef" => Faction.Uef,
        "aeon" => Faction.Aeon,
        "cybran" => Faction.Cybran,
        "seraphim" => Faction.Seraphim,
        _ => null,
    };

    /// <summary>The order a token names; the default for anything else.</summary>
    public static MatchSort SortOf(string? token) => token switch
    {
        "oldest" => MatchSort.Oldest,
        "longest" => MatchSort.Longest,
        "shortest" => MatchSort.Shortest,
        _ => MatchSort.Newest,
    };

    /// <summary>True when the address holds any filter or order (a page alone does not count).</summary>
    public static bool AnyFilter(Func<string, string?> get)
        => Parameters.Any(name => !string.IsNullOrEmpty(get(name)));

    private static string? Token(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
