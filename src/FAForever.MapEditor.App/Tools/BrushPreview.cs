using FAForever.FileFormats.Map;
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
/// <param name="Mirrored">Whether the picture is flipped left to right before it is turned: the
/// brush's image in a mirrored part of a symmetric map.</param>
public sealed record BrushPreview(float X, float Z, float Radius, float Rotation, BrushShape? Shape, bool Mirrored = false)
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

    /// <summary>
    /// The brush and its images in the other parts of the map, where a stroke paints them
    /// (<see cref="SymmetricDabs.Of"/>, user story F-12): the brush itself first; it alone for the
    /// identity alone.
    /// </summary>
    /// <param name="transforms">The parts an edit is mirrored into (<see cref="ViewModels.MainViewModel.EditTransforms"/>).</param>
    public IReadOnlyList<BrushPreview> Images(IReadOnlyList<MapSymmetryTransform> transforms)
    {
        if (transforms.Count <= 1)
        {
            return [this];
        }
        IReadOnlyList<SymmetricDab> dabs = SymmetricDabs.Of(transforms, X, Z, Rotation, Shape is null);
        return [.. dabs.Select(dab => this with { X = dab.X, Z = dab.Z, Rotation = dab.Rotation, Mirrored = dab.Mirrored })];
    }
}
