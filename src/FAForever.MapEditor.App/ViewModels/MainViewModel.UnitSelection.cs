using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// How a click or box picks a unit.
/// </summary>
public enum UnitPicking
{
    /// <summary>
    /// A structure by its footprint (the rectangle it takes up, turned by its heading); a mobile unit by its centre.
    /// </summary>
    Footprint,

    /// <summary>
    /// Every unit by its centre, within a few pixels.
    /// </summary>
    Centre,
}

/// <summary>
/// Choosing units (user story U-10): how a click picks them, and selecting them by what they are
/// (<c>select.where</c>), and the selections stored under the digits.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// How the select tools pick a unit: by the structure's footprint, or by every unit's centre.
    /// </summary>
    [ObservableProperty]
    public partial UnitPicking UnitPicks { get; set; } = UnitPicking.Footprint;

    /// <summary>
    /// What a structure takes up, for <see cref="MapObjects.Nearest"/> and <see cref="MapObjects.Inside"/>:
    /// the catalogue's footprint of a structure (1 by 1 when it names none), nothing for the other
    /// units and for a blueprint the catalogue lacks; null while units are picked by their centre.
    /// </summary>
    public MapObjects.UnitFootprints? UnitFootprintsForPicking => UnitPicks == UnitPicking.Footprint ? StructureFootprint : null;

    /// <summary>
    /// A structure's footprint from the catalogue: null for any other blueprint.
    /// </summary>
    public (double SizeX, double SizeZ)? StructureFootprint(string blueprint) =>
        Assets.Catalog.Find(blueprint) is { IsStructure: true } entry ? entry.Footprint ?? (1, 1) : null;

    /// <summary>
    /// The units that are of what <paramref name="query"/> asks, among those <paramref name="within"/> names, picked as a
    /// click would (not what is isolated away).
    /// </summary>
    /// <param name="within"><c>map</c>, <c>selection</c> or <c>area:NAME</c>.</param>
    /// <exception cref="ArgumentException">An area the map lacks, or a place that is none of the three.</exception>
    public string[] UnitsWhere(UnitQuery query, string within = "map")
    {
        MapDocument document = ShownDocument ?? throw new InvalidOperationException("No map is open");
        using Performance.Span measure = Performance.Measure("model.select-where");
        IReadOnlySet<string>? names = WithinOf(document, within);
        return [.. query.Select(document, Assets.Catalog.Find, names).Where(name => IsPickable(ObjectRef.Unit(name)))];
    }

    /// <summary>
    /// Selects the units of <see cref="UnitsWhere"/>, replacing the selection or adding to it.
    /// </summary>
    /// <returns>How many are selected afterwards.</returns>
    public int SelectWhere(UnitQuery query, string within = "map", bool add = false)
    {
        string[] names = UnitsWhere(query, within);
        Selection.Apply(names.Select(ObjectRef.Unit), add ? SelectionChange.Add : SelectionChange.Replace);
        return Selection.Count;
    }

    /// <summary>
    /// The names of the units a place holds: null for the whole map.
    /// </summary>
    private HashSet<string>? WithinOf(MapDocument document, string within)
    {
        string place = within.Trim();
        if (place.Length == 0 || place.Equals("map", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (place.Equals("selection", StringComparison.OrdinalIgnoreCase))
        {
            return [.. Selection.Items.Where(thing => thing.Kind == ObjectKind.Unit).Select(thing => thing.Name!)];
        }
        if (place.StartsWith("area:", StringComparison.OrdinalIgnoreCase))
        {
            string name = place[5..];
            Area area = document.Markers.Areas.FirstOrDefault(a => a.Name == name) ?? throw new ArgumentException($"The map has no area '{name}'");
            return [.. document.Inside((float)area.X0, (float)area.Z0, (float)area.X1, (float)area.Z1, ObjectKinds.Units).Select(thing => thing.Name!)];
        }
        throw new ArgumentException($"'{within}' is no place to select within: map, selection or area:<name>");
    }

    /// <summary>
    /// The popover of the select tool's Select by button.
    /// </summary>
    public SelectByViewModel SelectBy { get; private set; } = null!;

    /// <summary>
    /// The selections stored under the digits 1 to 9, kept with the project.
    /// </summary>
    public StoredSelectionsViewModel StoredSelections { get; private set; } = null!;

    private long _storedRefreshedAt;

    private void InitializeUnitSelection()
    {
        SelectBy = new SelectByViewModel(this);
        StoredSelections = new StoredSelectionsViewModel(this, Logger("Selection"));
        // another map has its own stored selections; a resize of the same map keeps them
        AnotherMap += (_, _) => StoredSelections.Open(Map?.ProjectFolder);
        DocumentChanged += (_, _) =>
        {
            if (StoredSelections.HasChips && !ThrottledDuringStroke(ref _storedRefreshedAt))
            {
                StoredSelections.Refresh(ShownDocument);
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActiveTool))
            {
                SelectBy.Close();
            }
        };
        Selection.Changed += (_, _) =>
        {
            SelectBy.SelectionChanged();
            StoredSelections.SelectionChanged();
        };
        DocumentChanged += (_, _) => SelectBy.DocumentChanged();
        MapChanged += (_, _) => SelectBy.DocumentChanged();
    }
}
