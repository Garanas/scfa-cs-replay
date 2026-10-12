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
    /// How large a symbol or a label is made by the tools' choices (user story N-4); the sketch itself keeps the
    /// size in ogrids, so any number can be given to a command.
    /// </summary>
    public enum SketchSize
    {
        Small,
        Medium,
        Large,
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
        /// The width of the box of a symbol of a size, in ogrids.
        /// </summary>
        public static float SymbolOgrids(SketchSize size) => size switch
        {
            SketchSize.Small => 8,
            SketchSize.Medium => 14,
            SketchSize.Large => 24,
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };

        /// <summary>
        /// The height of the letters of a label of a size, in ogrids.
        /// </summary>
        public static float LabelOgrids(SketchSize size) => size switch
        {
            SketchSize.Small => 10,
            SketchSize.Medium => 16,
            SketchSize.Large => 26,
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };

        /// <summary>
        /// The least and the greatest size of a symbol or a label, in ogrids.
        /// </summary>
        public const float MinimumOgrids = 1, MaximumOgrids = 200;

        /// <summary>
        /// The size by its name (<c>large</c>).
        /// </summary>
        /// <exception cref="ArgumentException">No such size.</exception>
        public static SketchSize ParseSize(string text) =>
            Enum.TryParse(text.Trim(), ignoreCase: true, out SketchSize size) && Enum.IsDefined(size)
                ? size
                : throw new ArgumentException($"There is no size '{text}': {string.Join(", ", Enum.GetNames<SketchSize>().Select(name => name.ToLowerInvariant()))}, or a number of ogrids");

        /// <summary>
        /// The symbol by its name (<c>hydrocarbon</c>).
        /// </summary>
        /// <exception cref="ArgumentException">No such symbol.</exception>
        public static SketchSymbolKind ParseSymbol(string text)
        {
            text = text.Trim();
            string name = text.ToLowerInvariant() switch
            {
                "hydro" => nameof(SketchSymbolKind.Hydrocarbon),
                "question mark" or "?" => nameof(SketchSymbolKind.Question),
                "x" => nameof(SketchSymbolKind.Cross),
                _ => text,
            };
            return Enum.TryParse(name, ignoreCase: true, out SketchSymbolKind kind) && Enum.IsDefined(kind)
                ? kind
                : throw new ArgumentException($"There is no symbol '{text}': {string.Join(", ", Enum.GetNames<SketchSymbolKind>().Select(n => n.ToLowerInvariant()))}");
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
    /// The six symbols a sheet can hold (user story N-4): what a map maker marks on a draft besides lines.
    /// </summary>
    public enum SketchSymbolKind
    {
        Spawn,
        Mass,
        Hydrocarbon,
        Arrow,
        Cross,
        Question,
    }

    /// <summary>
    /// A symbol stamped on a sheet (user story N-4): its kind, its centre in ogrids, its colour, its
    /// size (the width of the box it fits in, in ogrids) and, for the arrow, which way it points.
    /// </summary>
    /// <param name="Turn">Degrees, 0 pointing to +x and growing clockwise on the top view (towards +z); only the arrow has a direction.</param>
    public sealed record SketchSymbol(SketchSymbolKind Kind, Vector2 Place, SketchColour Colour, float Size, float Turn = 0);

    /// <summary>
    /// A label written on a sheet (user story N-4): a line of text centred on a place in ogrids, in a
    /// colour, <paramref name="Size"/> ogrids high.
    /// </summary>
    public sealed record SketchLabel(string Text, Vector2 Place, SketchColour Colour, float Size);

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
        /// The symbols stamped on the sheet (user story N-4), in the order they were stamped.
        /// </summary>
        public ImmutableArray<SketchSymbol> Symbols { get; init; } = [];

        /// <summary>
        /// The labels written on the sheet (user story N-4), in the order they were written.
        /// </summary>
        public ImmutableArray<SketchLabel> Labels { get; init; } = [];

        /// <summary>
        /// Strokes, symbols and labels together.
        /// </summary>
        public int ItemCount => Strokes.Length + Symbols.Length + Labels.Length;

        /// <summary>
        /// Whether the sheet holds nothing to draw.
        /// </summary>
        public bool IsBlank => ItemCount == 0;

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
        /// The colours some stroke, symbol or label of the sheet uses, in the legend's order.
        /// </summary>
        public IEnumerable<SketchColour> ColoursUsed => Strokes.Select(stroke => stroke.Colour)
            .Concat(Symbols.Select(symbol => symbol.Colour)).Concat(Labels.Select(label => label.Colour)).Distinct().Order();
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
        /// The strokes, symbols and labels of every sheet.
        /// </summary>
        public int ItemCount => Sheets.Sum(sheet => sheet.ItemCount);

        /// <summary>
        /// The sheet of a name: the exact name first, then without regard to case; null when there is none.
        /// </summary>
        public SketchSheet? Find(string name) =>
            Sheets.FirstOrDefault(sheet => sheet.Name == name) ?? Sheets.FirstOrDefault(sheet => sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
