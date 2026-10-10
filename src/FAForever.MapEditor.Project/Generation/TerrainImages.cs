using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The images of a map made from its terrain (user story S-4): the normal map the game's terrain
    /// shader lights the terrain with.
    /// </summary>
    public static class TerrainImages
    {
        /// <summary>
        /// The normal map of a heightmap, <paramref name="width"/> by <paramref name="height"/> pixels
        /// (the map's own size in ogrids): per pixel the normal of the terrain at its centre, in the
        /// form the game reads it, a DXT5 normal map with the normal's x in the alpha channel and its z
        /// in green (each as 0.5 + n / 2, x to the east and z to the south), red and blue 0.
        /// </summary>
        /// <remarks>
        /// As the FAF map editor makes it (<c>GenerateControlTex.GeneratingNormal</c>: the terrain's
        /// normal interpolated at the pixel's centre, written as <c>(0, 0.5 - z/2, 0, 0.5 + x/2)</c> in
        /// Unity's coordinates, whose z points north). The normal at a corner of the heightmap is that
        /// of its neighbours' slope; inside an ogrid the four corners' normals are blended.
        /// </remarks>
        public static TiledBuffer<Color32> NormalMap(Heightmap heightmap, int width, int height)
        {
            int samplesX = heightmap.Width + 1;
            int samplesZ = heightmap.Height + 1;
            ushort[] samples = heightmap.Samples.ToArray();
            float scale = heightmap.Scale;

            // the normal at every corner, from the slope to its neighbours
            Vector3[] corners = new Vector3[samples.Length];
            Parallel.For(0, samplesZ, z =>
            {
                int north = Math.Max(0, z - 1);
                int south = Math.Min(samplesZ - 1, z + 1);
                for (int x = 0; x < samplesX; x++)
                {
                    int west = Math.Max(0, x - 1);
                    int east = Math.Min(samplesX - 1, x + 1);
                    float slopeX = (samples[z * samplesX + east] - samples[z * samplesX + west]) * scale / (east - west);
                    float slopeZ = (samples[south * samplesX + x] - samples[north * samplesX + x]) * scale / (south - north);
                    corners[z * samplesX + x] = Vector3.Normalize(new Vector3(-slopeX, 1, -slopeZ));
                }
            });

            Color32[] pixels = new Color32[width * height];
            double stepX = heightmap.Width / (double)width;
            double stepZ = heightmap.Height / (double)height;
            Parallel.For(0, height, row =>
            {
                double z = (row + 0.5) * stepZ;
                int z0 = Math.Min((int)z, heightmap.Height - 1);
                float v = (float)(z - z0);
                for (int column = 0; column < width; column++)
                {
                    double x = (column + 0.5) * stepX;
                    int x0 = Math.Min((int)x, heightmap.Width - 1);
                    float u = (float)(x - x0);
                    int at = z0 * samplesX + x0;
                    Vector3 normal = Vector3.Normalize(
                        corners[at] * (1 - u) * (1 - v)
                        + corners[at + 1] * u * (1 - v)
                        + corners[at + samplesX] * (1 - u) * v
                        + corners[at + samplesX + 1] * u * v);
                    pixels[row * width + column] = new Color32(Channel(normal.X), 0, Channel(normal.Z), 0);
                }
            });
            return TiledBuffer<Color32>.FromArray(width, height, pixels);
        }

        /// <summary>
        /// A component of a normal, -1 to 1, as a channel: 0 to 255 around 128.
        /// </summary>
        private static byte Channel(float value) => (byte)Math.Clamp(MathF.Round((value * 0.5f + 0.5f) * 255), 0, 255);
    }
}
