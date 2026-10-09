using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What a brush will touch, as the views show it under the mouse: round, or its picture turned
/// over the square around the radius.
/// </summary>
/// <param name="X">The centre, in ogrids.</param>
/// <param name="Radius">In ogrids.</param>
/// <param name="Rotation">The picture's, in degrees clockwise in the top view.</param>
/// <param name="Shape">The picture; null for round (or while it is being read).</param>
public sealed record BrushPreview(float X, float Z, float Radius, float Rotation, BrushShape? Shape)
{
    /// <summary>
    /// The preview of a tool's brush at a place; null for a tool without a brush.
    /// </summary>
    public static BrushPreview? Of(IMapTool? tool, float x, float z) => tool switch
    {
        { CursorRadius: <= 0 } or null => null,
        IShapedBrushTool { Shape: var shape } => new BrushPreview(x, z, (float)tool.CursorRadius, (float)shape.Rotation, shape.Shown),
        _ => new BrushPreview(x, z, (float)tool.CursorRadius, 0, null),
    };
}
