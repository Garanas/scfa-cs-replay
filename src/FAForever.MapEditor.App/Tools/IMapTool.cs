using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What the left mouse does in the views while a tool is active, in ogrids. The views keep the
/// rest of the mouse (moving, zooming, alt and a click for the camera), which is the same
/// whatever the tool.
/// </summary>
/// <remarks>
/// A tool is made once per editor (<see cref="ViewModels.ToolDefinition.Make"/>), so its settings
/// last while the person switches tools. The views call the overloads with a <see cref="ToolInput"/>
/// (the keys held, the size of a pixel on the map) and give the tool the keys the view does not use
/// itself (<see cref="Key"/>); a tool that needs neither implements only the plain methods.
/// </remarks>
public interface IMapTool
{
    /// <summary>
    /// The radius of the circle the views draw around the mouse, in ogrids; 0 for none.
    /// </summary>
    double CursorRadius { get; }

    void Press(float x, float z);

    void Drag(float x, float z);

    void Release();

    void Press(float x, float z, ToolInput input) => Press(x, z);

    void Drag(float x, float z, ToolInput input) => Drag(x, z);

    /// <summary>
    /// A key pressed while the view has the keyboard, by Avalonia's name of it (<c>Escape</c>,
    /// <c>Q</c>, <c>Left</c>, ...).
    /// </summary>
    /// <returns>Whether the tool used it.</returns>
    bool Key(string key, ToolModifiers modifiers) => false;

    /// <summary>
    /// A rectangle the views draw while the mouse is dragged (corners in ogrids, any order), such
    /// as a box that selects; null for none.
    /// </summary>
    (float X0, float Z0, float X1, float Z1)? Box => null;

    /// <summary>
    /// What else the views draw for the tool, in ogrids (handles to drag, a line it measures);
    /// null for nothing.
    /// </summary>
    ToolMarks? Marks => null;
}

/// <summary>
/// What a tool shows over the map besides its box (<see cref="IMapTool.Marks"/>), in ogrids.
/// </summary>
/// <param name="Handles">Small squares to drag, such as the corners and edges of a selected area.</param>
/// <param name="Line">A line from its first point to its second, such as what the ruler measures.</param>
/// <param name="Label">Text at the line's end, such as the distance.</param>
/// <param name="Path">A line through many places, such as a curve (user story T-4).</param>
public sealed record ToolMarks(IReadOnlyList<(float X, float Z)> Handles, (float X0, float Z0, float X1, float Z1)? Line = null, string? Label = null,
    IReadOnlyList<(float X, float Z)>? Path = null);

/// <summary>
/// The keys held with the mouse or a key.
/// </summary>
[Flags]
public enum ToolModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>
/// What a view tells a tool with the mouse besides the place.
/// </summary>
/// <param name="OgridsPerPixel">How large a pixel of the view is on the map at the mouse, so that
/// a tool can reach "a few pixels" whatever the zoom.</param>
/// <param name="Picks">Whether the view picked the thing under the mouse itself (the 3D view, along
/// its ray through models and markers); the top view leaves it to the tool, which takes the nearest
/// thing on the ground.</param>
/// <param name="Picked">What the view picked, or null for nothing, when it <paramref name="Picks"/>.</param>
public readonly record struct ToolInput(ToolModifiers Modifiers, float OgridsPerPixel, bool Picks = false, ObjectRef? Picked = null)
{
    /// <summary>
    /// No keys held, at a zoom of two pixels an ogrid: for input without a view (automation's
    /// <c>stroke</c>, tests).
    /// </summary>
    public static ToolInput Default => new ToolInput(ToolModifiers.None, 0.5f);
}
