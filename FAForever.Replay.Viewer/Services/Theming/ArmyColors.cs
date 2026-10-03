namespace FAForever.Replay.Viewer.Services.Theming;

/// <summary>
/// The in-game army colour table, mirrored from the FAF game repository:
/// lua/GameColors.lua ("ArmyColors"). The
/// <see cref="FAForever.Replay.ReplayPlayerOptions.PlayerColor"/> and
/// <see cref="FAForever.Replay.ReplayPlayerOptions.ArmyColor"/> fields of the replay header
/// are 1-based indices into this table — verified against the colours that SpawnPing marker
/// callbacks carry for the same players. Re-check against GameColors.lua after game updates.
/// </summary>
public static class ArmyColors
{
    private static readonly string[] Colors =
    [
        "#e80a0a", // (01) Cybran red
        "#901427", // (02) dark red
        "#ff873e", // (03) Nomads orange
        "#b76518", // (04) new brown
        "#a79602", // (05) Sera golden
        "#fafa00", // (06) new yellow
        "#9fd802", // (07) Order green
        "#40bf40", // (08) mid green
        "#2e8b57", // (09) new green
        "#2f4f4f", // (10) olive (dark green)
        "#436eee", // (11) new blue1
        "#2929e1", // (12) UEF blue
        "#5f01a7", // (13) dark purple
        "#9161ff", // (14) purple
        "#66ffcc", // (15) aqua
        "#ffffff", // (16) white
        "#616d7e", // (17) grey
        "#ff88ff", // (18) pink
        "#ff32ff", // (19) new fuschia
    ];

    /// <summary>
    /// The CSS colour for a 1-based lobby colour index, or null when out of range.
    /// Prefer <c>PlayerColor</c> (the colour the game renders for the player; marker pings
    /// confirm it) and fall back to <c>ArmyColor</c>.
    /// </summary>
    public static string? Hex(int? index)
        => index is { } i && i >= 1 && i <= Colors.Length ? Colors[i - 1] : null;

    public static string? ForArmy(FAForever.Replay.ReplayPlayerOptions army)
        => Hex(army.PlayerColor) ?? Hex(army.ArmyColor);
}
