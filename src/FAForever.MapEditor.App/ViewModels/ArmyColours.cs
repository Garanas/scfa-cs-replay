using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The colour the views give an army's units (user story U-10). The game's lobby chooses the colours of
/// the players, so a map's own <see cref="Army.Color"/> is mostly unset (0): an army then takes the
/// colour of its place in the map's list, and the civilians (<c>NEUTRAL_CIVILIAN</c>) a grey. These are
/// colours of the map, made in code, not of the theme.
/// </summary>
public static class ArmyColours
{
    // distinct from each other on terrain, as ARGB
    private static readonly uint[] Palette =
    [
        0xFFE24A4A, 0xFF4A8BE2, 0xFF4AC26B, 0xFFE8C23A, 0xFFE88A3A, 0xFFA05AE2, 0xFF3AD0D0, 0xFFE25AAE,
        0xFFE8E8E8, 0xFF9A6A3A, 0xFF8AB43A, 0xFF3A5AE8, 0xFFB43A3A, 0xFF3A9A7A, 0xFF8A8A8A, 0xFFD8A0E0,
    ];

    /// <summary>
    /// The grey of the civilians.
    /// </summary>
    public const uint Civilian = 0xFF8A8A8A;

    /// <summary>
    /// The colour (ARGB) of an army: its <see cref="Army.Color"/> when the map sets one (1 or more, an
    /// index into the palette), else its place in the document's armies.
    /// </summary>
    public static uint Of(MapDocument document, string army)
    {
        if (army == "NEUTRAL_CIVILIAN")
        {
            return Civilian;
        }
        for (int i = 0; i < document.Armies.Length; i++)
        {
            if (document.Armies[i].Name == army)
            {
                int index = document.Armies[i].Color > 0 ? document.Armies[i].Color - 1 : i;
                return Palette[index % Palette.Length];
            }
        }
        return Civilian;
    }
}
