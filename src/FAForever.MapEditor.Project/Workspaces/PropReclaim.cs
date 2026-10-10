using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What a number of props give when they are reclaimed (user story P-5).
    /// </summary>
    /// <param name="Props">How many props were counted.</param>
    /// <param name="Unknown">How many of them have a blueprint the workspace has not, which count as 0.</param>
    public sealed record ReclaimTotal(double Mass, double Energy, int Props, int Unknown)
    {
        public static ReclaimTotal None { get; } = new ReclaimTotal(0, 0, 0, 0);
    }

    /// <summary>
    /// An area of the map and the reclaim of the props whose centre lies in it.
    /// </summary>
    public sealed record AreaReclaim(string Area, ReclaimTotal Total);

    /// <summary>
    /// The mass and energy of props when reclaimed (user story P-5): the values of their blueprints
    /// (<see cref="AssetEntry.ReclaimMass"/>, <see cref="AssetEntry.ReclaimEnergy"/>), as the game
    /// gives a prop's blueprint values to it, added up for the map, a selection or an area.
    /// </summary>
    public static class PropReclaim
    {
        /// <summary>
        /// The reclaim of props, each by its blueprint in the catalogue.
        /// </summary>
        public static ReclaimTotal Of(IEnumerable<Prop> props, AssetCatalog catalog)
        {
            Dictionary<string, AssetEntry?> entries = new Dictionary<string, AssetEntry?>(StringComparer.Ordinal);
            double mass = 0;
            double energy = 0;
            int count = 0;
            int unknown = 0;
            foreach (Prop prop in props)
            {
                if (!entries.TryGetValue(prop.Blueprint, out AssetEntry? entry))
                {
                    entry = catalog.Find(prop.Blueprint) is { Kind: AssetKind.Prop } found ? found : null;
                    entries[prop.Blueprint] = entry;
                }
                count++;
                if (entry is null)
                {
                    unknown++;
                    continue;
                }
                mass += entry.ReclaimMass;
                energy += entry.ReclaimEnergy;
            }
            return new ReclaimTotal(mass, energy, count, unknown);
        }

        /// <summary>
        /// The reclaim of the props in each of the map's areas, in the order of the areas.
        /// </summary>
        public static IReadOnlyList<AreaReclaim> PerArea(MapDocument document, AssetCatalog catalog) =>
            [.. document.Markers.Areas.Select(area => new AreaReclaim(area.Name, Of(document.Props.Where(prop =>
                prop.Position.X >= area.X0 && prop.Position.X <= area.X1 && prop.Position.Z >= area.Z0 && prop.Position.Z <= area.Z1), catalog)))];
    }
}
