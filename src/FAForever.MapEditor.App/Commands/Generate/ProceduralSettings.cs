using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Turns a result's regenerating on its own on or off (the Procedural mode's check box): a second
/// after the last change to its inputs it generates on a thread of the pool, and a newer change
/// stops that run. Kept with the map, a step of undo.
/// </summary>
/// <param name="Id">The generator; left out, walkability.</param>
/// <param name="On">Left out, the other way round.</param>
[EditorCommand("generate.auto", "Regenerate on its own", Story = "T-7", Needs = Needs.Map | Needs.NoStroke)]
public sealed record GenerateAuto(string? Id = null, bool? On = null) : IRequest;

public sealed class GenerateAutoHandler(MainViewModel model) : IRequestHandler<GenerateAuto>
{
    public Task Handle(GenerateAuto request, CancellationToken cancellationToken)
    {
        string id = request.Id ?? WalkabilityGenerator.GeneratorId;
        bool on = request.On ?? !model.Map!.Document.Procedural.RegeneratesOnItsOwn(id);
        model.SetAutoGenerate(id, on);
        string title = model.Generation.Results.Find(id)!.Title.ToLowerInvariant();
        model.Status = on ? $"The {title} regenerates on its own a second after a change to what it is made from" : $"The {title} is generated only when asked";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns whether the export makes a result again when it is out of date on or off (the normal map;
/// the Procedural mode's check box). Kept with the map, a step of undo.
/// </summary>
/// <param name="On">Left out, the other way round.</param>
[EditorCommand("generate.onExport", "Generate on export", Story = "M-9", Needs = Needs.Map | Needs.NoStroke)]
public sealed record GenerateOnExport(string Id, bool? On = null) : IRequest;

public sealed class GenerateOnExportHandler(MainViewModel model) : IRequestHandler<GenerateOnExport>
{
    public Task Handle(GenerateOnExport request, CancellationToken cancellationToken)
    {
        bool on = request.On ?? !model.Procedural.IsOnExport(model.Map!.Document.Procedural, request.Id);
        model.SetOnExport(request.Id, on);
        string title = model.Generation.Results.Find(request.Id)!.Title.ToLowerInvariant();
        model.Status = model.Procedural.ExportUseOf(request.Id) == ExportUse.Written
            ? on ? $"The export writes the {title} into the map's markers" : $"The export leaves the {title} out"
            : on ? $"The export generates the {title} again when it is out of date" : $"The export keeps the {title} as it is";
        return Task.CompletedTask;
    }
}

/// <summary>
/// The navigation mesh's choices (the Procedural mode): the layer the views and the list show, and
/// the connected region chosen. Both are the view's, no step of undo.
/// </summary>
/// <param name="Layer"><c>Land</c>, <c>Hover</c>, <c>Amphibious</c> or <c>Water</c> (naval units).</param>
/// <param name="Region">The connected region of the shown layer to choose, by its number; 0 chooses none.</param>
[EditorCommand("generate.setNavigation", "Navigation mesh settings", Story = "T-8", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetNavigation(string? Layer = null, int? Region = null) : IRequest;

public sealed class SetNavigationHandler(MainViewModel model, NavigationViewModel navigation) : IRequestHandler<SetNavigation>
{
    public Task Handle(SetNavigation request, CancellationToken cancellationToken)
    {
        if (request.Layer is string layerName)
        {
            navigation.Layer = Enum.TryParse(layerName, ignoreCase: true, out NavLayer layer) && Enum.IsDefined(layer)
                ? layer
                : throw new ArgumentException($"There is no layer '{layerName}': {string.Join(", ", navigation.Layers)}");
        }
        if (request.Region is int region)
        {
            if (region != 0 && !(navigation.Mesh?.RegionsOf(navigation.Layer).Any(r => r.Id == region) ?? false))
            {
                throw new ArgumentException($"The {navigation.Layer} layer has no connected region {region}");
            }
            navigation.ChosenRegion = region == 0 ? null : region;
        }
        model.Status = navigation.Summary;
        return Task.CompletedTask;
    }
}
