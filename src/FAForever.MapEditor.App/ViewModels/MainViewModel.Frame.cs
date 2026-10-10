using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Raised when the top view is to show a rectangle of the map (x0, z0, x1, z1 in ogrids), as
    /// large as it fits with a margin.
    /// </summary>
    public event Action<double, double, double, double>? TopViewFrameRequested;

    /// <summary>
    /// The least half size of a frame, in ogrids: one thing alone shows with its surroundings.
    /// </summary>
    public const float FrameMinimumRadius = 12;

    /// <summary>
    /// Frames things in both views (user story V-1): the top view shows the rectangle around them,
    /// the 3D camera focuses on its middle from as far as it takes to see it. Things the map does
    /// not have are left out.
    /// </summary>
    /// <returns>The rectangle framed (x0, z0, x1, z1), or null when none of the things exists.</returns>
    public (float X0, float Z0, float X1, float Z1)? Frame(IEnumerable<ObjectRef> things)
    {
        MapDocument document = ShownDocument ?? throw new InvalidOperationException("No map is open");
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        void Add(float x, float z)
        {
            x0 = Math.Min(x0, x);
            z0 = Math.Min(z0, z);
            x1 = Math.Max(x1, x);
            z1 = Math.Max(z1, z);
        }
        foreach (ObjectRef thing in things)
        {
            if (document.PlaceOf(thing) is Vector3 place)
            {
                Add(place.X, place.Z);
                foreach (Vector2 corner in document.OutlineOf(thing))
                {
                    Add(corner.X, corner.Y);
                }
            }
        }
        if (x0 > x1)
        {
            return null;
        }
        float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        float radius = Math.Max(FrameMinimumRadius, MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / 2);
        TopViewFrameRequested?.Invoke(cx - radius, cz - radius, cx + radius, cz + radius);
        FocusCamera(cx, cz, radius);
        return (x0, z0, x1, z1);
    }
}
