using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Replay;

namespace FAForever.Vault.Viewer.Features.Replay;

/// <summary>
/// The simple summary of an opening (the "Timings" view of the Build order tab): when a player
/// first ordered the milestones of an opening, and which units their factories were ordered to
/// build per minute. Everything is an order, not a completion: the replay holds the inputs, not
/// what came of them, so a cancelled structure still counts.
/// </summary>
public static class OpeningTimings
{
    /// <summary>A milestone, as the label in the comparison and a test on construction orders.</summary>
    public sealed record Moment(string Label, Func<BuildOrderItem, bool> Matches);

    /// <summary>Units of one type ordered in a minute (or the whole opening).</summary>
    public sealed record UnitCount(string BlueprintId, int Count);

    /// <summary>
    /// The milestones, in the order of a typical opening: firsts only. Counts such as "the 8th
    /// mass extractor" say little, as engineers get many of them shift-queued at once.
    /// </summary>
    public static readonly IReadOnlyList<Moment> Moments =
    [
        new("Land factory", item => IsStructure(item, "Land Factory", tech: 1)),
        new("Air factory", item => IsStructure(item, "Air Factory", tech: 1)),
        new("Naval factory", item => IsStructure(item, "Naval Factory", tech: 1)),
        new("Hydrocarbon", item => IsStructure(item, "Hydrocarbon", tech: 1)),
        new("Energy storage", item => IsStructure(item, "Energy Storage", tech: 1)),
        new("Mex upgrade", item => IsStructure(item, "Mass Extractor", tech: 2)),
        new("ACU upgrade", item => item.Enhancement is { } name && !name.StartsWith("Remove ", StringComparison.Ordinal)),
        new("Tech 2 factory", item => IsStructure(item, "Factory", tech: 2)),
        new("Tech 2 unit", item => IsMobile(item) && TechLevel(item.BlueprintId!) == 2),
        new("Tech 3 factory", item => IsStructure(item, "Factory", tech: 3)),
        new("Tech 3 unit", item => IsMobile(item) && TechLevel(item.BlueprintId!) == 3),
        new("Experimental", item => item.BlueprintId is { } id && TechLevel(id) == 4),
    ];

    /// <summary>The order that reached a milestone, or null when the player did not within the items.</summary>
    public static BuildOrderItem? Find(Moment moment, IEnumerable<BuildOrderItem> items)
        => items.FirstOrDefault(item => !item.IsQueueChange && moment.Matches(item));

    /// <summary>
    /// The mobile units ordered from <paramref name="from"/> up to <paramref name="to"/>, most
    /// first: factory orders times the factories they went to, plus queue changes. Units whose
    /// orders were all dequeued again are left out.
    /// </summary>
    public static List<UnitCount> UnitsOrdered(IEnumerable<BuildOrderItem> items, TimeSpan from, TimeSpan to)
        => items
            .Where(item => item.Timestamp >= from && item.Timestamp < to && IsMobile(item))
            .GroupBy(item => item.BlueprintId!.ToLowerInvariant())
            .Select(group => new UnitCount(group.Key, group.Sum(item => item.Quantity)))
            .Where(unit => unit.Count > 0)
            .OrderByDescending(unit => unit.Count)
            .ThenBy(unit => unit.BlueprintId)
            .ToList();

    /// <summary>
    /// The tech level in a blueprint id (1–3, 4 = experimental): the fifth character, e.g.
    /// uel0<b>2</b>01, ueb1<b>2</b>02; support factories (zeb9501, zeb9601) count from 5.
    /// </summary>
    public static int? TechLevel(string blueprintId)
    {
        if (blueprintId.Length < 7 || !char.IsAsciiDigit(blueprintId[4]))
        {
            return null;
        }

        int digit = blueprintId[4] - '0';
        return blueprintId[3] == '9' ? digit - 3 : digit;
    }

    /// <summary>Construction of a mobile unit: by a factory, or an experimental by engineers.</summary>
    private static bool IsMobile(BuildOrderItem item)
        => item.IsBuild && item.BlueprintId is { Length: >= 7 } id && char.ToLowerInvariant(id[2]) != 'b';

    /// <summary>A structure of a kind (by its display name) and tech level.</summary>
    private static bool IsStructure(BuildOrderItem item, string name, int tech)
        => item.IsBuild && item.BlueprintId is { Length: >= 7 } id && char.ToLowerInvariant(id[2]) == 'b'
            && TechLevel(id) == tech && UnitNames.GetOrNull(id)?.Contains(name, StringComparison.Ordinal) == true;
}
