using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Translates between the markers of a save file (a type name and a table of fields, linked by
    /// name) and the editor's <see cref="MarkerSet"/> (records linked by id), both ways.
    /// </summary>
    /// <remarks>
    /// Most fields of a marker are decoration for the map editors and the same for every marker of
    /// a type: its colour (<see cref="Marker.Color"/>), editor prop and <c>hint</c>, and for deposits
    /// <c>resource</c>, <c>size</c> and <c>amount</c> (100 in every map of the vault). The editor does
    /// not keep them; the export writes them as most maps of a local vault copy of 99 maps do. What
    /// differs per marker is kept: the links of path nodes, the army a deposit spawns with, the
    /// settings of the weather.
    /// </remarks>
    internal static partial class MarkerTranslation
    {
        private const string BlankProp = "/env/common/props/markers/M_Blank_prop.bp";

        private static readonly (string Type, PathLayer Layer)[] PathTypes =
        [
            ("Land Path Node", PathLayer.Land),
            ("Water Path Node", PathLayer.Water),
            ("Amphibious Path Node", PathLayer.Amphibious),
            ("Air Path Node", PathLayer.Air),
        ];

        /// <summary>
        /// The types whose markers the map editors write with <c>hint = true</c>.
        /// </summary>
        private static readonly HashSet<string> Hinted = new HashSet<string>(StringComparer.Ordinal)
        {
            "Air Path Node", "Amphibious Path Node", "Land Path Node", "Water Path Node", "Combat Zone", "Defensive Point",
            "Expansion Area", "Large Expansion Area", "Island", "Naval Area", "Naval Defensive Point", "Naval Rally Point",
            "Protected Experimental Construction", "Rally Point",
        };

        private static readonly string[] WeatherGeneratorFields =
        [
            "cloudCount", "cloudCountRange", "cloudEmitterScale", "cloudEmitterScaleRange", "cloudHeight", "cloudHeightRange", "cloudSpread", "spawnChance",
        ];

        #region From the save

        /// <summary>
        /// The markers of a save, numbered from 1 in the order of the file.
        /// </summary>
        /// <param name="noRushOffsets">The no-rush offsets of the scenario, by army.</param>
        public static MarkerSet ToMarkerSet(IEnumerable<MapMarker> markers, IEnumerable<MapArea> areas, IEnumerable<MapChain> chains, IReadOnlyDictionary<string, Vector2> noRushOffsets)
        {
            List<MapMarker> list = [.. markers];

            // the ids first: links name markers that come later in the file
            Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                ids.TryAdd(list[i].Name, i + 1);
            }
            ImmutableArray<int> Ids(IEnumerable<string> names) => [.. names.Where(ids.ContainsKey).Select(name => ids[name])];

            List<Spawn> spawns = [];
            List<ResourceMarker> resources = [];
            List<PathNode> pathNodes = [];
            List<AiMarker> aiMarkers = [];
            List<WeatherDefinition> weatherDefinitions = [];
            List<WeatherGenerator> weatherGenerators = [];

            for (int i = 0; i < list.Count; i++)
            {
                MapMarker marker = list[i];
                int id = i + 1;
                Vector3 orientation = Turn(marker.Orientation);
                LuaTableReader fields = new LuaTableReader(marker.Raw);
                switch (marker.Type)
                {
                    case "Blank Marker" when ArmyName().IsMatch(marker.Name):
                        spawns.Add(new Spawn
                        {
                            Id = id,
                            Name = marker.Name,
                            Position = marker.Position,
                            Orientation = orientation,
                            NoRushOffset = noRushOffsets.TryGetValue(marker.Name, out Vector2 offset) ? offset : null,
                        });
                        break;
                    case "Mass" or "Hydrocarbon":
                        resources.Add(new ResourceMarker
                        {
                            Id = id,
                            Name = marker.Name,
                            Kind = marker.Type == "Mass" ? ResourceKind.Mass : ResourceKind.Hydrocarbon,
                            Position = marker.Position,
                            Orientation = orientation,
                            SpawnWithArmy = fields.String("SpawnWithArmy"),
                        });
                        break;
                    case "Weather Definition":
                        weatherDefinitions.Add(new WeatherDefinition
                        {
                            Id = id,
                            Name = marker.Name,
                            Position = marker.Position,
                            Orientation = orientation,
                            MapStyle = fields.String("MapStyle") ?? "",
                            Description = fields.String("Description"),
                            DriftDirection = Vector(fields.Numbers("WeatherDriftDirection")),
                            Types = [.. Enumerable.Range(1, 4)
                                .Where(index => fields.String(WeatherType(index)) is not null)
                                .Select(index => new WeatherChance { Type = fields.String(WeatherType(index))!, Chance = fields.Number(WeatherType(index) + "Chance") ?? 0 })],
                        });
                        break;
                    case "Weather Generator":
                        weatherGenerators.Add(new WeatherGenerator
                        {
                            Id = id,
                            Name = marker.Name,
                            Position = marker.Position,
                            Orientation = orientation,
                            ForceType = fields.String("ForceType"),
                            SpawnChance = fields.Number("spawnChance"),
                            CloudCount = fields.Number("cloudCount"),
                            CloudCountRange = fields.Number("cloudCountRange"),
                            CloudEmitterScale = fields.Number("cloudEmitterScale"),
                            CloudEmitterScaleRange = fields.Number("cloudEmitterScaleRange"),
                            CloudHeight = fields.Number("cloudHeight"),
                            CloudHeightRange = fields.Number("cloudHeightRange"),
                            CloudSpread = fields.Number("cloudSpread"),
                        });
                        break;
                    default:
                        if (PathTypes.FirstOrDefault(p => p.Type == marker.Type) is { Type: not null } path)
                        {
                            pathNodes.Add(new PathNode
                            {
                                Id = id,
                                Name = marker.Name,
                                Layer = path.Layer,
                                Position = marker.Position,
                                Orientation = orientation,
                                AdjacentTo = Ids((fields.String("adjacentTo") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                            });
                        }
                        else
                        {
                            aiMarkers.Add(new AiMarker { Id = id, Name = marker.Name, Type = marker.Type, Position = marker.Position, Orientation = orientation });
                        }
                        break;
                }
            }

            return new MarkerSet
            {
                Spawns = [.. spawns],
                Resources = [.. resources],
                PathNodes = [.. pathNodes],
                AiMarkers = [.. aiMarkers],
                WeatherDefinitions = [.. weatherDefinitions],
                WeatherGenerators = [.. weatherGenerators],
                Areas = [.. areas.Select(a => new Area { Name = a.Name, X0 = a.X0, Z0 = a.Z0, X1 = a.X1, Z1 = a.Z1 })],
                Chains = [.. chains.Select(c => new Chain { Name = c.Name, MarkerIds = Ids(c.Markers) })],
            };
        }

        /// <summary>
        /// An orientation with -0 as 0: they turn a marker the same, and a project leaves an
        /// orientation of zero out.
        /// </summary>
        private static Vector3 Turn(Vector3 orientation) => orientation + Vector3.Zero;

        private static Vector3 Vector(IReadOnlyList<double> numbers) =>
            numbers.Count >= 3 ? new Vector3((float)numbers[0], (float)numbers[1], (float)numbers[2]) : Vector3.Zero;

        private static string WeatherType(int index) => $"WeatherType{index:00}";

        [GeneratedRegex(@"^ARMY_\d+$")]
        private static partial Regex ArmyName();

        #endregion

        #region To the save

        /// <summary>
        /// The markers as the save file writes them, each with the fields of its type.
        /// </summary>
        public static IEnumerable<MapMarker> ToMapMarkers(MarkerSet set)
        {
            Dictionary<int, string> names = Names(set);

            foreach (Spawn spawn in set.Spawns)
            {
                yield return Marker(spawn, "Blank Marker", []);
            }

            foreach (ResourceMarker resource in set.Resources)
            {
                bool mass = resource.Kind == ResourceKind.Mass;
                List<(string, LuaData)> fields =
                [
                    ("size", Number(mass ? 1 : 3)),
                    ("resource", new LuaData.Bool(true)),
                    ("amount", Number(100)),
                ];
                if (resource.SpawnWithArmy is string army)
                {
                    fields.Add(("SpawnWithArmy", new LuaData.String(army)));
                }
                yield return Marker(resource, mass ? "Mass" : "Hydrocarbon", fields);
            }

            foreach (PathNode node in set.PathNodes)
            {
                // a node without links gets an empty list, as most maps write it, never none
                List<(string, LuaData)> fields =
                [
                    ("graph", new LuaData.String("Default" + node.Layer)),
                    ("adjacentTo", new LuaData.String(string.Join(' ', NamesOf(names, node.AdjacentTo)))),
                ];
                yield return Marker(node, PathTypes.First(p => p.Layer == node.Layer).Type, fields);
            }

            foreach (AiMarker marker in set.AiMarkers)
            {
                yield return Marker(marker, marker.Type, []);
            }

            foreach (WeatherDefinition weather in set.WeatherDefinitions)
            {
                List<(string, LuaData)> fields =
                [
                    ("MapStyle", new LuaData.String(weather.MapStyle)),
                    ("WeatherDriftDirection", VectorTable(weather.DriftDirection)),
                ];
                for (int i = 0; i < weather.Types.Length; i++)
                {
                    fields.Add((WeatherType(i + 1), new LuaData.String(weather.Types[i].Type)));
                    fields.Add((WeatherType(i + 1) + "Chance", Number(weather.Types[i].Chance)));
                }
                if (weather.Description is string description)
                {
                    fields.Add(("Description", new LuaData.String(description)));
                }
                yield return Marker(weather, "Weather Definition", fields);
            }

            foreach (WeatherGenerator generator in set.WeatherGenerators)
            {
                double?[] values =
                [
                    generator.CloudCount, generator.CloudCountRange, generator.CloudEmitterScale, generator.CloudEmitterScaleRange,
                    generator.CloudHeight, generator.CloudHeightRange, generator.CloudSpread, generator.SpawnChance,
                ];
                List<(string, LuaData)> fields = [.. WeatherGeneratorFields.Zip(values)
                    .Where(entry => entry.Second is not null)
                    .Select(entry => (entry.First, (LuaData)Number(entry.Second!.Value)))];
                if (generator.ForceType is string forceType)
                {
                    fields.Add(("ForceType", new LuaData.String(forceType)));
                }
                yield return Marker(generator, "Weather Generator", fields);
            }
        }

        /// <summary>
        /// The chains as the save file writes them: the markers by name.
        /// </summary>
        public static IEnumerable<MapChain> ToMapChains(MarkerSet set)
        {
            Dictionary<int, string> names = Names(set);
            return set.Chains.Select(chain => new MapChain(chain.Name, NamesOf(names, chain.MarkerIds)));
        }

        private static Dictionary<int, string> Names(MarkerSet set) => set.All.ToDictionary(marker => marker.Id, marker => marker.Name);

        /// <summary>
        /// The names of markers by their ids, leaving out ids no marker has (any more).
        /// </summary>
        private static IReadOnlyList<string> NamesOf(Dictionary<int, string> names, IEnumerable<int> ids) =>
            [.. ids.Where(names.ContainsKey).Select(id => names[id])];

        /// <summary>
        /// A marker with its fields and the decoration of its type.
        /// </summary>
        private static MapMarker Marker(Marker marker, string type, IEnumerable<(string Key, LuaData Value)> fields)
        {
            Dictionary<string, LuaData> table = new Dictionary<string, LuaData>
            {
                ["color"] = new LuaData.String(marker.Color.ToArgb().ToString("x8", CultureInfo.InvariantCulture)),
            };
            if (Hinted.Contains(type))
            {
                table["hint"] = new LuaData.Bool(true);
            }
            foreach ((string key, LuaData value) in fields)
            {
                table[key] = value;
            }
            table["prop"] = new LuaData.String(BlankProp);
            return new MapMarker(marker.Name, type, marker.Position, marker.Orientation, new LuaData.Table(table));
        }

        private static LuaData.Number Number(double value) => new LuaData.Number(value);

        /// <summary>
        /// A vector as <c>VECTOR3</c> makes it (<see cref="MapLuaFunctions"/>).
        /// </summary>
        private static LuaData.Table VectorTable(Vector3 v) => new LuaData.Table(new Dictionary<string, LuaData>
        {
            ["1"] = Number(v.X),
            ["2"] = Number(v.Y),
            ["3"] = Number(v.Z),
            ["type"] = new LuaData.String("VECTOR3"),
        });

        /// <summary>
        /// A float as the double of its shortest text, so that 0.1f is written as 0.1.
        /// </summary>
        private static LuaData.Number Number(float value) =>
            new LuaData.Number(double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));

        #endregion
    }
}
