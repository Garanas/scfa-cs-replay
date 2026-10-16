using System.Numerics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Places a wave generator (user story W-1): without a place it chooses the Waves tool, whose click
/// places one and whose drag aims its waves; with <paramref name="X"/> and <paramref name="Z"/> it
/// places one there at once, one step of undo, its waves rolling <paramref name="Heading"/> (compass
/// degrees, 0 north; left out, the tool's), with its images in the other parts of the map while the
/// symmetry mirrors edits.
/// </summary>
/// <param name="Speed">How fast its waves roll, in ogrids a tick; left out, the tool's.</param>
/// <returns>The indices of the generators placed; none when it chose the tool.</returns>
[EditorCommand("water.placeWaveGenerator", "Place a wave generator...", Story = "W-1", Needs = Needs.Map)]
public sealed record PlaceWaveGenerator(float? X = null, float? Z = null, double? Heading = null, double? Speed = null) : IRequest<int[]>;

public sealed class PlaceWaveGeneratorHandler(MainViewModel model) : IRequestHandler<PlaceWaveGenerator, int[]>
{
    public Task<int[]> Handle(PlaceWaveGenerator r, CancellationToken cancellationToken)
    {
        if (r.X is null && r.Z is null)
        {
            model.SelectTool("water.waves");
            model.Status = "Click on the water to place a wave generator, drag to aim its waves";
            return Task.FromResult<int[]>([]);
        }
        if (r.X is null || r.Z is null)
        {
            throw new ArgumentException("Give both x and z, or neither to choose the Waves tool");
        }
        if (model.StrokeInProgress is not null)
        {
            throw new InvalidOperationException("Wait until the stroke ends");
        }
        WaveTool tool = model.Tool<WaveTool>();
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        WaveGenerator generator = WaveGeneratorEdits.Aimed(
            AddWaveGeneratorHandler.Default(new Vector3(r.X.Value, document.Water.Elevation, r.Z.Value)),
            WaveGeneratorEdits.DirectionOfHeading(r.Heading ?? tool.Heading), (float)(r.Speed ?? tool.Speed));
        int[] placed = [];
        model.EditMap(model.EditTransforms(document).Count > 1 ? "Place wave generators" : "Place a wave generator",
            d => d.AddWaveGenerators(generator, model.EditTransforms(d), out placed));
        if (placed.Length > 0 && placed[0] < model.WaterSettings.Generators.Count)
        {
            model.WaterSettings.SelectedGenerator = model.WaterSettings.Generators[placed[0]];
        }
        model.Status = $"Placed {(placed.Length == 1 ? "a wave generator" : $"{placed.Length} wave generators")} at {r.X:0.#}, {r.Z:0.#}";
        return Task.FromResult(placed);
    }
}
