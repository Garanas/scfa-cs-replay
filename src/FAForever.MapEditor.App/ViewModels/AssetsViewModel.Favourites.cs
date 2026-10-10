using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Stars and recents (user story F-9): the shelf's scopes Starred and Recent, a star on the tiles
/// starred, the inspector's star of the shown asset; the picker offers the same. They are the
/// workspace's (<see cref="AssetFavourites"/>, its <c>favourites.json</c>).
/// </summary>
public sealed partial class AssetsViewModel
{
    /// <summary>
    /// The workspace's stars and recents.
    /// </summary>
    public AssetFavourites Favourites { get; } = new AssetFavourites();

    public bool IsStarredScope => Scope == AssetScope.Starred;

    public bool IsRecentScope => Scope == AssetScope.Recent;

    /// <summary>
    /// How many assets of the kind are starred.
    /// </summary>
    public int StarredCount => Favourites.Starred.Count(key => Catalog.Find(key)?.Kind == Kind);

    /// <summary>
    /// How many assets of the kind were chosen lately.
    /// </summary>
    public int RecentCount => Favourites.Recent.Count(key => Catalog.Find(key)?.Kind == Kind);

    /// <summary>
    /// Whether the asset the inspector shows is starred.
    /// </summary>
    public bool ShownStarred => Shown is AssetEntry shown && Favourites.IsStarred(shown.Key);

    /// <summary>
    /// What the inspector's star button says.
    /// </summary>
    public string ShownStarText => ShownStarred ? "Starred" : "Star";

    /// <summary>
    /// Reads the stars and recents of a workspace; what cannot be read is logged and starts empty.
    /// </summary>
    public void OpenFavourites(Workspace workspace)
    {
        string file = AssetFavourites.FileOf(workspace);
        try
        {
            Favourites.Open(file);
        }
        catch (Exception exception) when (exception is FAForever.MapEditor.Project.MapProjectException or IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(exception, "Cannot read the stars and recents in {File}: {Reason}", file, exception.Message);
        }
    }

    /// <summary>
    /// Stars an asset or takes its star away; what cannot be written is logged.
    /// </summary>
    /// <returns>Whether it is starred now.</returns>
    public bool SetStar(string key, bool on)
    {
        try
        {
            Favourites.Star(key, on);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.LogError(exception, "Saving the stars in {File} failed: {Reason}", Favourites.File, exception.Message);
        }
        return Favourites.IsStarred(key);
    }

    /// <summary>
    /// Notes an asset chosen (a tile clicked on the shelf, an asset chosen in the picker): it is the
    /// newest of the recents.
    /// </summary>
    public void NoteUsed(string key)
    {
        try
        {
            Favourites.Used(key);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.LogError(exception, "Saving the recent assets in {File} failed: {Reason}", Favourites.File, exception.Message);
        }
    }

    /// <summary>
    /// The stars and recents for automation's <c>state</c>.
    /// </summary>
    public JsonObject DescribeFavourites() => new JsonObject
    {
        ["file"] = Favourites.File,
        ["starred"] = new JsonArray([.. Favourites.Starred.Select(key => (JsonNode)JsonValue.Create(key))]),
        ["recent"] = new JsonArray([.. Favourites.Recent.Select(key => (JsonNode)JsonValue.Create(key))]),
    };

    private void OnFavouritesChanged()
    {
        if (Scope is AssetScope.Starred or AssetScope.Recent)
        {
            Rebuild();
        }
        else
        {
            foreach (AssetTile tile in Tiles)
            {
                tile.IsStarred = IsStarred(tile);
            }
        }
        OnPropertyChanged(nameof(StarredCount));
        OnPropertyChanged(nameof(RecentCount));
        OnPropertyChanged(nameof(ShownStarred));
        OnPropertyChanged(nameof(ShownStarText));
    }

    /// <summary>
    /// Whether a tile is starred: any of its variants.
    /// </summary>
    public bool IsStarred(AssetTile tile) => tile.Members.Any(member => Favourites.IsStarred(member.Key));

    /// <summary>
    /// Whether an asset is among the scopes of stars and recents.
    /// </summary>
    private bool IsInFavourites(AssetEntry entry) => Scope switch
    {
        AssetScope.Starred => Favourites.IsStarred(entry.Key),
        AssetScope.Recent => Favourites.RecentIndex(entry.Key) >= 0,
        _ => false,
    };

    /// <summary>
    /// The shown assets in the order of their scope: the newest first among the recents.
    /// </summary>
    private IEnumerable<AssetEntry> InFavouritesOrder(IEnumerable<AssetEntry> entries) => Scope == AssetScope.Recent
        ? entries.OrderBy(entry => Favourites.RecentIndex(entry.Key))
        : entries;
}
