using System.Globalization;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What a brush preset holds (user stories F-10, T-2): the brush tool and its settings. The layer
/// the strata tool paints is the map's, so it is left out.
/// </summary>
/// <param name="Tool"><c>sculpt</c> or <c>strata</c>.</param>
/// <param name="Mode">How the sculpt tool changes the heights.</param>
/// <param name="Target">The height the sculpt tool flattens to.</param>
/// <param name="Erase">Whether the strata tool takes the layer away.</param>
/// <param name="Shape"><c>round</c>, or the key of a brush of the workspace.</param>
public sealed record BrushPresetSettings(
    string Tool,
    double Radius,
    double Strength,
    string Shape,
    double Rotation,
    bool RandomRotation,
    HeightBrushMode? Mode = null,
    double? Target = null,
    bool? Erase = null);

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
                sculpt.Shape.Rotation, sculpt.Shape.RandomRotation, Mode: sculpt.Mode, Target: sculpt.Target),
                $"{sculpt.Mode} {Number(sculpt.Radius)}"),
            StrataTool strata => (new BrushPresetSettings("strata", strata.Radius, strata.Strength, strata.Shape.Chosen.Key ?? "round",
                strata.Shape.Rotation, strata.Shape.RandomRotation, Erase: strata.Erase),
                $"{(strata.Erase ? "Erase" : "Paint")} {Number(strata.Radius)}"),
            _ => throw new InvalidOperationException($"The active tool ({model.ActiveTool?.Definition.Title}) has no brush to save: choose the sculpt or strata tool"),
        };
        string shape = settings.Shape == "round" ? "" : $", {Path.GetFileNameWithoutExtension(settings.Shape)}";
        return ($"{name}{shape}", Preset.Of(Kind, name, settings).Settings);
    }

    /// <summary>
    /// Chooses the preset's tool and gives it the preset's settings. A brush the workspace no longer
    /// has leaves the shape round, and the status line says so.
    /// </summary>
    public static void Apply(MainViewModel model, Preset preset)
    {
        BrushPresetSettings settings = preset.Read<BrushPresetSettings>();
        IShapedBrushTool tool;
        switch (settings.Tool)
        {
            case "sculpt":
                SculptTool sculpt = model.Tool<SculptTool>();
                model.SelectTool("terrain.sculpt");
                sculpt.Mode = settings.Mode ?? sculpt.Mode;
                sculpt.Target = settings.Target ?? sculpt.Target;
                sculpt.Radius = settings.Radius;
                sculpt.Strength = settings.Strength;
                tool = sculpt;
                break;
            case "strata":
                StrataTool strata = model.Tool<StrataTool>();
                model.SelectTool("paint.strata");
                strata.Erase = settings.Erase ?? strata.Erase;
                strata.Radius = settings.Radius;
                strata.Strength = settings.Strength;
                tool = strata;
                break;
            default:
                throw new ArgumentException($"The preset {preset.Name} is for a tool the editor has not: {settings.Tool}");
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
