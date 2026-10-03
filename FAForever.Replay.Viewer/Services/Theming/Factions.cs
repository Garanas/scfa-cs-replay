namespace FAForever.Replay.Viewer.Services.Theming;

/// <summary>
/// Display helpers for the faction index as stored in the replay header and the FAF API
/// (1 = UEF, 2 = Aeon, 3 = Cybran, 4 = Seraphim). The icons under wwwroot/images/factions
/// are taken from the FAForever game repository (textures/ui/common/faction_icon-lg).
/// </summary>
public static class Factions
{
    public static string? Name(int? faction)
        => faction is { } f && f is >= 1 and <= 5 ? ((Faction)f).DisplayName() : null;

    public static string? IconPath(int? faction) => faction switch
    {
        1 => "images/factions/uef.png",
        2 => "images/factions/aeon.png",
        3 => "images/factions/cybran.png",
        4 => "images/factions/seraphim.png",
        _ => null,
    };

    public static string Swatch(int? faction) => faction switch
    {
        1 => "var(--swatch-uef)",
        2 => "var(--swatch-aeon)",
        3 => "var(--swatch-cybran)",
        4 => "var(--swatch-seraphim)",
        _ => "var(--th-ink-faint)",
    };

    public static string? Name(Faction? faction) => Name((int?)faction);

    public static string? IconPath(Faction? faction) => IconPath((int?)faction);

    public static string Swatch(Faction? faction) => Swatch((int?)faction);
}
