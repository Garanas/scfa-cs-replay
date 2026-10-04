using FAForever.FileFormats.Replay;

namespace FAForever.Vault.Viewer.Services.Commands;

/// <summary>
/// Maps command categories to marker icons. Icon files live at
/// wwwroot/images/commands/&lt;slug&gt;.png with slug = the lowercase <see cref="CommandCategory"/>
/// name (e.g. "move", "attack", "launch"), generated from the game's waypoint button textures
/// by tools/convert-command-icons.ps1 (never edit them by hand). A category without a PNG
/// falls back to the inline SVG glyph symbols in <c>PlaythroughMarkerLayer</c>. Adding an
/// icon = extend the script mapping, re-run it, and add the slug to <see cref="Available"/>:
/// a manifest instead of an onerror fallback, because SVG &lt;image&gt; does not fire error
/// events reliably.
/// </summary>
public static class CommandIcons
{
    /// <summary>The slugs for which a PNG exists in wwwroot/images/commands/. Keep in sync with the folder.</summary>
    private static readonly HashSet<string> Available =
    [
        "move", "attack", "aggressive", "patrol", "build", "launch", "reclaim",
        "repair", "capture", "guard", "transport", "teleport", "stop",
    ];

    public static string Slug(CommandCategory category) => category.ToString().ToLowerInvariant();

    /// <summary>The icon path for a category, or null when no icon has been delivered yet.</summary>
    public static string? IconPath(CommandCategory category)
        => Available.Contains(Slug(category)) ? $"images/commands/{Slug(category)}.png" : null;
}
