using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// How the shelf and the picker search the assets: each word of the search must be in an asset's
/// name, code, group (biome, faction, folder) or category.
/// </summary>
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
    public static string TechTitle(int? level) => level switch
    {
        null => "No tech level",
        4 => "Experimental",
        int tech => $"T{tech}",
    };
}
