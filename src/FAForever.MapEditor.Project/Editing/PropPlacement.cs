using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// How a placed prop stands on the terrain.
    /// </summary>
    public enum PropAlignment
    {
        /// <summary>
        /// By what it is: rocks and the like tilt with the ground, trees and the rest stand upright
        /// (<see cref="PropOrientation.TiltsWithTerrain"/>).
        /// </summary>
        ByCategory,

        Upright,

        /// <summary>
        /// Tilted so that its up follows the terrain's normal.
        /// </summary>
        Terrain,
    }

    /// <summary>
    /// How props are placed, by hand or with the paint brush (user stories P-1, P-2): how they stand
    /// on the terrain, and how much at random they are turned, tilted and scaled.
    /// </summary>
    public sealed record PropPlacement
    {
        public PropAlignment Alignment { get; init; } = PropAlignment.ByCategory;

        /// <summary>
        /// Whether each prop gets a heading at random.
        /// </summary>
        public bool RandomTurn { get; init; } = true;

        /// <summary>
        /// The most a prop leans at random, in degrees, after it is aligned.
        /// </summary>
        public float RandomTilt { get; init; }

        /// <summary>
        /// The smallest scale at random; with <see cref="ScaleMax"/> the same, every prop has that scale.
        /// </summary>
        public float ScaleMin { get; init; } = 1;

        public float ScaleMax { get; init; } = 1;

        /// <summary>
        /// How far into the terrain a prop is set, in game units.
        /// </summary>
        public float Sink { get; init; }

        /// <summary>
        /// A prop of a blueprint at a place in ogrids, standing on the terrain as these settings say.
        /// </summary>
        /// <param name="heading">Its heading in radians; null for a random one when
        /// <see cref="RandomTurn"/>, else 0.</param>
        public Prop Make(string blueprint, float x, float z, Heightmap heights, Random random, float? heading = null)
        {
            float turn = heading ?? (RandomTurn ? (float)(random.NextDouble() * 2 * Math.PI - Math.PI) : 0);
            bool tilts = Alignment switch
            {
                PropAlignment.Terrain => true,
                PropAlignment.Upright => false,
                _ => PropOrientation.TiltsWithTerrain(PropOrientation.CategoryOf(blueprint)),
            };
            Vector3 up = tilts ? PropOrientation.TerrainNormal(heights, x, z) : Vector3.UnitY;
            Prop prop = PropOrientation.Oriented(new Prop { Id = 0, Blueprint = blueprint }, turn, up);
            if (RandomTilt > 0)
            {
                // a lean in a direction at random, up to the most, on top of the alignment
                float lean = (float)(random.NextDouble() * RandomTilt * Math.PI / 180);
                float towards = (float)(random.NextDouble() * 2 * Math.PI);
                (float pitch, float roll) = PropOrientation.Tilt(prop);
                prop = PropOrientation.WithTilt(prop, pitch + lean * MathF.Cos(towards), roll + lean * MathF.Sin(towards));
            }
            float low = Math.Max(0.01f, Math.Min(ScaleMin, ScaleMax));
            float high = Math.Max(low, Math.Max(ScaleMin, ScaleMax));
            float scale = low + (float)random.NextDouble() * (high - low);
            return prop with
            {
                Position = new Vector3(x, heights.HeightAt(x, z) - Sink, z),
                Scale = new Vector3(scale),
            };
        }
    }
}
