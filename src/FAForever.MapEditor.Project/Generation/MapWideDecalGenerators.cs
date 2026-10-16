using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The map-wide normals decal as the editor previews it (<see cref="MapWideDecalImages.Normals"/>),
    /// at the export's 16 pixels an ogrid but at most <see cref="PreviewSide"/> on a side; the export
    /// makes it again at full size (<see cref="MapWideDecalsExport"/>). Its result is a <see cref="DecodedTexture"/>.
    /// </summary>
    public sealed class NormalDecalGenerator : IGenerator
    {
        public const string GeneratorId = "normalDecal";

        /// <summary>
        /// The largest side of a preview.
        /// </summary>
        public const int PreviewSide = 2048;

        public string Id => GeneratorId;

        public string Title => "Normal decal";

        public string Story => "D-1";

        public IReadOnlyList<GeneratedInput> Inputs { get; } = [GeneratedInput.Heightmap];

        public object Generate(MapDocument document) => Generate(document, CancellationToken.None);

        public object Generate(MapDocument document, CancellationToken cancel) =>
            MapWideDecalsExport.GenerateNormals(document, largest: PreviewSide, cancel: cancel);
    }

    /// <summary>
    /// The map-wide shadow decal as the editor previews it (<see cref="MapWideDecalImages.Shadow"/>),
    /// at the export's 4 pixels an ogrid but at most <see cref="NormalDecalGenerator.PreviewSide"/> on
    /// a side. Its result is a <see cref="DecodedTexture"/>.
    /// </summary>
    public sealed class ShadowDecalGenerator : IGenerator
    {
        public const string GeneratorId = "shadowDecal";

        /// <summary>
        /// The shadow decal's settings; the sun matters only while they cast its shadows.
        /// </summary>
        public static GeneratedInput Settings { get; } = new GeneratedInput("shadow decal settings", document => document.WideDecals.ShadowSettings);

        public static GeneratedInput Sun { get; } = new GeneratedInput("sun", document => document.WideDecals.ShadowSettings.SunShadows ? document.Lighting.SunDirection : null);

        public string Id => GeneratorId;

        public string Title => "Shadow decal";

        public string Story => "D-1";

        public IReadOnlyList<GeneratedInput> Inputs { get; } = [GeneratedInput.Heightmap, Settings, Sun];

        public object Generate(MapDocument document) => Generate(document, CancellationToken.None);

        public object Generate(MapDocument document, CancellationToken cancel) =>
            MapWideDecalsExport.GenerateShadow(document, largest: NormalDecalGenerator.PreviewSide, cancel: cancel);
    }
}
