using System.Text.Json.Nodes;
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
internal static class HeightLayersState
{
    public static JsonObject Describe(MainViewModel model)
    {
        MapDocument? document = model.ShownDocument;
        CurveTool curveTool = model.Tool<CurveTool>();
        return new JsonObject
        {
            ["chosen"] = model.HeightStack.Chosen,
            ["sculptLayer"] = model.Map?.Session.SculptLayer,
            ["sculptProblem"] = document is null ? null : HeightLayers.SculptProblem(document, model.HeightStack.Chosen),
            ["layers"] = new JsonArray([.. (document?.HeightLayers ?? []).Select(layer => (JsonNode)new JsonObject
            {
                ["name"] = layer.Name,
                ["kind"] = layer.Kind.ToString(),
                ["combine"] = layer.Combine.ToString(),
                ["strength"] = layer.Strength,
                ["mask"] = layer.Mask,
                ["visible"] = layer.Visible,
                ["locked"] = layer.Locked,
                ["source"] = layer.Source,
                ["coverage"] = layer.Coverage is not null,
                ["curve"] = layer.Curve is HeightCurve curve ? new JsonObject
                {
                    ["profile"] = curve.Profile.ToString(),
                    ["width"] = curve.Width,
                    ["falloff"] = curve.Falloff,
                    ["closed"] = curve.Closed,
                    ["points"] = new JsonArray([.. curve.Points.Select(point => (JsonNode)new JsonArray(point.X, point.Z, point.Height))]),
                } : null,
            })]),
            ["curveTool"] = new JsonObject
            {
                ["profile"] = curveTool.Profile.ToString(),
                ["width"] = curveTool.CurveWidth,
                ["falloff"] = curveTool.Falloff,
                ["height"] = curveTool.Height,
                ["editing"] = curveTool.Curve?.Name,
            },
        };
    }
}
