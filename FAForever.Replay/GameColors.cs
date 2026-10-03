
namespace FAForever.Replay
{
    /// <summary>
    /// The in-game army colour table, mirrored from the FAF game repository:
    /// lua/GameColors.lua ("ArmyColors"). <see cref="ReplayPlayerOptions.PlayerColor"/> and
    /// <see cref="ReplayPlayerOptions.ArmyColor"/> are 1-based indices into this table -
    /// verified against the colours that SpawnPing marker callbacks carry for the same
    /// players. Re-check against GameColors.lua after game updates.
    /// </summary>
    public static class GameColors
    {
        private static readonly string[] ArmyColors =
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
        /// </summary>
        public static string? ToCss(int? index)
            => index is { } i && i >= 1 && i <= ArmyColors.Length ? ArmyColors[i - 1] : null;

        /// <summary>
        /// Colour per source id (index into <see cref="ReplayHeader.Clients"/>), for inputs
        /// and callbacks.
        /// </summary>
        public static Dictionary<int, string> BySource(ReplayHeader header)
        {
            Dictionary<int, string> colors = new();
            foreach (ReplayPlayerOptions army in header.Armies)
            {
                if (army.SourceId is { } sourceId && army.Color is { } color)
                {
                    colors[sourceId] = color;
                }
            }

            return colors;
        }

        /// <summary>
        /// Colour per player name, for chat messages and other name-keyed data.
        /// </summary>
        public static Dictionary<string, string> ByName(ReplayHeader header)
        {
            Dictionary<string, string> colors = new();
            foreach (ReplayPlayerOptions army in header.Armies)
            {
                if (army is { Civilian: not true, PlayerName: { Length: > 0 } name } && army.Color is { } color)
                {
                    colors.TryAdd(name, color);
                }
            }

            return colors;
        }
    }
}
