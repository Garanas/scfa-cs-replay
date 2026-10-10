using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The inspector's part for what is selected on the map (user story F-11): how many of which
/// kind, the fields they share (where they are and which way they face), editable for one or many
/// at once, and a section per kind (<see cref="SelectionSection"/>).
/// </summary>
/// <remarks>
/// A shared field shows the value all the selected things have, or <see cref="Mixed"/> when they
/// differ; typing a value gives it to every one of them through a command (<c>edit.place</c>), one
/// step of undo. The height follows the terrain, so it is shown and not typed.
/// </remarks>
public sealed partial class SelectionInspector : ObservableObject
{
    /// <summary>
    /// What a field shows when the selected things differ in it.
    /// </summary>
    public const string Mixed = "—";

    private readonly SelectionViewModel _selection;
    private readonly Func<string, JsonObject, Task> _run;
    private readonly Dictionary<ObjectKind, SelectionSection> _sections = [];

    public SelectionInspector(SelectionViewModel selection, Func<string, JsonObject, Task> run)
    {
        _selection = selection;
        _run = run;
    }

    /// <summary>
    /// The selected thing's name, or how many are selected.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    /// <summary>
    /// How many of each kind, e.g. <c>2 markers, 1 prop</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Kinds { get; private set; } = "";

    /// <summary>
    /// Whether any of the selected things turns: areas do not.
    /// </summary>
    [ObservableProperty]
    public partial bool CanTurn { get; private set; }

    /// <summary>
    /// The height of the selected things' centres, in game units (read only: it follows the terrain).
    /// </summary>
    [ObservableProperty]
    public partial string Height { get; private set; } = "";

    /// <summary>
    /// A section per kind that is selected, in the order of <see cref="ObjectKind"/>.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<SelectionSection> Sections { get; private set; } = [];

    private string _x = "";
    private string _z = "";
    private string _heading = "";

    /// <summary>
    /// The x of the selected things' centres in ogrids; typing one puts each there.
    /// </summary>
    public string X
    {
        get => _x;
        set => Type(value, ref _x, nameof(X), "x");
    }

    /// <summary>
    /// The z of the selected things' centres in ogrids; typing one puts each there.
    /// </summary>
    public string Z
    {
        get => _z;
        set => Type(value, ref _z, nameof(Z), "z");
    }

    /// <summary>
    /// Which way the selected things face, in degrees clockwise from +x in the top view; typing
    /// one turns each about its own centre to it.
    /// </summary>
    public string Heading
    {
        get => _heading;
        set => Type(value, ref _heading, nameof(Heading), "heading");
    }

    /// <summary>
    /// Shows the selection as it is in a version of the document.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        using Performance.Span measure = Performance.Measure("model.selection-inspector");
        IReadOnlyList<ObjectRef> things = _selection.Items;
        if (document is null || things.Count == 0)
        {
            Title = "";
            Kinds = "";
            Show(ref _x, nameof(X), "");
            Show(ref _z, nameof(Z), "");
            Show(ref _heading, nameof(Heading), "");
            Height = "";
            CanTurn = false;
            if (Sections.Count > 0)
            {
                Sections = [];
            }
            return;
        }

        List<Vector3> places = [];
        List<float> headings = [];
        foreach (ObjectRef thing in things)
        {
            if (document.PlaceOf(thing) is Vector3 place)
            {
                places.Add(place);
            }
            if (document.HeadingOf(thing) is float heading)
            {
                headings.Add(Degrees(heading));
            }
        }
        Title = things.Count == 1 ? NameOf(document, things[0]) : $"{things.Count} selected";
        Kinds = _selection.Summary;
        Show(ref _x, nameof(X), Shared(places.Select(p => p.X), "0.##"));
        Show(ref _z, nameof(Z), Shared(places.Select(p => p.Z), "0.##"));
        Height = Shared(places.Select(p => p.Y), "0.##");
        CanTurn = headings.Count > 0;
        Show(ref _heading, nameof(Heading), Shared(headings, "0.#"));

        // a section per kind, kept while its kind stays selected so its fields keep their focus
        ObjectKind[] kinds = [.. things.Select(thing => thing.Kind).Distinct().Order()];
        List<SelectionSection> sections = [];
        foreach (ObjectKind kind in kinds)
        {
            if (!_sections.TryGetValue(kind, out SelectionSection? section))
            {
                section = SelectionSection.For(kind, _run);
                _sections[kind] = section;
            }
            section.Refresh(document, [.. things.Where(thing => thing.Kind == kind)]);
            sections.Add(section);
        }
        if (!sections.SequenceEqual(Sections))
        {
            Sections = sections;
        }
    }

    /// <summary>
    /// What a thing is called in the inspector's title.
    /// </summary>
    public static string NameOf(MapDocument document, ObjectRef thing) => document.Find(thing) switch
    {
        Marker marker => marker.Name,
        Prop prop => Path.GetFileNameWithoutExtension(prop.Blueprint),
        Decal decal => decal.Textures.IsDefaultOrEmpty ? $"Decal {decal.Id}" : Path.GetFileNameWithoutExtension(decal.Textures[0]),
        PlacedUnit unit => $"{unit.Unit.Name} ({unit.Unit.Blueprint})",
        Area area => area.Name,
        _ => thing.ToString(),
    };

    /// <summary>
    /// An angle in radians as degrees from 0 to 360.
    /// </summary>
    public static float Degrees(float radians)
    {
        float degrees = radians * 180 / MathF.PI % 360;
        degrees = degrees < 0 ? degrees + 360 : degrees;
        return MathF.Round(degrees, 3) >= 360 ? 0 : degrees;
    }

    private static string Shared(IEnumerable<float> values, string format)
    {
        string? shown = null;
        foreach (float value in values)
        {
            string text = value.ToString(format, CultureInfo.InvariantCulture);
            if (shown is not null && shown != text)
            {
                return Mixed;
            }
            shown = text;
        }
        return shown ?? "";
    }

    private void Show(ref string field, string property, string value)
    {
        if (field != value)
        {
            field = value;
            OnPropertyChanged(property);
        }
    }

    /// <summary>
    /// A value typed into a field: a number goes to every selected thing; anything else (the mixed
    /// sign, nothing, a typo) changes nothing and the field shows its value again.
    /// </summary>
    private void Type(string value, ref string field, string property, string argument)
    {
        if (value == field)
        {
            return;
        }
        if (_selection.IsEmpty || !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
        {
            // the binding wrote the text: show the field's own value again
            string shown = field;
            field = value;
            OnPropertyChanged(property);
            field = shown;
            OnPropertyChanged(property);
            return;
        }
        _ = _run("edit.place", new JsonObject { [argument] = number });
    }
}

/// <summary>
/// The inspector's part for the selected things of one kind: a template per class in
/// <c>Views/Inspector.axaml</c>. Each kind's own fields (a marker's name, a prop's scale, a decal's
/// distances, a unit's army) go into its class and its template; <see cref="Refresh"/> keeps them
/// up to date with the document.
/// </summary>
public abstract partial class SelectionSection : ObservableObject
{
    /// <summary>
    /// The most lines a section lists before it says how many more there are.
    /// </summary>
    protected const int MostLines = 8;

    public abstract ObjectKind Kind { get; }

    /// <summary>
    /// E.g. <c>3 props</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    /// <summary>
    /// A line per selected thing (or per kind of thing), the first few.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Lines { get; private set; } = [];

    /// <param name="run">Runs a command with arguments, for the fields that change the selected things.</param>
    public static SelectionSection For(ObjectKind kind, Func<string, JsonObject, Task> run) => kind switch
    {
        ObjectKind.Marker => new MarkerSelection(),
        ObjectKind.Prop => new PropSelection(),
        ObjectKind.Decal => new DecalSelection(),
        ObjectKind.Unit => new UnitSelection(),
        _ => new AreaSelection(run),
    };

    /// <summary>
    /// Shows the selected things of this kind as they are in a version of the document.
    /// </summary>
    public virtual void Refresh(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        Title = SelectionViewModel.Plural(things.Count, Kind);
        string[] lines = [.. LinesOf(document, things)];
        string[] shown = lines.Length > MostLines ? [.. lines.Take(MostLines), $"and {lines.Length - MostLines} more"] : lines;
        if (!shown.SequenceEqual(Lines))
        {
            Lines = shown;
        }
    }

    /// <summary>
    /// The lines of the list, all of them (<see cref="Refresh"/> shows the first few).
    /// </summary>
    protected abstract IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things);

    /// <summary>
    /// The same lines once each, with how many times when more than once: <c>pine06_prop ×3</c>.
    /// </summary>
    protected static IEnumerable<string> Counted(IEnumerable<string> lines) => lines
        .GroupBy(line => line, StringComparer.Ordinal)
        .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} ×{group.Count()}");
}

/// <summary>
/// The selected markers: their names and kinds (user stories R-1, R-2).
/// </summary>
public sealed class MarkerSelection : SelectionSection
{
    public override ObjectKind Kind => ObjectKind.Marker;

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        things.Select(document.Find).OfType<Marker>().Select(marker => $"{marker.Name} ({KindOf(marker)})");

    /// <summary>
    /// What a marker is, in a word or two: <c>spawn</c>, <c>mass</c>, <c>Rally Point</c>, <c>land path node</c>.
    /// </summary>
    public static string KindOf(Marker marker) => marker switch
    {
        Spawn => "spawn",
        ResourceMarker resource => resource.Kind.ToString().ToLowerInvariant(),
        PathNode node => $"{node.Layer.ToString().ToLowerInvariant()} path node",
        AiMarker ai => ai.Type,
        WeatherDefinition => "weather",
        WeatherGenerator => "weather generator",
        _ => "marker",
    };
}

/// <summary>
/// The selected props: their blueprints (user story P-1).
/// </summary>
public sealed class PropSelection : SelectionSection
{
    public override ObjectKind Kind => ObjectKind.Prop;

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        Counted(things.Select(document.Find).OfType<Prop>().Select(prop => Path.GetFileNameWithoutExtension(prop.Blueprint)));
}

/// <summary>
/// The selected decals: their textures (user story D-1).
/// </summary>
public sealed class DecalSelection : SelectionSection
{
    public override ObjectKind Kind => ObjectKind.Decal;

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        Counted(things.Select(document.Find).OfType<Decal>().Select(decal => decal.Textures.IsDefaultOrEmpty ? $"decal {decal.Id}" : Path.GetFileNameWithoutExtension(decal.Textures[0])));
}

/// <summary>
/// The selected units: their names, blueprints and armies (user stories U-2, U-3).
/// </summary>
public sealed class UnitSelection : SelectionSection
{
    public override ObjectKind Kind => ObjectKind.Unit;

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        things.Select(document.Find).OfType<PlacedUnit>().Select(unit => $"{unit.Unit.Name}: {unit.Unit.Blueprint}, {unit.Army}");
}

/// <summary>
/// The selected areas: their names and rectangles (user story M-5); one area alone has fields for
/// its name and corners, which change it through <c>map.setArea</c>.
/// </summary>
public sealed partial class AreaSelection : SelectionSection
{
    private string _name = "";

    public AreaSelection(Func<string, JsonObject, Task> run)
    {
        const string command = "map.setArea";
        JsonObject Fixed() => new JsonObject { ["name"] = _name };
        Area Of(MapDocument d) => d.Markers.Areas.FirstOrDefault(area => area.Name == _name) ?? new Area { Name = _name };
        Fields = new SettingGroup("Area",
        [
            new TextSetting("Name", command, "rename", d => Of(d).Name, run, Fixed) { Help = "The first area is the playable area; scripts and the lobby name areas" },
            new NumberSetting("Left", command, "x0", d => Of(d).X0, run, Fixed) { Help = "In ogrids" },
            new NumberSetting("Top", command, "z0", d => Of(d).Z0, run, Fixed) { Help = "In ogrids" },
            new NumberSetting("Right", command, "x1", d => Of(d).X1, run, Fixed) { Help = "In ogrids" },
            new NumberSetting("Bottom", command, "z1", d => Of(d).Z1, run, Fixed) { Help = "In ogrids" },
        ]);
    }

    public override ObjectKind Kind => ObjectKind.Area;

    /// <summary>
    /// The fields of the one selected area.
    /// </summary>
    public SettingGroup Fields { get; }

    /// <summary>
    /// Whether one area is selected, whose fields show.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSingle { get; private set; }

    /// <summary>
    /// Whether the selected area is the playable area (the first).
    /// </summary>
    [ObservableProperty]
    public partial bool IsPlayable { get; private set; }

    public override void Refresh(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        base.Refresh(document, things);
        IsSingle = things.Count == 1 && document.Find(things[0]) is Area;
        if (IsSingle && document.Find(things[0]) is Area area)
        {
            _name = area.Name;
            IsPlayable = document.Markers.Areas.Length > 0 && document.Markers.Areas[0].Name == area.Name;
            Fields.Refresh(document);
        }
    }

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        things.Select(document.Find).OfType<Area>().Select(area => string.Create(CultureInfo.InvariantCulture, $"{area.Name}: {area.X0:0.#}, {area.Z0:0.#} to {area.X1:0.#}, {area.Z1:0.#}"));
}
