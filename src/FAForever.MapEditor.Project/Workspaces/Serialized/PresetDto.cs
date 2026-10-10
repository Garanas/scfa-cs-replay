using System.Text.Json.Nodes;

namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// A preset's file, <c>presets/&lt;kind&gt;/&lt;name&gt;.json</c>: what it is called, its kind,
    /// and the settings it holds, whose shape the feature of that kind owns.
    /// </summary>
    public sealed record PresetDto
    {
        public required int FormatVersion { get; init; }

        /// <summary>
        /// E.g. <c>brush</c>, <c>lighting</c>; the name of its folder.
        /// </summary>
        public required string Kind { get; init; }

        /// <summary>
        /// The name people chose, which may hold characters a file name cannot.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The settings, written by the feature of the kind (a brush's radius, a lighting's colours).
        /// </summary>
        public required JsonObject Settings { get; init; }
    }
}
