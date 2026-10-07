using System.Text.RegularExpressions;

namespace FAForever.Vault.Viewer.Services.Api;

/// <summary>
/// Search parameters for the map vault. Empty fields and empty sets are not filtered on. Maps whose
/// latest version is hidden are always left out (see <see cref="FafApiClient.SearchMapsAsync"/>).
/// </summary>
public sealed record MapSearchQuery
{
    /// <summary>Part of the map name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The author's exact login, * as wildcard.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Widths of the latest version in game units (<see cref="MapSizes.Units"/>); empty for any.</summary>
    public IReadOnlyList<int> Sizes { get; init; } = [];

    /// <summary>Exact player counts of the latest version; empty for any.</summary>
    public IReadOnlyList<int> Players { get; init; } = [];

    /// <summary>Only maps whose latest version counts for rating.</summary>
    public bool RankedOnly { get; init; }

    /// <summary>Only the featured maps (<c>recommended</c> in the API).</summary>
    public bool FeaturedOnly { get; init; }

    public MapSortOrder Sort { get; init; } = MapSortOrder.MostPlayed;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 24;

    // Records compare lists by reference; the page compares queries to skip a search it already ran.
    public bool Equals(MapSearchQuery? other)
        => other is not null && Name == other.Name && Author == other.Author
           && Sizes.SequenceEqual(other.Sizes) && Players.SequenceEqual(other.Players)
           && RankedOnly == other.RankedOnly && FeaturedOnly == other.FeaturedOnly
           && Sort == other.Sort && Page == other.Page && PageSize == other.PageSize;

    public override int GetHashCode() => HashCode.Combine(Name, Author, Sizes.Count, Players.Count, RankedOnly, FeaturedOnly, Sort, Page);
}

public enum MapSortOrder
{
    /// <summary>Games played on every version together.</summary>
    MostPlayed,

    /// <summary>The lower bound of the reviews' score, so a map with one good review does not top the list.</summary>
    BestRated,

    /// <summary>Upload of the latest version. Not the map's <c>updateTime</c>, which changes with every game played.</summary>
    Newest,

    Name,
}

/// <summary>
/// Map sizes. The vault stores them in game units; players know them in kilometres, as the game
/// lobby shows them (256 units = 5 km). Every visible map has one of these widths (measured 2026-10-06).
/// </summary>
public static class MapSizes
{
    public static readonly IReadOnlyList<int> Units = [64, 128, 256, 512, 1024, 2048, 4096];

    /// <summary>"5 km" for 256; sizes between the steps (generated maps: 576 is 11.25 km) get decimals.</summary>
    public static string Kilometres(int units) => units switch
    {
        64 => "1.25 km",
        128 => "2.5 km",
        4096 => "81 km",
        _ => units % 256 == 0 ? $"{units / 256 * 5} km" : $"{(units * 5 / 256.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} km",
    };

    /// <summary>"10 km" for a square map, "10 × 20 km" otherwise.</summary>
    public static string Kilometres(int width, int height)
        => width == height ? Kilometres(width) : $"{Kilometres(width).Replace(" km", "")} × {Kilometres(height)}";
}

/// <summary>A matchmaker queue with its current map pools, lowest rating first.</summary>
public sealed record LadderQueue(string TechnicalName, int TeamSize, IReadOnlyList<LadderPool> Pools)
{
    /// <summary>The queue as players know it: "1v1", "4v4 full share".</summary>
    public string Name => TechnicalName switch
    {
        "ladder1v1" => "1v1",
        "tmm2v2" => "2v2",
        "tmm3v3" => "3v3",
        "tmm4v4_full_share" => "4v4 full share",
        "tmm4v4_share_until_death" => "4v4 share until death",
        _ => TeamSize > 0 ? $"{TeamSize}v{TeamSize} ({TechnicalName})" : TechnicalName,
    };
}

/// <summary>
/// The maps of one rating bracket of a queue. The bracket's label comes from the pool's name ("TMM 2v2
/// 300-800" gives "300-800"): the pool's own rating bounds are on another scale (that pool has 500 to
/// 1000, measured 2026-10-06), so the name is what players recognise.
/// </summary>
public sealed record LadderPool(string Name, string Label, double? MinRating, double? MaxRating, IReadOnlyList<LadderPoolEntry> Entries)
{
    /// <summary>The last word of the pool's name: "&lt;800", "800-1800", "1800+".</summary>
    public static string LabelOf(string name) => name.Trim().Split(' ')[^1];
}

/// <summary>A map in a pool: a vault map at the version the pool holds, or a generated map.</summary>
public sealed record LadderPoolEntry(MapSummary? Map, GeneratedMapParams? Generated, int Weight);

/// <summary>What the matchmaker asks the map generator for: size in game units, spawns, generator version.</summary>
public sealed record GeneratedMapParams(int? Size, int? Spawns, string? GeneratorVersion);

public sealed record MapSearchResult(IReadOnlyList<MapSummary> Maps, int Page, int? TotalPages, int? TotalRecords);

/// <summary>A map of the vault with its latest version.</summary>
public sealed record MapSummary(int Id, string Name, string? Author)
{
    public int GamesPlayed { get; init; }

    /// <summary>Chosen by the FAF team (<c>recommended</c> in the API).</summary>
    public bool Featured { get; init; }

    /// <summary>The latest version's number, e.g. 6 for <c>osiris.v0006</c>.</summary>
    public int? Version { get; init; }

    public string? FolderName { get; init; }

    public string? Description { get; init; }

    public int? MaxPlayers { get; init; }

    /// <summary>Width in game units.</summary>
    public int? Width { get; init; }

    /// <summary>Height in game units.</summary>
    public int? Height { get; init; }

    public bool? Ranked { get; init; }

    public string? PreviewUrl { get; init; }

    /// <summary>Upload of the latest version.</summary>
    public DateTimeOffset? UploadedAt { get; init; }

    /// <summary>Average review score, 1 to 5; null without reviews.</summary>
    public double? AverageScore { get; init; }

    public int Reviews { get; init; }

    public string? Size => Width is { } width && Height is { } height ? MapSizes.Kilometres(width, height) : null;

    /// <summary>
    /// The description as players should read it: without the <c>&lt;LOC key&gt;</c> that older maps
    /// start with (a key for the game's translations), and with runs of spaces closed up.
    /// </summary>
    public string? ReadableDescription => Description is { } text
        && Regex.Replace(Regex.Replace(text, @"^\s*<LOC[^>]*>", ""), @"[ \t]{2,}", " ").Trim() is { Length: > 0 } readable
            ? readable
            : null;
}

/// <summary>
/// One version of a vault map, as the map page shows it: the map's name and author, the version's
/// numbers and the summary of its reviews. Found by its folder (<c>theta_passage.v0001</c>).
/// </summary>
public sealed record MapVersionDetails(int VersionId, int MapId, string Name, string? Author, int Version)
{
    /// <summary>Games played on this version.</summary>
    public int GamesPlayed { get; init; }

    /// <summary>Games played on every version of the map together.</summary>
    public int MapGamesPlayed { get; init; }

    public bool Ranked { get; init; }

    /// <summary>Taken out of the vault by its author; games on it stay in the vault.</summary>
    public bool Hidden { get; init; }

    public bool Featured { get; init; }

    public DateTimeOffset? UploadedAt { get; init; }

    /// <summary>Average review score of this version, 1 to 5; null without reviews.</summary>
    public double? AverageScore { get; init; }

    public int Reviews { get; init; }

    /// <summary>The newest version of the map that is not hidden, when it is not this one.</summary>
    public MapVersionReference? NewerVersion { get; init; }
}

/// <summary>Another version of a map: its number and folder.</summary>
public sealed record MapVersionReference(int Version, string FolderName);

/// <summary>A player's review of a map version: a score from 1 to 5 and an optional text.</summary>
public sealed record MapReview(int Id, int? PlayerId, string Player, int Score, string? Text, DateTimeOffset? UpdatedAt);
