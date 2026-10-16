using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A stroke of a brush, from the press of the mouse to its release: one step of undo, shown
/// while it is painted, with dabs a quarter of the radius apart along the way. A tool that paints
/// with a brush makes one when it is pressed.
/// </summary>
/// <remarks>
/// <para>
/// A stroke is input that lasts while the mouse is down, so it holds its own transaction
/// (<see cref="MainViewModel.StartStroke"/>) rather than running as a command with
/// <c>[UndoStep]</c>; commands that edit wait until it ends.
/// </para>
/// <para>
/// While the symmetry mirrors edits (user stories F-12, S-1), every dab is painted in every part of
/// the map, in the same step (<see cref="SymmetricDabs"/>): the tool's dab is called once per image,
/// with the image's place, the turn of its shape and whether it is flipped. The parts are those of the
/// moment the stroke starts. Images that coincide are painted once. A tool that mirrors its own
/// way (the props painter, which mirrors the props it scatters) passes <c>mirror: false</c> and
/// reads <see cref="Images"/>.
/// </para>
/// </remarks>
public sealed class BrushStroke
{
    private readonly MainViewModel _model;
    private readonly EditTransaction _edit;
    private readonly float _spacing;
    private readonly Action<EditTransaction, SymmetricDab> _dab;
    private readonly Func<float>? _rotation;
    private readonly IReadOnlyList<MapSymmetryTransform> _mirrorInto;
    private (float X, float Z) _last;

    /// <param name="description">The step of the history, e.g. <c>Raise terrain</c>.</param>
    /// <param name="radius">The brush's radius in ogrids, which spaces the dabs.</param>
    /// <param name="x">Where the mouse was pressed, in ogrids.</param>
    /// <param name="dab">Paints one dab into the stroke's edit: at its place (in ogrids), its shape
    /// turned by its rotation and flipped when it is mirrored.</param>
    /// <param name="rotation">The turn of the shape for the next dab, in degrees (the shape's
    /// <see cref="BrushShapeSettings.NextRotation"/>), asked once per dab and turned for each image;
    /// null for a round brush, whose turn does not matter.</param>
    /// <param name="mirror">Whether the stroke paints each dab in every part of a symmetric map.</param>
    /// <exception cref="InvalidOperationException">No map is open.</exception>
    public BrushStroke(MainViewModel model, string description, double radius, float x, float z, Action<EditTransaction, SymmetricDab> dab,
        Func<float>? rotation = null, bool mirror = true)
    {
        _model = model;
        _spacing = Math.Max(0.5f, (float)radius / 4);
        _dab = dab;
        _rotation = rotation;
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        Images = model.EditTransforms(document);
        _mirrorInto = mirror ? Images : [MapSymmetryTransform.Identity];
        _edit = model.StartStroke(description);
        Dab(x, z);
        model.ShowStroke();
    }

    /// <summary>
    /// The parts of the map the stroke's edits are mirrored into, the identity first (the identity
    /// alone while the symmetry does not mirror edits).
    /// </summary>
    public IReadOnlyList<MapSymmetryTransform> Images { get; }

    /// <summary>
    /// Moves the stroke on to where the mouse is now.
    /// </summary>
    public void MoveTo(float x, float z)
    {
        using Performance.Span measure = Performance.Measure("tool.stroke");
        if (_model.StrokeInProgress != _edit)
        {
            // the editor ended it: another map was opened
            return;
        }
        float dx = x - _last.X;
        float dz = z - _last.Z;
        int steps = (int)(MathF.Sqrt(dx * dx + dz * dz) / _spacing);
        if (steps == 0)
        {
            return;
        }
        (float x0, float z0) = _last;
        for (int i = 1; i <= steps; i++)
        {
            Dab(x0 + dx * i / steps, z0 + dz * i / steps);
        }
        _model.ShowStroke();
    }

    /// <summary>
    /// Ends the stroke: it becomes one step of the history.
    /// </summary>
    public void End() => _model.EndStroke(_edit);

    private void Dab(float x, float z)
    {
        float rotation = _rotation?.Invoke() ?? 0;
        foreach (SymmetricDab dab in SymmetricDabs.Of(_mirrorInto, x, z, rotation, round: _rotation is null))
        {
            _dab(_edit, dab);
        }
        _last = (x, z);
    }
}
