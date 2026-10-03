using FAForever.Replay;

namespace FAForever.Replay.Viewer.Services.Commands;

/// <summary>
/// Maps command categories to marker icons. Icon files live at
/// wwwroot/images/commands/&lt;slug&gt;.png with slug = the lowercase <see cref="CommandCategory"/>
/// name (e.g. "move", "attack", "launch"). Until a file lands, markers fall back to the
/// inline SVG glyph symbols in <c>PlaythroughMarkerLayer</c>. Adding an icon = drop the PNG
/// in the folder and add its slug to <see cref="Available"/> — a manifest instead of an
/// onerror fallback, because SVG &lt;image&gt; does not fire error events reliably.
/// </summary>
public static class CommandIcons
{
    /// <summary>The slugs for which a PNG exists in wwwroot/images/commands/. Keep in sync with the folder.</summary>
    private static readonly HashSet<string> Available = [];

    public static string Slug(CommandCategory category) => category.ToString().ToLowerInvariant();

    /// <summary>The icon path for a category, or null when no icon has been delivered yet.</summary>
    public static string? IconPath(CommandCategory category)
        => Available.Contains(Slug(category)) ? $"images/commands/{Slug(category)}.png" : null;
}
