using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Cliff faces that block walkability (user story T-4, D3): the ogrids under the face of every
    /// cliff curve that blocks (<see cref="HeightCurve.BlocksWalkability"/>) among the visible curve
    /// layers of the final heightmap's stack. Walkability, <see cref="WalkabilityMarks.TerrainTypes"/>
    /// and the export take them as marked blocked unless an ogrid is marked open by hand; they are
    /// never written into the marks, so the marks and the curves stay apart and editing a curve moves
    /// its face.
    /// </summary>
    /// <remarks>
    /// A curve's ogrids are kept per curve (<see cref="HeightCurves.FaceCells"/>; curves are
    /// immutable, so a curve that did not change costs nothing) and merged per call of
    /// <see cref="Grid"/>, which keeps its last answer for the same curves.
    /// </remarks>
    public static class CliffFaces
    {
        private sealed record Cells(int Width, int Height, int[] Indices);

        private static readonly ConditionalWeakTable<HeightCurve, Cells> PerCurve = [];

        private static readonly Lock Gate = new Lock();

        private static (Key Key, bool[]? Grid, int[] Indices)? _last;

        /// <summary>
        /// Which curves' faces block a version of the document, by reference: equal for two versions
        /// whose blocking faces are the same, so it serves as a generated result's input.
        /// </summary>
        public sealed class Key : IEquatable<Key>
        {
            internal Key(int width, int height, ImmutableArray<HeightCurve> curves)
            {
                Width = width;
                Height = height;
                Curves = curves;
            }

            public int Width { get; }

            public int Height { get; }

            /// <summary>
            /// The cliffs whose faces block, lowest layer first.
            /// </summary>
            public ImmutableArray<HeightCurve> Curves { get; }

            public bool IsEmpty => Curves.IsEmpty;

            public bool Equals(Key? other)
            {
                if (other is null || other.Width != Width || other.Height != Height || other.Curves.Length != Curves.Length)
                {
                    return false;
                }
                for (int i = 0; i < Curves.Length; i++)
                {
                    if (!ReferenceEquals(Curves[i], other.Curves[i]))
                    {
                        return false;
                    }
                }
                return true;
            }

            public override bool Equals(object? obj) => Equals(obj as Key);

            public override int GetHashCode() => HashCode.Combine(Width, Height, Curves.Length, Curves.IsEmpty ? 0 : RuntimeHelpers.GetHashCode(Curves[0]));
        }

        /// <summary>
        /// The cliffs of a version whose faces block walkability: curve layers of the final stack
        /// (<see cref="MapDocument.HeightStack"/>) that are visible and have strength, whose curve is a
        /// cliff with <see cref="HeightCurve.BlocksWalkability"/>.
        /// </summary>
        public static Key KeyOf(MapDocument document)
        {
            ImmutableArray<HeightCurve>.Builder? curves = null;
            foreach (Layer layer in document.HeightStack.Layers)
            {
                if (layer is { Visible: true, Strength: > 0, Curve: { Profile: CurveProfile.Cliff, BlocksWalkability: true } curve })
                {
                    (curves ??= ImmutableArray.CreateBuilder<HeightCurve>()).Add(curve);
                }
            }
            return new Key(document.Width, document.Height, curves?.ToImmutable() ?? []);
        }

        /// <summary>
        /// The ogrids under one cliff's face on a map of that size (<see cref="HeightCurves.FaceCells"/>),
        /// kept per curve.
        /// </summary>
        public static int[] Of(HeightCurve curve, int mapWidth, int mapHeight)
        {
            if (PerCurve.TryGetValue(curve, out Cells? cells) && cells.Width == mapWidth && cells.Height == mapHeight)
            {
                return cells.Indices;
            }
            cells = new Cells(mapWidth, mapHeight, HeightCurves.FaceCells(curve, mapWidth, mapHeight));
            PerCurve.AddOrUpdate(curve, cells);
            return cells.Indices;
        }

        /// <summary>
        /// Per ogrid, row by row, whether a blocking cliff's face lies there; null when none does.
        /// The same array for the same curves (do not change it).
        /// </summary>
        public static bool[]? Grid(MapDocument document) => Merged(document).Grid;

        /// <summary>
        /// How many ogrids lie under blocking cliff faces.
        /// </summary>
        public static int Count(MapDocument document) => Merged(document).Indices.Length;

        /// <summary>
        /// The ogrids under blocking cliff faces, each once as its index row by row, in order.
        /// </summary>
        public static IReadOnlyList<int> Indices(MapDocument document) => Merged(document).Indices;

        private static (bool[]? Grid, int[] Indices) Merged(MapDocument document)
        {
            Key key = KeyOf(document);
            if (key.IsEmpty || key.Width <= 0 || key.Height <= 0)
            {
                return (null, []);
            }
            lock (Gate)
            {
                if (_last is { } last && last.Key.Equals(key))
                {
                    return (last.Grid, last.Indices);
                }
            }
            bool[] grid = new bool[key.Width * key.Height];
            List<int> indices = [];
            foreach (HeightCurve curve in key.Curves)
            {
                foreach (int index in Of(curve, key.Width, key.Height))
                {
                    if (!grid[index])
                    {
                        grid[index] = true;
                        indices.Add(index);
                    }
                }
            }
            int[] sorted = [.. indices];
            Array.Sort(sorted);
            bool[]? made = sorted.Length == 0 ? null : grid;
            lock (Gate)
            {
                _last = (key, made, sorted);
            }
            return (made, sorted);
        }
    }
}
