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
    /// Translates between the markers of a save file (a type name and a table of fields) and the
    /// editor's <see cref="MarkerSet"/>, both ways.
    /// </summary>
    /// <remarks>
    /// Most fields of a marker are decoration for the map editors and the same for every marker of
    /// a type: its colour, editor prop, <c>hint</c>, and for deposits <c>resource</c>, <c>size</c> and
    /// <c>amount</c> (100 in every map of the vault). The editor does not keep them; the export
    /// writes them from <see cref="Decorations"/>, the values most maps of a local vault copy of 99
    /// maps use. What differs per marker is kept: the links of path nodes, the army a deposit spawns
    /// with, the settings of the weather.
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
        /// The colour of each type and whether it has a <c>hint</c>; other types get the colour of a
        /// blank marker and no hint.
        /// </summary>
        private static readonly Dictionary<string, (string Color, bool Hint)> Decorations = new Dictionary<string, (string, bool)>
        {
            ["Air Path Node"] = ("ffffffff", true),
            ["Amphibious Path Node"] = ("ff00ffff", true),
            ["Land Path Node"] = ("ff00ff00", true),
            ["Water Path Node"] = ("ff0000ff", true),
            ["Blank Marker"] = ("ff800080", false),
            ["Mass"] = ("ff808080", false),
            ["Hydrocarbon"] = ("ff008000", false),
            ["Combat Zone"] = ("ff800000", true),
            ["Defensive Point"] = ("ff008000", true),
            ["Expansion Area"] = ("ff008080", true),
            ["Large Expansion Area"] = ("ff008080", true),
            ["Island"] = ("ff0000AA", true),
            ["Naval Area"] = ("ff0000FF", true),
            ["Naval Defensive Point"] = ("ff0080FF", true),
            ["Naval Link"] = ("ffff0000", false),
            ["Naval Rally Point"] = ("ff00FFFF", true),
            ["Protected Experimental Construction"] = ("ff0000AA", true),
            ["Rally Point"] = ("FF808000", true),
            ["Transport Marker"] = ("ff80A088", false),
            ["Weather Definition"] = ("FF808000", false),
            ["Weather Generator"] = ("FF808000", false),
        };

        private static readonly string[] WeatherGeneratorFields =
        [
            "cloudCount", "cloudCountRange", "cloudEmitterScale", "cloudEmitterScaleRange", "cloudHeight", "cloudHeightRange", "cloudSpread", "spawnChance",
        ];

        #region From the save

        public static MarkerSet ToMarkerSet(IEnumerable<MapMarker> markers, IEnumerable<MapArea> areas, IEnumerable<MapChain> chains)
        {
            List<Spawn> spawns = [];
            List<ResourceMarker> resources = [];
            List<PathNode> pathNodes = [];
            List<AiMarker> aiMarkers = [];
            List<WeatherDefinition> weatherDefinitions = [];
            List<WeatherGenerator> weatherGenerators = [];

            foreach (MapMarker marker in markers)
            {
                LuaTableReader fields = new LuaTableReader(marker.Raw);
                switch (marker.Type)
                {
                    case "Blank Marker" when ArmyName().IsMatch(marker.Name):
                        spawns.Add(new Spawn { Army = marker.Name, Position = marker.Position, Orientation = Turn(marker.Orientation) });
                        break;
                    case "Mass" or "Hydrocarbon":
                        resources.Add(new ResourceMarker
                        {
                            Name = marker.Name,
                            Kind = marker.Type == "Mass" ? ResourceKind.Mass : ResourceKind.Hydrocarbon,
                            Position = marker.Position,
                            Orientation = Turn(marker.Orientation),
                            SpawnWithArmy = fields.String("SpawnWithArmy"),
                        });
                        break;
                    case "Weather Definition":
                        weatherDefinitions.Add(new WeatherDefinition
                        {
                            Name = marker.Name,
                            Position = marker.Position,
                            Orientation = Turn(marker.Orientation),
                            MapStyle = fields.String("MapStyle") ?? "",
                            Description = fields.String("Description"),
                            DriftDirection = Vector(fields.Numbers("WeatherDriftDirection")),
                            Types = [.. Enumerable.Range(1, 4)
                                .Where(i => fields.String(WeatherType(i)) is not null)
                                .Select(i => new WeatherChance { Type = fields.String(WeatherType(i))!, Chance = fields.Number(WeatherType(i) + "Chance") ?? 0 })],
                        });
                        break;
                    case "Weather Generator":
                        weatherGenerators.Add(new WeatherGenerator
                        {
                            Name = marker.Name,
                            Position = marker.Position,
                            Orientation = Turn(marker.Orientation),
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
                                Name = marker.Name,
                                Layer = path.Layer,
                                Position = marker.Position,
                                Orientation = Turn(marker.Orientation),
                                AdjacentTo = [.. (fields.String("adjacentTo") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)],
                            });
                        }
                        else
                        {
                            aiMarkers.Add(new AiMarker { Name = marker.Name, Type = marker.Type, Position = marker.Position, Orientation = Turn(marker.Orientation) });
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
                Chains = [.. chains.Select(c => new Chain { Name = c.Name, Markers = [.. c.Markers] })],
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
            foreach (Spawn spawn in set.Spawns)
            {
                yield return Marker(spawn.Army, "Blank Marker", spawn.Position, spawn.Orientation, []);
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
                yield return Marker(resource.Name, mass ? "Mass" : "Hydrocarbon", resource.Position, resource.Orientation, fields);
            }

            foreach (PathNode node in set.PathNodes)
            {
                // a node without links gets an empty list, as most maps write it, never none
                List<(string, LuaData)> fields =
                [
                    ("graph", new LuaData.String("Default" + node.Layer)),
                    ("adjacentTo", new LuaData.String(string.Join(' ', node.AdjacentTo))),
                ];
                yield return Marker(node.Name, PathTypes.First(p => p.Layer == node.Layer).Type, node.Position, node.Orientation, fields);
            }

            foreach (AiMarker marker in set.AiMarkers)
            {
                yield return Marker(marker.Name, marker.Type, marker.Position, marker.Orientation, []);
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
                yield return Marker(weather.Name, "Weather Definition", weather.Position, weather.Orientation, fields);
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
                yield return Marker(generator.Name, "Weather Generator", generator.Position, generator.Orientation, fields);
            }
        }

        /// <summary>
        /// A marker with its fields and the decoration of its type.
        /// </summary>
        private static MapMarker Marker(string name, string type, Vector3 position, Vector3 orientation, IEnumerable<(string Key, LuaData Value)> fields)
        {
            (string color, bool hint) = Decorations.GetValueOrDefault(type, Decorations["Blank Marker"]);
            Dictionary<string, LuaData> table = new Dictionary<string, LuaData> { ["color"] = new LuaData.String(color) };
            if (hint)
            {
                table["hint"] = new LuaData.Bool(true);
            }
            foreach ((string key, LuaData value) in fields)
            {
                table[key] = value;
            }
            table["prop"] = new LuaData.String(BlankProp);
            return new MapMarker(name, type, position, orientation, new LuaData.Table(table));
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
