using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A line of the Workspace settings page's Procedural defaults: a generated result, whether maps
/// made or imported in the workspace let it regenerate on its own, and whether their export makes it.
/// </summary>
/// <param name="HasExport">Whether the export has a choice about it (the normal map, the path networks).</param>
public sealed record WorkspaceProceduralRow(string Id, string Title, bool Auto, bool HasExport, bool OnExport)
{
    public string AutoLabel => $"New maps regenerate the {Title.ToLowerInvariant()} on their own";

    public string OnExportLabel => $"New maps make or write the {Title.ToLowerInvariant()} on export";
}

/// <summary>
/// Results regenerating on their own (the owner's decisions Q4 and Q5): every generator may, each
/// only while the views show its result; a switch in the views' bar turns it on and off for the
/// editor (remembered in the user's settings) and says what waits or runs; a result that is part
/// of the map goes into it without a step of undo; the workspace gives new and imported maps their
/// settings, and a map's settings can become the workspace's.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Whether results regenerate on their own (the views' bar's switch, <c>generate.automatic</c>):
    /// the editor's, for every map; each map says which (<see cref="ProceduralSettings.AutoGenerate"/>).
    /// </summary>
    public bool RegenerateOnTheirOwn => AutoGeneration.IsOn;

    /// <summary>
    /// The switch's text: <c>Auto</c>, with what waits or runs.
    /// </summary>
    [ObservableProperty]
    public partial string AutoRegenerateText { get; private set; } = "Auto";

    /// <summary>
    /// The switch's tool tip: what regenerates on its own, what waits, runs or waits to be shown.
    /// </summary>
    [ObservableProperty]
    public partial string AutoRegenerateTip { get; private set; } = "";

    /// <summary>
    /// Whether something waits or runs on its own now (the switch shows it).
    /// </summary>
    [ObservableProperty]
    public partial bool AutoRegenerateBusy { get; private set; }

    /// <summary>
    /// The workspace's Procedural defaults, a line per generated result, for the Workspace settings page.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<WorkspaceProceduralRow> WorkspaceProceduralRows { get; private set; } = [];

    /// <summary>
    /// The workspace's Procedural defaults in words, for the Workspace settings page.
    /// </summary>
    [ObservableProperty]
    public partial string WorkspaceProceduralText { get; private set; } = "";

    [RelayCommand]
    private Task ToggleWorkspaceAuto(string id) => RunWithAsync("workspace.setProcedural", new JsonObject
    {
        ["id"] = id,
        ["auto"] = !(Setup.Workspace?.Procedural.RegeneratesOnItsOwn(id) ?? false),
    });

    [RelayCommand]
    private Task ToggleWorkspaceOnExport(string id) => RunWithAsync("workspace.setProcedural", new JsonObject
    {
        ["id"] = id,
        ["onExport"] = !(Setup.Workspace?.Procedural.OnExport.Contains(id) ?? false),
    });

    /// <summary>
    /// Shows the workspace's Procedural defaults as they are now.
    /// </summary>
    private void RefreshWorkspaceProcedural()
    {
        if (Setup.Workspace is not Workspace workspace)
        {
            WorkspaceProceduralRows = [];
            WorkspaceProceduralText = "";
            return;
        }
        ProceduralDefaults defaults = workspace.Procedural;
        WorkspaceProceduralRows = [.. Generation.Results.InOrder().Select(generator => new WorkspaceProceduralRow(
            generator.Id, generator.Title, defaults.RegeneratesOnItsOwn(generator.Id),
            ProceduralDefaults.ExportChoices.Contains(generator.Id), defaults.OnExport.Contains(generator.Id)))];
        WorkspaceProceduralText = $"{(defaults.IsDefault ? "The editor's defaults" : "The workspace's own")}: {DescribeDefaults(defaults)}.";
    }

    private void InitializeAutoRegenerate()
    {
        Setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceSetupViewModel.Workspace))
            {
                RefreshWorkspaceProcedural();
            }
        };
        AutoGeneration.Apply = (generator, value) =>
        {
            if (Map is EditedMap map)
            {
                DeriveResult(map, generator, value);
            }
        };
        AutoGeneration.Changed += RefreshAutoRegenerate;

        // when the views show each result: only then does it regenerate on its own
        AutoGeneration.ShowsWhen(WalkabilityGenerator.GeneratorId, _ => ShowWalkability, "while the Walkability chip is on");
        AutoGeneration.ShowsWhen(NavigationMeshGenerator.GeneratorId, _ => ShowNavigation, "while the Connected regions chip is on");
        AutoGeneration.ShowsWhen(PathNetworkGenerator.GeneratorId, _ => ShowGeneratedPathNodes, "while the Generated nodes chip is on");
        AutoGeneration.ShowsWhen(MaskRulesGenerator.GeneratorId, MasksFromRulesShown,
            "while a stratum has a rules layer (the terrain always shows the strata), or the Mask chip shows a mask with rules");
        AutoGeneration.ShowsWhen(PropRulesGenerator.GeneratorId, _ => ShowProps, "while the Props chip is on");
        AutoGeneration.ShowsWhen(NormalMapGenerator.GeneratorId, _ => SelectedMode?.Definition.Id == ProceduralMode.Id,
            "while the Procedural mode shows its picture (the views do not draw the normal map)");
        AutoGeneration.ShowsWhen(MapPreview.GeneratorId, _ => SelectedMode?.Definition.Id is ProceduralMode.Id or "map",
            "while the Map or Procedural mode shows it");
        AutoGeneration.ShowsWhen(NormalDecalGenerator.GeneratorId, document => Shows3DView && ShowDecals && document.WideDecals.Normal.Source == MapWideSource.Generated,
            "while the 3D view shows the decals and the map's normal decal is the generated one");
        AutoGeneration.ShowsWhen(ShadowDecalGenerator.GeneratorId, document => Shows3DView && ShowDecals && document.WideDecals.Shadow.Source == MapWideSource.Generated,
            "while the 3D view shows the decals and the map's shadow decal is the generated one");
        AutoGeneration.ShowsWhen(SkyCubeGenerator.GeneratorId, document => Shows3DView && document.Files.ContainsKey(SkyCubeMaps.FilePath),
            "while the 3D view shows and the map holds a generated sky cube map");

        // what the views show changed: a result shown now may be out of date
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShowWalkability) or nameof(ShowNavigation) or nameof(ShowGeneratedPathNodes) or nameof(ShowMask)
                or nameof(ShowProps) or nameof(ShowDecals) or nameof(View) or nameof(SelectedMode))
            {
                FollowShown();
            }
        };
        Masks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MasksViewModel.Chosen))
            {
                FollowShown();
            }
        };
        DocumentChanged += (_, _) => RefreshAutoRegenerate();
        MapChanged += (_, _) =>
        {
            _recentResults.Clear();
            RefreshAutoRegenerate();
        };
        RefreshAutoRegenerate();
    }

    private void FollowShown()
    {
        if (AutoGeneration is not null && Map is not null)
        {
            AutoGeneration.Follow();
        }
        RefreshAutoRegenerate();
    }

    /// <summary>
    /// Whether the views show masks made from rules: a stratum with a rules layer (the terrain always
    /// shows the strata), or the chosen mask with one while the mask overlay or the Masks mode shows it.
    /// </summary>
    private bool MasksFromRulesShown(MapDocument document)
    {
        static bool HasRules(LayerStack stack) => stack.Layers.Any(layer => layer.Source is RulesSource);
        if (document.CoverageStacks.Any(pair => pair.Key.StartsWith("stratum-", StringComparison.Ordinal) && HasRules(pair.Value)))
        {
            return true;
        }
        return (ShowMask || SelectedMode?.Definition.Id == "masks") && Masks.Chosen is string name
            && document.CoverageStacks.TryGetValue(LayerTarget.Mask(name).Key, out LayerStack? mask) && HasRules(mask);
    }

    private string TitleOf(string id) => (Generation.Results.Find(id)?.Title ?? id).ToLowerInvariant();

    private void RefreshAutoRegenerate()
    {
        if (AutoGeneration is null)
        {
            return;
        }
        IReadOnlyList<string> running = AutoGeneration.Running;
        IReadOnlyList<string> waiting = AutoGeneration.Waiting;
        IReadOnlyList<string> hidden = AutoGeneration.Hidden;
        AutoRegenerateBusy = running.Count + waiting.Count > 0;
        AutoRegenerateText = !AutoGeneration.IsOn ? "Auto off"
            : running.Count > 0 ? $"Auto: {running.Count} running"
            : waiting.Count > 0 ? $"Auto: {waiting.Count} waiting"
            : "Auto";
        List<string> lines = [];
        if (!AutoGeneration.IsOn)
        {
            lines.Add("Results regenerate only when asked; click to let them regenerate on their own");
        }
        else
        {
            string[] on = Map?.Document.Procedural.AutoGenerate.Where(id => Generation.Results.Find(id) is not null).Select(TitleOf).ToArray() ?? [];
            lines.Add(on.Length == 0 ? "This map lets nothing regenerate on its own (each Procedural entry's check box)"
                : $"Regenerating on their own while the views show them: {string.Join(", ", on)}");
            if (running.Count > 0)
            {
                lines.Add($"Running: {string.Join(", ", running.Select(TitleOf))}");
            }
            if (waiting.Count > 0)
            {
                lines.Add($"Waiting a moment after the last change: {string.Join(", ", waiting.Select(TitleOf))}");
            }
            if (hidden.Count > 0)
            {
                lines.Add($"Out of date, waiting until the views show them: {string.Join(", ", hidden.Select(TitleOf))}");
            }
            lines.Add("Click to turn regenerating on their own off for every map");
        }
        AutoRegenerateTip = string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Reads the user's choice whether results regenerate on their own.
    /// </summary>
    private void LoadAutoRegenerate(UserSettings settings)
    {
        AutoGeneration.IsOn = settings.RegenerateOnTheirOwn;
        OnPropertyChanged(nameof(RegenerateOnTheirOwn));
        RefreshAutoRegenerate();
    }

    /// <summary>
    /// Turns regenerating on their own on or off for the editor, and remembers it in the user's
    /// settings (when the editor has them).
    /// </summary>
    public void SetRegenerateOnTheirOwn(bool on)
    {
        AutoGeneration.IsOn = on;
        OnPropertyChanged(nameof(RegenerateOnTheirOwn));
        RefreshAutoRegenerate();
        if (Diagnostics.Settings is UserSettings settings)
        {
            try
            {
                settings.SetRegenerateOnTheirOwn(on);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MapProjectException)
            {
                Logger("Editor").LogWarning(exception, "Could not remember in {File} whether results regenerate on their own: {Reason}", settings.File, exception.Message);
            }
        }
    }

    /// <summary>
    /// Makes the open map's Procedural settings (what regenerates on its own, what the export makes)
    /// the workspace's defaults for the maps made or imported in it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No map is open, or no workspace is set up.</exception>
    public ProceduralDefaults UseAsWorkspaceDefault()
    {
        EditedMap map = RequireMap();
        ProceduralDefaults defaults = ProceduralDefaults.Of(map.Document.Procedural);
        Setup.ChangeProcedural(defaults);
        RefreshWorkspaceProcedural();
        return defaults;
    }

    /// <summary>
    /// Changes the workspace's default for one generated result: whether it regenerates on its own,
    /// whether the export makes or writes it.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such generator, or it has no choice on export.</exception>
    public ProceduralDefaults SetWorkspaceProcedural(string id, bool? auto, bool? onExport)
    {
        Workspace workspace = Setup.Workspace ?? throw new InvalidOperationException("Set up a workspace first");
        if (Generation.Results.Find(id) is null)
        {
            throw new ArgumentException($"There is no generator '{id}': {string.Join(", ", Generation.Results.Generators.Select(generator => generator.Id))}");
        }
        if (onExport is not null && !ProceduralDefaults.ExportChoices.Contains(id))
        {
            throw new ArgumentException($"The export has no choice about the {TitleOf(id)}; it has about: {string.Join(", ", ProceduralDefaults.ExportChoices.Select(TitleOf))}");
        }
        ProceduralDefaults defaults = workspace.Procedural;
        if (auto is bool a)
        {
            defaults = defaults.WithAutoGenerate(id, a);
        }
        if (onExport is bool e)
        {
            defaults = defaults.WithOnExport(id, e);
        }
        Setup.ChangeProcedural(defaults);
        RefreshWorkspaceProcedural();
        return defaults;
    }

    /// <summary>
    /// The workspace's Procedural defaults in words, e.g. <c>regenerate on their own: walkability,
    /// navigation mesh; on export: normal map</c>.
    /// </summary>
    public string DescribeDefaults(ProceduralDefaults defaults)
    {
        string auto = defaults.AutoGenerate.Count == 0 ? "nothing" : string.Join(", ", defaults.AutoGenerate.Select(TitleOf));
        string export = defaults.OnExport.Count == 0 ? "nothing chosen" : string.Join(", ", defaults.OnExport.Select(TitleOf));
        return $"regenerating on their own: {auto}; made or written on export: {export}";
    }

    /// <summary>
    /// Regenerating on their own as automation reads it (<c>state.procedural.auto</c>).
    /// </summary>
    internal JsonObject DescribeAutoRegenerate() => new JsonObject
    {
        ["on"] = AutoGeneration.IsOn,
        ["delay"] = AutoGeneration.Delay.TotalMilliseconds,
        ["pending"] = new JsonArray([.. AutoGeneration.Allowed.Where(AutoGeneration.IsPending).Select(id => (JsonNode)JsonValue.Create(id))]),
        ["waiting"] = new JsonArray([.. AutoGeneration.Waiting.Select(id => (JsonNode)JsonValue.Create(id))]),
        ["running"] = new JsonArray([.. AutoGeneration.Running.Select(id => (JsonNode)JsonValue.Create(id))]),
        ["hidden"] = new JsonArray([.. AutoGeneration.Hidden.Select(id => (JsonNode)JsonValue.Create(id))]),
        ["shown"] = Map?.Document is MapDocument document
            ? new JsonObject([.. AutoGeneration.Allowed.Select(id => new KeyValuePair<string, JsonNode?>(id, AutoGeneration.IsShown(id, document)))])
            : null,
        ["text"] = AutoRegenerateText,
        ["tip"] = AutoRegenerateTip,
        ["kept"] = AutoGeneration.Kept,
        ["cancelled"] = AutoGeneration.Cancelled,
    };
}
