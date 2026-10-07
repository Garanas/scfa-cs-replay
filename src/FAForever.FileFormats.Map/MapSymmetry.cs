using System.Numerics;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// The ways a map can be symmetric, as map makers build them for fair games.
    /// </summary>
    public enum MapSymmetryKind
    {
        /// <summary>Turned half a circle around the centre.</summary>
        Rotational,

        /// <summary>Mirrored from left to right.</summary>
        MirroredLeftRight,

        /// <summary>Mirrored from top to bottom.</summary>
        MirroredTopBottom,

        /// <summary>Mirrored on the diagonal from the top left to the bottom right corner.</summary>
        MirroredDiagonal,

        /// <summary>Mirrored on the diagonal from the top right to the bottom left corner.</summary>
        MirroredOtherDiagonal,
    }

    /// <summary>
    /// Finds the symmetry of a map's markers.
    /// </summary>
    public static class MapSymmetry
    {
        /// <summary>
        /// The first symmetry under which every position has a counterpart within the tolerance,
        /// or null when there is none. Pass the start positions and the resource markers: they decide
        /// whether a map is fair.
        /// </summary>
        public static MapSymmetryKind? Find(int width, int height, IReadOnlyList<Vector3> positions, float tolerance = 2)
        {
            if (positions.Count == 0)
            {
                return null;
            }

            foreach (MapSymmetryKind kind in Enum.GetValues<MapSymmetryKind>())
            {
                // the diagonals only map a square map onto itself
                if (width != height && kind is MapSymmetryKind.MirroredDiagonal or MapSymmetryKind.MirroredOtherDiagonal)
                {
                    continue;
                }

                if (positions.All(position => HasCounterpart(Transform(kind, width, height, position), positions, tolerance)))
                {
                    return kind;
                }
            }
            return null;
        }

        private static Vector2 Transform(MapSymmetryKind kind, int width, int height, Vector3 p) => kind switch
        {
            MapSymmetryKind.Rotational => new Vector2(width - p.X, height - p.Z),
            MapSymmetryKind.MirroredLeftRight => new Vector2(width - p.X, p.Z),
            MapSymmetryKind.MirroredTopBottom => new Vector2(p.X, height - p.Z),
            MapSymmetryKind.MirroredDiagonal => new Vector2(p.Z, p.X),
            _ => new Vector2(width - p.Z, height - p.X),
        };

        private static bool HasCounterpart(Vector2 target, IReadOnlyList<Vector3> positions, float tolerance) =>
            positions.Any(other => Vector2.Distance(target, new Vector2(other.X, other.Z)) <= tolerance);
    }
}
