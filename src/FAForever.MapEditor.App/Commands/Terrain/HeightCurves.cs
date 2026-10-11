using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// A curve's point as a command takes it (user story T-4): <c>[x, z]</c>, <c>[x, z, height]</c>, or
/// an object <c>{x, z, height?, width?, falloffLeft?, falloffRight?, strength?, sharp?}</c> whose
/// values override the curve's at that point (<see cref="CurvePoint"/>).
/// </summary>
[JsonConverter(typeof(CurvePointArgumentConverter))]
public sealed record CurvePointArgument(float X, float Z, float? Height = null, float? Width = null, float? FalloffLeft = null, float? FalloffRight = null,
    float? Strength = null, bool Sharp = false)
{
    /// <summary>
    /// The point with <paramref name="height"/> when it has none of its own.
    /// </summary>
    public CurvePoint ToPoint(float height) => new CurvePoint(X, Z, Height ?? height, Width, FalloffLeft, FalloffRight, Strength, Sharp);

    /// <summary>
    /// A point as commands and <c>state.heightLayers</c> write it: <c>[x, z, height]</c>, an object
    /// when it has values of its own.
    /// </summary>
    public static JsonNode ToJson(CurvePoint point)
    {
        if (point.IsPlain)
        {
            return new JsonArray(point.X, point.Z, point.Height);
        }
        JsonObject json = new JsonObject { ["x"] = point.X, ["z"] = point.Z, ["height"] = point.Height };
        if (point.Width is float width)
        {
            json["width"] = width;
        }
        if (point.FalloffLeft is float left)
        {
            json["falloffLeft"] = left;
        }
        if (point.FalloffRight is float right)
        {
            json["falloffRight"] = right;
        }
        if (point.Strength is float strength)
        {
            json["strength"] = strength;
        }
        if (point.Sharp)
        {
            json["sharp"] = true;
        }
        return json;
    }
}

/// <summary>
/// Reads a <see cref="CurvePointArgument"/> from an array or an object.
/// </summary>
public sealed class CurvePointArgumentConverter : JsonConverter<CurvePointArgument>
{
    public override CurvePointArgument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;
        if (element.ValueKind == JsonValueKind.Array)
        {
            float[] values = [.. element.EnumerateArray().Select(Number)];
            return values.Length is 2 or 3
                ? new CurvePointArgument(values[0], values[1], values.Length > 2 ? values[2] : null)
                : throw new JsonException("A curve's point is [x, z], [x, z, height] or {x, z, height?, width?, falloffLeft?, falloffRight?, strength?, sharp?}");
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A curve's point is [x, z], [x, z, height] or {x, z, ...}");
        }
        float? x = null, z = null, height = null, width = null, left = null, right = null, strength = null;
        bool sharp = false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }
            switch (property.Name.ToLowerInvariant())
            {
                case "x": x = Number(property.Value); break;
                case "z": z = Number(property.Value); break;
                case "height": height = Number(property.Value); break;
                case "width": width = Number(property.Value); break;
                case "falloffleft": left = Number(property.Value); break;
                case "falloffright": right = Number(property.Value); break;
                case "strength": strength = Number(property.Value); break;
                case "sharp": sharp = property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.String && bool.Parse(property.Value.GetString()!); break;
                default: throw new JsonException($"A curve's point has no '{property.Name}': x, z, height, width, falloffLeft, falloffRight, strength, sharp");
            }
        }
        return x is float px && z is float pz
            ? new CurvePointArgument(px, pz, height, width, left, right, strength, sharp)
            : throw new JsonException("A curve's point needs x and z");
    }

    public override void Write(Utf8JsonWriter writer, CurvePointArgument value, JsonSerializerOptions options) =>
        CurvePointArgument.ToJson(value.ToPoint(value.Height ?? 0)).WriteTo(writer, options);

    private static float Number(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? float.Parse(element.GetString()!, CultureInfo.InvariantCulture)
        : element.GetSingle();
}

/// <summary>
/// The curve settings <c>terrain.addCurve</c> and <c>terrain.setCurve</c> take besides profile,
/// width, falloff, height and closed (user story T-4, wave 17).
/// </summary>
public interface ICurveFields
{
    /// <summary>The right falloff in ogrids; negative: the same as the falloff.</summary>
    float? FalloffRight { get; }

    /// <summary><c>smooth</c>, <c>linear</c>, <c>easeIn</c>, <c>easeOut</c>, <c>terraced</c>, or <c>default</c> (the profile's).</summary>
    string? Shape { get; }

    int? TerraceSteps { get; }

    float? CliffFace { get; }

    /// <summary><c>both</c>, <c>cutOnly</c>, <c>fillOnly</c>.</summary>
    string? RoadMode { get; }

    float? Camber { get; }

    float? NoiseAmplitude { get; }

    float? NoiseScale { get; }

    int? NoiseSeed { get; }

    /// <summary><c>[min, max]</c> in game units.</summary>
    float[]? HeightVariation { get; }

    /// <summary><c>[min, max]</c> in ogrids.</summary>
    float[]? WidthVariation { get; }

    float? VariationScale { get; }

    bool? LineOnly { get; }
}

/// <summary>
/// Reading the curve settings of the curve commands.
/// </summary>
public static class HeightCurveArguments
{
    /// <summary>
    /// The curve with the settings given; the others as they are.
    /// </summary>
    /// <exception cref="ArgumentException">A setting is out of its range or not one of its names.</exception>
    public static HeightCurve Apply(HeightCurve curve, ICurveFields fields)
    {
        if (fields.TerraceSteps is < 1 || fields.CliffFace is < 0 || fields.NoiseScale is <= 0 || fields.VariationScale is <= 0)
        {
            throw new ArgumentException("A curve's terrace steps are 1 or more, its cliff face 0 or more ogrids, its noise and variation scales above 0");
        }
        return curve with
        {
            FalloffRight = fields.FalloffRight is float right ? right < 0 ? null : right : curve.FalloffRight,
            FalloffShape = fields.Shape is string shape ? ParseShape(shape) : curve.FalloffShape,
            TerraceSteps = fields.TerraceSteps ?? curve.TerraceSteps,
            CliffFace = fields.CliffFace ?? curve.CliffFace,
            RoadMode = fields.RoadMode is string mode ? Parse<CurveRoadMode>(mode, "A road's mode") : curve.RoadMode,
            Camber = fields.Camber ?? curve.Camber,
            NoiseAmplitude = fields.NoiseAmplitude ?? curve.NoiseAmplitude,
            NoiseScale = fields.NoiseScale ?? curve.NoiseScale,
            NoiseSeed = fields.NoiseSeed ?? curve.NoiseSeed,
            HeightVariation = fields.HeightVariation is float[] height ? RangeOf(height) : curve.HeightVariation,
            WidthVariation = fields.WidthVariation is float[] width ? RangeOf(width) : curve.WidthVariation,
            VariationScale = fields.VariationScale ?? curve.VariationScale,
            LineOnly = fields.LineOnly ?? curve.LineOnly,
        };
    }

    /// <summary>
    /// The names of the settings given, for the history and merging steps.
    /// </summary>
    public static (string Name, object? Value)[] Given(ICurveFields fields) =>
    [
        ("falloffRight", fields.FalloffRight), ("shape", fields.Shape), ("terraceSteps", fields.TerraceSteps), ("cliffFace", fields.CliffFace),
        ("roadMode", fields.RoadMode), ("camber", fields.Camber), ("noiseAmplitude", fields.NoiseAmplitude), ("noiseScale", fields.NoiseScale),
        ("noiseSeed", fields.NoiseSeed), ("heightVariation", fields.HeightVariation), ("widthVariation", fields.WidthVariation),
        ("variationScale", fields.VariationScale), ("lineOnly", fields.LineOnly),
    ];

    public static CurveFalloffShape? ParseShape(string text) =>
        text.Trim().Equals("default", StringComparison.OrdinalIgnoreCase) ? null : Parse<CurveFalloffShape>(text, "A curve's shape");

    public static T Parse<T>(string text, string what) where T : struct, Enum =>
        Enum.TryParse(text.Trim(), ignoreCase: true, out T value) && Enum.IsDefined(value)
            ? value
            : throw new ArgumentException($"{what} is {string.Join(", ", Enum.GetNames<T>())}, not '{text}'");

    private static CurveRange RangeOf(float[] range) => range is [float min, float max]
        ? new CurveRange(Math.Min(min, max), Math.Max(min, max))
        : throw new ArgumentException("A variation is [min, max]");

    /// <summary>
    /// The points given: each with its own height, else <paramref name="height"/>; a road's without
    /// one lies on the terrain, a river's bed lies <paramref name="height"/> below the water.
    /// </summary>
    public static CurvePoint[] PointsOf(MapDocument document, IEnumerable<CurvePointArgument> points, CurveProfile profile, float height)
    {
        CurvePoint[] made = [.. points.Select(point => point.ToPoint(point.Height ?? DefaultHeight(document, point, profile, height)))];
        if (made.Any(point => point.Strength is < 0 or > 1 || point.Width is < 0 || point.FalloffLeft is < 0 || point.FalloffRight is < 0))
        {
            throw new ArgumentException("A point's width and falloffs are 0 or more ogrids, its strength 0 to 1");
        }
        return made;
    }

    private static float DefaultHeight(MapDocument document, CurvePointArgument point, CurveProfile profile, float height) => profile switch
    {
        CurveProfile.Road => document.Terrain.Heightmap.HeightAt(point.X, point.Z),
        CurveProfile.River => RiverBase(document) - height,
        _ => height,
    };

    /// <summary>
    /// What a river's depth is measured from: the water's elevation, else 0.
    /// </summary>
    public static float RiverBase(MapDocument document) => document.Water.HasWater ? document.Water.Elevation : 0;

    /// <summary>
    /// The grades of a curve for <c>state.heightLayers</c>.
    /// </summary>
    public static JsonArray GradesOf(HeightCurve curve) => new JsonArray([.. HeightCurves.Grades(curve).Select(grade => (JsonNode)new JsonObject
    {
        ["from"] = grade.From,
        ["to"] = grade.To,
        ["rise"] = MathF.Round(grade.Rise, 3),
        ["run"] = MathF.Round(grade.Run, 3),
        ["percent"] = MathF.Round(grade.Percent, 2),
        ["degrees"] = MathF.Round(grade.Degrees, 2),
        ["steepestPercent"] = MathF.Round(grade.SteepestPercent, 2),
        ["steepestAt"] = new JsonArray(MathF.Round(grade.SteepestX, 2), MathF.Round(grade.SteepestZ, 2)),
    })]);
}

/// <summary>
/// Turns a curve round (user story T-4), one step of undo: its points in the other order, the left
/// and right falloffs swapped so it keeps its look; a cliff's high side flips.
/// </summary>
/// <param name="Layer">Left out, the chosen one.</param>
[EditorCommand("terrain.reverseCurve", "Reverse a curve", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ReverseHeightCurve(string? Layer = null) : IRequest;

public sealed class ReverseHeightCurveHandler(MainViewModel model) : IRequestHandler<ReverseHeightCurve>
{
    public Task Handle(ReverseHeightCurve request, CancellationToken cancellationToken)
    {
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        HeightCurve curve = layer.Curve ?? throw new ArgumentException($"The height layer {layer.Name} is no curve");
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Reverse the curve {layer.Name}", d => HeightLayerEdits.ReverseCurve(d, layer.Name));
        model.Status = curve.Profile == CurveProfile.Cliff
            ? $"Reversed the curve {layer.Name}: the cliff raises its other side"
            : $"Reversed the curve {layer.Name}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets one point of a curve (user story T-4), one step of undo; typing the same values of the same
/// point again is one step. A value left out stays; <paramref name="Clear"/> gives values back to
/// the curve.
/// </summary>
/// <param name="Layer">Left out, the chosen one.</param>
/// <param name="Index">The point, from 0.</param>
/// <param name="Height">The point's height (a road's or river bed's) or rise, in game units.</param>
/// <param name="Width">Its own width in ogrids.</param>
/// <param name="Strength">How much of the profile there is there, 0 to 1.</param>
/// <param name="Sharp">Whether the path makes a corner there.</param>
/// <param name="Clear">Values to give back to the curve, separated by commas: <c>width</c>,
/// <c>falloffLeft</c>, <c>falloffRight</c>, <c>strength</c>, or <c>all</c>.</param>
[EditorCommand("terrain.setCurvePoint", "Change a point of a curve", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetHeightCurvePoint(int Index, string? Layer = null, float? X = null, float? Z = null, float? Height = null, float? Width = null,
    float? FalloffLeft = null, float? FalloffRight = null, float? Strength = null, bool? Sharp = null, string? Clear = null) : IRequest;

public sealed class SetHeightCurvePointHandler(MainViewModel model) : IRequestHandler<SetHeightCurvePoint>
{
    private static readonly string[] Clearable = ["width", "falloffLeft", "falloffRight", "strength"];

    public Task Handle(SetHeightCurvePoint request, CancellationToken cancellationToken)
    {
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        HeightCurve curve = layer.Curve ?? throw new ArgumentException($"The height layer {layer.Name} is no curve");
        if (request.Index < 0 || request.Index >= curve.Points.Length)
        {
            throw new ArgumentException($"The curve {layer.Name} has points 0 to {curve.Points.Length - 1}, not {request.Index}");
        }
        string[] clear = [.. (request.Clear ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        if (clear.Contains("all", StringComparer.OrdinalIgnoreCase))
        {
            clear = Clearable;
        }
        if (clear.FirstOrDefault(name => !Clearable.Contains(name, StringComparer.OrdinalIgnoreCase)) is string unknown)
        {
            throw new ArgumentException($"A point's values to clear are {string.Join(", ", Clearable)} or all, not '{unknown}'");
        }
        bool Cleared(string name) => clear.Contains(name, StringComparer.OrdinalIgnoreCase);
        CurvePoint point = curve.Points[request.Index];
        CurvePoint changed = point with
        {
            X = request.X ?? point.X,
            Z = request.Z ?? point.Z,
            Height = request.Height ?? point.Height,
            Width = Cleared("width") ? null : request.Width ?? point.Width,
            FalloffLeft = Cleared("falloffLeft") ? null : request.FalloffLeft ?? point.FalloffLeft,
            FalloffRight = Cleared("falloffRight") ? null : request.FalloffRight ?? point.FalloffRight,
            Strength = Cleared("strength") ? null : request.Strength ?? point.Strength,
            Sharp = request.Sharp ?? point.Sharp,
        };
        string fields = HeightLayerSettings.Fields(("x", request.X), ("z", request.Z), ("height", request.Height), ("width", request.Width),
            ("falloffLeft", request.FalloffLeft), ("falloffRight", request.FalloffRight), ("strength", request.Strength), ("sharp", request.Sharp), ("clear", request.Clear));
        string description = $"Curve {layer.Name}: point {request.Index} {fields}";
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap(description, d => HeightLayerEdits.SetCurvePoint(d, layer.Name, request.Index, changed), $"terrain.setCurvePoint:{layer.Name}:{request.Index}:{fields}");
        model.Status = description;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Evens out the grade of a curve between two of its points (user story T-4, a ramp), one step of
/// undo: the points between get heights on a straight line by the distance along the spline.
/// </summary>
/// <param name="From">A point, from 0.</param>
/// <param name="To">Another point.</param>
/// <param name="Layer">Left out, the chosen one.</param>
[EditorCommand("terrain.evenGrade", "Even out a curve's grade", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record EvenHeightCurveGrade(int From, int To, string? Layer = null) : IRequest;

public sealed class EvenHeightCurveGradeHandler(MainViewModel model) : IRequestHandler<EvenHeightCurveGrade>
{
    public Task Handle(EvenHeightCurveGrade request, CancellationToken cancellationToken)
    {
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        _ = layer.Curve ?? throw new ArgumentException($"The height layer {layer.Name} is no curve");
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Curve {layer.Name}: even grade from point {request.From} to {request.To}", d => HeightLayerEdits.EvenGrade(d, layer.Name, request.From, request.To));
        HeightCurve graded = HeightLayerEdits.CurveOf(model.ShownDocument!, layer.Name);
        CurveGrade? grade = HeightCurves.Grades(graded).FirstOrDefault(g => g.From == Math.Min(request.From, request.To));
        model.Status = string.Create(CultureInfo.InvariantCulture,
            $"Curve {layer.Name}: an even grade from point {Math.Min(request.From, request.To)} to {Math.Max(request.From, request.To)}{(grade is null ? "" : $", {grade.Percent:0.#} %")}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts points of a road or river on the ground (user story T-4), one step of undo: each gets the
/// height the layers below the curve's layer make at its place. Once; the points do not follow the
/// ground afterwards.
/// </summary>
/// <param name="Layer">Left out, the chosen one.</param>
/// <param name="Points"><c>ends</c> (left out), <c>all</c>, or the points' indices as <c>[0, 3]</c>.</param>
[EditorCommand("terrain.snapCurve", "Put a curve's points on the ground", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SnapHeightCurve(string? Layer = null, JsonNode? Points = null) : IRequest;

public sealed class SnapHeightCurveHandler(MainViewModel model) : IRequestHandler<SnapHeightCurve>
{
    public Task Handle(SnapHeightCurve request, CancellationToken cancellationToken)
    {
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        HeightCurve curve = layer.Curve ?? throw new ArgumentException($"The height layer {layer.Name} is no curve");
        int last = curve.Points.Length - 1;
        int[]? indices = request.Points switch
        {
            null => [0, last],
            JsonValue value when value.TryGetValue(out string? text) => text.Trim().ToLowerInvariant() switch
            {
                "ends" => [0, last],
                "all" => null,
                _ => throw new ArgumentException($"A snap's points are ends, all or a list of indices, not '{text}'"),
            },
            JsonArray array => [.. array.Select(item => item?.GetValue<int>() ?? throw new ArgumentException("A snap's points are indices"))],
            _ => throw new ArgumentException("A snap's points are ends, all or a list of indices"),
        };
        if (last < 0)
        {
            throw new ArgumentException($"The curve {layer.Name} has no points");
        }
        using Performance.Span span = Performance.Measure("model.height-layers");
        string which = indices is null ? "every point" : indices.Distinct().Count() == 1 ? $"point {indices[0]}" : $"points {string.Join(", ", indices.Distinct())}";
        model.EditMap($"Curve {layer.Name}: {which} on the ground", d => HeightLayerEdits.SnapCurve(d, layer.Name, indices?.Distinct().ToArray()));
        model.Status = $"Put {which} of the curve {layer.Name} on the ground";
        return Task.CompletedTask;
    }
}
