namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What the left mouse does in the views while a tool is active, in ogrids. The views keep the
/// rest of the mouse (moving, zooming, control and a click for the camera), which is the same
/// whatever the tool.
/// </summary>
/// <remarks>
/// A tool is made once per editor (<see cref="ViewModels.ToolDefinition.Make"/>), so its settings
/// last while the person switches tools.
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
}
