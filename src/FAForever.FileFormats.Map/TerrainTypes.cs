namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// A terrain type: what the ground of an ogrid is (dirt, vegetation, rock, water, ...). The
    /// map stores one type code per ogrid (<see cref="Scmap.TerrainTypes"/>).
    /// </summary>
    /// <param name="Style">The style key (Desert, Evergreen, ...); absent for the generic types.</param>
    /// <param name="Blocking">Whether units cannot path over it.</param>
    public sealed record TerrainType(int TypeCode, string Name, string? Style, string Description, bool Blocking);

    /// <summary>
    /// The terrain types of the game, copied from <c>lua/TerrainTypes.lua</c> in the FA repository
    /// (commit 24f24b2e77, 2026-04-10) without their effects. Re-copy them when the file changes.
    /// </summary>
    public static class TerrainTypes
    {
        /// <summary>
        /// Every terrain type, in the order of the file.
        /// </summary>
        public static IReadOnlyList<TerrainType> All { get; } =
        [
            new TerrainType(1, "Default", "Default", "Default", false),
            new TerrainType(2, "Dirt01", null, "Default Dirt", false),
            new TerrainType(3, "Dirt02", "Evergreen", "Red/brown/biege dirt, with slight vegetation", false),
            new TerrainType(4, "Dirt03", null, "Brown/gray earth", false),
            new TerrainType(5, "Dirt05", "RedRock", "Red earth", false),
            new TerrainType(6, "Dirt06", "RedRock", "darker red/brown earth", false),
            new TerrainType(7, "Dirt07", "Desert", "White/Beige Desert", false),
            new TerrainType(8, "Dirt08", "Desert", "Beige/Brown Rocky Desert", false),
            new TerrainType(9, "Dirt09", "Evergreen", "Blocking variant of Red/brown/biege dirt, with slight vegetation (Dirt02)", true),
            new TerrainType(40, "Sand01", "Tropical", "White sand", false),
            new TerrainType(41, "Sand02", "Evergreen", "Sand grassy", false),
            new TerrainType(80, "Vegetation01", null, "Default Vegetation", false),
            new TerrainType(81, "Vegetation02", "Evergreen", "Rocky grass", false),
            new TerrainType(82, "Vegetation03", "Evergreen", "Light green olive vegetation", false),
            new TerrainType(83, "Vegetation04", "Evergreen", "Dark Green olive vegetation", false),
            new TerrainType(84, "Vegetation05", "Tropical", "Green olive vegetation", false),
            new TerrainType(150, "Rocky01", null, "Default Rocky", false),
            new TerrainType(151, "Concrete01", null, "Concrete", false),
            new TerrainType(152, "Rocky02", "Evergreen", "Gray rock", false),
            new TerrainType(153, "Rocky03", "Tundra", "Icy Black/Gray Rocky", false),
            new TerrainType(154, "Rocky04", "RedRock", "Red rock", false),
            new TerrainType(155, "Rocky05", "Desert", "Beige dusty rock", false),
            new TerrainType(156, "Rocky06", "Lava", "Light Lava Rock", false),
            new TerrainType(157, "Rocky07", "Lava", "Dark Lava Rock", false),
            new TerrainType(158, "Rocky08", "Evergreen", "Green rock", false),
            new TerrainType(159, "Rocky09", "Tropical", "Green/gray/block rock w/ vegetation", false),
            new TerrainType(160, "Rocky10", "Tropical", "Gray/brown rock, cliffsides", false),
            new TerrainType(161, "Rocky11", "Geothermal", "Geothermal dark green/gray rock", false),
            new TerrainType(162, "Rocky12", "Geothermal", "Geothermal gray rock", false),
            new TerrainType(163, "Rocky13", "Geothermal", "Geothermal light gray/green rock", false),
            new TerrainType(164, "Rocky14", null, "Blue Crystal", false),
            new TerrainType(165, "Rocky15", null, "Cyber Strata", false),
            new TerrainType(190, "TarmacUEF", null, "UEF Tarmac", false),
            new TerrainType(191, "TarmacAeon", null, "Aeon Tarmac", false),
            new TerrainType(192, "TarmacCybran", null, "Cybran Tarmac", false),
            new TerrainType(200, "Snowy01", "Tundra", "Snowy, dark blue, hard ice", false),
            new TerrainType(201, "Snowy02", "Tundra", "Snowy, light blue snow pack", false),
            new TerrainType(202, "Snowy03", "Tundra", "Snowy, high albedo, bright white snow", false),
            new TerrainType(220, "Water01", null, "Default Water", false),
            new TerrainType(221, "Water02", "Evergreen", "Shoreline water", false),
            new TerrainType(222, "Water03", "Evergreen", "Deep Dark Blue Ocean Water", false),
            new TerrainType(223, "Water04", "Evergreen", "Shallow Light Blue Water", false),
            new TerrainType(224, "Water05", "RedRock", "Shoreline red rock water", false),
            new TerrainType(225, "Water06", "RedRock", "Deep default red rock water", false),
            new TerrainType(226, "Water07", "Tropical", "Tropical Shoreline", false),
            new TerrainType(227, "Water08", "Tropical", "Tropical light blue water shallows", false),
            new TerrainType(228, "Water09", "Tropical", "Tropical mid-depth blue", false),
            new TerrainType(229, "Water10", "Tropical", "Tropical deep water, dark blue", false),
            new TerrainType(230, "Lava01", "Lava", "Lava", true),
            new TerrainType(231, "Water11", "Desert", "Desert shoreline water", false),
            new TerrainType(232, "Water12", "Desert", "Desert deep water", false),
            new TerrainType(233, "Water13", "Tundra", "Tundra shoreline water", false),
            new TerrainType(234, "Water14", "Tundra", "Tundra deep water, blue", false),
            new TerrainType(235, "Water15", "Tundra", "Tundra mid-level water, aquamarine", false),
            new TerrainType(236, "Water16", "Lava", "Lava water shoreline", false),
            new TerrainType(237, "Water17", "Lava", "Lava water", false),
            new TerrainType(238, "Water18", "Geothermal", "Geothermal shoreline water", false),
            new TerrainType(239, "Water19", "Geothermal", "Geothermal olive water", false),
            new TerrainType(240, "Water20", "Geothermal", "Geothermal mint water", false),
        ];

        private static readonly Dictionary<int, TerrainType> ByTypeCode = All.ToDictionary(type => type.TypeCode);

        /// <summary>
        /// The terrain type with this code. An unknown code reads as <c>Default</c>, the type the
        /// game returns for a position outside the map.
        /// </summary>
        public static TerrainType Get(int typeCode) => ByTypeCode.TryGetValue(typeCode, out TerrainType? type) ? type : All[0];
    }
}
