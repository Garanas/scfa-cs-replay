using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The six pencils of the sketch (user story N-1). A colour means nothing to the editor: each
    /// sheet's legend (<see cref="SketchSheet.Legend"/>) says what the map maker meant by it.
    /// </summary>
    public enum SketchColour
    {
        Red,
        Yellow,
        Green,
        Blue,
        White,
        Black,
    }

    /// <summary>
    /// How thick a stroke is drawn: a number of pixels on screen at every zoom, as the grid's lines are.
    /// </summary>
    public enum SketchWidth
    {
        Thin,
        Medium,
        Thick,
    }

    /// <summary>
    /// The fixed values of the sketch's colours and widths, the same in both themes: they are map
    /// content, like a marker's colour.
    /// </summary>
    public static class SketchLook
    {
        /// <summary>
        /// How many colours there are, and how many texts a legend holds.
        /// </summary>
        public const int ColourCount = 6;

        /// <summary>
        /// The colour as <c>0xAARRGGBB</c>.
        /// </summary>
        public static uint Argb(SketchColour colour) => colour switch
        {
            SketchColour.Red => 0xFFFF5A4E,
            SketchColour.Yellow => 0xFFFFD23F,
            SketchColour.Green => 0xFF5FD47E,
            SketchColour.Blue => 0xFF4AA8FF,
            SketchColour.White => 0xFFF4F1EA,
            SketchColour.Black => 0xFF161616,
            _ => throw new ArgumentOutOfRangeException(nameof(colour)),
        };

        /// <summary>
        /// The width of a stroke on screen, in pixels.
        /// </summary>
        public static float Pixels(SketchWidth width) => width switch
        {
            SketchWidth.Thin => 2,
            SketchWidth.Medium => 4,
            SketchWidth.Thick => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(width)),
        };

        /// <summary>
        /// The colour by its name (<c>red</c>), its number from 1 to 6 (the pencil's keys), or its
        /// place from 0 as an enum value.
        /// </summary>
        /// <exception cref="ArgumentException">No such colour.</exception>
        public static SketchColour ParseColour(string text)
        {
            text = text.Trim();
            if (int.TryParse(text, out int number) && number is >= 1 and <= ColourCount)
            {
                return (SketchColour)(number - 1);
            }
            return Enum.TryParse(text, ignoreCase: true, out SketchColour colour) && Enum.IsDefined(colour)
                ? colour
                : throw new ArgumentException($"There is no sketch colour '{text}': {string.Join(", ", Enum.GetNames<SketchColour>().Select(name => name.ToLowerInvariant()))}, or 1 to {ColourCount}");
        }

        /// <summary>
        /// The width by its name (<c>medium</c>).
        /// </summary>
        /// <exception cref="ArgumentException">No such width.</exception>
        public static SketchWidth ParseWidth(string text) =>
            Enum.TryParse(text.Trim(), ignoreCase: true, out SketchWidth width) && Enum.IsDefined(width)
                ? width
                : throw new ArgumentException($"There is no stroke width '{text}': {string.Join(", ", Enum.GetNames<SketchWidth>().Select(name => name.ToLowerInvariant()))}");
    }

    /// <summary>
    /// A stroke of the pencil: a line through places of the map, in ogrids, of one colour and width.
    /// A stroke of a single point is a dot.
    /// </summary>
    public sealed record SketchStroke(SketchColour Colour, SketchWidth Width, ImmutableArray<Vector2> Points);

    /// <summary>
    /// A sheet of the sketch (a layer): strokes that are shown or hidden together, with a legend that
    /// says what its colours mean.
    /// </summary>
    public sealed record SketchSheet
    {
        /// <summary>
        /// What its panel and the commands call it: not empty, unique in the sketch without regard to case.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Whether the views draw it.
        /// </summary>
        public bool Shown { get; init; } = true;

        /// <summary>
        /// How opaque it is in the views outside the Sketch mode, from 0 to 1: it guides without
        /// getting in the way. In the Sketch mode it is drawn fully.
        /// </summary>
        public float Opacity { get; init; } = DefaultOpacity;

        /// <summary>
        /// What each colour means, in the order of <see cref="SketchColour"/>; six texts, empty for none.
        /// </summary>
        public ImmutableArray<string> Legend { get; init; } = NoLegend;

        public ImmutableArray<SketchStroke> Strokes { get; init; } = [];

        /// <summary>
        /// The opacity outside the Sketch mode of a new sheet.
        /// </summary>
        public const float DefaultOpacity = 0.45f;

        /// <summary>
        /// A legend that says nothing.
        /// </summary>
        public static ImmutableArray<string> NoLegend { get; } = [.. Enumerable.Repeat("", SketchLook.ColourCount)];

        /// <summary>
        /// What a colour means on this sheet; empty for nothing.
        /// </summary>
        public string LegendOf(SketchColour colour) => Legend.IsDefaultOrEmpty ? "" : Legend[(int)colour];

        /// <summary>
        /// Whether any colour has a text.
        /// </summary>
        public bool HasLegend => !Legend.IsDefaultOrEmpty && Legend.Any(text => text.Length > 0);

        /// <summary>
        /// The colours some stroke of the sheet uses, in the legend's order.
        /// </summary>
        public IEnumerable<SketchColour> ColoursUsed => Strokes.Select(stroke => stroke.Colour).Distinct().Order();
    }

    /// <summary>
    /// The map's sketch (user story N-1): drafts of the map maker's own, kept in the project as
    /// <c>sketch/sketch.json</c> and never exported. A map without sheets has none of it.
    /// </summary>
    public sealed record Sketch
    {
        public static Sketch Empty { get; } = new Sketch();

        public ImmutableArray<SketchSheet> Sheets { get; init; } = [];

        public bool IsEmpty => Sheets.IsDefaultOrEmpty;

        /// <summary>
        /// The strokes of every sheet.
        /// </summary>
        public int StrokeCount => Sheets.Sum(sheet => sheet.Strokes.Length);

        /// <summary>
        /// The sheet of a name: the exact name first, then without regard to case; null when there is none.
        /// </summary>
        public SketchSheet? Find(string name) =>
            Sheets.FirstOrDefault(sheet => sheet.Name == name) ?? Sheets.FirstOrDefault(sheet => sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
