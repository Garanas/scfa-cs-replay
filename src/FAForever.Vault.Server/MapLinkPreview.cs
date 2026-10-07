using System.Globalization;
using System.Text.RegularExpressions;
using FAForever.Vault.Viewer.Services;

/// <summary>
/// The link preview of a map page (<c>/maps/theta_passage_-_faf_version.v0001</c>): the map's name
/// from its folder and the vault's large preview, so nothing is fetched. The page itself reads the
/// map's files in the browser.
/// </summary>
public static partial class MapLinkPreview
{
    /// <summary>
    /// The card of a map folder, or null when the folder is not one the vault could have (the page
    /// then gets the default card).
    /// </summary>
    public static LinkPreviewCard? Card(string folder, string previewUrlFormat)
    {
        folder = folder.ToLowerInvariant();
        if (!FolderPattern().IsMatch(folder))
        {
            return null;
        }

        string name = MapPreviews.DisplayName(folder) ?? folder;
        Match version = VersionPattern().Match(folder);
        string description = version.Success
            ? $"Version {int.Parse(version.Groups[1].Value, CultureInfo.InvariantCulture)} of {name}: its terrain, resources and routes, read from the map's own files, with its games and reviews."
            : $"{name}: its terrain, resources and routes, read from the map's own files, with its games and reviews.";

        return new LinkPreviewCard(
            name,
            description,
            string.Format(CultureInfo.InvariantCulture, previewUrlFormat, Uri.EscapeDataString(folder)),
            $"Preview of {name}",
            LargeImage: true);
    }

    // vault folders are lower case letters, digits and a few separators, ending in the version
    [GeneratedRegex(@"^[a-z0-9_\-. ]{1,200}$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"\.v(\d{1,4})$")]
    private static partial Regex VersionPattern();
}
