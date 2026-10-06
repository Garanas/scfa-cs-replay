using System.Globalization;
using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Features.Units;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// Facts about how the units changed over the game versions, for the Units page: counted from the
/// index's lists of changed units, so they follow the data. The facts about units also need the
/// latest data file, for their names and to leave out units no player can build.
/// Visible text: simple English, no hyphens, underscores or dashes (see the About pages).
/// </summary>
public static class UnitFacts
{
    /// <summary>
    /// A fact: a title, the number it is about in large print, a sentence, optionally a link to the
    /// page that shows it, and the units it names (by blueprint id, for their icons).
    /// </summary>
    public sealed record Fact(string Title, string Headline, string Text, string? Link = null, string? LinkLabel = null, IReadOnlyList<string>? Units = null);

    /// <summary>The unit that changed in the most versions, with the two after it.</summary>
    public static Fact? MostChanged(UnitDataIndex index, UnitData latest)
    {
        List<(string Id, int Count)> top = ChangeCounts(index)
            .Where(entry => latest.GetOrNull(entry.Key)?.Buildable == true)
            .OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(entry => (entry.Key, entry.Value))
            .ToList();
        if (top.Count == 0)
        {
            return null;
        }

        string text = $"The {Name(latest, top[0].Id)} changed in {top[0].Count} of the {index.Versions.Count} releases with data.";
        if (top.Count > 1)
        {
            text += $" Next are {string.Join(" and ", top.Skip(1).Select(unit => $"the {Name(latest, unit.Id)} ({unit.Count})"))}.";
        }

        return new Fact("The most changed unit", $"{top[0].Count} releases", text,
            UnitLinks.History(top.Select(unit => unit.Id)), "Follow them through history", top.Select(unit => unit.Id).ToList());
    }

    /// <summary>The units that went the longest without a change, counted from the last version that changed them.</summary>
    public static Fact? UntouchedLongest(UnitDataIndex index, UnitData latest)
    {
        int[] versions = index.Versions.Keys.Order().ToArray();
        if (versions.Length == 0)
        {
            return null;
        }

        // The last version that changed or added each unit; a unit never listed is as it was in the oldest version.
        Dictionary<string, int> lastChange = [];
        foreach (int version in versions)
        {
            if (index.Versions[version].Changes is { } changes)
            {
                foreach (string id in changes.Changed.Concat(changes.Added))
                {
                    lastChange[id] = version;
                }
            }
        }

        List<UnitSummary> buildable = latest.Units.Where(unit => unit.Buildable).ToList();
        if (buildable.Count == 0)
        {
            return null;
        }

        int since = buildable.Min(unit => lastChange.GetValueOrDefault(unit.BlueprintId, versions[0]));
        List<UnitSummary> untouched = buildable
            .Where(unit => lastChange.GetValueOrDefault(unit.BlueprintId, versions[0]) == since)
            .OrderByDescending(unit => unit.TechLevel ?? 0).ThenBy(unit => unit.BlueprintId, StringComparer.Ordinal)
            .ToList();
        int releasesSince = versions.Count(version => version > since);

        string units = untouched.Count == 1 ? $"The {Name(latest, untouched[0].BlueprintId)} has" : $"{untouched.Count} units have";
        string text = since == versions[0]
            ? $"{units} not changed since release {since}{Date(index, since)}, the oldest with data."
            : $"{units} not changed since release {since}{Date(index, since)}, {releasesSince} releases ago.";
        List<string> shown = untouched.Take(UnitLinks.MaxCompared).Select(unit => unit.BlueprintId).ToList();

        return new Fact("Untouched the longest", untouched.Count == 1 ? "1 unit" : $"{untouched.Count} units", text,
            UnitLinks.History(shown), untouched.Count > shown.Count ? $"Follow {shown.Count} of them" : "Follow them through history", shown);
    }

    /// <summary>The newest version that changed units. With the latest data, the units no player can build come last.</summary>
    public static Fact? LatestChanges(UnitDataIndex index, UnitData? latest)
    {
        if (index.Newest.FirstOrDefault(version => index.Versions[version].Changes?.Changed.Count > 0) is not (> 0 and var version))
        {
            return null;
        }

        IReadOnlyList<string> changed = index.Versions[version].Changes!.Changed;
        List<string> shown = changed
            .OrderBy(id => latest?.GetOrNull(id)?.Buildable == true ? 0 : 1)
            .Take(UnitLinks.MaxCompared)
            .ToList();

        return new Fact("The latest changes", Units(changed.Count),
            $"Release {version}{Date(index, version)} is the newest one that changed units.",
            ChangedIn(index, version), $"See what changed in {version}", shown);
    }

    /// <summary>The version that changed the most units at once.</summary>
    public static Fact? BiggestPatch(UnitDataIndex index)
    {
        if (index.Versions.Where(entry => entry.Value.Changes?.Changed.Count > 0)
                .OrderByDescending(entry => entry.Value.Changes!.Changed.Count).ThenBy(entry => entry.Key)
                .FirstOrDefault() is not { Value: not null } biggest)
        {
            return null;
        }

        int count = biggest.Value.Changes!.Changed.Count;
        return new Fact("The biggest patch", Units(count),
            $"Release {biggest.Key}{Date(index, biggest.Key)} changed {count} of its {biggest.Value.UnitCount} units at once.",
            ChangedIn(index, biggest.Key), $"See what changed in {biggest.Key}");
    }

    /// <summary>The most days between two releases that changed units.</summary>
    public static Fact? LongestWait(UnitDataIndex index)
    {
        List<(int Version, DateOnly Released)> changing = index.Versions
            .Where(entry => entry.Value.Changes is { IsEmpty: false } && entry.Value.Released is not null)
            .Select(entry => (entry.Key, entry.Value.Released!.Value))
            .OrderBy(entry => entry.Key)
            .ToList();

        (int From, int To, int Days)? longest = null;
        for (int i = 1; i < changing.Count; i++)
        {
            int days = changing[i].Released.DayNumber - changing[i - 1].Released.DayNumber;
            if (longest is null || days > longest.Value.Days)
            {
                longest = (changing[i - 1].Version, changing[i].Version, days);
            }
        }

        if (longest is not { } wait)
        {
            return null;
        }

        return new Fact("The longest wait", $"{wait.Days} days",
            $"After release {wait.From}{Date(index, wait.From)}, no release changed a unit until {wait.To}{Date(index, wait.To)}.",
            ChangedIn(index, wait.To), $"See what changed in {wait.To}");
    }

    /// <summary>How many releases changed no unit at all.</summary>
    public static Fact? QuietReleases(UnitDataIndex index)
    {
        // The oldest version has nothing to compare with, so it counts as neither.
        List<UnitDataIndex.UnitChanges> compared = index.Versions.Values.Select(entry => entry.Changes).OfType<UnitDataIndex.UnitChanges>().ToList();
        int quiet = compared.Count(changes => changes.IsEmpty);
        if (compared.Count == 0)
        {
            return null;
        }

        return new Fact("Releases without unit changes", $"{quiet} of {compared.Count}",
            $"{quiet} releases left every unit exactly as it was. Their changes were elsewhere in the game.");
    }

    private static Dictionary<string, int> ChangeCounts(UnitDataIndex index)
    {
        Dictionary<string, int> counts = [];
        foreach (UnitDataIndex.Entry entry in index.Versions.Values)
        {
            foreach (string id in entry.Changes?.Changed ?? [])
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        return counts;
    }

    // The database of that version, filtered to the units it changed; the latest version is the database's default.
    private static string ChangedIn(UnitDataIndex index, int version) =>
        version == index.Latest ? $"{UnitLinks.Database}?changed=1" : $"{UnitLinks.Database}?version={version}&changed=1";

    private static string Name(UnitData latest, string id) =>
        latest.GetOrNull(id) is { } unit ? UnitFilters.DisplayName(unit) : id;

    private static string Units(int count) => count == 1 ? "1 unit" : $"{count} units";

    // Non-breaking spaces keep a date on one line.
    private static string Date(UnitDataIndex index, int version) =>
        index.Versions.GetValueOrDefault(version)?.Released is { } released
            ? $" ({released.ToString("d MMM yyyy", CultureInfo.InvariantCulture)})"
            : "";
}
