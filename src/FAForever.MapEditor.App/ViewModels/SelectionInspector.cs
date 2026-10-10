using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

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
    /// The workspace's assets, for what a section shows of the selected things' blueprints (a
    /// prop's reclaim); empty until it is given.
    /// </summary>
    public Func<AssetCatalog> Catalog { get; set; } = () => AssetCatalog.Empty;

    /// <summary>
    /// Runs a command with arguments, as a section's field does when a value is typed.
    /// </summary>
    public Task Run(string id, JsonObject arguments) => _run(id, arguments);

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
                section.Owner = this;
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

    /// <summary>
    /// The value all of them have, as text, or <see cref="Mixed"/> when they differ; empty for none.
    /// </summary>
    internal static string Shared(IEnumerable<float> values, string format)
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

    /// <summary>
    /// Runs a command with arguments, as the window does (what fails shows in the status line): a
    /// section's fields change the selected things through commands, one step of undo each.
    /// </summary>
    public Func<string, JsonObject, Task> Run { get; internal set; } = (_, _) => Task.CompletedTask;

    public abstract ObjectKind Kind { get; }


    /// <summary>
    /// The inspector the section is part of: its catalogue and commands.
    /// </summary>
    public SelectionInspector? Owner { get; set; }

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

    /// <param name="run">Runs a command with arguments, as a field of the inspector does.</param>
    public static SelectionSection For(ObjectKind kind, Func<string, JsonObject, Task> run)
    {
        SelectionSection section = kind switch
        {
            ObjectKind.Marker => new MarkerSelection(),
            ObjectKind.Prop => new PropSelection(),
            ObjectKind.Decal => new DecalSelection(),
            ObjectKind.Unit => new UnitSelection(run),
            _ => new AreaSelection(run),
        };
        section.Run = run;
        return section;
    }

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
/// The selected props: their blueprints, their tilt (pitch and roll) and scale, editable for one or
/// many at once, and what they give when reclaimed (user stories P-1, P-5).
/// </summary>
public sealed partial class PropSelection : SelectionSection
{
    private string _pitch = "";
    private string _roll = "";
    private string _scale = "";

    public override ObjectKind Kind => ObjectKind.Prop;

    /// <summary>
    /// How far the selected props lean towards their own z, in degrees; typing one tilts each.
    /// </summary>
    public string Pitch
    {
        get => _pitch;
        set => Type(value, ref _pitch, nameof(Pitch), "props.tilt", "pitch");
    }

    /// <summary>
    /// How far the selected props lean towards their own -x, in degrees; typing one tilts each.
    /// </summary>
    public string Roll
    {
        get => _roll;
        set => Type(value, ref _roll, nameof(Roll), "props.tilt", "roll");
    }

    /// <summary>
    /// The selected props' scale (1 is the model's own size); typing one scales each.
    /// </summary>
    public string Scale
    {
        get => _scale;
        set => Type(value, ref _scale, nameof(Scale), "props.scale", "scale");
    }

    /// <summary>
    /// What the selected props give when reclaimed (user story P-5).
    /// </summary>
    [ObservableProperty]
    public partial string Reclaim { get; private set; } = "";

    public override void Refresh(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        base.Refresh(document, things);
        Prop[] props = [.. things.Select(document.Find).OfType<Prop>()];
        List<float> pitches = [];
        List<float> rolls = [];
        foreach (Prop prop in props)
        {
            (float pitch, float roll) = PropOrientation.Tilt(prop);
            pitches.Add(MathF.Round(pitch * 180 / MathF.PI, 1));
            rolls.Add(MathF.Round(roll * 180 / MathF.PI, 1));
        }
        Show(ref _pitch, nameof(Pitch), Shared(pitches, "0.#"));
        Show(ref _roll, nameof(Roll), Shared(rolls, "0.#"));
        Show(ref _scale, nameof(Scale), Shared(props.Select(PropOrientation.UniformScale), "0.###"));
        Reclaim = props.Length == 0 ? "" : PropsViewModel.Describe(PropReclaim.Of(props, Owner?.Catalog() ?? AssetCatalog.Empty));
    }

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        Counted(things.Select(document.Find).OfType<Prop>().Select(prop => Path.GetFileNameWithoutExtension(prop.Blueprint)));

    private static string Shared(IEnumerable<float> values, string format)
    {
        string? shown = null;
        foreach (float value in values)
        {
            // -0 and 0 are the same lean
            string text = (value == 0 ? 0 : value).ToString(format, CultureInfo.InvariantCulture);
            if (shown is not null && shown != text)
            {
                return SelectionInspector.Mixed;
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
    /// A value typed into a field: a number goes to every selected prop through the command;
    /// anything else changes nothing and the field shows its value again.
    /// </summary>
    private void Type(string value, ref string field, string property, string command, string argument)
    {
        if (value == field)
        {
            return;
        }
        if (Owner is null || !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
        {
            string shown = field;
            field = value;
            OnPropertyChanged(property);
            field = shown;
            OnPropertyChanged(property);
            return;
        }
        _ = Owner.Run(command, new JsonObject { [argument] = number });
    }
}

/// <summary>
/// The selected decals (user story D-1): their textures, and the fields of decals, for one or many
/// at once: width and length in ogrids (<c>decals.size</c>), the distances at which the game draws
/// them (<c>decals.lod</c>), each showing <see cref="SelectionInspector.Mixed"/> when they differ.
/// </summary>
public sealed class DecalSelection : SelectionSection
{
    private string _width = "";
    private string _length = "";
    private string _cutOff = "";
    private string _nearCutOff = "";

    public override ObjectKind Kind => ObjectKind.Decal;

    /// <summary>
    /// Their width (along each decal's own x axis) in ogrids; typing one gives it to each.
    /// </summary>
    public string Width
    {
        get => _width;
        set => Type(value, ref _width, nameof(Width), "decals.size", "width");
    }

    /// <summary>
    /// Their length (along each decal's own z axis) in ogrids.
    /// </summary>
    public string Length
    {
        get => _length;
        set => Type(value, ref _length, nameof(Length), "decals.size", "length");
    }

    /// <summary>
    /// The distance beyond which the game does not draw them.
    /// </summary>
    public string CutOff
    {
        get => _cutOff;
        set => Type(value, ref _cutOff, nameof(CutOff), "decals.lod", "cutOff");
    }

    /// <summary>
    /// The distance closer than which the game does not draw them.
    /// </summary>
    public string NearCutOff
    {
        get => _nearCutOff;
        set => Type(value, ref _nearCutOff, nameof(NearCutOff), "decals.lod", "nearCutOff");
    }

    /// <summary>
    /// Their types, e.g. <c>albedo</c> or <c>albedo, normals</c>.
    /// </summary>
    public string Types { get; private set; } = "";

    public override void Refresh(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        base.Refresh(document, things);
        Decal[] decals = [.. things.Select(document.Find).OfType<Decal>()];
        Show(ref _width, nameof(Width), SelectionInspector.Shared(decals.Select(decal => decal.Scale.X), "0.##"));
        Show(ref _length, nameof(Length), SelectionInspector.Shared(decals.Select(decal => decal.Scale.Z), "0.##"));
        Show(ref _cutOff, nameof(CutOff), SelectionInspector.Shared(decals.Select(decal => decal.CutOffLod), "0.#"));
        Show(ref _nearCutOff, nameof(NearCutOff), SelectionInspector.Shared(decals.Select(decal => decal.NearCutOffLod), "0.#"));
        string types = string.Join(", ", decals.Select(decal => decal.Type.ToString().ToLowerInvariant()).Distinct().Order());
        if (types != Types)
        {
            Types = types;
            OnPropertyChanged(nameof(Types));
        }
    }

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        Counted(things.Select(document.Find).OfType<Decal>().Select(decal => decal.Textures.IsDefaultOrEmpty ? $"decal {decal.Id}" : Path.GetFileNameWithoutExtension(decal.Textures[0])));

    private void Show(ref string field, string property, string value)
    {
        if (field != value)
        {
            field = value;
            OnPropertyChanged(property);
        }
    }

    /// <summary>
    /// A value typed into a field: a number goes to every selected decal through the command;
    /// anything else changes nothing and the field shows its value again.
    /// </summary>
    private void Type(string value, ref string field, string property, string command, string argument)
    {
        if (value == field)
        {
            return;
        }
        if (Run is null || !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
        {
            string shown = field;
            field = value;
            OnPropertyChanged(property);
            field = shown;
            OnPropertyChanged(property);
            return;
        }
        _ = Run(command, new JsonObject { [argument] = number });
    }
}

/// <summary>
/// The selected units: their names, blueprints and armies (user stories U-2, U-3), the army they
/// belong to (choosing another gives them to it, <c>units.moveToArmy</c>) and their group.
/// </summary>
public sealed partial class UnitSelection(Func<string, JsonObject, Task> run) : SelectionSection
{
    private string? _army;
    private bool _showing;

    public override ObjectKind Kind => ObjectKind.Unit;

    /// <summary>
    /// The map's armies, to give the units to.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Armies { get; private set; } = [];

    /// <summary>
    /// The army the selected units belong to; null when they belong to different ones.
    /// Choosing one gives them all to it, into its group <c>INITIAL</c>.
    /// </summary>
    public string? Army
    {
        get => _army;
        set
        {
            if (value is null || value == _army)
            {
                return;
            }
            _army = value;
            OnPropertyChanged();
            if (!_showing)
            {
                _ = run("units.moveToArmy", new JsonObject { ["army"] = value });
            }
        }
    }

    private bool? _wrecks;

    /// <summary>
    /// Whether the selected units are wrecks (user story U-5: in a <c>WRECKAGE</c> group); null when
    /// some are. Checking or clearing it runs <c>units.asWrecks</c>.
    /// </summary>
    public bool? Wrecks
    {
        get => _wrecks;
        set
        {
            if (value is null || value == _wrecks)
            {
                return;
            }
            _wrecks = value;
            OnPropertyChanged();
            if (!_showing)
            {
                _ = run("units.asWrecks", new JsonObject { ["wreck"] = value });
            }
        }
    }

    /// <summary>
    /// The group the selected units are in (<c>INITIAL/Town</c>), or <see cref="SelectionInspector.Mixed"/>.
    /// </summary>
    [ObservableProperty]
    public partial string Group { get; private set; } = "";

    public override void Refresh(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        base.Refresh(document, things);
        PlacedUnit[] units = [.. things.Select(document.Find).OfType<PlacedUnit>()];
        _showing = true;
        try
        {
            if (!Armies.SequenceEqual(document.Armies.Select(army => army.Name)))
            {
                Armies = [.. document.Armies.Select(army => army.Name)];
            }
            string[] armies = [.. units.Select(unit => unit.Army).Distinct()];
            _army = armies.Length == 1 ? armies[0] : null;
            OnPropertyChanged(nameof(Army));
            string[] groups = [.. units.Select(unit => UnitGroupEdits.PathText(unit.Group)).Distinct()];
            Group = groups.Length == 1 ? (groups[0].Length == 0 ? "(the root group)" : groups[0]) : SelectionInspector.Mixed;
            int wrecks = units.Count(UnitWrecks.IsWreck);
            _wrecks = wrecks == 0 ? false : wrecks == units.Length ? true : null;
            OnPropertyChanged(nameof(Wrecks));
        }
        finally
        {
            _showing = false;
        }
    }

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
    private readonly Func<string, JsonObject, Task> _run;

    public AreaSelection(Func<string, JsonObject, Task> run)
    {
        _run = run;
        const string command = "map.setArea";
        JsonObject Fixed() => new JsonObject { ["name"] = _name };
        Area Of(MapDocument d) => d.Markers.Areas.FirstOrDefault(area => area.Name == _name) ?? new Area { Name = _name };
        Fields = new SettingGroup("Area",
        [
            new TextSetting("Name", command, "rename", d => Of(d).Name, run, Fixed) { Help = "Scripts name areas; the map's script makes the playable area playable by its name" },
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
    /// Whether the selected area is the playable area (user story M-3): the one the scenario
    /// names, else the first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Playable))]
    public partial bool IsPlayable { get; private set; }

    /// <summary>
    /// Whether the selected area is the playable area; checking it makes it so
    /// (<c>map.setPlayableArea</c>), unchecking makes the first area the playable one again.
    /// </summary>
    public bool Playable
    {
        get => IsPlayable;
        set
        {
            if (value != IsPlayable)
            {
                _ = _run("map.setPlayableArea", value ? new JsonObject { ["name"] = _name } : []);
            }
        }
    }

    /// <summary>
    /// Whether the selected area is the playable area by convention: the first, none named.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPlayableByConvention { get; private set; }

    public override void Refresh(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        base.Refresh(document, things);
        IsSingle = things.Count == 1 && document.Find(things[0]) is Area;
        if (IsSingle && document.Find(things[0]) is Area area)
        {
            _name = area.Name;
            IsPlayable = document.PlayableArea?.Name == area.Name;
            IsPlayableByConvention = IsPlayable && document.Scenario.PlayableArea is null;
            Fields.Refresh(document);
        }
    }

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        things.Select(document.Find).OfType<Area>().Select(area => string.Create(CultureInfo.InvariantCulture, $"{area.Name}: {area.X0:0.#}, {area.Z0:0.#} to {area.X1:0.#}, {area.Z1:0.#}"));
}
