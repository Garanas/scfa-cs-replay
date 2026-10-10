using System.Globalization;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What a brush preset holds (user stories F-10, T-2): the brush tool and its settings. What the
/// painting tools paint (a stratum, a mask for tools) is the map's, so it is left out; the water's
/// mask and the terrain type are the game's, so they are kept.
/// </summary>
/// <param name="Tool"><c>sculpt</c>, or a painting tool's <see cref="GridPaintTool.PresetName"/>:
/// <c>strata</c>, <c>water</c>, <c>masks</c>, <c>types</c>.</param>
/// <param name="Mode">How the sculpt tool changes the heights.</param>
/// <param name="Target">The height the sculpt tool flattens to, sets, or scales around.</param>
/// <param name="Erase">Whether a painting tool takes the mask away.</param>
/// <param name="Shape"><c>round</c>, or the key of a brush of the workspace.</param>
/// <param name="Falloff">How a round brush fades; smooth when left out.</param>
/// <param name="Factor">What the sculpt tool scales by.</param>
/// <param name="NoiseScale">The size of the sculpt tool's noise, in ogrids.</param>
/// <param name="MinHeight">The sculpt tool's lowest height, or where the strata tool paints from; null for no limit.</param>
/// <param name="MaxHeight">The sculpt tool's highest height, or where the strata tool paints up to; null for no limit.</param>
/// <param name="MinSlope">Where the strata tool paints from, in degrees; null for no limit.</param>
/// <param name="MaxSlope">Where the strata tool paints up to, in degrees; null for no limit.</param>
/// <param name="WaterMask">The water's mask the water tool paints.</param>
/// <param name="TerrainType">The terrain type the types tool paints, by name.</param>
public sealed record BrushPresetSettings(
    string Tool,
    double Radius,
    double Strength,
    string Shape,
    double Rotation,
    bool RandomRotation,
    HeightBrushMode? Mode = null,
    double? Target = null,
    bool? Erase = null,
    BrushFalloff? Falloff = null,
    double? Factor = null,
    double? NoiseScale = null,
    double? MinHeight = null,
    double? MaxHeight = null,
    double? MinSlope = null,
    double? MaxSlope = null,
    WaterMask? WaterMask = null,
    string? TerrainType = null);

/// <summary>
/// Brush presets: the active brush tool's settings, saved and applied again.
/// </summary>
public static class BrushPreset
{
    public const string Kind = "brush";

    public static PresetKind Of(MainViewModel model) => new PresetKind(Kind, "Brush presets", () => Capture(model), preset => Apply(model, preset));

    /// <summary>
    /// The active brush tool's settings, and a name for them such as <c>Raise 12</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The active tool has no brush.</exception>
    public static (string Name, JsonObject Settings) Capture(MainViewModel model)
    {
        (BrushPresetSettings settings, string name) = model.ActiveTool?.Tool switch
        {
            SculptTool sculpt => (new BrushPresetSettings("sculpt", sculpt.Radius, sculpt.Strength, sculpt.Shape.Chosen.Key ?? "round",
                sculpt.Shape.Rotation, sculpt.Shape.RandomRotation, Mode: sculpt.Mode, Target: sculpt.Target, Falloff: sculpt.Falloff,
                Factor: sculpt.Factor, NoiseScale: sculpt.NoiseScale,
                MinHeight: sculpt.Limited ? sculpt.MinHeight : null, MaxHeight: sculpt.Limited ? sculpt.MaxHeight : null),
                $"{sculpt.Mode} {Number(sculpt.Radius)}"),
            TerrainTypeTool types => (new BrushPresetSettings(types.PresetName, types.Radius, types.Strength, types.Shape.Chosen.Key ?? "round",
                types.Shape.Rotation, types.Shape.RandomRotation, Falloff: types.Falloff, TerrainType: types.Chosen.Type.Name),
                $"{types.Chosen.Type.Name} {Number(types.Radius)}"),
            GridPaintTool grid => (Paint(grid), $"{(grid.Erase ? "Erase" : "Paint")} {Painted(grid)}{Number(grid.Radius)}"),
            _ => throw new InvalidOperationException($"The active tool ({model.ActiveTool?.Definition.Title}) has no brush to save: choose a sculpt or painting tool"),
        };
        string shape = settings.Shape == "round" ? "" : $", {Path.GetFileNameWithoutExtension(settings.Shape)}";
        return ($"{name}{shape}", Preset.Of(Kind, name, settings).Settings);
    }

    private static BrushPresetSettings Paint(GridPaintTool tool)
    {
        PaintLimits? limits = (tool as StrataTool)?.Limiting.ToLimits();
        return new BrushPresetSettings(tool.PresetName, tool.Radius, tool.Strength, tool.Shape.Chosen.Key ?? "round",
            tool.Shape.Rotation, tool.Shape.RandomRotation, Erase: tool.Erase, Falloff: tool.Falloff,
            MinHeight: limits?.MinHeight, MaxHeight: limits?.MaxHeight, MinSlope: limits?.MinSlope, MaxSlope: limits?.MaxSlope,
            WaterMask: (tool as WaterPaintTool)?.Painted.Mask);
    }

    /// <summary>
    /// What the name of a painting tool's preset says it paints, before the radius.
    /// </summary>
    private static string Painted(GridPaintTool tool) => tool switch
    {
        WaterPaintTool water => $"{water.Painted.Title.ToLowerInvariant()} ",
        MaskPaintTool => "mask ",
        _ => "",
    };

    /// <summary>
    /// Chooses the preset's tool and gives it the preset's settings. A brush the workspace no longer
    /// has leaves the shape round, and the status line says so.
    /// </summary>
    public static void Apply(MainViewModel model, Preset preset)
    {
        BrushPresetSettings settings = preset.Read<BrushPresetSettings>();
        IShapedBrushTool tool;
        if (settings.Tool == "sculpt")
        {
            SculptTool sculpt = model.Tool<SculptTool>();
            model.SelectTool("terrain.sculpt");
            sculpt.Mode = settings.Mode ?? sculpt.Mode;
            sculpt.Target = settings.Target ?? sculpt.Target;
            sculpt.Radius = settings.Radius;
            sculpt.Strength = settings.Strength;
            sculpt.Falloff = settings.Falloff ?? BrushFalloff.Smooth;
            sculpt.Factor = settings.Factor ?? sculpt.Factor;
            sculpt.NoiseScale = settings.NoiseScale ?? sculpt.NoiseScale;
            sculpt.Limited = settings.MinHeight is not null || settings.MaxHeight is not null;
            sculpt.MinHeight = settings.MinHeight ?? sculpt.MinHeight;
            sculpt.MaxHeight = settings.MaxHeight ?? sculpt.MaxHeight;
            tool = sculpt;
        }
        else
        {
            (string id, GridPaintTool grid) = settings.Tool switch
            {
                "strata" => ("paint.strata", (GridPaintTool)model.Tool<StrataTool>()),
                "water" => ("water.paint", model.Tool<WaterPaintTool>()),
                "masks" => ("paint.masks", model.Tool<MaskPaintTool>()),
                "types" => ("terrain.types", model.Tool<TerrainTypeTool>()),
                _ => throw new ArgumentException($"The preset {preset.Name} is for a tool the editor has not: {settings.Tool}"),
            };
            model.SelectTool(id);
            grid.Erase = settings.Erase ?? grid.Erase;
            grid.Radius = settings.Radius;
            grid.Strength = settings.Strength;
            grid.Falloff = settings.Falloff ?? (grid is TerrainTypeTool ? BrushFalloff.Sharp : BrushFalloff.Smooth);
            switch (grid)
            {
                case StrataTool strata:
                    strata.Limiting.BySlope = settings.MinSlope is not null || settings.MaxSlope is not null;
                    strata.Limiting.MinSlope = settings.MinSlope ?? strata.Limiting.MinSlope;
                    strata.Limiting.MaxSlope = settings.MaxSlope ?? strata.Limiting.MaxSlope;
                    strata.Limiting.ByHeight = settings.MinHeight is not null || settings.MaxHeight is not null;
                    strata.Limiting.MinHeight = settings.MinHeight ?? strata.Limiting.MinHeight;
                    strata.Limiting.MaxHeight = settings.MaxHeight ?? strata.Limiting.MaxHeight;
                    break;
                case WaterPaintTool water when settings.WaterMask is WaterMask mask:
                    water.Choose(mask.ToString());
                    break;
                case TerrainTypeTool types when settings.TerrainType is string type:
                    types.Choose(type);
                    break;
            }
            tool = grid;
        }
        try
        {
            tool.Shape.Choose(settings.Shape);
        }
        catch (ArgumentException exception)
        {
            tool.Shape.Choose(null);
            model.Logger("Editor").LogWarning("The preset {Name} has a brush the workspace has not: {Reason}", preset.Name, exception.Message);
            model.Status = $"The preset {preset.Name} has a brush the workspace has not ({settings.Shape}); the brush is round";
        }
        tool.Shape.Rotation = settings.Rotation;
        tool.Shape.RandomRotation = settings.RandomRotation;
    }

    private static string Number(double value) => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);
}
