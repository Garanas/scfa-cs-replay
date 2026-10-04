
namespace FAForever.Replay
{
    /// <summary>
    /// The faction indices as the lobby stores them in the replay header; the FAF API uses
    /// the same numbering. 5 is the lobby's "random" slot; civilian armies carry it too.
    /// </summary>
    public enum Faction
    {
        Uef = 1,
        Aeon = 2,
        Cybran = 3,
        Seraphim = 4,
        Random = 5,
    }

    public static class FactionExtensions
    {
        public static string DisplayName(this Faction faction) => faction switch
        {
            Faction.Uef => "UEF",
            Faction.Aeon => "Aeon",
            Faction.Cybran => "Cybran",
            Faction.Seraphim => "Seraphim",
            Faction.Random => "Random",
            _ => faction.ToString(),
        };
    }
}
