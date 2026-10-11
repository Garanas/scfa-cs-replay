using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Checks
{
    /// <summary>
    /// The editor's own checks of a map (user story F-14).
    /// </summary>
    public static class MapChecks
    {
        public static IReadOnlyList<IProblemCheck> All { get; } =
        [
            new ProblemCheck("army-without-spawn", "Armies without a spawn", ArmiesWithoutSpawn),
            new ProblemCheck("missing-texture", "Textures the workspace and map lack", MissingTextures),
            new ProblemCheck("spawn-without-mass", "Spawns without mass nearby", SpawnsWithoutMass),
            new ProblemCheck("missing-model", "Props and units without a model", MissingModels),
            new ProblemCheck("walkability-changes", "Passages opened or closed", WalkabilityChanges),
            new ProblemCheck("wrecks-in-playable-army", "Wrecks only a player's slot creates", WrecksInPlayableArmies),
            new ProblemCheck("steep-deposit", "Deposits on steep ground", SteepDeposits),
            new ProblemCheck("walkability-marks", "Ground marked open that the game blocks", OpenMarksTheGameBlocks),
        ];

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TiledBuffer<byte>, OpenMarksResult> OpenMarks = [];

        private sealed record OpenMarksResult(Heightmap Heights, float? Water, int Count, (int X, int Z)? First);

        /// <summary>
        /// Ogrids marked open by hand (user story T-7) where the game will not let land units pass,
        /// whatever the map's files say: too steep, or under water (<see cref="WalkabilityMarks.OpenWhereTheGameWillNot"/>).
        /// Walkability and the path networks count them open; units in the game do not. Kept per
        /// version of the marks, heights and water, so a step that changes none of them costs nothing.
        /// </summary>
        public static IEnumerable<Problem> OpenMarksTheGameBlocks(ProblemContext context)
        {
            MapDocument document = context.Document;
            if (document.WalkabilityMarks is not TiledBuffer<byte> marks)
            {
                yield break;
            }
            float? water = document.Water.HasWater ? document.Water.Elevation : null;
            if (!OpenMarks.TryGetValue(marks, out OpenMarksResult? result) || !ReferenceEquals(result.Heights, document.Terrain.Heightmap) || result.Water != water)
            {
                (int count, (int X, int Z)? first) = WalkabilityMarks.OpenWhereTheGameWillNot(document);
                result = new OpenMarksResult(document.Terrain.Heightmap, water, count, first);
                OpenMarks.AddOrUpdate(marks, result);
            }
            if (result is { Count: > 0, First: (int x, int z) })
            {
                yield return new Problem("walkability-marks", ProblemSeverity.Warning,
                    $"{result.Count} ogrids are marked open where the ground is too steep or under water: walkability counts them open, the game's units will not pass there",
                    null, new Vector2(x + 0.5f, z + 0.5f));
            }
            if (OpenOnCliffFaces(document, marks) is { Count: > 0, First: (int fx, int fz) } faces)
            {
                yield return new Problem("walkability-marks", ProblemSeverity.Info,
                    $"{faces.Count} ogrids under cliff faces that block walkability are marked open by hand: the marks win, units pass there where the ground lets them",
                    null, new Vector2(fx + 0.5f, fz + 0.5f));
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TiledBuffer<byte>, OpenOnFacesResult> OpenOnFaces = [];

        private sealed record OpenOnFacesResult(CliffFaces.Key Faces, int Count, (int X, int Z)? First);

        /// <summary>
        /// The ogrids under the faces of cliffs that block walkability (<see cref="CliffFaces"/>) that
        /// are marked open by hand, where the mark wins: their count and the first; kept per version
        /// of the marks and the cliffs.
        /// </summary>
        private static (int Count, (int X, int Z)? First) OpenOnCliffFaces(MapDocument document, TiledBuffer<byte> marks)
        {
            CliffFaces.Key key = CliffFaces.KeyOf(document);
            if (key.IsEmpty || marks.Width != document.Width || marks.Height != document.Height)
            {
                return (0, null);
            }
            if (!OpenOnFaces.TryGetValue(marks, out OpenOnFacesResult? result) || !result.Faces.Equals(key))
            {
                int count = 0;
                (int, int)? first = null;
                foreach (int i in CliffFaces.Indices(document))
                {
                    int x = i % marks.Width;
                    int z = i / marks.Width;
                    if (marks[x, z] == WalkabilityMarks.Open)
                    {
                        count++;
                        first ??= (x, z);
                    }
                }
                result = new OpenOnFacesResult(key, count, first);
                OpenOnFaces.AddOrUpdate(marks, result);
            }
            return (result.Count, result.First);
        }

        /// <summary>
        /// A mass or hydrocarbon deposit whose ground rises more than <see cref="MarkerEdits.MaxGroundVariation"/>
        /// over the square it needs (<see cref="MarkerEdits.Steepness"/>): the storages around an
        /// extractor, or a hydrocarbon plant, cannot be built there.
        /// </summary>
        public static IEnumerable<Problem> SteepDeposits(ProblemContext context)
        {
            Heightmap heights = context.Document.Terrain.Heightmap;
            foreach (ResourceMarker deposit in context.Document.Markers.Resources)
            {
                if (MarkerEdits.Steepness(heights, deposit.Kind, deposit.Position.X, deposit.Position.Z) is float steepness)
                {
                    yield return new Problem("steep-deposit", ProblemSeverity.Warning,
                        string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"{deposit.Name} lies on steep ground: it rises {steepness:0.#} over {MarkerEdits.DepositSize(deposit.Kind)} ogrids, more than the {MarkerEdits.MaxGroundVariation} the buildings around it allow"),
                        ObjectRef.Marker(deposit.Id), new Vector2(deposit.Position.X, deposit.Position.Z));
                }
            }
        }

        /// <summary>
        /// How near a spawn a mass deposit must be, in ogrids: a player's first extractors lie close
        /// to the start, well inside the radius of a base in the extractor layout
        /// (<see cref="ExtractorThresholds.BaseRadius"/>, 60).
        /// </summary>
        public const float MassNearSpawn = 30;

        /// <summary>
        /// A playable army of a skirmish map without the spawn marker named after it (<c>ARMY_3</c>):
        /// the game has no place to start it. Co-op missions start their players by script.
        /// </summary>
        public static IEnumerable<Problem> ArmiesWithoutSpawn(ProblemContext context)
        {
            MapDocument document = context.Document;
            if (document.Scenario.Type != ScenarioType.Skirmish)
            {
                yield break;
            }
            HashSet<string> spawns = [.. document.Markers.Spawns.Select(spawn => spawn.Army)];
            foreach (string army in document.Scenario.PlayableArmies)
            {
                if (!spawns.Contains(army))
                {
                    yield return new Problem("army-without-spawn", ProblemSeverity.Error, $"{army} is playable but has no spawn marker");
                }
            }
        }

        /// <summary>
        /// Wrecks (a <c>WRECKAGE</c> group, user story U-5) of a playable army: the game creates an
        /// army's groups only when the army is in the game, so they show only when a player takes
        /// that slot. Wrecks for every game belong to an extra army, such as the civilians.
        /// </summary>
        public static IEnumerable<Problem> WrecksInPlayableArmies(ProblemContext context)
        {
            MapDocument document = context.Document;
            foreach (string army in document.PlayableArmiesWithWrecks())
            {
                PlacedUnit[] wrecks = [.. document.AllUnits().Where(unit => unit.Army == army && UnitWrecks.IsWreck(unit))];
                Unit first = wrecks[0].Unit;
                yield return new Problem("wrecks-in-playable-army", ProblemSeverity.Warning,
                    $"{army} is playable: its {wrecks.Length} wreck{(wrecks.Length == 1 ? "" : "s")} show only when a player takes that slot; move them to an extra army such as the civilians",
                    ObjectRef.Unit(first.Name), new Vector2(first.Position.X, first.Position.Z));
            }
        }

        /// <summary>
        /// A spawn with no mass deposit within <see cref="MassNearSpawn"/> ogrids.
        /// </summary>
        public static IEnumerable<Problem> SpawnsWithoutMass(ProblemContext context)
        {
            MarkerSet markers = context.Document.Markers;
            Vector2[] mass = [.. markers.Resources.Where(resource => resource.Kind == ResourceKind.Mass).Select(resource => new Vector2(resource.Position.X, resource.Position.Z))];
            float within = MassNearSpawn * MassNearSpawn;
            foreach (Spawn spawn in markers.Spawns)
            {
                Vector2 at = new Vector2(spawn.Position.X, spawn.Position.Z);
                if (!mass.Any(deposit => Vector2.DistanceSquared(deposit, at) <= within))
                {
                    yield return new Problem("spawn-without-mass", ProblemSeverity.Warning,
                        $"{spawn.Army} has no mass deposit within {MassNearSpawn:0} ogrids", ObjectRef.Marker(spawn.Id));
                }
            }
        }

        /// <summary>
        /// A texture of a stratum or decal that is neither one of the map's own files nor one of the
        /// workspace's assets: the game draws it black or not at all. Without a workspace only the
        /// map's own files are checked.
        /// </summary>
        public static IEnumerable<Problem> MissingTextures(ProblemContext context)
        {
            MapDocument document = context.Document;
            Dictionary<string, bool> known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool? Has(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return true;
                }
                if (known.TryGetValue(path, out bool has))
                {
                    return has;
                }
                string key = GameArchives.Normalize(path);
                bool? found;
                if (key.StartsWith("maps/", StringComparison.Ordinal))
                {
                    int folderEnd = key.IndexOf('/', "maps/".Length);
                    string inFolder = folderEnd < 0 ? "" : key[(folderEnd + 1)..];
                    found = document.Files.Keys.Any(file => string.Equals(file, inFolder, StringComparison.OrdinalIgnoreCase));
                }
                else if (context.Catalog is null && context.HasAsset is null)
                {
                    found = null;
                }
                else
                {
                    found = context.Catalog?.Find(path) is not null || context.HasAsset?.Invoke(path) == true;
                }
                if (found is bool result)
                {
                    known[path] = result;
                }
                return found;
            }

            for (int i = 0; i < document.Terrain.Layers.Length; i++)
            {
                TerrainLayer layer = document.Terrain.Layers[i];
                foreach ((string path, string what) in new[] { (layer.AlbedoPath, "albedo"), (layer.NormalPath, "normal map") })
                {
                    if (Has(path) == false)
                    {
                        yield return new Problem("missing-texture", ProblemSeverity.Warning, $"The {what} of stratum {i} is in neither the map nor the workspace: {path}");
                    }
                }
            }

            foreach (IGrouping<string, Decal> uses in document.Decals
                .SelectMany(decal => decal.Textures.Where(texture => !string.IsNullOrWhiteSpace(texture)).Select(texture => (texture, decal)))
                .Where(use => Has(use.texture) == false)
                .GroupBy(use => use.texture, use => use.decal, StringComparer.OrdinalIgnoreCase))
            {
                Decal first = uses.First();
                int count = uses.Count();
                yield return new Problem("missing-texture", ProblemSeverity.Warning,
                    $"{(count == 1 ? "A decal's texture is" : $"The texture of {count} decals is")} in neither the map nor the workspace: {uses.Key}", ObjectRef.Decal(first.Id));
            }

            if (MissingAssets.ChecksCubemaps(context.Catalog))
            {
                IEnumerable<(string Path, string What)> cubemaps =
                [
                    (document.Sky.Cubemap, "The sky's cube map"),
                    .. document.Sky.EnvironmentCubemaps.Select(cubemap => (cubemap.Texture, $"The cube map units reflect ({cubemap.Name})")),
                    (document.Water.Cubemap, "The cube map the water reflects"),
                ];
                foreach ((string path, string what) in cubemaps)
                {
                    if (!string.IsNullOrWhiteSpace(path) && Has(path) == false)
                    {
                        yield return new Problem("missing-texture", ProblemSeverity.Warning, $"{what} is in neither the map nor the workspace: {path}");
                    }
                }
            }
        }

        /// <summary>
        /// Props and units whose blueprint the workspace lacks (a warning: the game lacks it too, or
        /// it is a mod's), or has without a model (the views draw a box). Needs the workspace's assets.
        /// </summary>
        public static IEnumerable<Problem> MissingModels(ProblemContext context)
        {
            if (context.Catalog is not AssetCatalog catalog || catalog.Entries.Count == 0)
            {
                yield break;
            }
            MapDocument document = context.Document;
            IEnumerable<(string Blueprint, ObjectRef Thing, string Kind)> things =
                document.Props.Select(prop => (prop.Blueprint, ObjectRef.Prop(prop.Id), "prop"))
                    .Concat(document.AllUnits().Select(unit => (unit.Unit.Blueprint, ObjectRef.Unit(unit.Unit.Name), "unit")));
            foreach (IGrouping<string, (string Blueprint, ObjectRef Thing, string Kind)> uses in things.GroupBy(thing => AssetCatalog.KeyOf(thing.Blueprint), StringComparer.Ordinal))
            {
                (string blueprint, ObjectRef first, string kind) = uses.First();
                int count = uses.Count();
                string them = count == 1 ? $"A {kind} of {blueprint}" : $"{count} {kind}s of {blueprint}";
                switch (catalog.Find(blueprint))
                {
                    case null:
                        yield return new Problem("missing-model", ProblemSeverity.Warning, $"{them}: the workspace has no such blueprint", first);
                        break;
                    case { Model: null }:
                        yield return new Problem("missing-model", ProblemSeverity.Info, $"{them}: the blueprint has no converted model, drawn as a box", first);
                        break;
                }
            }
        }

        /// <summary>
        /// Where the land's regions joined or split when walkability was generated last (user story
        /// T-7): a new passage through a cliff, or a path that closed.
        /// </summary>
        public static IEnumerable<Problem> WalkabilityChanges(ProblemContext context)
        {
            if (context.Walkability is not { } changes)
            {
                yield break;
            }
            string layer = changes.Layer.ToString().ToLowerInvariant();
            foreach (Generation.WalkabilityLink passage in changes.Passages)
            {
                yield return new Problem("walkability-changes", ProblemSeverity.Warning,
                    $"A new passage joins {passage.Regions} regions of {layer} at {passage.Place.X:0}, {passage.Place.Y:0}", Place: passage.Place);
            }
            foreach (Generation.WalkabilityLink closure in changes.Closures)
            {
                yield return new Problem("walkability-changes", ProblemSeverity.Warning,
                    $"A path closed: a region of {layer} split in {closure.Regions} at {closure.Place.X:0}, {closure.Place.Y:0}", Place: closure.Place);
            }
        }
    }
}
