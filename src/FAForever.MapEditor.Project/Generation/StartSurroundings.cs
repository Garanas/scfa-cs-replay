using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// Mass in props and in wrecks.
    /// </summary>
    public sealed record ReclaimShare(double Props, double Wrecks)
    {
        public static ReclaimShare None { get; } = new ReclaimShare(0, 0);

        public double Total => Props + Wrecks;

        public ReclaimShare Add(double mass, bool wreck) => wreck ? this with { Wrecks = Wrecks + mass } : this with { Props = Props + mass };
    }

    /// <summary>
    /// What lies around a start (the preview page, <see cref="PreviewPage"/>): the deposits within
    /// <see cref="Near"/> and <see cref="Far"/> ogrids, and the reclaim of the props and wrecks for
    /// which it is the nearest start, within those distances and on the whole map.
    /// </summary>
    /// <remarks>
    /// Each prop and wreck counts for one start only, the nearest in a straight line, so reclaim
    /// between two starts is never counted twice and the starts' shares add up to the map's reclaim.
    /// Deposits are counted for every start they are near: two starts may share one.
    /// </remarks>
    /// <param name="MassNear">Mass deposits within <see cref="Near"/> ogrids.</param>
    /// <param name="MassFar">Mass deposits within <see cref="Far"/> ogrids.</param>
    /// <param name="HydrocarbonsFar">Hydrocarbon deposits within <see cref="Far"/> ogrids.</param>
    public sealed record StartSurroundings(
        Spawn Start, int MassNear, int MassFar, int HydrocarbonsFar, ReclaimShare ReclaimNear, ReclaimShare ReclaimFar, ReclaimShare ReclaimClosest)
    {
        /// <summary>
        /// The near distance in ogrids (0.6 km).
        /// </summary>
        public const float Near = 30;

        /// <summary>
        /// The far distance in ogrids (1.2 km).
        /// </summary>
        public const float Far = 60;

        /// <summary>
        /// The surroundings of every start, in the order of the starts.
        /// </summary>
        /// <param name="propMass">The mass a prop gives when reclaimed (<see cref="ReclaimValues.PropMass"/>).</param>
        /// <param name="wreckMass">The mass a wreck gives (<see cref="ReclaimValues.WreckMass"/>).</param>
        public static IReadOnlyList<StartSurroundings> Of(MapDocument document, Func<Prop, double> propMass, Func<PlacedUnit, double> wreckMass)
        {
            IReadOnlyList<Spawn> starts = document.Markers.Spawns;
            Vector2[] places = [.. starts.Select(start => Flat(start.Position))];
            ReclaimShare[] near = [.. starts.Select(_ => ReclaimShare.None)];
            ReclaimShare[] far = [.. near];
            ReclaimShare[] closest = [.. near];

            IEnumerable<(Vector2 At, double Mass, bool Wreck)> reclaim =
                document.Props.Select(prop => (Flat(prop.Position), propMass(prop), false))
                    .Concat(document.AllUnits().Where(UnitWrecks.IsWreck).Select(unit => (Flat(unit.Unit.Position), wreckMass(unit), true)));
            foreach ((Vector2 at, double mass, bool wreck) in reclaim)
            {
                if (mass <= 0 || places.Length == 0)
                {
                    continue;
                }
                int nearest = 0;
                float distance = float.MaxValue;
                for (int i = 0; i < places.Length; i++)
                {
                    float d = Vector2.Distance(at, places[i]);
                    if (d < distance)
                    {
                        (nearest, distance) = (i, d);
                    }
                }
                closest[nearest] = closest[nearest].Add(mass, wreck);
                if (distance <= Far)
                {
                    far[nearest] = far[nearest].Add(mass, wreck);
                }
                if (distance <= Near)
                {
                    near[nearest] = near[nearest].Add(mass, wreck);
                }
            }

            return [.. starts.Select((start, i) => new StartSurroundings(
                start,
                Count(document, places[i], ResourceKind.Mass, Near),
                Count(document, places[i], ResourceKind.Mass, Far),
                Count(document, places[i], ResourceKind.Hydrocarbon, Far),
                near[i], far[i], closest[i]))];
        }

        private static int Count(MapDocument document, Vector2 start, ResourceKind kind, float within) =>
            document.Markers.Resources.Count(resource => resource.Kind == kind && Vector2.Distance(Flat(resource.Position), start) <= within);

        private static Vector2 Flat(Vector3 position) => new Vector2(position.X, position.Z);
    }

    /// <summary>
    /// How much a wreck holds, in the bands the preview page shows instead of its mass.
    /// </summary>
    public enum WreckValue
    {
        /// <summary>Under 100 mass.</summary>
        Low,

        /// <summary>100 to 500 mass.</summary>
        Medium,

        /// <summary>500 mass or more.</summary>
        High,
    }

    /// <summary>
    /// The mass of props and wrecks when reclaimed, from the workspace's blueprints.
    /// </summary>
    public static class ReclaimValues
    {
        /// <summary>
        /// A prop's reclaim (its blueprint's <c>Economy.ReclaimMassMax</c>, as the game gives it);
        /// 0 for a prop the catalogue lacks.
        /// </summary>
        public static double PropMass(Prop prop, AssetCatalog catalog) =>
            catalog.Find(prop.Blueprint) is { Kind: AssetKind.Prop } entry ? entry.ReclaimMass : 0;

        /// <summary>
        /// The unit of a wreck, from the catalogue: by its blueprint id (<c>uel0202</c>), also when the
        /// map names the blueprint's file.
        /// </summary>
        public static AssetEntry? UnitOf(string blueprint, AssetCatalog catalog) =>
            catalog.Find(blueprint) is { Kind: AssetKind.Unit } entry ? entry
            : catalog.Find(UnitId(blueprint)) is { Kind: AssetKind.Unit } byId ? byId
            : null;

        /// <summary>
        /// <c>/units/UEL0202/UEL0202_unit.bp</c> and <c>uel0202</c> both become <c>uel0202</c>.
        /// </summary>
        public static string UnitId(string blueprint)
        {
            string id = Path.GetFileNameWithoutExtension(blueprint.Replace('\\', '/')).ToLowerInvariant();
            return id.EndsWith("_unit", StringComparison.Ordinal) ? id[..^"_unit".Length] : id;
        }

        /// <summary>
        /// What a wreck holds: the unit's mass cost times the share its wreck keeps
        /// (<c>Unit:CreateWreckageProp</c> in <c>lua/sim/Unit.lua</c>, which
        /// <c>ScenarioUtils.CreateWreckageUnit</c> calls with no overkill); 0 for a unit the catalogue
        /// lacks or that leaves no wreck.
        /// </summary>
        public static double WreckMass(PlacedUnit unit, AssetCatalog catalog) =>
            UnitOf(unit.Unit.Blueprint, catalog) is { } entry ? (entry.MassCost ?? 0) * (entry.WreckageMass ?? 0) : 0;

        /// <summary>
        /// The band of a wreck's mass.
        /// </summary>
        public static WreckValue ValueOf(double mass) => mass >= 500 ? WreckValue.High : mass >= 100 ? WreckValue.Medium : WreckValue.Low;
    }
}
