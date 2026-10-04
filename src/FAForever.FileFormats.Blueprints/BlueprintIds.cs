
namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// The layer a blueprint id encodes in its third letter.
    /// </summary>
    public enum BlueprintLayer
    {
        Land,
        Air,
        Naval,
        Structure,
    }

    /// <summary>
    /// Decodes the conventions of blueprint ids: <c>ueb0101</c> is [prefix u][faction e]
    /// [layer b][number 0101]. Mod units do not have to follow the convention, so every
    /// result is nullable.
    /// </summary>
    public static class BlueprintIds
    {
        /// <summary>
        /// The faction from the second letter: e = UEF, a = Aeon, r = Cybran, s = Seraphim.
        /// </summary>
        public static Faction? GetFaction(string? blueprintId)
        {
            if (blueprintId is not { Length: >= 2 })
            {
                return null;
            }

            return char.ToLowerInvariant(blueprintId[1]) switch
            {
                'e' => Faction.Uef,
                'a' => Faction.Aeon,
                'r' => Faction.Cybran,
                's' => Faction.Seraphim,
                _ => null,
            };
        }

        /// <summary>
        /// The layer from the third letter: l = land, a = air, s = naval, b = structure.
        /// </summary>
        public static BlueprintLayer? GetLayer(string? blueprintId)
        {
            if (blueprintId is not { Length: >= 3 })
            {
                return null;
            }

            return char.ToLowerInvariant(blueprintId[2]) switch
            {
                'l' => BlueprintLayer.Land,
                'a' => BlueprintLayer.Air,
                's' => BlueprintLayer.Naval,
                'b' => BlueprintLayer.Structure,
                _ => null,
            };
        }

        /// <summary>
        /// The tech tier from the hundreds digit of the unit number: 1-3 are the tech levels
        /// and 4 is experimental. This is a heuristic: it holds for the base game's units,
        /// but a handful of experimentals carry 3xx numbers (e.g. uaa0310, the Czar).
        /// </summary>
        public static int? GetTechLevel(string? blueprintId)
        {
            if (blueprintId is not { Length: >= 7 } || !int.TryParse(blueprintId[3..7], out int number))
            {
                return null;
            }

            return (number / 100) % 10 is >= 1 and <= 4 and var tier ? tier : null;
        }
    }
}
