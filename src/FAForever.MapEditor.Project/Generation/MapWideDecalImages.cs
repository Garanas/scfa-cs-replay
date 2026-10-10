using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The pictures of the generated map-wide decals (the Decals mode's normal and shadow slots), made
    /// from the final heightmap: the normals decal in the game's two-channel layout with its blend in
    /// red, and the shadow decal, black with the darkness in its alpha.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game's terrain shader (<c>effects/terrain.fx</c>, <c>DecalsNormalsPS</c>, the same in the
    /// game's <c>effects.scd</c> and FAF's <c>effects.nx2</c>) reads a normals decal's x from alpha and
    /// its z from green (<c>decalRaw.ag * 2 - 1</c>), turns it by the decal's turn, and blends it over
    /// the terrain's normal with its red channel as the weight ("our blend mask is stored in the r
    /// channel of the decal": the output's alpha is <c>blendFactor * mask * DecalAlpha</c>, blended
    /// source alpha over the rest). So red says how strongly the decal's normal replaces the
    /// terrain's own. Kaali's (kaali.v0004) grows with the slope: about 0.1 on flat ground, 0.5 at 17
    /// degrees, 0.75 at 32, 0.9 at 55, which is the square root of the sine of the slope; the
    /// generator writes that, so the sharper normals show on the cliffs and the terrain's own on
    /// flat ground.
    /// </para>
    /// <para>
    /// Kaali's shadow decal (its <c>map_lighting.dds</c>, an albedo decal) is black with an alpha of 0
    /// to 0.45 that grows with the slope as ambient occlusion does: about 0.03 on flat ground and 0.25
    /// on slopes of 50 degrees, the mean of the horizon's sines in eight directions.
    /// </para>
    /// </remarks>
    public static class MapWideDecalImages
    {
        /// <summary>
        /// The largest side a map-wide decal's texture is given.
        /// </summary>
        public const int LargestSide = 8192;

        /// <summary>
        /// Pixels per ogrid of the albedo and normals decals (Kaali's: 8192 over 512 ogrids).
        /// </summary>
        public const int DetailPixelsPerOgrid = 16;

        /// <summary>
        /// Pixels per ogrid of the shadow decal and the specular (Kaali's: 2048 over 512 ogrids).
        /// </summary>
        public const int ShadowPixelsPerOgrid = 4;

        /// <summary>
        /// The size of a map-wide decal's texture at <paramref name="pixelsPerOgrid"/>, its longer side
        /// at most <paramref name="largest"/>, in the map's proportions.
        /// </summary>
        public static (int Width, int Height) SizeOf(int mapWidth, int mapHeight, int pixelsPerOgrid, int largest = LargestSide)
        {
            long width = (long)mapWidth * pixelsPerOgrid;
            long height = (long)mapHeight * pixelsPerOgrid;
            long longer = Math.Max(width, height);
            if (longer > largest)
            {
                width = Math.Max(1, width * largest / longer);
                height = Math.Max(1, height * largest / longer);
            }
            return ((int)width, (int)height);
        }

        /// <summary>
        /// The normals decal of a heightmap, <paramref name="width"/> by <paramref name="height"/>
        /// pixels over the whole map: per pixel the terrain's normal (the corners' normals blended),
        /// x in alpha and z in green as 0.5 + n / 2 (x to the east, z to the south, as the terrain's
        /// normal map), blue 0, and in red the blend: the square root of the sine of the slope.
        /// </summary>
        public static DecodedTexture Normals(Heightmap heightmap, int width, int height, CancellationToken cancel = default)
        {
            Vector3[] corners = CornerNormals(heightmap);
            int samplesX = heightmap.Width + 1;
            byte[] rgba = new byte[checked(width * height * 4)];
            double stepX = heightmap.Width / (double)width;
            double stepZ = heightmap.Height / (double)height;
            Parallel.For(0, height, new ParallelOptions { CancellationToken = cancel }, row =>
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
                    int to = (row * width + column) * 4;
                    float sine = MathF.Sqrt(normal.X * normal.X + normal.Z * normal.Z);
                    rgba[to] = Byte(MathF.Sqrt(sine));
                    rgba[to + 1] = Byte(normal.Z * 0.5f + 0.5f);
                    rgba[to + 2] = 0;
                    rgba[to + 3] = Byte(normal.X * 0.5f + 0.5f);
                }
            });
            return new DecodedTexture(width, height, rgba);
        }

        /// <summary>
        /// The shadow decal of a heightmap, <paramref name="width"/> by <paramref name="height"/>
        /// pixels over the whole map: black, with in alpha how dark each place is: its ambient
        /// occlusion (the mean over eight directions of the sine of the horizon within
        /// <see cref="ShadowDecalSettings.Radius"/>, times the strength) and, as an option, the sun's
        /// shadow (as dark as the darkest), at most <see cref="ShadowDecalSettings.Darkest"/>.
        /// </summary>
        /// <param name="sunDirection">The direction towards the sun (<see cref="Lighting.SunDirection"/>).</param>
        public static DecodedTexture Shadow(Heightmap heightmap, ShadowDecalSettings settings, Vector3 sunDirection, int width, int height, CancellationToken cancel = default)
        {
            float[] darkness = Darkness(heightmap, settings, sunDirection, cancel);
            int samplesX = heightmap.Width + 1;
            byte[] rgba = new byte[checked(width * height * 4)];
            double stepX = heightmap.Width / (double)width;
            double stepZ = heightmap.Height / (double)height;
            Parallel.For(0, height, new ParallelOptions { CancellationToken = cancel }, row =>
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
                    float value = darkness[at] * (1 - u) * (1 - v) + darkness[at + 1] * u * (1 - v)
                        + darkness[at + samplesX] * (1 - u) * v + darkness[at + samplesX + 1] * u * v;
                    rgba[(row * width + column) * 4 + 3] = Byte(value);
                }
            });
            return new DecodedTexture(width, height, rgba);
        }

        /// <summary>
        /// How dark each corner of the heightmap is, 0 to <see cref="ShadowDecalSettings.Darkest"/>.
        /// </summary>
        internal static float[] Darkness(Heightmap heightmap, ShadowDecalSettings settings, Vector3 sunDirection, CancellationToken cancel = default)
        {
            int samplesX = heightmap.Width + 1;
            int samplesZ = heightmap.Height + 1;
            ushort[] samples = heightmap.Samples.ToArray();
            float scale = heightmap.Scale;
            float radius = Math.Max(1, settings.Radius);

            // distances that grow, so a large radius costs little more than a small one
            List<float> steps = [];
            for (float d = 1; d <= radius; d = d < 4 ? d + 1 : d * 1.35f)
            {
                steps.Add(d);
            }
            Vector2[] directions = [.. Enumerable.Range(0, 8).Select(i => new Vector2(MathF.Cos(i * MathF.PI / 4), MathF.Sin(i * MathF.PI / 4)))];

            // the sun: its way across the ground and how much a ray towards it rises per ogrid
            Vector2 sunWay = new Vector2(sunDirection.X, sunDirection.Z);
            float sunRise = sunWay.Length() < 1e-4f ? float.PositiveInfinity : sunDirection.Y / sunWay.Length();
            bool sun = settings.SunShadows && sunDirection.Y > 0 && sunRise < 50;
            if (sun)
            {
                sunWay = Vector2.Normalize(sunWay);
            }
            float sunReach = sun ? Math.Min(Math.Max(samplesX, samplesZ), (samples.Max() - samples.Min()) * scale / Math.Max(sunRise, 1e-3f) + 1) : 0;

            float HeightAt(float x, float z)
            {
                x = Math.Clamp(x, 0, samplesX - 1);
                z = Math.Clamp(z, 0, samplesZ - 1);
                int x0 = Math.Min((int)x, samplesX - 2);
                int z0 = Math.Min((int)z, samplesZ - 2);
                float u = x - x0, v = z - z0;
                int at = z0 * samplesX + x0;
                return ((samples[at] * (1 - u) + samples[at + 1] * u) * (1 - v) + (samples[at + samplesX] * (1 - u) + samples[at + samplesX + 1] * u) * v) * scale;
            }

            float[] darkness = new float[samples.Length];
            Parallel.For(0, samplesZ, new ParallelOptions { CancellationToken = cancel }, z =>
            {
                for (int x = 0; x < samplesX; x++)
                {
                    float own = samples[z * samplesX + x] * scale;
                    float occlusion = 0;
                    foreach (Vector2 direction in directions)
                    {
                        float horizon = 0;
                        foreach (float d in steps)
                        {
                            float rise = (HeightAt(x + direction.X * d, z + direction.Y * d) - own) / d;
                            horizon = MathF.Max(horizon, rise);
                        }
                        occlusion += horizon / MathF.Sqrt(1 + horizon * horizon);
                    }
                    float value = occlusion / directions.Length * settings.Strength;
                    if (sun && value < settings.Darkest)
                    {
                        for (float d = 1; d <= sunReach; d += d < 8 ? 1 : 2)
                        {
                            if (HeightAt(x + sunWay.X * d, z + sunWay.Y * d) > own + sunRise * d + 0.05f)
                            {
                                value = settings.Darkest;
                                break;
                            }
                        }
                    }
                    darkness[z * samplesX + x] = Math.Clamp(value, 0, settings.Darkest);
                }
            });
            return darkness;
        }

        /// <summary>
        /// The normal at every corner of the heightmap, from the slope to its neighbours (as the
        /// terrain's normal map: <see cref="TerrainImages.NormalMap"/>).
        /// </summary>
        private static Vector3[] CornerNormals(Heightmap heightmap)
        {
            int samplesX = heightmap.Width + 1;
            int samplesZ = heightmap.Height + 1;
            ushort[] samples = heightmap.Samples.ToArray();
            float scale = heightmap.Scale;
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
            return corners;
        }

        private static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
    }
}
