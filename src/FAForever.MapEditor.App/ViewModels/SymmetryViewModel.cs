using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A symmetry the editor can mirror edits by, as the views' choice offers it.
/// </summary>
/// <param name="Kind">Null for none.</param>
/// <param name="Count">The players of a rotational symmetry or the turns of a kaleidoscope; 2 for the other kinds.</param>
public sealed record SymmetryChoice(MapSymmetryKind? Kind, int Count, string Title)
{
    public static SymmetryChoice Off { get; } = new SymmetryChoice(null, 1, "Off");

    /// <summary>
    /// What the views' bar shows of it, where room is short: <c>Point</c>, <c>4-way</c>, <c>Left-right</c>;
    /// a diagonal mirror as its line, <c>Diagonal \</c> from the top left corner, <c>Diagonal /</c> from the top right.
    /// </summary>
    public string Short => Kind switch
    {
        null => "Off",
        MapSymmetryKind.Rotational when Count == 2 => "Point",
        MapSymmetryKind.Rotational => $"{Count}-way",
        MapSymmetryKind.MirroredLeftRight => "Left-right",
        MapSymmetryKind.MirroredTopBottom => "Top-bottom",
        MapSymmetryKind.MirroredDiagonal => "Diagonal \\",
        MapSymmetryKind.MirroredOtherDiagonal => "Diagonal /",
        MapSymmetryKind.MirroredBothWays => "Both ways",
        MapSymmetryKind.MirroredBothDiagonals => "Both diagonals",
        _ => $"{2 * Count}-way mirrored",
    };

    /// <summary>
    /// How many parts it divides the map into; 1 for off.
    /// </summary>
    public int Parts => Kind is MapSymmetryKind kind ? MapSymmetry.PartsOf(kind, Count) : 1;

    public override string ToString() => Title;
}

/// <summary>
/// The symmetry edits are mirrored by (user story F-12): off unless chosen; the point symmetry and
/// rotational for 3 to 8 players; a mirror, both mirrors or both diagonals; or turned and mirrored
/// for 6 to 16 parts (the eight-way symmetry among them). Around the whole map, or around the
/// playable area (<see cref="Around"/>). The top view draws its guides (the overlay <c>symmetry</c>); while
/// <see cref="MirrorsEdits"/>, brush strokes, placing, moving, turning and deleting work in every part
/// of the map (<see cref="EditTransforms"/>).
/// </summary>
public sealed partial class SymmetryViewModel : ObservableObject
{
    public SymmetryViewModel()
    {
        Choices =
        [
            SymmetryChoice.Off,
            new SymmetryChoice(MapSymmetryKind.Rotational, 2, "Point symmetry (rotational, 2 players)"),
            .. Enumerable.Range(3, MapSymmetry.MaximumCount - 2).Select(count => new SymmetryChoice(MapSymmetryKind.Rotational, count, $"Rotational, {count} players")),
            new SymmetryChoice(MapSymmetryKind.MirroredLeftRight, 2, "Mirrored left to right"),
            new SymmetryChoice(MapSymmetryKind.MirroredTopBottom, 2, "Mirrored top to bottom"),
            new SymmetryChoice(MapSymmetryKind.MirroredDiagonal, 2, "Mirrored diagonally: top right and bottom left"),
            new SymmetryChoice(MapSymmetryKind.MirroredOtherDiagonal, 2, "Mirrored diagonally: top left and bottom right"),
            new SymmetryChoice(MapSymmetryKind.MirroredBothWays, 2, "Mirrored both ways, 4 parts"),
            new SymmetryChoice(MapSymmetryKind.MirroredBothDiagonals, 2, "Mirrored on both diagonals, 4 parts"),
            .. Enumerable.Range(3, MapSymmetry.MaximumCount - 2).Select(count => new SymmetryChoice(MapSymmetryKind.Kaleidoscope, count,
                count == 4 ? "Eight-way: turned and mirrored, 8 parts" : $"Turned and mirrored, {2 * count} parts")),
        ];
    }

    /// <summary>
    /// Raised when another symmetry is chosen.
    /// </summary>
    public event EventHandler? Changed;

    public IReadOnlyList<SymmetryChoice> Choices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn), nameof(IsMirroring), nameof(OffersAround))]
    public partial SymmetryChoice Chosen { get; set; } = SymmetryChoice.Off;

    public bool IsOn => Chosen.Kind is not null;

    /// <summary>
    /// Whether edits are mirrored while a symmetry is chosen; off, the symmetry only shows its guides
    /// (and the check of things without a partner still runs).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMirroring))]
    public partial bool MirrorsEdits { get; set; } = true;

    /// <summary>
    /// Whether edits are mirrored now: a symmetry is chosen and <see cref="MirrorsEdits"/>.
    /// </summary>
    public bool IsMirroring => IsOn && MirrorsEdits;

    partial void OnMirrorsEditsChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The symmetry used last, which turning it on again chooses.
    /// </summary>
    private SymmetryChoice? _last;

    /// <summary>
    /// The rectangle the symmetry is around (the playable area when it was put around it); null for
    /// the whole map.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAroundPlayableArea), nameof(CanBeAroundPlayableArea), nameof(OffersAround), nameof(AroundText))]
    public partial MapSymmetryArea? Around { get; set; }

    partial void OnAroundChanged(MapSymmetryArea? value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The open map's playable area when it is not the whole map, which the symmetry can be put
    /// around; null otherwise. The core sets it as the map changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBeAroundPlayableArea), nameof(OffersAround), nameof(AroundText))]
    public partial MapSymmetryArea? PlayableArea { get; set; }

    /// <summary>
    /// Whether the views' bar offers to put the symmetry around the playable area: the playable area
    /// is not the whole map, or the symmetry is around a rectangle already.
    /// </summary>
    public bool CanBeAroundPlayableArea => PlayableArea is not null || Around is not null;

    /// <summary>
    /// Whether the views' bar shows the chip that puts the symmetry around the playable area: a
    /// symmetry is chosen and <see cref="CanBeAroundPlayableArea"/>.
    /// </summary>
    public bool OffersAround => IsOn && CanBeAroundPlayableArea;

    /// <summary>
    /// Whether the symmetry is around the playable area rather than the whole map. On, it takes the
    /// playable area as it is now (where it stays when the area changes); off, the whole map.
    /// </summary>
    public bool IsAroundPlayableArea
    {
        get => Around is not null;
        set => Around = value ? PlayableArea ?? Around : null;
    }

    /// <summary>
    /// What the symmetry is around, for the chip's tip and automation: <c>the whole map</c>, or the
    /// rectangle with whether it still is the playable area.
    /// </summary>
    public string AroundText => Around is MapSymmetryArea area
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"the rectangle {area.X0:0.#}, {area.Z0:0.#} to {area.X1:0.#}, {area.Z1:0.#}{(area == PlayableArea ? ", the playable area" : ", which the playable area no longer is: turn it off and on to follow the area")}")
        : "the whole map";

    partial void OnChosenChanged(SymmetryChoice value)
    {
        if (value.Kind is not null)
        {
            _last = value;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets the symmetry used last: another map turns on its own (or the one it detects).
    /// </summary>
    public void Forget() => _last = null;

    /// <summary>
    /// The choice of a symmetry the map keeps; off for none.
    /// </summary>
    public SymmetryChoice ChoiceOf(SymmetrySetting? setting) => setting is null
        ? SymmetryChoice.Off
        : Find(setting.Kind, setting.Count) ?? SymmetryChoice.Off;

    /// <summary>
    /// The choice of a kind and count: a kaleidoscope of two turns is both mirrors.
    /// </summary>
    private SymmetryChoice? Find(MapSymmetryKind kind, int count) => kind == MapSymmetryKind.Kaleidoscope && count == 2
        ? Find(MapSymmetryKind.MirroredBothWays, 2)
        : Choices.FirstOrDefault(choice => choice.Kind == kind && (!MapSymmetry.HasCount(kind) || choice.Count == count));

    /// <summary>
    /// What the map keeps of the chosen symmetry; null while it is off.
    /// </summary>
    public SymmetrySetting? Setting => Chosen.Kind is MapSymmetryKind kind
        ? new SymmetrySetting(kind, MapSymmetry.HasCount(kind) ? Chosen.Count : 2) { Around = Around }
        : null;

    /// <summary>
    /// Chooses a symmetry; null for none.
    /// </summary>
    /// <param name="count">The players of a rotational symmetry, 2 to 8, or the turns of a kaleidoscope, 2 to 8 (two is both mirrors); the other kinds leave it out.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count outside those.</exception>
    public void Set(MapSymmetryKind? kind, int count = 2)
    {
        Chosen = kind is not MapSymmetryKind chosen
            ? SymmetryChoice.Off
            : Find(chosen, count)
                ?? throw new ArgumentOutOfRangeException(nameof(count), count, chosen == MapSymmetryKind.Kaleidoscope
                    ? $"Turned and mirrored is for 2 to {MapSymmetry.MaximumCount} turns"
                    : $"A rotational symmetry is for 2 to {MapSymmetry.MaximumCount} players");
    }

    /// <summary>
    /// Turns the symmetry on or off: on, the one used last, else the map's own (from its spawns and
    /// deposits), else rotational for two players.
    /// </summary>
    public void Toggle(MapDocument? document)
    {
        if (IsOn)
        {
            Chosen = SymmetryChoice.Off;
            return;
        }
        if (_last is not null)
        {
            Chosen = _last;
            return;
        }
        SymmetrySetting found = (document is null ? null : DetectSetting(document)) ?? new SymmetrySetting(MapSymmetryKind.Rotational, 2);
        Around = found.Around;
        Set(found.Kind, found.Count);
    }

    /// <summary>
    /// The kind of the map's own symmetry (<see cref="DetectSetting"/>); null when it has none.
    /// </summary>
    public static MapSymmetryKind? Detect(MapDocument document) => DetectSetting(document)?.Kind;

    /// <summary>
    /// The map's own symmetry, from its spawns and deposits: the one with the most parts around the
    /// whole map, else around the playable area when that is not the whole map; null when it has none.
    /// </summary>
    public static SymmetrySetting? DetectSetting(MapDocument document)
    {
        Vector3[] positions = [.. document.Markers.Spawns.Select(spawn => spawn.Position), .. document.Markers.Resources.Select(resource => resource.Position)];
        if (MapSymmetry.Detect(document.Width, document.Height, positions) is MapSymmetryMatch whole)
        {
            return new SymmetrySetting(whole.Kind, whole.Count);
        }
        return PlayableAreaOf(document) is MapSymmetryArea area && MapSymmetry.Detect(area, positions) is MapSymmetryMatch inside
            ? new SymmetrySetting(inside.Kind, inside.Count) { Around = area }
            : null;
    }

    /// <summary>
    /// The playable area of the document when it is not the whole map; null otherwise.
    /// </summary>
    public static MapSymmetryArea? PlayableAreaOf(MapDocument document) =>
        SymmetrySetting.PlayableAreaOf(document) is MapSymmetryArea area && area != MapSymmetryArea.Whole(document.Width, document.Height) ? area : null;

    /// <summary>
    /// The parts of the map by the chosen symmetry, the identity first; empty while it is off.
    /// </summary>
    public IReadOnlyList<MapSymmetryTransform> Transforms(MapDocument document) => Setting is SymmetrySetting setting
        ? setting.Transforms(document)
        : [];

    /// <summary>
    /// The parts edits are mirrored into, the identity first: those of <see cref="Transforms"/> while
    /// <see cref="IsMirroring"/>, else the identity alone.
    /// </summary>
    public IReadOnlyList<MapSymmetryTransform> EditTransforms(MapDocument document) => IsMirroring
        ? Transforms(document)
        : [MapSymmetryTransform.Identity];

    /// <summary>
    /// The lines between the parts, for the views; empty while it is off.
    /// </summary>
    public IReadOnlyList<(Vector2 From, Vector2 To)> Guides(MapDocument document) => Setting is SymmetrySetting setting
        ? setting.Guides(document.Width, document.Height)
        : [];

    /// <summary>
    /// The point the point symmetry turns about, which the views draw as its guide; null while it is
    /// off and for the kinds whose guides are lines.
    /// </summary>
    public Vector2? GuidePoint(MapDocument document) => Setting?.Point(document.Width, document.Height);

    /// <summary>
    /// The count a name implies: <c>point</c> is rotational for two, <c>eightWay</c> a kaleidoscope
    /// of four turns; null for the others.
    /// </summary>
    public static int? CountOf(string name) => name.ToLowerInvariant() switch
    {
        "point" => 2,
        "eightway" or "8way" => 4,
        _ => null,
    };

    /// <summary>
    /// A symmetry by the name automation and the command give it: <c>off</c>, <c>point</c> (rotational
    /// for two), <c>rotational</c>, <c>leftRight</c>, <c>topBottom</c>, <c>diagonal</c>, <c>otherDiagonal</c>,
    /// <c>bothWays</c>, <c>bothDiagonals</c>, <c>kaleidoscope</c>, <c>eightWay</c> (a kaleidoscope of
    /// four turns), or the kind's own name (<c>MirroredLeftRight</c>).
    /// </summary>
    /// <exception cref="ArgumentException">No such symmetry.</exception>
    public static MapSymmetryKind? Parse(string name) => name.ToLowerInvariant() switch
    {
        "off" or "none" => null,
        "point" => MapSymmetryKind.Rotational,
        "leftright" => MapSymmetryKind.MirroredLeftRight,
        "topbottom" => MapSymmetryKind.MirroredTopBottom,
        "diagonal" => MapSymmetryKind.MirroredDiagonal,
        "otherdiagonal" => MapSymmetryKind.MirroredOtherDiagonal,
        "bothways" => MapSymmetryKind.MirroredBothWays,
        "bothdiagonals" => MapSymmetryKind.MirroredBothDiagonals,
        "eightway" or "8way" => MapSymmetryKind.Kaleidoscope,
        _ => Enum.TryParse(name, ignoreCase: true, out MapSymmetryKind kind)
            ? kind
            : throw new ArgumentException($"There is no symmetry '{name}': off, point, rotational, leftRight, topBottom, diagonal, otherDiagonal, bothWays, bothDiagonals, kaleidoscope or eightWay"),
    };
}
