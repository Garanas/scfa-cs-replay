using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The keys and the wheel the views (and the window, while no field has the keyboard) give the
/// active tool: the brushes' radius, strength and turn (<see cref="BrushKeys"/>), then the tool's own keys.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// A key for the active tool, by its name (<c>Q</c>, <c>Escape</c>, <c>OemOpenBrackets</c>).
    /// </summary>
    /// <returns>Whether the tool used it.</returns>
    public bool ToolKey(string key, ToolModifiers modifiers)
    {
        if (!HasMap || ActiveTool?.Tool is not IMapTool tool)
        {
            return false;
        }
        if (BrushKeys.Key(tool, key, modifiers, out string? status))
        {
            Status = status!;
            return true;
        }
        return tool.Key(key, modifiers);
    }

    /// <summary>
    /// The wheel turned with control held over a view: the active brush's radius, or with shift its
    /// strength; false when the tool has no brush (the view zooms then).
    /// </summary>
    public bool BrushWheel(int notches, bool strength)
    {
        if (!HasMap || !BrushKeys.Wheel(ActiveTool?.Tool, notches, strength, out string? status))
        {
            return false;
        }
        Status = status!;
        return true;
    }

    /// <summary>
    /// The wheel turned with alt held over a view: turns the picture of the active brush 5 degrees a
    /// notch; false when the tool has no brush with a picture (the view does what the wheel does).
    /// </summary>
    public bool BrushTurnWheel(int notches)
    {
        if (!HasMap || !BrushKeys.TurnWheel(ActiveTool?.Tool, notches, out string? status))
        {
            return false;
        }
        Status = status!;
        return true;
    }
}
