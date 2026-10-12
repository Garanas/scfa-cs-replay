using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// <c>sketch/sketch.json</c> (user stories N-1 and N-4): the map maker's sheets of pencil strokes, symbols and labels. Its own
    /// <see cref="Version"/>, not the project's format version: a project without the file has no
    /// sketch.
    /// </summary>
    public sealed record SketchDto
    {
        /// <summary>
        /// 1: strokes only. 2: sheets may also hold symbols and labels; the editor writes 2 only when
        /// some sheet does, so that a sketch of strokes stays readable by an older editor.
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

        /// <summary>
        /// The symbols stamped on the sheet (version 2); left out when it has none.
        /// </summary>
        public IReadOnlyList<SketchSymbolDto>? Symbols { get; init; }

        /// <summary>
        /// The labels written on the sheet (version 2); left out when it has none.
        /// </summary>
        public IReadOnlyList<SketchLabelDto>? Labels { get; init; }
    }

    /// <summary>
    /// A symbol (user story N-4): its kind, its centre as <c>x,z</c> in ogrids, its colour and size in
    /// ogrids and, for an arrow, its turn in degrees.
    /// </summary>
    public sealed record SketchSymbolDto
    {
        public required SketchSymbolKind Kind { get; init; }

        public required string Place { get; init; }

        public SketchColour Colour { get; init; } = SketchColour.Red;

        public float Size { get; init; } = 14;

        public float Turn { get; init; }
    }

    /// <summary>
    /// A label (user story N-4): its text, the centre as <c>x,z</c> in ogrids, its colour and its height in ogrids.
    /// </summary>
    public sealed record SketchLabelDto
    {
        public required string Text { get; init; }

        public required string Place { get; init; }

        public SketchColour Colour { get; init; } = SketchColour.Red;

        public float Size { get; init; } = 16;
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
