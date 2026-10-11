using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The panels of the map and its atmosphere (user stories M-1, M-2, M-5, M-6, W-1, L-1, L-2,
/// S-2): the parts that show the map's information, areas, script and files, water, lighting and
/// layer textures, kept up to date with the shown document, and the page of a new map.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The map's information, the Map panel's Information entry (user story M-1).
    /// </summary>
    public MapInformationViewModel MapInformation { get; private set; } = null!;

    /// <summary>
    /// The map's areas (user story M-5).
    /// </summary>
    public AreaListViewModel AreaList { get; private set; } = null!;

    /// <summary>
    /// The script and the files of the map's folder, the Map panel's Script and files entry (user story M-6).
    /// </summary>
    public ScriptFilesViewModel ScriptFiles { get; } = new ScriptFilesViewModel();

    /// <summary>
    /// The Water panel and inspector (user story W-1).
    /// </summary>
    public WaterSettingsViewModel WaterSettings { get; private set; } = null!;

    /// <summary>
    /// The Lighting panel (user story L-1).
    /// </summary>
    public LightingSettingsViewModel LightingSettings { get; private set; } = null!;

    /// <summary>
    /// The Paint panel's layer textures and the inspector's layer (user story S-2).
    /// </summary>
    public LayerTexturesViewModel LayerTextures { get; private set; } = null!;

    /// <summary>
    /// The page of File, New map (user story M-2).
    /// </summary>
    public NewMapPageViewModel NewMapPage { get; private set; } = null!;

    /// <summary>
    /// Raised when the window is to open a file in the system's default application for it, such
    /// as the map's script in the person's own editor (user story M-6).
    /// </summary>
    public event Action<string>? OpenFileRequested;

    public void OpenFile(string file) => OpenFileRequested?.Invoke(file);

    private void InitializeMapPanels()
    {
        MapInformation = new MapInformationViewModel(RunWithAsync);
        AreaList = new AreaListViewModel(RunWithAsync);
        WaterSettings = new WaterSettingsViewModel(RunWithAsync);
        LightingSettings = new LightingSettingsViewModel(RunWithAsync, (message, buttons) => Question.AskAsync("Background", message, buttons, cancel: buttons.Count - 1, focus: 0));
        LayerTextures = new LayerTexturesViewModel(RunWithAsync, path => Assets.Catalog.Find(path)?.File);
        Assets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetsViewModel.Catalog))
            {
                LayerTextures.RefreshTextures();
            }
        };
        NewMapPage = new NewMapPageViewModel(ConfirmCloseAsync, RunWithAsync);
        Presets.Register(new PresetKind(AtmospherePresets.LightingKind, "Lighting presets", CaptureLighting, ApplyLighting));
        Presets.Register(new PresetKind(AtmospherePresets.WaterKind, "Water presets", CaptureWater, ApplyWater));
        DocumentChanged += (_, _) => RefreshMapPanels();
        MapChanged += (_, _) => RefreshMapPanels();

        // the Paint panel's one list of layers (S-1, S-2): a stratum chosen there is the one the strata tool paints, and back
        LayerTextures.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayerTexturesViewModel.Selected) && LayerTextures.Selected is { IsStratum: true } item
                && StrataTool.Strata.Any(stratum => stratum.Layer == item.Index))
            {
                StrataTool.Choose(item.Index);
            }
        };
        StrataTool.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StrataTool.Painted) && StrataTool.Painted is Stratum painted)
            {
                LayerTextures.Choose(painted.Layer);
            }
        };
    }

    /// <summary>
    /// Shows the shown document in the panels; each part shows again only what changed (it
    /// compares the parts of the document by reference), so a dab of a brush costs nothing here.
    /// </summary>
    private void RefreshMapPanels()
    {
        using Performance.Span measure = Performance.Measure("model.map-panels");
        MapDocument? document = ShownDocument;
        MapInformation.Refresh(document);
        AreaList.Refresh(document);
        ScriptFiles.Refresh(document, Map?.ProjectFolder is not null);
        WaterSettings.Refresh(document);
        LightingSettings.Refresh(document);
        LayerTextures.Refresh(document);
    }

    /// <summary>
    /// Makes one edit of the map, one step of undo: what a field of a panel does. Edits with the
    /// same <paramref name="mergeKey"/> that follow each other are one step, so a slider dragged
    /// or a field typed in again is undone at once.
    /// </summary>
    /// <returns>Whether the map changed.</returns>
    /// <exception cref="InvalidOperationException">No map is open, or a stroke is going on.</exception>
    public bool EditMap(string description, Func<MapDocument, MapDocument> edit, string? mergeKey = null)
    {
        EditedMap map = RequireMap();
        if (_stroke is not null)
        {
            throw new InvalidOperationException("Wait until the stroke ends");
        }
        return map.Session.Edit(description, edit, mergeKey);
    }

    /// <summary>
    /// Opens a map made in the editor (user story M-2): it is no project until it is saved.
    /// </summary>
    public void OpenNewMap(MapDocument document)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("The editor is still busy with the previous operation");
        }
        Attach(EditedMap.New(document), $"Made the new map {document.Scenario.Name} ({document.Width} by {document.Height} ogrids); save it as a project to keep it");
        Logger("Files").LogInformation("Made a new map {Name} of {Width} by {Height} ogrids", document.Scenario.Name, document.Width, document.Height);
    }

    /// <summary>
    /// What the panels of the map and its atmosphere show, for automation's <c>state</c>: the
    /// information, the areas, the script and files, the water, the lighting, the layers, the
    /// ruler's last measurement and whether the page of a new map is open.
    /// </summary>
    public JsonObject DescribeMapPanels()
    {
        MapDocument? document = ShownDocument;
        (double azimuth, double elevation) = document is null ? (0, 0) : SunAngles.Of(document.Lighting.SunDirection);
        return new JsonObject
        {
            ["information"] = new JsonObject
            {
                ["name"] = document?.Scenario.Name,
                ["author"] = document?.Scenario.Author,
                ["version"] = document?.Version,
                ["type"] = document is null ? null : MapInformationViewModel.TypeName(document.Scenario.Type),
                ["size"] = MapInformation.Size,
                ["noRushRadius"] = document?.Scenario.NoRushRadius,
                ["adaptive"] = document?.Scenario.AdaptiveMap,
            },
            ["areas"] = new JsonArray([.. AreaList.Items.Select(item => (JsonNode)JsonValue.Create(item.Rectangle.Length > 0 ? $"{item.Area.Name}: {item.Rectangle}" : item.Area.Name))]),
            ["script"] = ScriptFiles.Script,
            ["files"] = ScriptFiles.Summary,
            ["water"] = document is null ? null : new JsonObject
            {
                ["on"] = document.Water.HasWater,
                ["elevation"] = document.Water.Elevation,
                ["deep"] = document.Water.ElevationDeep,
                ["abyss"] = document.Water.ElevationAbyss,
                ["waveGenerators"] = document.Water.WaveGenerators.Length,
            },
            ["lighting"] = document is null ? null : new JsonObject
            {
                ["azimuth"] = Math.Round(azimuth, 1),
                ["elevation"] = Math.Round(elevation, 1),
                ["multiplier"] = document.Lighting.Multiplier,
            },
            ["layers"] = new JsonArray([.. LayerTextures.Items.Select(item => (JsonNode)JsonValue.Create($"{item.Index} {item.Title}: {item.Textures}"))]),
            ["chosenLayer"] = LayerTextures.Selected?.Index,
            ["ruler"] = Tool<RulerTool>().Measured?.ToString(),
            ["newMapPageOpen"] = NewMapPage.IsOpen,
        };
    }

    private (string Name, System.Text.Json.Nodes.JsonObject Settings) CaptureLighting()
    {
        MapDocument document = ShownDocument ?? throw new InvalidOperationException("Open a map whose lighting to save");
        string name = $"{document.Scenario.Name} lighting";
        return (name, AtmospherePresets.OfLighting(name, document.Lighting).Settings);
    }

    private void ApplyLighting(Preset preset)
    {
        Lighting lighting = AtmospherePresets.ReadLighting(preset);
        EditMap($"Apply the lighting {preset.Name}", document => document with { Lighting = lighting });
    }

    private (string Name, System.Text.Json.Nodes.JsonObject Settings) CaptureWater()
    {
        MapDocument document = ShownDocument ?? throw new InvalidOperationException("Open a map whose water to save");
        string name = $"{document.Scenario.Name} water";
        return (name, AtmospherePresets.OfWater(name, document.Water).Settings);
    }

    private void ApplyWater(Preset preset)
    {
        RequireMap();
        EditMap($"Apply the water {preset.Name}", document => document with { Water = AtmospherePresets.ApplyWater(preset, document.Water) });
    }
}
