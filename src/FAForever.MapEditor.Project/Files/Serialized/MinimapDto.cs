using System.Text.Json.Serialization;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The colours of the minimap.
    /// </summary>
    public sealed record MinimapDto
    {
        public required int ContourInterval { get; init; }

        public required string DeepWaterColor { get; init; }

        public required string ContourColor { get; init; }

        public required string ShoreColor { get; init; }

        public required string LandStartColor { get; init; }

        public required string LandEndColor { get; init; }

        /// <summary>
        /// How far the borders of the map imager are lifted above the terrain's middle height (version
        /// 57 on). Its key is <c>unknown</c>, the name projects have stored it under since before its
        /// meaning was known.
        /// </summary>
        [JsonPropertyName("unknown")]
        public float? ImagerElevationOffset { get; init; }
    }
}
