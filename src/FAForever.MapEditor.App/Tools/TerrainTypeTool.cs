using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A terrain type of the game as the terrain types tool offers it, with its colour in the top view.
/// </summary>
/// <param name="Colour">0xAARRGGBB (<see cref="TerrainTypeColours"/>).</param>
/// <param name="Count">How many ogrids of the map have it, in the legend.</param>
public sealed record TerrainTypeChoice(TerrainType Type, uint Colour, int Count = 0)
{
    public int Code => Type.TypeCode;

    /// <summary>
    /// The name, its style and whether it blocks: <c>Rocky02 (Evergreen)</c>.
    /// </summary>
    public string Title => $"{Type.Name}{(Type.Style is string style && style != Type.Name ? $" ({style})" : "")}{(Type.Blocking ? ", blocks" : "")}";

    public string Description => $"{Type.TypeCode}: {Type.Description}";

    /// <summary>
    /// How many ogrids have it, for the legend.
    /// </summary>
    public string CountText => $"{Type.Name} {Count}";

    public override string ToString() => Title;
}

/// <summary>
/// Paints the terrain type of each ogrid (user story T-6), the game's types (<see cref="TerrainTypes"/>)
/// that decide footsteps, effects and, for a few, whether units can pass. It sets the chosen type
/// where the brush works at least half, without blending. While it is active the top view shows
/// every ogrid's type in its colour; the tool options hold the legend of the types the map uses.
/// </summary>
public sealed partial class TerrainTypeTool : GridPaintTool
{
    /// <summary>
    /// The most types the legend lists, the most used first.
    /// </summary>
    public const int LegendSize = 8;

    private TiledBuffer<byte>? _counted;

    public TerrainTypeTool(MainViewModel model) : base(model)
    {
        Types = [.. TerrainTypes.All.Select(type => new TerrainTypeChoice(type, TerrainTypeColours.Of(type.TypeCode)))];
        Chosen = Types[0];
        Falloff = BrushFalloff.Sharp;
        model.DocumentChanged += (_, _) => CountTypes();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTool))
            {
                CountTypes();
            }
        };
    }

    public override string PresetName => "types";

    /// <summary>
    /// Every type of the game, in the order of its file.
    /// </summary>
    public IReadOnlyList<TerrainTypeChoice> Types { get; }

    /// <summary>
    /// The type it paints.
    /// </summary>
    [ObservableProperty]
    public partial TerrainTypeChoice Chosen { get; set; }

    /// <summary>
    /// The types the map uses, the most used first, with how many ogrids have each: the legend.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TerrainTypeChoice> Legend { get; private set; } = [];

    public override PaintTarget? Target => Model.HasMap ? PaintTarget.TerrainTypes : null;

    public override GridOverlay? Shown => Model.HasMap ? GridOverlay.TerrainTypes : null;

    protected override bool SetsCodes => true;

    protected override byte Code => (byte)Chosen.Code;

    protected override string Describe(PaintTarget target) => $"Paint terrain type {Chosen.Type.Name}";

    /// <summary>
    /// Paints the type with this code or name (<c>152</c>, <c>Rocky02</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The game has no such type.</exception>
    public void Choose(string type) =>
        Chosen = Types.FirstOrDefault(choice => choice.Type.Name.Equals(type, StringComparison.OrdinalIgnoreCase) || choice.Code.ToString(System.Globalization.CultureInfo.InvariantCulture) == type)
            ?? throw new ArgumentException($"The game has no terrain type '{type}'");

    /// <summary>
    /// Counts the types of the shown document again when they changed while the tool is active:
    /// after a stroke, an undo, a reload. A 4096 by 4096 map has 16.8 million ogrids, so not during
    /// a stroke.
    /// </summary>
    private void CountTypes()
    {
        if (Model.ActiveTool?.Tool != this)
        {
            return;
        }
        TiledBuffer<byte>? types = Model.StrokeInProgress is null ? Model.ShownDocument?.Terrain.Types : _counted;
        if (ReferenceEquals(types, _counted))
        {
            return;
        }
        _counted = types;
        if (types is null)
        {
            Legend = [];
            return;
        }
        using Diagnostics.Performance.Span measure = Diagnostics.Performance.Measure("tool.count-types");
        int[] counts = new int[256];
        foreach (byte code in types.ToArray())
        {
            counts[code]++;
        }
        Legend = [.. Enumerable.Range(0, 256)
            .Where(code => counts[code] > 0)
            .OrderByDescending(code => counts[code])
            .Take(LegendSize)
            .Select(code => new TerrainTypeChoice(
                TerrainTypes.All.FirstOrDefault(type => type.TypeCode == code) ?? new TerrainType(code, $"Code {code}", null, "Not a terrain type of the game", false),
                TerrainTypeColours.Of(code), counts[code]))];
    }
}
