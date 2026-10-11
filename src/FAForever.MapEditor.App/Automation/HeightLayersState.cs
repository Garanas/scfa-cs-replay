using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The height layers (user stories T-5, T-4) for automation's <c>state.heightLayers</c>: the
/// stack, lowest first, with each layer's settings, the chosen one, what the sculpt tool would say
/// about it, and the curve tool's settings.
/// </summary>
public static class HeightLayersState
{
    public static JsonObject Describe(MainViewModel model)
    {
        MapDocument? document = model.ShownDocument;
        CurveTool curveTool = model.Tool<CurveTool>();
        return new JsonObject
        {
            ["chosen"] = model.HeightStack.Chosen,
            ["sculptLayer"] = model.Map?.Session.SculptLayer,
            ["sculptProblem"] = document is null ? null : HeightLayers.SculptProblem(document, model.HeightStack.Chosen, model.ShownHeights),
            ["filter"] = new JsonObject { ["use"] = model.HeightStack.UseFilter, ["source"] = model.HeightStack.SourceFilter },
            ["listed"] = new JsonArray([.. model.HeightStack.Items.Select(item => (JsonNode)JsonValue.Create(item.Name))]),
            ["layers"] = new JsonArray([.. (document?.HeightStack.Layers ?? []).Select(layer => (JsonNode)new JsonObject
            {
                ["name"] = layer.Name,
                ["kind"] = KindOf(layer),
                ["sourceKind"] = LayerSources.NameOf(layer.Source),
                ["combine"] = layer.Combine.ToString(),
                ["strength"] = layer.Strength,
                ["mask"] = layer.Clip,
                ["visible"] = layer.Visible,
                ["use"] = document is null ? null : HeightStacks.UseOf(document, layer.Name)?.ToString(),
                ["locked"] = layer.Locked,
                ["source"] = layer.Path,
                ["coverage"] = layer.Coverage is not null,
                ["curve"] = layer.Curve is HeightCurve curve ? new JsonObject
                {
                    ["profile"] = curve.Profile.ToString(),
                    ["width"] = curve.Width,
                    ["falloff"] = curve.Falloff,
                    ["closed"] = curve.Closed,
                    ["mirrored"] = curve.Symmetry is null ? null : $"{curve.Symmetry.Kind} {curve.Symmetry.Count}",
                    ["points"] = new JsonArray([.. curve.Points.Select(point => (JsonNode)new JsonArray(point.X, point.Z, point.Height))]),
                    // wave 17: the points' own values, the curve's other settings and its grades
                    ["nodes"] = new JsonArray([.. curve.Points.Select((point, index) => (point, index)).Where(node => !node.point.IsPlain)
                        .Select(node => { JsonObject json = (JsonObject)CurvePointArgument.ToJson(node.point); json["index"] = node.index; return (JsonNode)json; })]),
                    ["falloffRight"] = curve.FalloffRight ?? curve.Falloff,
                    ["shape"] = HeightCurves.ShapeOf(curve).ToString(),
                    ["shapeSet"] = curve.FalloffShape is not null,
                    ["terraceSteps"] = curve.TerraceSteps,
                    ["cliffFace"] = curve.CliffFace,
                    ["roadMode"] = curve.RoadMode.ToString(),
                    ["camber"] = curve.Camber,
                    ["noiseAmplitude"] = curve.NoiseAmplitude,
                    ["noiseScale"] = curve.NoiseScale,
                    ["noiseSeed"] = curve.NoiseSeed,
                    ["heightVariation"] = new JsonArray(curve.HeightVariation.Min, curve.HeightVariation.Max),
                    ["widthVariation"] = new JsonArray(curve.WidthVariation.Min, curve.WidthVariation.Max),
                    ["variationScale"] = curve.VariationScale,
                    ["lineOnly"] = curve.LineOnly,
                    ["fills"] = curve.Fills,
                    ["grades"] = HeightCurveArguments.GradesOf(curve),
                } : null,
            })]),
            ["curveTool"] = new JsonObject
            {
                ["profile"] = curveTool.Profile.ToString(),
                ["width"] = curveTool.CurveWidth,
                ["falloff"] = curveTool.Falloff,
                ["height"] = curveTool.Height,
                ["editing"] = curveTool.Curve?.Name,
                // wave 18: where a new point's height comes from, the lines of reach, the chosen points, the table's grade limit
                ["pointHeight"] = curveTool.PointHeightChoice,
                ["typedHeight"] = curveTool.TypedHeight,
                ["showReach"] = curveTool.ShowReach,
                ["chosenPoints"] = new JsonArray([.. model.HeightStack.Nodes.Chosen.Select(index => (JsonNode)index)]),
                ["gradeLimit"] = model.HeightStack.Nodes.GradeLimit,
            },
        };
    }

    /// <summary>
    /// The kind of a height layer as automation has always named it: <c>Base</c>, <c>Sculpt</c>,
    /// <c>Imported</c>, <c>Curve</c>, or <c>Mask</c> for a mask reference.
    /// </summary>
    private static string KindOf(Layer layer) => layer.IsBase ? "Base" : layer.Source switch
    {
        ImageSource => "Imported",
        CurveSource => "Curve",
        MaskRefSource => "Mask",
        _ => "Sculpt",
    };
}
