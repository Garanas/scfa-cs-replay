using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The lists of the Water and Lighting panels (<see cref="PanelParts"/>): the panel says what there
/// is, the inspector edits the chosen entry. A water map chosen makes the Paint water tool paint
/// it, the wave generators chosen choose the Waves tool; and back, a tool chosen shows its entry.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The lists of entries by mode: <c>water</c>, <c>lighting</c>.
    /// </summary>
    public IReadOnlyDictionary<string, PanelParts> PanelPartLists { get; private set; } = new Dictionary<string, PanelParts>();

    /// <summary>
    /// The parts of the selected mode's panel that show: those for the active tool
    /// (<see cref="PanelSection.ForTools"/>) and the chosen entry of its list (<see cref="PanelSection.ForParts"/>).
    /// </summary>
    public IReadOnlyList<PanelSection> PanelSections { get; private set; } = [];

    private void InitializePanelParts()
    {
        PanelPartLists = new Dictionary<string, PanelParts>(StringComparer.Ordinal)
        {
            ["water"] = WaterSettings.Parts,
            ["lighting"] = LightingSettings.Parts,
            ["decals"] = WideDecals.Parts,
            ["procedural"] = ProceduralList.Parts,
        };
        Presets.Register(new PresetKind(AtmospherePresets.SkyKind, "Sky presets", CaptureSky, ApplySky));
        foreach (PanelParts list in PanelPartLists.Values)
        {
            list.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PanelParts.Chosen))
                {
                    RefreshPanelSections();
                }
            };
        }
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SelectedMode) or nameof(ActiveTool))
            {
                RefreshPanelSections();
            }
        };
        RefreshPanelSections();
        WaterPaintTool brush = Tool<WaterPaintTool>();
        foreach (WaterMap map in WaterMaps.All)
        {
            WaterSettings.Parts.Replace(map.PartId, new WaterMapPart(map, brush));
        }

        WaterSettings.Parts.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PanelParts.Chosen) || SelectedMode?.Definition.Id != "water")
            {
                return;
            }
            if (WaterSettings.Parts.Chosen.Content is WaterMapPart part)
            {
                if (brush.Painted.Mask != part.Map.Mask)
                {
                    brush.Choose(part.Map.Mask.ToString());
                }
                if (ActiveTool?.Definition.Id != "water.paint")
                {
                    SelectTool("water.paint");
                }
            }
            else if (WaterSettings.Parts.Chosen.Id == WaterSettingsViewModel.WavesPart && ActiveTool?.Definition.Id != "water.waves")
            {
                SelectTool("water.waves");
            }
        };
        brush.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WaterPaintTool.Painted) && ActiveTool?.Tool == brush)
            {
                WaterSettings.Parts.Choose(WaterMaps.Of(brush.Painted.Mask).PartId);
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ActiveTool))
            {
                return;
            }
            switch (ActiveTool?.Definition.Id)
            {
                case "water.paint":
                    WaterSettings.Parts.Choose(WaterMaps.Of(brush.Painted.Mask).PartId);
                    break;
                case "water.waves":
                    WaterSettings.Parts.Choose(WaterSettingsViewModel.WavesPart);
                    break;
                case "lighting.sun":
                    if (LightingSettings.Parts.Chosen.Content is not SettingsPart)
                    {
                        LightingSettings.Parts.Choose(LightingSettingsViewModel.SunPart);
                    }
                    break;
            }
        };
    }

    /// <summary>
    /// Works out <see cref="PanelSections"/> again, and the kind of presets the panel lists; the panel
    /// makes its parts again only when they changed.
    /// </summary>
    private void RefreshPanelSections()
    {
        if (SelectedMode is not ModeItem mode)
        {
            return;
        }
        string? tool = ActiveTool?.Definition.Id;
        string? part = PanelPartLists.GetValueOrDefault(mode.Definition.Id)?.Chosen.Id;
        PanelSection[] shown = [.. mode.Definition.Sections.Where(section =>
            (section.ForTools is null || tool is not null && section.ForTools.Contains(tool))
            && (section.ForParts is null || part is not null && section.ForParts.Contains(part)))];
        if (!shown.SequenceEqual(PanelSections))
        {
            PanelSections = shown;
            OnPropertyChanged(nameof(PanelSections));
        }
        Presets.FollowSections(PanelSections);
    }

    private (string Name, JsonObject Settings) CaptureSky()
    {
        MapDocument document = ShownDocument ?? throw new InvalidOperationException("Open a map whose sky to save");
        string name = $"{document.Scenario.Name} sky";
        return (name, AtmospherePresets.OfSky(name, document.Sky).Settings);
    }

    private void ApplySky(Preset preset)
    {
        Sky sky = AtmospherePresets.ReadSky(preset);
        RequireMap();
        EditMap($"Apply the sky {preset.Name}", document => document with { Sky = sky });
    }

    /// <summary>
    /// Shows an entry of a mode panel's list in the inspector, choosing its mode first.
    /// </summary>
    /// <param name="id">E.g. <c>water.foam</c>, <c>lighting.sky</c>.</param>
    /// <exception cref="ArgumentException">No panel lists it.</exception>
    public void InspectPart(string id)
    {
        (string mode, PanelParts parts) = PanelPartLists.FirstOrDefault(list => list.Value.Find(id) is not null) is { Value: not null } found
            ? (found.Key, found.Value)
            : throw new ArgumentException($"No panel lists the part '{id}': {string.Join(", ", PanelPartLists.Values.SelectMany(list => list.Items).Select(part => part.Id))}");
        if (SelectedMode?.Definition.Id != mode)
        {
            SelectMode(mode);
        }
        parts.Choose(id);
    }

    /// <summary>
    /// The lists of entries for automation: per mode the chosen entry and every entry with its detail.
    /// </summary>
    public JsonObject DescribePanelParts() => new JsonObject([.. PanelPartLists.Select(list => KeyValuePair.Create(list.Key, (JsonNode?)new JsonObject
    {
        ["chosen"] = list.Value.Chosen.Id,
        ["content"] = list.Value.Chosen.Content.GetType().Name,
        ["items"] = new JsonArray([.. list.Value.Items.Select(part => (JsonNode)new JsonObject
        {
            ["id"] = part.Id,
            ["title"] = part.Title,
            ["group"] = part.Group,
            ["detail"] = part.Detail,
        })]),
    }))]);
}
