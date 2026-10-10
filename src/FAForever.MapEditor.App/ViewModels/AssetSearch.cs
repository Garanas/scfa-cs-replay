using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// How the shelf and the picker search the assets: each word of the search must be in an asset's
/// name, code, group (biome, faction, folder) or category.
/// </summary>
/// <summary>
/// How the shelf and the picker order their tiles.
/// </summary>
public enum AssetSort
{
    /// <summary>
    /// As the catalogue lists them (starred and recent first where the scope says so).
    /// </summary>
    Listed,

    /// <summary>
    /// The most mass first: a unit's build cost, a prop's reclaim.
    /// </summary>
    Mass,

    /// <summary>
    /// The most energy first.
    /// </summary>
    Energy,
}

public static class AssetSearch
{
    /// <summary>
    /// The words of a search, without the spaces between them.
    /// </summary>
    public static string[] Words(string query) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Whether every word is in the asset's name, code, group or category, in any case.
    /// </summary>
    public static bool Matches(AssetEntry entry, string[] words) => words.All(word =>
        entry.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
        || entry.Code.Contains(word, StringComparison.OrdinalIgnoreCase)
        || entry.Group.Contains(word, StringComparison.OrdinalIgnoreCase)
        || entry.Category.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A unit's tech level as the game says it: <c>T1</c> to <c>T3</c>, <c>Experimental</c>;
    /// <c>No tech level</c> for null.
    /// </summary>
    /// <summary>
    /// The entries in the order a sort asks for: as they come, or the most mass or energy first (a
    /// unit's cost, a prop's reclaim); entries of equal value keep their order.
    /// </summary>
    public static IEnumerable<AssetEntry> Sorted(IEnumerable<AssetEntry> entries, AssetSort sort) => sort switch
    {
        AssetSort.Mass => entries.OrderByDescending(entry => entry.Mass),
        AssetSort.Energy => entries.OrderByDescending(entry => entry.Energy),
        _ => entries,
    };

    /// <summary>
    /// What a sort's choice says, in the order of <see cref="AssetSort"/>.
    /// </summary>
    public static IReadOnlyList<string> SortTitles { get; } = ["As listed", "Most mass first", "Most energy first"];

    /// <summary>
    /// Mass or energy in a few characters for a tile: <c>850</c>, <c>5,220</c>, <c>47.4k</c>.
    /// </summary>
    public static string Short(double value) => value >= 10000
        ? (value / 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k"
        : value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

    public static string TechTitle(int? level) => level switch
    {
        null => "No tech level",
        4 => "Experimental",
        int tech => $"T{tech}",
    };
}
