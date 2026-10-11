using System.Text.Json.Nodes;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Who has the inspector (<see cref="MainViewModel.InspectorOwner"/>): what was chosen last of a
/// tile of the shelf or picker, things on the map, and a part of the mode.
/// </summary>
public enum InspectorOwner
{
    /// <summary>
    /// A part of the mode: the mode itself (on the rail, its key, the Modes menu), an entry of its
    /// panel's list, or an item of a list or tree in its panel (a stratum, a height layer, a group).
    /// </summary>
    Part,

    /// <summary>
    /// The things selected on the map, in the modes of things.
    /// </summary>
    Selection,

    /// <summary>
    /// A tile chosen on the shelf or in the picker.
    /// </summary>
    Tile,
}

/// <summary>
/// What the inspector shows of the mode: the parts of the active tool (a part with
/// <see cref="PanelSection.ForTools"/> only for those tools, with <see cref="PanelSection.ForParts"/>
/// only while one of those entries of the panel's list is chosen), and the selection only in the
/// modes that work on things on the map, while the selection owns the inspector.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The modes whose tools pick things on the map, where the inspector shows the selection.
    /// </summary>
    private static readonly HashSet<string> ModesOfThings = new HashSet<string>(StringComparer.Ordinal) { "map", "markers", "props", "decals", "units" };

    /// <summary>
    /// The mode's parts of the inspector for the active tool and the chosen entry of the panel's
    /// list: the Paint mode's strata tool shows the stratum, its masks tool the mask rules.
    /// </summary>
    public IReadOnlyList<PanelSection> InspectorParts { get; private set; } = [];

    /// <summary>
    /// Whether the mode has no part of its own to show in the inspector (the Markers mode): the
    /// inspector then says how to select things, and the selection keeps it.
    /// </summary>
    public bool HasNoInspectorParts => InspectorParts.Count == 0;

    /// <summary>
    /// What was chosen last of a tile, things on the map and a part of the mode: the inspector
    /// shows it. Choosing a part keeps the selection (still highlighted, still what Delete and the
    /// buttons that read the selection act on); a selection changed afterwards, by a click or a
    /// command, takes the inspector back.
    /// </summary>
    public InspectorOwner InspectorOwner
    {
        get => _inspectorOwner;
        private set
        {
            if (_inspectorOwner != value)
            {
                _inspectorOwner = value;
                OnPropertyChanged();
                NotifyInspected();
            }
        }
    }

    private InspectorOwner _inspectorOwner = InspectorOwner.Part;

    /// <summary>
    /// Whether the inspector shows the selection: something is selected, the mode works on things on
    /// the map, and the selection was chosen after a part of the mode (or the mode has no part of
    /// its own to show).
    /// </summary>
    public bool InspectsSelection => Selection.HasAny && !Assets.HasSelection && SelectedMode is ModeItem mode && ModesOfThings.Contains(mode.Definition.Id)
        && (InspectorOwner == InspectorOwner.Selection || HasNoInspectorParts);

    /// <summary>
    /// Whether the inspector shows the mode's parts: neither an asset of the shelf nor the selection.
    /// </summary>
    public bool InspectsMode => !Assets.HasSelection && !InspectsSelection;

    /// <summary>
    /// Gives the inspector to the mode's part: the tile chosen on the shelf is unchosen, and the
    /// selection stays as it is (still highlighted on the map). The rule of who has the inspector:
    /// what was chosen last of a shelf tile, things on the map and a part of the mode (a mode on
    /// the rail, an entry of the panel's lists, an item of a list or tree in the panel). The tools
    /// keep what they took from a tile (the unit, prop or decal they place, the brush's shape).
    /// </summary>
    public void InspectTheMode()
    {
        if (Assets.HasSelection)
        {
            Assets.Select((AssetTile?)null);
        }
        InspectorOwner = InspectorOwner.Part;
    }

    /// <summary>
    /// Gives the inspector to the selection, as a click on the map does; nothing changes while
    /// nothing is selected.
    /// </summary>
    public void InspectTheSelection()
    {
        if (Selection.HasAny)
        {
            InspectorOwner = InspectorOwner.Selection;
        }
    }

    private void InitializeInspectorParts()
    {
        Selection.Changed += (_, _) =>
        {
            // a selection changed by a click or a command takes the inspector; one that only lost
            // what an undo took away leaves it where it is
            if (Selection.HasAny && !Selection.IsPruning)
            {
                InspectorOwner = InspectorOwner.Selection;
            }
            else if (!Selection.HasAny && InspectorOwner == InspectorOwner.Selection)
            {
                InspectorOwner = InspectorOwner.Part;
            }
            NotifyInspected();
        };
        Assets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetsViewModel.HasSelection))
            {
                if (Assets.HasSelection)
                {
                    InspectorOwner = InspectorOwner.Tile;
                }
                else if (InspectorOwner == InspectorOwner.Tile)
                {
                    InspectorOwner = Selection.HasAny ? InspectorOwner.Selection : InspectorOwner.Part;
                }
                NotifyInspected();
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SelectedMode) or nameof(ActiveTool))
            {
                RefreshInspectorParts();
                NotifyInspected();
            }
        };
        RefreshInspectorParts();
    }

    /// <summary>
    /// Works out <see cref="InspectorParts"/> again (also when another entry of the panel's list is
    /// chosen, <see cref="RefreshPanelSections"/>); the inspector makes its parts again only when
    /// they changed.
    /// </summary>
    private void RefreshInspectorParts()
    {
        PanelSection[] shown = SelectedMode is not ModeItem mode ? []
            : [.. mode.Definition.Inspector.Where(part => Shows(part, mode.Definition.Id))];
        if (!shown.SequenceEqual(InspectorParts))
        {
            bool had = HasNoInspectorParts;
            InspectorParts = shown;
            OnPropertyChanged(nameof(InspectorParts));
            if (had != HasNoInspectorParts)
            {
                OnPropertyChanged(nameof(HasNoInspectorParts));
                NotifyInspected();
            }
        }
    }

    /// <summary>
    /// Whether a part of a mode's panel or inspector shows now: it is for the active tool
    /// (<see cref="PanelSection.ForTools"/>) and the chosen entry of the mode's list
    /// (<see cref="PanelSection.ForParts"/>).
    /// </summary>
    private bool Shows(PanelSection section, string mode) =>
        Shows(section, ActiveTool?.Definition.Id, PanelPartLists.GetValueOrDefault(mode)?.Chosen.Id);

    /// <summary>
    /// Whether a part of a mode's panel or inspector shows while a tool is active and an entry of
    /// the mode's list is chosen.
    /// </summary>
    /// <param name="tool">The active tool's id; null for none.</param>
    /// <param name="part">The id of the entry chosen in the mode panel's list; null when the mode has no list.</param>
    public static bool Shows(PanelSection section, string? tool, string? part) =>
        (section.ForTools is null || tool is not null && section.ForTools.Contains(tool))
        && (section.ForParts is null || part is not null && section.ForParts.Contains(part));

    private void NotifyInspected()
    {
        OnPropertyChanged(nameof(InspectsSelection));
        OnPropertyChanged(nameof(InspectsMode));
    }

    /// <summary>
    /// Who has the inspector for automation's <c>state</c>: the owner (<c>part</c>, <c>selection</c>,
    /// <c>tile</c>), what it shows (<c>asset</c>, <c>selection</c>, <c>mode</c>) and the mode's parts.
    /// </summary>
    public JsonObject DescribeInspector() => new JsonObject
    {
        ["owner"] = InspectorOwner switch { InspectorOwner.Selection => "selection", InspectorOwner.Tile => "tile", _ => "part" },
        ["shows"] = Assets.HasSelection ? "asset" : InspectsSelection ? "selection" : "mode",
        ["parts"] = new JsonArray([.. InspectorParts.Select(part => (JsonNode)JsonValue.Create(part.GetType().Name))]),
    };
}
