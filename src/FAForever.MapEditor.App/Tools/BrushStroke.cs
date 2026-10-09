using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A stroke of a round brush, from the press of the mouse to its release: one step of undo, shown
/// while it is painted, with dabs a quarter of the radius apart along the way. A tool that paints
/// with a brush makes one when it is pressed.
/// </summary>
/// <remarks>
/// A stroke is input that lasts while the mouse is down, so it holds its own transaction
/// (<see cref="MainViewModel.StartStroke"/>) rather than running as a command with
/// <c>[UndoStep]</c>; commands that edit wait until it ends.
/// </remarks>
public sealed class BrushStroke
{
    private readonly MainViewModel _model;
    private readonly EditTransaction _edit;
    private readonly float _spacing;
    private readonly Action<EditTransaction, float, float> _dab;
    private (float X, float Z) _last;

    /// <param name="description">The step of the history, e.g. <c>Raise terrain</c>.</param>
    /// <param name="radius">The brush's radius in ogrids, which spaces the dabs.</param>
    /// <param name="x">Where the mouse was pressed, in ogrids.</param>
    /// <param name="dab">Paints one dab at a place, in ogrids, into the stroke's edit.</param>
    /// <exception cref="InvalidOperationException">No map is open.</exception>
    public BrushStroke(MainViewModel model, string description, double radius, float x, float z, Action<EditTransaction, float, float> dab)
    {
        _model = model;
        _spacing = Math.Max(0.5f, (float)radius / 4);
        _dab = dab;
        _edit = model.StartStroke(description);
        Dab(x, z);
        model.Redraw();
    }

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
        _model.Redraw();
    }

    /// <summary>
    /// Ends the stroke: it becomes one step of the history.
    /// </summary>
    public void End() => _model.EndStroke(_edit);

    private void Dab(float x, float z)
    {
        _dab(_edit, x, z);
        _last = (x, z);
    }
}
