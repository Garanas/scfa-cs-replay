
namespace FAForever.Replay
{
    /// <summary>
    /// The lobby options of a single army, as passed to `CLobby:LaunchGame` and stored in the
    /// replay header. Mirrors the PlayerOptions record of faf-java-commons.
    ///
    /// All fields are optional: offline skirmishes, AI armies and older replays carry fewer
    /// keys. <see cref="Raw"/> exposes the complete Lua table for anything not typed here
    /// (such as OwnerID, the FAForever user id).
    /// </summary>
    /// <param name="SourceId">Index into <see cref="ReplayHeader.Clients"/> that controls this army, or null when no client does (AI and civilian armies).</param>
    /// <param name="PlayerName">The display name of the player or AI.</param>
    /// <param name="Faction">The lobby faction index: 1 = UEF, 2 = Aeon, 3 = Cybran, 4 = Seraphim.</param>
    /// <param name="Team">The team index as stored in the lobby, where 1 means no team (FFA).</param>
    /// <param name="StartSpot">The starting position on the map, 1-based.</param>
    /// <param name="Human">Whether the army is controlled by a human.</param>
    /// <param name="Civilian">Whether the army is a civilian army.</param>
    /// <param name="AIPersonality">The AI personality, for AI armies.</param>
    /// <param name="PlayerColor">Index into the game's color table chosen by the player.</param>
    /// <param name="ArmyColor">Index into the game's color table assigned to the army.</param>
    /// <param name="Country">Two-letter country code, as known by FAForever.</param>
    /// <param name="Clan">The clan tag, if any.</param>
    /// <param name="RatingMean">The mean of the player's TrueSkill rating (MEAN).</param>
    /// <param name="RatingDeviation">The deviation of the player's TrueSkill rating (DEV). The displayed rating is conventionally mean - 3 * deviation.</param>
    /// <param name="RatedGames">The number of rated games the player has played (NG).</param>
    /// <param name="Raw">The complete Lua table of this army, for keys that are not typed.</param>
    public record ReplayPlayerOptions(
        int? SourceId,
        string? PlayerName,
        int? Faction,
        int? Team,
        int? StartSpot,
        bool? Human,
        bool? Civilian,
        string? AIPersonality,
        int? PlayerColor,
        int? ArmyColor,
        string? Country,
        string? Clan,
        double? RatingMean,
        double? RatingDeviation,
        int? RatedGames,
        LuaData.Table Raw)
    {
        /// <summary>
        /// The in-game colour of this army as CSS hex, resolved against
        /// <see cref="GameColors"/> (lua/GameColors.lua). PlayerColor is what the game
        /// renders for the player (marker pings confirm it); ArmyColor is the fallback.
        /// </summary>
        public string? Color => GameColors.ToCss(PlayerColor) ?? GameColors.ToCss(ArmyColor);

        /// <summary>
        /// The rating as FAForever displays it: mean - 3 x deviation, rounded. Null when the
        /// lobby carried no rating (offline games, AI and civilian armies).
        /// </summary>
        public int? Rating => DisplayRating(RatingMean, RatingDeviation);

        /// <summary>
        /// The FAForever display-rating convention, shared with rating data from the API.
        /// </summary>
        public static int? DisplayRating(double? mean, double? deviation)
            => mean is { } m && deviation is { } d ? (int)Math.Round(m - 3 * d) : null;
    }
}
