using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A symmetry the editor can mirror edits by, as the views' choice offers it.
/// </summary>
/// <param name="Kind">Null for none.</param>
/// <param name="Count">The players of a rotational symmetry; 2 for a mirror.</param>
public sealed record SymmetryChoice(MapSymmetryKind? Kind, int Count, string Title)
{
    public static SymmetryChoice Off { get; } = new SymmetryChoice(null, 1, "Off");

    public override string ToString() => Title;
}

/// <summary>
/// The symmetry edits are mirrored by (user story F-12): off unless chosen, rotational for 2 to 8
/// players or a mirror. The top view draws its guides (the overlay <c>symmetry</c>); while
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
            .. Enumerable.Range(2, MapSymmetry.MaximumCount - 1).Select(count => new SymmetryChoice(MapSymmetryKind.Rotational, count, $"Rotational, {count} players")),
            new SymmetryChoice(MapSymmetryKind.MirroredLeftRight, 2, "Mirrored left to right"),
            new SymmetryChoice(MapSymmetryKind.MirroredTopBottom, 2, "Mirrored top to bottom"),
            new SymmetryChoice(MapSymmetryKind.MirroredDiagonal, 2, "Mirrored on the diagonal"),
            new SymmetryChoice(MapSymmetryKind.MirroredOtherDiagonal, 2, "Mirrored on the other diagonal"),
        ];
    }

    /// <summary>
    /// Raised when another symmetry is chosen.
    /// </summary>
    public event EventHandler? Changed;

    public IReadOnlyList<SymmetryChoice> Choices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn), nameof(IsMirroring))]
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

    partial void OnChosenChanged(SymmetryChoice value)
    {
        if (value.Kind is not null)
        {
            _last = value;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Chooses a symmetry; null for none.
    /// </summary>
    /// <param name="count">The players of a rotational symmetry, 2 to 8; a mirror leaves it out.</param>
    /// <exception cref="ArgumentOutOfRangeException">A rotational symmetry for fewer than 2 or more than 8 players.</exception>
    public void Set(MapSymmetryKind? kind, int count = 2)
    {
        Chosen = kind is null
            ? SymmetryChoice.Off
            : Choices.FirstOrDefault(choice => choice.Kind == kind && (kind != MapSymmetryKind.Rotational || choice.Count == count))
                ?? throw new ArgumentOutOfRangeException(nameof(count), count, $"A rotational symmetry is for 2 to {MapSymmetry.MaximumCount} players");
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
        MapSymmetryKind kind = (document is null ? null : Detect(document)) ?? MapSymmetryKind.Rotational;
        Set(kind, 2);
    }

    /// <summary>
    /// The map's own symmetry, from its spawns and deposits; null when it has none.
    /// </summary>
    public static MapSymmetryKind? Detect(MapDocument document) => MapSymmetry.Find(document.Width, document.Height,
        [.. document.Markers.Spawns.Select(spawn => spawn.Position), .. document.Markers.Resources.Select(resource => resource.Position)]);

    /// <summary>
    /// The parts of the map by the chosen symmetry, the identity first; empty while it is off.
    /// </summary>
    public IReadOnlyList<MapSymmetryTransform> Transforms(MapDocument document) => Chosen.Kind is MapSymmetryKind kind
        ? MapSymmetry.Transforms(kind, Chosen.Count, document.Width, document.Height)
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
    public IReadOnlyList<(Vector2 From, Vector2 To)> Guides(MapDocument document) => Chosen.Kind is MapSymmetryKind kind
        ? MapSymmetry.Guides(kind, Chosen.Count, document.Width, document.Height)
        : [];

    /// <summary>
    /// A symmetry by the name automation and the command give it: <c>off</c>, <c>rotational</c>,
    /// <c>leftRight</c>, <c>topBottom</c>, <c>diagonal</c>, <c>otherDiagonal</c>, or the kind's own
    /// name (<c>MirroredLeftRight</c>).
    /// </summary>
    /// <exception cref="ArgumentException">No such symmetry.</exception>
    public static MapSymmetryKind? Parse(string name) => name.ToLowerInvariant() switch
    {
        "off" or "none" => null,
        "leftright" => MapSymmetryKind.MirroredLeftRight,
        "topbottom" => MapSymmetryKind.MirroredTopBottom,
        "diagonal" => MapSymmetryKind.MirroredDiagonal,
        "otherdiagonal" => MapSymmetryKind.MirroredOtherDiagonal,
        _ => Enum.TryParse(name, ignoreCase: true, out MapSymmetryKind kind)
            ? kind
            : throw new ArgumentException($"There is no symmetry '{name}': off, rotational, leftRight, topBottom, diagonal or otherDiagonal"),
    };
}
