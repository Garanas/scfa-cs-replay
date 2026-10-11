using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Procedural mode as a list (the panel lists, the inspector edits): an entry per generated
/// result with how it stands, a picture and Generate; the chosen one's settings and details in the
/// inspector; the export's steps last. Choosing the navigation mesh or the path networks picks their
/// tool, and picking the tool shows its entry.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The Procedural panel's entries (<c>procedural.walkability</c>, ..., <c>procedural.export</c>).
    /// </summary>
    public ProceduralListViewModel ProceduralList { get; private set; } = null!;

    private void InitializeProceduralParts()
    {
        ProceduralList = new ProceduralListViewModel(Generation, Procedural, Export, () => Map?.Document, RunWithAsync, new ProceduralExportPart(this));
        const string movement = "Movement";
        const string surface = "Terrain surface";
        const string rules = "From rules";
        const string pictures = "Pictures";
        ProceduralList.Describe(new ProceduralEntry(WalkabilityGenerator.GeneratorId, movement)
        {
            Details = new ProceduralWalkabilityDetails(this),
            Picture = (_, result) => result?.Value is MapNavigation navigation ? new NavGridPicture(navigation[Walkability.Layer]) : null,
            Help = "Where units can go, as the game works it out when the map loads: white where the chosen layer of units goes",
        });
        ProceduralList.Describe(new ProceduralEntry(NavigationMeshGenerator.GeneratorId, movement)
        {
            Details = new ProceduralNavigationDetails(this),
            Picture = (_, result) => result?.Value is NavigationMesh mesh ? new NavGridPicture(mesh.Navigation[Navigation.Layer], Regions: true) : null,
            Tool = "procedural.regions",
            Help = "The regions units of a layer can move in, with their starts and deposits, and routes between the starts; the Regions tool chooses one",
        });
        ProceduralList.Describe(new ProceduralEntry(PathNetworkGenerator.GeneratorId, movement)
        {
            Details = PathNodes,
            Tool = "procedural.pathNodes",
            OwnGenerate = true,
            Help = "Path nodes along the middle of every region, previewed in the views and written into the markers on export; the Path nodes tool edits them by hand",
        });
        ProceduralList.Describe(new ProceduralEntry(NormalMapGenerator.GeneratorId, surface)
        {
            // a new map's normal map is a placeholder of one colour: no picture until it is made
            Picture = (document, result) => document is null || result is null ? null : new ColourGridPicture(document.Terrain.NormalMap),
            Facts = document => document is null ? [] : [$"{document.Terrain.NormalMap.Width} by {document.Terrain.NormalMap.Height} pixels, {document.Terrain.NormalMapFormat}"],
            Help = "The terrain's normal map, made from the final heightmap at the size of the map's own",
        });
        ProceduralList.Describe(WideDecalEntry(WideDecals.Normal, NormalDecalGenerator.GeneratorId, surface));
        ProceduralList.Describe(WideDecalEntry(WideDecals.Shadow, ShadowDecalGenerator.GeneratorId, surface));
        ProceduralList.Describe(new ProceduralEntry(MaskRulesGenerator.GeneratorId, rules)
        {
            Facts = document => document is null ? [] : [RulesLayers(document), "The rules are layers of a mask's or stratum's stack: add and edit them in the Masks and Paint modes"],
            Help = "The rules layers of the masks and strata, made from their rules",
        });
        ProceduralList.Describe(new ProceduralEntry(PropRulesGenerator.GeneratorId, rules)
        {
            Facts = document => document is null ? [] :
            [
                document.PropRules.IsEmpty ? "The map has no prop rules" : $"{Count(document.PropRules.Length, "prop rule")}; they placed {Count(document.CountRuleProps(), "prop")}",
                "Generating places the props of every rule again; the props placed by hand stay. The rules are edited in the Props mode",
            ],
            Help = "The props the prop rules place",
        });
        ProceduralList.Describe(new ProceduralEntry(MapPreview.GeneratorId, pictures)
        {
            Picture = (document, _) => document is null ? null : new ColourGridPicture(document.Terrain.Preview),
            Facts = document => document is null ? [] : [$"{document.Terrain.Preview.Width} by {document.Terrain.Preview.Height} pixels, drawn with the strata's textures and lit by the map's sun"],
            Help = "The picture of the map the game and the vault show",
        });
        if (ProceduralList.Parts.Items.Count > 1)
        {
            ProceduralList.Parts.Choose(ProceduralList.Parts.Items[0].Id);
        }

        foreach (MapWideSlotViewModel slot in new[] { WideDecals.Normal, WideDecals.Shadow })
        {
            slot.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MapWideSlotViewModel.Preview) or nameof(MapWideSlotViewModel.Slot) or nameof(MapWideSlotViewModel.Status))
                {
                    ProceduralList.Refresh();
                }
            };
        }
        Navigation.Changed += (_, _) => ProceduralList.Refresh();
        Walkability.Changed += (_, _) => ProceduralList.Refresh();
        DocumentChanged += (_, _) => ProceduralList.Refresh();

        // an entry with a tool picks it; the tool picked shows its entry
        ProceduralList.Parts.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelParts.Chosen) && SelectedMode?.Definition.Id == ProceduralMode.Id
                && ToolOf(ProceduralList.Parts.Chosen) is string tool && ActiveTool?.Definition.Id != tool)
            {
                SelectTool(tool);
            }
        };
        string? mode = null;
        bool entering = false;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedMode))
            {
                string? before = mode;
                mode = SelectedMode?.Definition.Id;
                entering = mode == ProceduralMode.Id && before != mode;
                return;
            }
            if (e.PropertyName != nameof(ActiveTool) || SelectedMode?.Definition.Id != ProceduralMode.Id || ActiveTool is null)
            {
                return;
            }
            string id = ActiveTool.Definition.Id;
            // coming into the mode with its first tool keeps the chosen entry; any other tool shows its own
            bool keep = entering && id == ProceduralMode.Tools[0].Id;
            entering = false;
            if (keep || ToolOf(ProceduralList.Parts.Chosen) == id)
            {
                return;
            }
            if (ProceduralList.Results.FirstOrDefault(result => result.Entry.Tool == id) is ProceduralPart part)
            {
                ProceduralList.Parts.Choose(part.PartId);
            }
        };
    }

    private static string? ToolOf(PanelPart part) => (part.Content as ProceduralPart)?.Entry.Tool;

    private ProceduralEntry WideDecalEntry(MapWideSlotViewModel slot, string generatorId, string group) => new ProceduralEntry(generatorId, group)
    {
        Details = slot,
        Picture = (_, _) => slot.Preview,
        Detail = () => slot.Slot.Source == MapWideSource.Generated ? null : slot.Detail,
        CanGenerate = () => slot.CanGenerate,
        Facts = _ => [slot.Status],
        Help = slot.Help,
    };

    private static string RulesLayers(MapDocument document)
    {
        int layers = document.CoverageStacks.Values.Sum(stack => stack.Layers.Count(layer => layer.Source is RulesSource));
        return layers == 0 ? "No mask or stratum has a rules layer" : $"{Count(layers, "rules layer")} in the stacks of the masks and strata";
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)} {noun}s";

    /// <summary>
    /// The Procedural list for automation (<c>state.procedural.list</c>): the chosen entry and every
    /// entry with its state, picture and tool.
    /// </summary>
    public JsonObject DescribeProceduralList() => new JsonObject
    {
        ["chosen"] = ProceduralList.Parts.Chosen.Id,
        ["entries"] = new JsonArray([.. ProceduralList.Parts.Items.Select(part => (JsonNode)new JsonObject
        {
            ["id"] = part.Id,
            ["title"] = part.Title,
            ["group"] = part.Group,
            ["detail"] = part.Detail,
            ["state"] = (part.Content as ProceduralPart)?.StateText,
            ["picture"] = (part.Content as ProceduralPart)?.HasPicture,
            ["tool"] = (part.Content as ProceduralPart)?.Entry.Tool,
            ["made"] = (part.Content as ProceduralPart)?.MadeText,
        })]),
    };
}
