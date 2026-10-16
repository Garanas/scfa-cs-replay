using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A chain of the Markers panel's list, in the inspector while it is chosen (user story R-4): its
/// name, its markers in order with up, down and take out, Select and Delete.
/// </summary>
public sealed partial class ChainPart(MarkersViewModel owner, string name) : ObservableObject
{
    public MarkersViewModel Owner => owner;

    /// <summary>
    /// The chain's name, which names its entry (a renamed chain is another entry).
    /// </summary>
    public string Name => name;

    /// <summary>
    /// The chain as the map has it now; null once it is gone.
    /// </summary>
    [ObservableProperty]
    public partial ChainItem? Chain { get; internal set; }

    /// <summary>
    /// Its name field: typing another renames it (<c>markers.renameChain</c>); an empty one is not taken.
    /// </summary>
    public string NameText
    {
        get => name;
        set
        {
            string trimmed = value.Trim();
            if (trimmed.Length == 0 || trimmed == name)
            {
                // a chain keeps a name: show it again
                OnPropertyChanged();
                return;
            }
            _ = ShowNameAfter(owner.Run("markers.renameChain", new JsonObject { ["name"] = name, ["to"] = trimmed }));
        }
    }

    /// <summary>
    /// A name the map refused (another chain has it) is not left in the field: it shows the chain's own again.
    /// </summary>
    private async Task ShowNameAfter(Task rename)
    {
        try
        {
            await rename;
        }
        finally
        {
            OnPropertyChanged(nameof(NameText));
        }
    }

    /// <summary>
    /// Selects its markers on the map.
    /// </summary>
    [RelayCommand]
    private Task Select() => Chain is ChainItem chain
        ? owner.Run("select.set", new JsonObject { ["refs"] = new JsonArray([.. chain.Entries.Select(entry => (JsonNode)JsonValue.Create(ObjectRef.Marker(entry.Id).ToString()))]) })
        : Task.CompletedTask;

    [RelayCommand]
    private Task Delete() => owner.Run("markers.deleteChain", new JsonObject { ["name"] = name });
}

/// <summary>
/// The adaptive map's entry of the Markers panel's list, in the inspector while it is chosen (user
/// story R-5): whether the map adapts, the deposits per army, the groups by lobby option.
/// </summary>
public sealed record AdaptivePart(MarkersViewModel Markers);

/// <summary>
/// An entry of the Markers panel's list (a chain, the adaptive map), with the list that chooses it.
/// </summary>
public record MarkerListRow(PanelPart Part, PanelParts Parts);

/// <summary>
/// The entry of a kind or a layer of path nodes, whose row shows its show check box and colour.
/// </summary>
public sealed record MarkerKindRow(PanelPart Part, MarkerKindItem Kind, PanelParts Parts) : MarkerListRow(Part, Parts);

/// <summary>
/// A heading of the Markers panel's list with its entries, and the overlay its check box turns on
/// and off (Markers, Path nodes); null for none.
/// </summary>
public sealed record MarkerListGroup(string Title, OverlayItem? Overlay, IReadOnlyList<MarkerListRow> Rows)
{
    public bool HasOverlay => Overlay is not null;

    /// <summary>
    /// The name of its check box: <c>Show markers</c>, <c>Show path nodes</c>.
    /// </summary>
    public string OverlayLabel => $"Show {Title.ToLowerInvariant()}";
}

/// <summary>
/// The Markers panel's list (the panel lists, the inspector edits): under Markers a kind each with its
/// colour, count and show check box, under Path nodes a layer each, under Chains and options a chain
/// each (kept in step with the map, <see cref="PanelParts.SyncGroup"/>) and the adaptive map.
/// </summary>
public sealed partial class MarkersViewModel
{
    /// <summary>
    /// The heading of the kinds, and of the overlay Markers.
    /// </summary>
    public const string KindsGroup = "Markers";

    /// <summary>
    /// The heading of the layers of path nodes, and of the overlay Path nodes.
    /// </summary>
    public const string PathsGroup = "Path nodes";

    public const string ChainsGroup = "Chains and options";

    /// <summary>
    /// The adaptive map's entry, after the chains.
    /// </summary>
    public const string AdaptivePartId = "markers.adaptive";

    /// <summary>
    /// The entry of a chain: <c>markers.chain.Chain_1</c>.
    /// </summary>
    public static string ChainPartId(string name) => $"markers.chain.{name}";

    /// <summary>
    /// The panel's list; the inspector shows the chosen entry (<see cref="MarkerDetailsSection"/>).
    /// </summary>
    public PanelParts Parts { get; }

    /// <summary>
    /// The list by heading with each row's check box, as the panel draws it.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<MarkerListGroup> ListGroups { get; private set; } = [];

    private IReadOnlyList<OverlayItem> _overlays = [];

    private PanelParts MakeParts()
    {
        PanelParts parts = new PanelParts([
            .. Kinds.Select(kind => new PanelPart(kind.PartId, kind.Title, kind.IsPathLayer ? PathsGroup : KindsGroup, kind,
                kind.IsPathLayer ? $"The map's own {Noun(kind.Kind, 1)}s: placed by hand, imported, or written by an older editor" : $"The map's {Noun(kind.Kind, 1)}s")),
            AdaptiveEntry(),
        ]);
        parts.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelParts.Groups))
            {
                MakeListGroups();
            }
            else if (e.PropertyName == nameof(PanelParts.Chosen) && parts.Chosen.Content is ChainPart chain && SelectedChain?.Name != chain.Name)
            {
                // a chain chosen in the list is the one the chain tool adds to
                ChooseChain(chain.Name);
            }
        };
        return parts;
    }

    private PanelPart AdaptiveEntry() => new PanelPart(AdaptivePartId, "Adaptive map", ChainsGroup, new AdaptivePart(this),
        "Deposits and groups of units that the game leaves out by the armies that play and the lobby's options");

    /// <summary>
    /// Gives the headings Markers and Path nodes the check boxes of their overlays.
    /// </summary>
    internal void UseOverlays(IReadOnlyList<OverlayItem> overlays)
    {
        _overlays = overlays;
        MakeListGroups();
    }

    private void MakeListGroups()
    {
        ListGroups = [.. Parts.Groups.Select(group => new MarkerListGroup(group.Title,
            _overlays.FirstOrDefault(overlay => overlay.Definition.Panel == "markers" && overlay.Title == group.Title),
            [.. group.Parts.Select(part => part.Content is MarkerKindItem kind ? new MarkerKindRow(part, kind, Parts) : new MarkerListRow(part, Parts))]))];
    }

    /// <summary>
    /// The counts per kind and layer, the links per layer and what the export does with the
    /// generated networks, in one pass over the markers.
    /// </summary>
    private void RefreshKinds(MapDocument? document)
    {
        Dictionary<MarkerKinds, int> counts = [];
        if (document is not null)
        {
            foreach (Marker marker in document.Markers.All)
            {
                MarkerKinds kind = MarkerEdits.KindOf(marker);
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
        }
        foreach (MarkerKindItem item in Kinds)
        {
            int count = counts.GetValueOrDefault(item.Kind);
            PathLayer? layer = Choices.First(choice => choice.Kind == item.Kind).Layer;
            int links = document is null || layer is null || count == 0 ? 0 : document.CountLinks(item.Kind);
            item.Refresh(count, links, layer is PathLayer l && document is not null ? Export(document.Procedural.PathNodes, l) : ("", ""));
            if (Parts.Find(item.PartId) is PanelPart part)
            {
                part.Detail = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// How the export treats the Procedural mode's generated network of a layer beside the map's own
    /// path nodes of it (<see cref="PathNodeSettings.Replace"/>, <see cref="PathNodeSettings.OnExport"/>).
    /// </summary>
    public static (string Text, string Fact) Export(PathNodeSettings settings, PathLayer layer)
    {
        string name = MarkerKindChoice.LayerName(layer);
        if (!settings.WritesOnExport)
        {
            return ($"The Procedural mode's generated networks are not written on export: the export writes these {name} path nodes as they are.", "not written");
        }
        if (!settings.Layers.Contains(layer))
        {
            return ($"The Procedural mode generates no {name} network: the export writes these {name} path nodes as they are.", "no network");
        }
        return settings.Replace
            ? ($"Replacing: the export writes the Procedural mode's generated {name} network and leaves these nodes out. Clearing them changes nothing in the export.", "replace at export")
            : ($"Joined: the export writes the Procedural mode's generated {name} network beside these nodes, each piece of them linked to it. Clear them to make the generated network the only one.", "joined at export");
    }

    /// <summary>
    /// Keeps the chains' entries in step with the map's chains, the adaptive map's entry after them.
    /// </summary>
    private void SyncChainParts()
    {
        PanelPartEntry[] entries =
        [
            .. Chains.Select(chain => new PanelPartEntry(ChainPartId(chain.Name), chain.Name, () => new ChainPart(this, chain.Name), ChainDetail(chain),
                "A chain of markers in order, such as a patrol route that a script reads")),
            new PanelPartEntry(AdaptivePartId, "Adaptive map", () => new AdaptivePart(this), AdaptiveDetail()),
        ];
        Parts.SyncGroup(ChainsGroup, entries);
        foreach (PanelPart part in Parts.Items)
        {
            if (part.Content is ChainPart chainPart)
            {
                chainPart.Chain = Chains.FirstOrDefault(chain => chain.Name == chainPart.Name);
            }
        }
    }

    private static string ChainDetail(ChainItem chain) => chain.Entries.Count == 1 ? "1 marker" : $"{chain.Entries.Count} markers";

    private string AdaptiveDetail() => !IsAdaptive ? "off" : OptionalGroups.Count == 1 ? "on · 1 group" : $"on · {OptionalGroups.Count} groups";

    partial void OnIsAdaptiveChanged(bool value) => RefreshAdaptiveDetail();

    partial void OnOptionalGroupsChanged(IReadOnlyList<OptionalGroupItem> value) => RefreshAdaptiveDetail();

    private void RefreshAdaptiveDetail()
    {
        if (Parts?.Find(AdaptivePartId) is PanelPart part)
        {
            part.Detail = AdaptiveDetail();
        }
    }

    /// <summary>
    /// The entry of the chain the panel chose for the chain tool, when it has one (after a chain was
    /// made, renamed or clicked into being with the tool).
    /// </summary>
    partial void OnSelectedChainChanged(ChainItem? oldValue, ChainItem? newValue)
    {
        if (newValue is not null && !_refreshingChains)
        {
            ChooseChainEntry(newValue);
        }
    }

    private void ChooseChainEntry(ChainItem chain)
    {
        if (Parts?.Find(ChainPartId(chain.Name)) is PanelPart part && Parts.Chosen != part)
        {
            Parts.Choose(part.Id);
        }
    }
}
