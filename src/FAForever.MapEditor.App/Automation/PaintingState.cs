using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The state's <c>painting</c>: the brushes on the terrain's grids beyond <c>brush</c> (user stories
/// T-1, S-1, T-6, W-2, K-1): the sculpt tool's falloff, extra modes and limits, the strata tool's
/// limits, the water, masks and terrain types tools, the masks for tools and what the top view lays
/// over the terrain.
/// </summary>
internal static class PaintingState
{
    public static JsonObject Describe(MainViewModel model)
    {
        SculptTool sculpt = model.Tool<SculptTool>();
        StrataTool strata = model.Tool<StrataTool>();
        WaterPaintTool water = model.Tool<WaterPaintTool>();
        MaskPaintTool masks = model.Tool<MaskPaintTool>();
        TerrainTypeTool types = model.Tool<TerrainTypeTool>();
        return new JsonObject
        {
            ["sculpt"] = new JsonObject
            {
                ["falloff"] = sculpt.Falloff.ToString(),
                ["factor"] = sculpt.Factor,
                ["noiseScale"] = sculpt.NoiseScale,
                ["limits"] = sculpt.Limited ? new JsonArray(sculpt.MinHeight, sculpt.MaxHeight) : null,
            },
            ["strata"] = new JsonObject
            {
                ["falloff"] = strata.Falloff.ToString(),
                ["slope"] = strata.Limiting.BySlope ? new JsonArray(strata.Limiting.MinSlope, strata.Limiting.MaxSlope) : null,
                ["height"] = strata.Limiting.ByHeight ? new JsonArray(strata.Limiting.MinHeight, strata.Limiting.MaxHeight) : null,
            },
            ["water"] = Brush(water, new JsonObject { ["mask"] = water.Painted.Mask.ToString() }),
            ["masks"] = Brush(masks, new JsonObject { ["mask"] = model.Masks.Chosen }),
            ["types"] = Brush(types, new JsonObject
            {
                ["type"] = types.Chosen.Type.Name,
                ["code"] = types.Chosen.Code,
                ["legend"] = new JsonArray([.. types.Legend.Select(entry => (JsonNode)new JsonObject { ["type"] = entry.Type.Name, ["code"] = entry.Code, ["ogrids"] = entry.Count })]),
            }),
            ["maskList"] = new JsonArray([.. model.Masks.Items.Select(item => (JsonNode)new JsonObject { ["name"] = item.Name, ["size"] = item.Size })]),
            ["chosenMask"] = model.Masks.Chosen,
            ["showMask"] = model.ShowMask,
            ["shownGrid"] = model.ShownGrid?.Key,
        };
    }

    private static JsonObject Brush(GridPaintTool tool, JsonObject own)
    {
        own["radius"] = tool.Radius;
        own["strength"] = tool.Strength;
        own["erase"] = tool.Erase;
        own["falloff"] = tool.Falloff.ToString();
        own["shape"] = tool.Shape.Chosen.Key ?? "round";
        own["target"] = tool.Target?.Title;
        return own;
    }
}
