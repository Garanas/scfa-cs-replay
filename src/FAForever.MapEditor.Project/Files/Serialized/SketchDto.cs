using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// <c>sketch/sketch.json</c> (user story N-1): the map maker's sheets of pencil strokes. Its own
    /// <see cref="Version"/>, not the project's format version: a project without the file has no
    /// sketch.
    /// </summary>
    public sealed record SketchDto
    {
        /// <summary>
        /// 1.
        /// </summary>
        public int Version { get; init; } = 1;

        public IReadOnlyList<SketchSheetDto> Sheets { get; init; } = [];
    }

    /// <summary>
    /// A sheet of the sketch; a value left out has the editor's default.
    /// </summary>
    public sealed record SketchSheetDto
    {
        public required string Name { get; init; }

        public bool Shown { get; init; } = true;

        /// <summary>
        /// From 0 to 1: how opaque the sheet is outside the Sketch mode.
        /// </summary>
        public float Opacity { get; init; } = SketchSheet.DefaultOpacity;

        /// <summary>
        /// What each colour means, in the order red, yellow, green, blue, white, black; left out when none does.
        /// </summary>
        public IReadOnlyList<string>? Legend { get; init; }

        public IReadOnlyList<SketchStrokeDto> Strokes { get; init; } = [];
    }

    /// <summary>
    /// A stroke: a colour, a width, and its places in ogrids as one line of text, <c>x,z x,z ...</c>
    /// with a point and two decimals at most, so that a sheet of many strokes stays readable and small.
    /// </summary>
    public sealed record SketchStrokeDto
    {
        public required SketchColour Colour { get; init; }

        public SketchWidth Width { get; init; } = SketchWidth.Medium;

        public required string Points { get; init; }
    }
}
