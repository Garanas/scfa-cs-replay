using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The game's weather (user story R-1): the map styles a weather definition chooses from, the
    /// cloud types of each style, and the settings a new weather marker starts with. Static data
    /// copied from the FA repository: the styles and types from <c>lua/weatherdefinitions.lua</c>
    /// (<c>MapStyleList</c>, <c>MapWeatherList</c>), the defaults from <c>lua/MarkerTemplates.lua</c>
    /// (<c>Weather Definition</c>, <c>Weather Generator</c>), which the FAF map editor uses too.
    /// <c>lua/weather.lua</c> reads the markers: the first definition decides the weather.
    /// </summary>
    public static class WeatherStyles
    {
        /// <summary>
        /// What a type of a definition or a generator's forced type is when it has none.
        /// </summary>
        public const string None = "None";

        /// <summary>
        /// The cirrus layers every style has.
        /// </summary>
        private static readonly string[] Cirrus =
        [
            "CirrusSparse256", "CirrusMedium256", "CirrusHeavy256",
            "CirrusSparse512", "CirrusMedium512", "CirrusHeavy512",
            "CirrusSparse1024", "CirrusMedium1024", "CirrusHeavy1024",
            "CirrusSparse4096", "CirrusMedium4096", "CirrusHeavy4096",
        ];

        /// <summary>
        /// The map styles in the game's order, each with its cloud types (its own first, then the cirrus layers).
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Styles { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Desert"] = ["LightStratus", .. Cirrus],
            ["Evergreen"] = ["CumulusClouds", "StormClouds", "RainClouds", .. Cirrus],
            ["Geothermal"] = [.. Cirrus],
            ["Lava"] = [.. Cirrus],
            ["RedRock"] = ["LightStratus", .. Cirrus],
            ["Tropical"] = ["LightStratus", .. Cirrus],
            ["Tundra"] = ["SnowClouds", "WhiteThickClouds", "WhitePatchyClouds", .. Cirrus],
        };

        /// <summary>
        /// The styles' names, in the game's order.
        /// </summary>
        public static IReadOnlyList<string> StyleNames { get; } = ["Desert", "Evergreen", "Geothermal", "Lava", "RedRock", "Tropical", "Tundra"];

        /// <summary>
        /// Every cloud type of any style, with <see cref="None"/> first: what a generator may force.
        /// </summary>
        public static IReadOnlyList<string> AllTypes { get; } = [None, .. StyleNames.SelectMany(style => Styles[style]).Distinct()];

        /// <summary>
        /// The types a definition of a style may hold, with <see cref="None"/> first; an unknown
        /// style offers every type.
        /// </summary>
        public static IReadOnlyList<string> TypesOf(string style) =>
            Styles.TryGetValue(style, out IReadOnlyList<string>? types) ? [None, .. types] : AllTypes;

        /// <summary>
        /// A new weather definition as the game's marker template makes it: Tundra, with snow,
        /// thick and patchy white clouds and none, drifting along +x.
        /// </summary>
        public static WeatherDefinition NewDefinition(string name, Vector3 position) => new WeatherDefinition
        {
            Id = 0,
            Name = name,
            Position = position,
            MapStyle = "Tundra",
            DriftDirection = new Vector3(1, 0, 0),
            Types =
            [
                new WeatherChance { Type = "SnowClouds", Chance = 0.3 },
                new WeatherChance { Type = "WhiteThickClouds", Chance = 0.3 },
                new WeatherChance { Type = "WhitePatchyClouds", Chance = 0.3 },
                new WeatherChance { Type = None, Chance = 0.1 },
            ],
        };

        /// <summary>
        /// A new weather generator as the game's marker template makes it.
        /// </summary>
        public static WeatherGenerator NewGenerator(string name, Vector3 position) => new WeatherGenerator
        {
            Id = 0,
            Name = name,
            Position = position,
            ForceType = None,
            SpawnChance = 1,
            CloudCount = 10,
            CloudCountRange = 0,
            CloudEmitterScale = 1,
            CloudEmitterScaleRange = 0,
            CloudHeight = 180,
            CloudHeightRange = 15,
            CloudSpread = 150,
        };
    }
}
