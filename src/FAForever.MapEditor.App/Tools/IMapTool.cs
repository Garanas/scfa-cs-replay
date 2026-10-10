using System.Numerics;
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

    /// <summary>
    /// What the views draw for the tool at a fixed size on screen, in the view's pixels: the
    /// handles of the selection, the sun's arrow in 3D. Asked for every frame the view draws;
    /// empty for nothing.
    /// </summary>
    IReadOnlyList<ToolShape> Shapes(ToolView view) => [];
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

    /// <summary>
    /// X is held: a move goes along x only.
    /// </summary>
    AxisX = 8,

    /// <summary>
    /// Z is held: a move goes along z only.
    /// </summary>
    AxisZ = 16,
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
/// <param name="View">How the view shows the map, for what a tool draws and finds on screen (its
/// handles); null without a view, which <see cref="ViewAt"/> stands in for.</param>
public readonly record struct ToolInput(ToolModifiers Modifiers, float OgridsPerPixel, bool Picks = false, ObjectRef? Picked = null, ToolView? View = null)
{
    /// <summary>
    /// The view, or without one a view straight down at <see cref="OgridsPerPixel"/> with the mouse at a place.
    /// </summary>
    public ToolView ViewAt(float x, float z) => View ?? ToolView.Flat(OgridsPerPixel, new Vector2(x, z));

    /// <summary>
    /// No keys held, at a zoom of two pixels an ogrid: for input without a view (automation's
    /// <c>stroke</c>, tests).
    /// </summary>
    public static ToolInput Default => new ToolInput(ToolModifiers.None, 0.5f);
}

/// <summary>
/// How a view shows the map, for the shapes a tool draws at a fixed size on screen and finds under
/// the mouse (<see cref="IMapTool.Shapes"/>): where a place shows in the view's pixels, how large
/// a pixel is on the map there, the mouse, and in 3D the ray under the mouse.
/// </summary>
/// <param name="IsTopDown">The top view: straight down, heights do not matter.</param>
/// <param name="Mouse">The mouse in the view's pixels; null while it is outside.</param>
/// <param name="Ray">In 3D, the ray from the eye through the mouse; null in the top view.</param>
/// <param name="Eye">In 3D, where the camera is.</param>
public sealed class ToolView(bool isTopDown, Vector2? mouse, Func<Vector3, Vector2?> toScreen, Func<Vector3, float> ogridsPerPixelAt,
    (Vector3 Origin, Vector3 Direction)? ray = null, Vector3? eye = null)
{
    public bool IsTopDown { get; } = isTopDown;

    public Vector2? Mouse { get; } = mouse;

    public (Vector3 Origin, Vector3 Direction)? Ray { get; } = ray;

    public Vector3? Eye { get; } = eye;

    /// <summary>
    /// Where a place shows, in the view's pixels; null behind the camera.
    /// </summary>
    public Vector2? ToScreen(Vector3 place) => toScreen(place);

    /// <summary>
    /// How large a pixel of the view is on the map at a place, in ogrids.
    /// </summary>
    public float OgridsPerPixelAt(Vector3 place) => ogridsPerPixelAt(place);

    /// <summary>
    /// The place on the level plane at a height that the mouse points at: in 3D where the ray
    /// meets it (null when it does not), in the top view the place under the mouse itself.
    /// </summary>
    public Vector2? PlaneAt(float height, Vector2 place) =>
        Ray is (Vector3 origin, Vector3 direction) ? HandleMaths.PlaneHit(origin, direction, height) : place;

    /// <summary>
    /// A view straight down without a window (automation's strokes, tests): a pixel is
    /// <paramref name="ogridsPerPixel"/> ogrids, the map's origin at the view's, the mouse at a place.
    /// </summary>
    public static ToolView Flat(float ogridsPerPixel, Vector2? mouseAt = null)
    {
        float scale = 1 / MathF.Max(ogridsPerPixel, 1e-6f);
        return new ToolView(true, mouseAt * scale, place => new Vector2(place.X, place.Z) * scale, _ => ogridsPerPixel);
    }
}

/// <summary>
/// The colours a tool's shapes are drawn in, by use: each view takes them from the theme's tokens.
/// </summary>
public enum ToolInk
{
    /// <summary>
    /// The selection's outline and handles (<c>MapSelectionBrush</c>).
    /// </summary>
    Selection,

    /// <summary>
    /// The inside of a handle (<c>CanvasBrush</c>).
    /// </summary>
    Fill,

    /// <summary>
    /// The handle under the mouse or being dragged (<c>OverlayBrush</c>).
    /// </summary>
    Hover,

    /// <summary>
    /// What moves along x (<c>MapHandleAxisXBrush</c>).
    /// </summary>
    AxisX,

    /// <summary>
    /// What moves along z (<c>MapHandleAxisZBrush</c>).
    /// </summary>
    AxisZ,

    /// <summary>
    /// A dark line under a light one, so it shows on any ground (<c>OverlayShadowBrush</c>).
    /// </summary>
    Shadow,

    /// <summary>
    /// The sun (<c>WarningBrush</c>).
    /// </summary>
    Sun,
}

/// <summary>
/// Something a tool draws at a fixed size on screen (<see cref="IMapTool.Shapes"/>), in the view's pixels.
/// </summary>
public abstract record ToolShape;

/// <summary>
/// A line, or several joined (a ring), <paramref name="Width"/> pixels wide.
/// </summary>
public sealed record ToolLine(Vector2[] Points, bool Closed, float Width, ToolInk Ink) : ToolShape;

/// <summary>
/// A square, <paramref name="Half"/> pixels from its centre to a side, filled and outlined.
/// </summary>
public sealed record ToolSquare(Vector2 Centre, float Half, ToolInk Fill, ToolInk Stroke) : ToolShape;

/// <summary>
/// A line in a colour of the map's own rather than an ink of the theme, such as the links between
/// path nodes in their layer's colour; red, green, blue and opacity from 0 to 1.
/// </summary>
public sealed record ToolMapLine(Vector2 From, Vector2 To, float Width, Vector4 Color) : ToolShape;

/// <summary>
/// A filled triangle, such as an arrow's head.
/// </summary>
public sealed record ToolTriangle(Vector2 A, Vector2 B, Vector2 C, ToolInk Ink) : ToolShape;
