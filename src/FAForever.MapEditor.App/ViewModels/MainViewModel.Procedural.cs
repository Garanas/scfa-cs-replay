using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Procedural mode's side of the core: the list of generated results with their previews and
/// settings, results regenerating on their own, the navigation mesh, and the export as steps.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The generated results as the Procedural mode lists them.
    /// </summary>
    public ProceduralViewModel Procedural { get; private set; } = null!;

    /// <summary>
    /// The results that regenerate on their own a second after a change to their inputs.
    /// </summary>
    public AutoGeneration AutoGeneration { get; private set; } = null!;

    /// <summary>
    /// The navigation mesh: its connected regions, the layer shown.
    /// </summary>
    public NavigationViewModel Navigation { get; private set; } = null!;

    /// <summary>
    /// The export's steps, and how they stand in the export running or the last one.
    /// </summary>
    public ExportViewModel Export { get; } = new ExportViewModel();

    /// <summary>
    /// Whether the views colour the navigation mesh's connected regions of the shown layer (the
    /// overlay <c>navigation</c>). One tint at a time: it turns walkability and
    /// the mask off, and they turn it off.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowNavigation { get; set; }

    partial void OnShowNavigationChanged(bool value)
    {
        if (value)
        {
            ShowWalkability = false;
            ShowMask = false;
        }
        RefreshOverlays();
        Procedural.Refresh();
    }

    private void InitializeProcedural()
    {
        AutoGeneration = new AutoGeneration(Generation, () => Map?.Document, _ui, Logger("Editor"));
        AutoGeneration.Allow(WalkabilityGenerator.GeneratorId);
        AutoGeneration.Generated += id => Status = $"Regenerated the {Generation.Results.Find(id)?.Title.ToLowerInvariant() ?? id} on its own";
        Procedural = new ProceduralViewModel(Generation, AutoGeneration, () => Map?.Document,
            id => Overlays.FirstOrDefault(overlay => overlay.Id == id)?.IsOn == true);
        Navigation = new NavigationViewModel(Generation, () => Map?.Document);
        Navigation.Changed += (_, _) => RefreshOverlays();

        Generation.Register(new NavigationMeshGenerator(), "generate.navigation");
        Procedural.PreviewWith(WalkabilityGenerator.GeneratorId, "walkability");
        Procedural.PreviewWith(NavigationMeshGenerator.GeneratorId, "navigation");
        Procedural.OnExport(NormalMapGenerator.GeneratorId, ExportUse.WhenOutOfDate);
        Procedural.OnExport(MapPreview.GeneratorId, ExportUse.Always);

        PropertyChanged += (_, e) =>
        {
            // walkability's red or a mask's violet in place of the regions
            if ((e.PropertyName == nameof(ShowWalkability) && ShowWalkability) || (e.PropertyName == nameof(ShowMask) && ShowMask))
            {
                ShowNavigation = false;
            }
            if (e.PropertyName is nameof(ShowWalkability) or nameof(ShowMask) or nameof(ShowPathNodes))
            {
                Procedural.Refresh();
            }
        };
        DocumentChanged += (_, _) =>
        {
            AutoGeneration.Follow();
            Procedural.Refresh();
        };
        MapChanged += (_, _) =>
        {
            AutoGeneration.StopAll();
            Export.Reset();
        };

        RegisterExportSteps();
        InitializePathNodes();
    }

    /// <summary>
    /// The Procedural panel's check boxes and choices: each runs its command.
    /// </summary>
    [RelayCommand]
    private Task ToggleAutoGenerate(string id) => RunWithAsync("generate.auto", new JsonObject { ["id"] = id });

    [RelayCommand]
    private Task ToggleOnExport(string id) => RunWithAsync("generate.onExport", new JsonObject { ["id"] = id });

    [RelayCommand]
    private Task ChooseRegion(int id) => RunWithAsync("generate.setNavigation", new JsonObject { ["region"] = id == Navigation.ChosenRegion ? 0 : id });

    /// <summary>
    /// Turns a result's regenerating on its own on or off for the open map (a step of undo).
    /// </summary>
    /// <exception cref="ArgumentException">The result cannot regenerate on its own.</exception>
    public void SetAutoGenerate(string id, bool on)
    {
        IGenerator generator = Generation.Results.Find(id) ?? throw new ArgumentException($"There is no generator '{id}'");
        if (!AutoGeneration.Allowed.Contains(id))
        {
            throw new ArgumentException($"The {generator.Title.ToLowerInvariant()} cannot regenerate on its own; these can: {string.Join(", ", AutoGeneration.Allowed)}");
        }
        string title = generator.Title.ToLowerInvariant();
        EditMap(on ? $"Regenerate the {title} on its own" : $"Generate the {title} only when asked",
            document => document with { Procedural = document.Procedural.WithAutoGenerate(id, on) });
        AutoGeneration.Follow();
        Procedural.Refresh();
    }

    /// <summary>
    /// Turns whether the export makes a result again when it is out of date on or off (a step of undo).
    /// </summary>
    /// <exception cref="ArgumentException">The export does not make the result, or always does.</exception>
    public void SetOnExport(string id, bool on)
    {
        IGenerator generator = Generation.Results.Find(id) ?? throw new ArgumentException($"There is no generator '{id}'");
        ExportUse use = Procedural.ExportUseOf(id);
        if (use is not (ExportUse.WhenOutOfDate or ExportUse.Written))
        {
            throw new ArgumentException($"The export {(use == ExportUse.Always ? "always makes" : "does not make")} the {generator.Title.ToLowerInvariant()}");
        }
        string title = generator.Title.ToLowerInvariant();
        EditMap(use == ExportUse.Written ? (on ? $"Write the {title} on export" : $"Leave the {title} out of the export")
                : on ? $"Generate the {title} on export" : $"Keep the {title} on export",
            document => document with { Procedural = Procedural.WithOnExport(document.Procedural, id, on) });
        Procedural.Refresh();
    }

    /// <summary>
    /// The editor's steps of the export: the normal map when out of date, the preview always, the
    /// walkability marks into the terrain types of the files.
    /// </summary>
    private void RegisterExportSteps()
    {
        Export.Register(new ExportStep(NormalMapGenerator.GeneratorId, "Generate the normal map", ExportStep.GenerateOrder)
        {
            Applies = run =>
            {
                if (!run.Document.Procedural.GeneratesOnExport(NormalMapGenerator.GeneratorId))
                {
                    run.Note = "kept as it is (the map's setting)";
                    return false;
                }
                GeneratedResult? result = Generation.Results.ResultOf(NormalMapGenerator.GeneratorId);
                if (result is null || !result.IsStale(run.Document))
                {
                    run.Note = result is null ? "not generated in this session, kept" : "up to date";
                    return false;
                }
                return true;
            },
            Run = async run =>
            {
                string changed = string.Join(", ", Generation.Results.ResultOf(NormalMapGenerator.GeneratorId)!.ChangedInputs(run.Document));
                await GenerateIntoExportAsync(run, NormalMapGenerator.GeneratorId);
                run.Note = $"{changed} changed; made for the export, the project keeps its own";
            },
        });
        Export.Register(new ExportStep(MapPreview.GeneratorId, "Render the preview", ExportStep.PreviewOrder)
        {
            // always, as the textures it is drawn with are no input
            Run = run => GenerateIntoExportAsync(run, MapPreview.GeneratorId),
        });
        Export.Register(new ExportStep("walkabilityMarks", "Write the walkability marks into the terrain types", ExportStep.GameFilesOrder)
        {
            Applies = run =>
            {
                (int blocked, int open) = WalkabilityMarks.Count(run.Document.WalkabilityMarks);
                run.Note = blocked + open == 0 ? "no marks" : null;
                return blocked + open > 0;
            },
            Run = run =>
            {
                (int blocked, int open) = WalkabilityMarks.Count(run.Document.WalkabilityMarks);
                run.Document = run.Document with
                {
                    Terrain = run.Document.Terrain with { Types = TiledBuffer<byte>.FromArray(run.Document.Width, run.Document.Height, WalkabilityMarks.TerrainTypes(run.Document)) },
                    WalkabilityMarks = null,
                };
                run.Note = $"{Units(blocked, "ogrid")} blocked, {Units(open, "ogrid")} open";
                return Task.CompletedTask;
            },
        });
    }

    private static string Units(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    /// <summary>
    /// Generates a part of the map for an export, on a thread of the pool, into the version the
    /// export writes only: the export works on a copy, so the project, its history and its generated
    /// results stay as they are (Generate in the Procedural mode keeps a result with the project).
    /// </summary>
    private async Task GenerateIntoExportAsync(ExportRun run, string id)
    {
        IGenerator generator = Generation.Results.Find(id) ?? throw new InvalidOperationException($"There is no generator '{id}'");
        Generation.Results.HandResults(generator);
        MapDocument from = run.Document;
        object value = await Task.Run(() =>
        {
            using Performance.Span measure = Performance.Measure("model.generate-export");
            return generator.Generate(from);
        });
        if (generator is IDocumentGenerator part)
        {
            run.Document = part.Apply(run.Document, value);
        }
    }

    /// <returns>The map folder that was written.</returns>
    /// <exception cref="Project.OperationVetoedException">A handler of <see cref="EditorEvents.BeforeExport"/> stopped it.</exception>
    public async Task<string> ExportAsync(string outputFolder, int? version = null, bool raise = false)
    {
        Project.EditedMap map = RequireMap();
        Events.CheckExport(map, outputFolder);
        if (raise)
        {
            // the map keeps its new version: the next export and the project go on from it (M-8)
            int next = map.Document.NextVersion;
            EditMap($"Raise the version to {next}", document => document with { Version = next });
        }
        int? exported = version ?? map.Document.Version;
        string folder = await Export.RunAsync(new ExportRun(this, outputFolder, version, map.Document), async run =>
        {
            string written = "";
            await BusyAsync("export", "Exporting...", async () =>
            {
                written = await map.ExportAsync(run.OutputFolder, run.Version, run.Document);
            });
            return written;
        }, (step, steps, title) => Status = $"Exporting, step {step} of {steps}: {title}");
        Status = $"Exported {folder} (version {exported?.ToString() ?? "none"}); exporting with raise makes version {(exported ?? 0) + 1}";
        if (!map.Document.IsPublishable)
        {
            // to play it, which is fine; it may never go to the vault
            Logger("Files").LogWarning("Exported {Map}, which is the sandbox: it is for trying the editor and is not to be uploaded", map.Document.Scenario.Name);
            Status = $"Exported {folder}. This is the sandbox: play it, but it cannot be uploaded to the vault";
        }
        Events.OnMapExported(map, folder);
        return folder;
    }
}
