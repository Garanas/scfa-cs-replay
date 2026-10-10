using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// Walkability (user story T-7): the navigational mesh of the map (<see cref="Walkability.Generate"/>),
    /// a <see cref="MapNavigation"/>. It stays beside the map: the game makes its own when the map loads.
    /// </summary>
    public sealed class WalkabilityGenerator : IGenerator
    {
        public const string GeneratorId = "walkability";

        public string Id => GeneratorId;

        public string Title => "Walkability";

        public string Story => "T-7";

        public IReadOnlyList<GeneratedInput> Inputs { get; } =
            [GeneratedInput.Heightmap, GeneratedInput.TerrainTypes, GeneratedInput.WaterLevel, GeneratedInput.DepositsAndAreas, GeneratedInput.MapType, GeneratedInput.WalkabilityMarks];

        public object Generate(MapDocument document) => Walkability.Generate(document);

        public object Generate(MapDocument document, CancellationToken cancel) => Walkability.Generate(document, cancel);
    }

    /// <summary>
    /// The normal map of the terrain (user story S-4, <see cref="TerrainImages.NormalMap"/>), made at
    /// the size of the map's own and put in its place; its texture format stays.
    /// </summary>
    public sealed class NormalMapGenerator : IDocumentGenerator
    {
        public const string GeneratorId = "normalMap";

        public string Id => GeneratorId;

        public string Title => "Normal map";

        public string Story => "S-4";

        public IReadOnlyList<GeneratedInput> Inputs { get; } = [GeneratedInput.Heightmap];

        public object Generate(MapDocument document) =>
            TerrainImages.NormalMap(document.Terrain.Heightmap, document.Terrain.NormalMap.Width, document.Terrain.NormalMap.Height);

        public MapDocument Apply(MapDocument document, object value) =>
            document with { Terrain = document.Terrain with { NormalMap = (TiledBuffer<Color32>)value } };
    }
}
