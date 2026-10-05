using FAForever.Vault.Viewer.Services.Replays;

namespace FAForever.Vault.Viewer.Services;

/// <summary>
/// Where the preview image of a map comes from: the vault for uploaded maps, the map generator's
/// preview service for generated maps (<c>neroxis_map_generator_{version}_{seed}_{options}</c>).
/// Both are loaded as images (<c>&lt;img&gt;</c>, SVG <c>&lt;image&gt;</c>), which need no CORS:
/// the preview service sends no CORS headers, so it cannot be fetched from code.
/// </summary>
public static class MapPreviews
{
    private const string GeneratedPrefix = "neroxis_map_generator_";

    /// <summary>
    /// The preview service renders a generated map on request: a map it has seen takes about half a
    /// second, a new one several seconds. Any other name gets a 500.
    /// </summary>
    private const string GeneratedPreviewUrl = "https://mapgen.services.atlantishq.de/api-dev/request/preview/";

    /// <summary>The generator versions the preview service can render; it fails on any other.</summary>
    private static readonly HashSet<string> SupportedGeneratorVersions = ["1.19.0", "1.21.1", "1.21.2"];

    /// <summary>
    /// The map folder of a replay ("osiris.v0006"): from the metadata, or from the scenario's map path
    /// when the metadata has none. Generated maps are not in the vault, so the server writes "None".
    /// </summary>
    public static string? Folder(LoadedReplay model)
    {
        if (model.Metadata?.mapname is { Length: > 0 } name && name != "None")
        {
            return name;
        }

        if (model.Replay.Header.Scenario.Map.SCMapReference is { Length: > 0 } reference)
        {
            string[] segments = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2 ? segments[^2] : null;
        }

        return null;
    }

    /// <summary>Whether the map in this folder came from the map generator.</summary>
    public static bool IsGenerated(string? folder)
        => folder is not null && folder.StartsWith(GeneratedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The large preview of the map in this folder, or null when there is none to ask for: a
    /// generated map of a version the preview service does not support.
    /// </summary>
    public static string? Url(string? folder)
    {
        if (folder is not { Length: > 0 })
        {
            return null;
        }

        string name = folder.ToLowerInvariant();
        return IsGenerated(name)
            ? GeneratedUrl(name)
            : $"https://content.faforever.com/maps/previews/large/{Uri.EscapeDataString(name)}.png";
    }

    /// <summary>
    /// The preview of a map from the FAF API: the vault's thumbnail, except for generated maps, which
    /// the vault has no image of.
    /// </summary>
    public static string? Url(string? folder, string? vaultThumbnail)
        => IsGenerated(folder) ? GeneratedUrl(folder!.ToLowerInvariant()) : vaultThumbnail;

    private static string? GeneratedUrl(string name)
    {
        string version = name[GeneratedPrefix.Length..].Split('_')[0];
        return SupportedGeneratorVersions.Contains(version) ? GeneratedPreviewUrl + Uri.EscapeDataString(name) : null;
    }
}
