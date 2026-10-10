using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Small maps made in code for tests that need terrain of a known shape: no file of the game.
    /// </summary>
    public static class TestMaps
    {
        /// <summary>
        /// The height of <see cref="Slope"/>'s low ground, in game units.
        /// </summary>
        public const float Low = 40;

        /// <summary>
        /// Where <see cref="Slope"/>'s ramp starts and ends (x in ogrids); it runs along the whole z.
        /// </summary>
        public const int RampStart = 96, RampEnd = 160;

        /// <summary>
        /// A 5 km map (256 ogrids), no water: flat at <see cref="Low"/> west of x = 96, a clean ramp
        /// rising eastwards at <paramref name="degrees"/> (as near as the heightmap holds, <see cref="Rise"/>) to x = 160 (the same slope at every
        /// sample of it, z makes no difference), and flat again east of it at
        /// <see cref="High"/>. For slope limits, aligning things to the terrain, following it.
        /// </summary>
        public static MapDocument Slope(float degrees = 30)
        {
            MapDocument flat = NewMap.Create(new NewMapSettings { Name = "Slope", Size = 256, Height = Low, Water = false });
            Heightmap heights = flat.Terrain.Heightmap;
            TiledBuffer<ushort>.Builder samples = heights.Samples.ToBuilder();
            float rise = Rise(degrees);
            for (int z = 0; z < samples.Height; z++)
            {
                for (int x = 0; x < samples.Width; x++)
                {
                    float height = Low + rise * Math.Clamp(x - RampStart, 0, RampEnd - RampStart);
                    samples[x, z] = (ushort)Math.Clamp(MathF.Round(height / heights.Scale), 0, ushort.MaxValue);
                }
            }
            return flat with { Terrain = flat.Terrain with { Heightmap = heights with { Samples = samples.ToBuffer() } } };
        }

        /// <summary>
        /// The height of <see cref="Slope"/>'s high ground for a slope of <paramref name="degrees"/>.
        /// </summary>
        public static float High(float degrees = 30) => Low + Rise(degrees) * (RampEnd - RampStart);

        /// <summary>
        /// How much the ramp rises per ogrid: the tangent of the slope, rounded to the heightmap's
        /// step (1/128), so that every sample of the ramp rises exactly as much (30 degrees is
        /// 74/128, 30.03 degrees).
        /// </summary>
        public static float Rise(float degrees) => MathF.Round(MathF.Tan(degrees * MathF.PI / 180) * 128) / 128;

        /// <summary>
        /// <see cref="Slope"/> saved as a project in a new folder, to open.
        /// </summary>
        /// <returns>The project's folder.</returns>
        public static string SlopeProject(float degrees = 30)
        {
            string folder = Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", "slope-" + Guid.NewGuid().ToString("N"));
            MapProjectStore.Save(Slope(degrees), folder);
            return folder;
        }
    }
}
