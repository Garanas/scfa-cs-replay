using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// A sky cube map made from the map (the Procedural mode, user story L-1): six faces seen from a
    /// point above the map's middle, the map's version 60 sky above the horizon (<see cref="SkyboxSky"/>;
    /// one made from the lighting when it has none, <see cref="Skyboxes.FromLighting"/>) and below it
    /// the map itself with its water, written as a DDS cube map among the map's own files, so it can
    /// be the sky's, the units' and the water's cube map and reflections show this map's colours.
    /// </summary>
    /// <remarks>
    /// The editor draws the map below the horizon with its 3D renderer when it can; on its own (and in
    /// tests) <see cref="DrawTerrain"/> does it on the processor: each ray from the eye is followed
    /// over the heightmap (its highest points in squares of 1 to 2^n ogrids let it pass high ground
    /// in long steps) to where it meets the ground or the water, coloured by the map's textured
    /// picture from above (<see cref="TexturedPreview"/>: the strata lit by the sun, the water's
    /// colour over the depth); the water adds the sky it reflects by FAF's Fresnel term
    /// (<c>water2.fx</c>, F0 0.08) times its sky reflection. A ray that leaves the map shows the sky.
    /// </remarks>
    public static class SkyCubeMaps
    {
        /// <summary>
        /// Where the cube map lies in the map's folder (one of <see cref="MapDocument.Files"/>; the
        /// project keeps it in <c>files/</c>, the export copies it).
        /// </summary>
        public const string FilePath = "env/cubemaps/skycube_generated.dds";

        /// <summary>
        /// The game path of the cube map in a map's folder, as the sky and water name it.
        /// </summary>
        public static string GamePath(MapDocument document) => $"/maps/{document.Folder}/{FilePath}";

        /// <summary>
        /// The size of the picture from above the terrain is coloured by, at most.
        /// </summary>
        public const int LargestPicture = 2048;

        /// <summary>
        /// Where the faces are seen from: above the map's middle by the eye height, over the terrain or
        /// the water there, whichever is higher.
        /// </summary>
        public static Vector3 Eye(MapDocument document, SkyCubeSettings settings)
        {
            float x = document.Width / 2f;
            float z = document.Height / 2f;
            float ground = document.Terrain.Heightmap.HeightAt(x, z);
            if (document.Water.HasWater)
            {
                ground = Math.Max(ground, document.Water.Elevation);
            }
            return new Vector3(x, ground + Math.Max(0, settings.EyeHeight), z);
        }

        /// <summary>
        /// The sky the faces show: the map's skybox, or one made from its lighting.
        /// </summary>
        public static Skybox SkyOf(MapDocument document) => document.Sky.Skybox ?? Skyboxes.FromLighting(document);

        /// <summary>
        /// The sky alone as six faces, the sun's disc in it when the settings say so.
        /// </summary>
        /// <param name="texture">The texture of a game path (the sky's atlas, glow and cirrus); null
        /// when it cannot be read.</param>
        public static DecodedTexture RenderSky(MapDocument document, SkyCubeSettings settings, Func<string, DecodedTexture?> texture, CancellationToken cancel = default)
        {
            SkyboxSky sky = new SkyboxSky(SkyOf(document), Eye(document, settings), texture,
                settings.SunDisc ? document.Lighting.SunDirection : null, document.Lighting.SunColor * Math.Max(0.5f, document.Lighting.Multiplier));
            return sky.Render(SkyCubeSettings.ValidResolution(settings.Resolution), cancel);
        }

        /// <summary>
        /// The whole cube map on the processor: the sky, and the map below the horizon when the
        /// settings include it.
        /// </summary>
        public static DecodedTexture Render(MapDocument document, SkyCubeSettings settings, Func<string, DecodedTexture?> texture, CancellationToken cancel = default)
        {
            DecodedTexture sky = RenderSky(document, settings, texture, cancel);
            return settings.IncludeTerrain ? DrawTerrain(document, settings, sky, texture, cancel) : sky;
        }

        /// <summary>
        /// The map drawn over the faces of a sky (<see cref="RenderSky"/>) where the rays from the eye
        /// meet the ground or the water; a new strip.
        /// </summary>
        public static DecodedTexture DrawTerrain(MapDocument document, SkyCubeSettings settings, DecodedTexture sky, Func<string, DecodedTexture?> texture, CancellationToken cancel = default)
        {
            int pictureSize = Math.Clamp((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(document.Width, document.Height)), 256, LargestPicture);
            TiledBuffer<Color32> picture = TexturedPreview.Render(document, texture, pictureSize);
            Color32[] colours = picture.ToArray();
            float perPixel = Math.Max(document.Width, document.Height) / (float)pictureSize;
            HeightPyramid heights = new HeightPyramid(document.Terrain.Heightmap);
            Vector3 eye = Eye(document, settings);
            Water water = document.Water;
            float skyReflection = water.SkyReflection == 1.5f ? 1 : Math.Clamp(water.SkyReflection, 0, 2);

            Vector3 Ground(float x, float z)
            {
                int column = Math.Clamp((int)(x / perPixel), 0, pictureSize - 1);
                int row = Math.Clamp((int)(z / perPixel), 0, pictureSize - 1);
                Color32 colour = colours[row * pictureSize + column];
                return new Vector3(colour.Red, colour.Green, colour.Blue) / 255f;
            }

            int size = sky.Height;
            byte[] rgba = (byte[])sky.Rgba.Clone();
            for (int face = 0; face < 6; face++)
            {
                cancel.ThrowIfCancellationRequested();
                Parallel.For(0, size, new ParallelOptions { CancellationToken = cancel }, row =>
                {
                    for (int column = 0; column < size; column++)
                    {
                        Vector3 direction = CubeFaces.PixelDirection(face, column, row, size);
                        float? ground = heights.Hit(eye, direction);
                        float? surface = water.HasWater && direction.Y < 0 && eye.Y > water.Elevation ? (water.Elevation - eye.Y) / direction.Y : null;
                        if (surface is float s && !heights.Inside(eye + direction * s))
                        {
                            surface = null;
                        }
                        Vector3 colour;
                        if (surface is float t && (ground is null || t <= ground.Value))
                        {
                            Vector3 at = eye + direction * t;
                            colour = Ground(at.X, at.Z);
                            float fresnel = 0.08f + 0.92f * MathF.Pow(1 - Math.Clamp(-direction.Y, 0, 1), 5);
                            float strength = Math.Clamp(fresnel * skyReflection, 0, 1);
                            Vector3 reflected = new Vector3(direction.X, -direction.Y, direction.Z);
                            colour = Vector3.Lerp(colour, CubeFaces.Sample(sky, reflected), strength);
                        }
                        else if (ground is float g)
                        {
                            Vector3 at = eye + direction * g;
                            colour = Ground(at.X, at.Z);
                        }
                        else
                        {
                            continue;
                        }
                        int index = (row * 6 * size + face * size + column) * 4;
                        rgba[index] = (byte)Math.Clamp((int)MathF.Round(colour.X * 255), 0, 255);
                        rgba[index + 1] = (byte)Math.Clamp((int)MathF.Round(colour.Y * 255), 0, 255);
                        rgba[index + 2] = (byte)Math.Clamp((int)MathF.Round(colour.Z * 255), 0, 255);
                        rgba[index + 3] = 255;
                    }
                });
            }
            return new DecodedTexture(sky.Width, sky.Height, rgba);
        }

        /// <summary>
        /// A strip of six faces as a DDS cube map: DXT1 with a full chain of mipmaps per face, the
        /// faces in their order (+x, -x, +y, -y, +z, -z), as the game's own sky cube maps are DXT1
        /// (<c>textures/environment/SkyCube_Desert01.dds</c>) and its environment cube maps carry
        /// mipmaps (<c>EnvCube_aeon_desert.dds</c>).
        /// </summary>
        public static byte[] Dds(DecodedTexture strip) => DdsTexture.WriteCube(strip, DdsCompression.Dxt1);

        /// <summary>
        /// The heights of the map with, for squares of 1, 2, 4, ... ogrids, the highest point in each:
        /// a ray passes a square above its highest point in one step.
        /// </summary>
        private sealed class HeightPyramid
        {
            private readonly Heightmap _heightmap;
            private readonly List<(int Width, int Height, float[] Highest)> _levels = [];

            public HeightPyramid(Heightmap heightmap)
            {
                _heightmap = heightmap;
                int width = Math.Max(1, heightmap.Width);
                int height = Math.Max(1, heightmap.Height);
                float[] cells = new float[width * height];
                Parallel.For(0, height, z =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int x1 = Math.Min(x + 1, heightmap.Width);
                        int z1 = Math.Min(z + 1, heightmap.Height);
                        cells[z * width + x] = Math.Max(Math.Max(heightmap.GetHeight(x, z), heightmap.GetHeight(x1, z)), Math.Max(heightmap.GetHeight(x, z1), heightmap.GetHeight(x1, z1)));
                    }
                });
                _levels.Add((width, height, cells));
                while (width > 1 || height > 1)
                {
                    int w = (width + 1) / 2;
                    int h = (height + 1) / 2;
                    float[] above = new float[w * h];
                    (int Width, int Height, float[] Highest) below = _levels[^1];
                    for (int z = 0; z < h; z++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            float highest = float.MinValue;
                            for (int dz = 0; dz < 2; dz++)
                            {
                                for (int dx = 0; dx < 2; dx++)
                                {
                                    int bx = Math.Min(2 * x + dx, below.Width - 1);
                                    int bz = Math.Min(2 * z + dz, below.Height - 1);
                                    highest = Math.Max(highest, below.Highest[bz * below.Width + bx]);
                                }
                            }
                            above[z * w + x] = highest;
                        }
                    }
                    _levels.Add((w, h, above));
                    (width, height) = (w, h);
                }
            }

            public bool Inside(Vector3 place) => place.X >= 0 && place.Z >= 0 && place.X <= _heightmap.Width && place.Z <= _heightmap.Height;

            /// <summary>
            /// How far along a direction (of length one) from the eye the ray meets the ground; null
            /// when it leaves the map first.
            /// </summary>
            public float? Hit(Vector3 eye, Vector3 direction)
            {
                float width = _heightmap.Width;
                float height = _heightmap.Height;
                // the stretch of the ray over the map
                float near = 0, far = float.MaxValue;
                if (!Clip(eye.X, direction.X, width, ref near, ref far) || !Clip(eye.Z, direction.Z, height, ref near, ref far))
                {
                    return null;
                }
                float top = _levels[^1].Highest[0];
                // not below the highest point anywhere on the way
                if (direction.Y >= 0 && eye.Y > top)
                {
                    return null;
                }
                if (direction.Y < 0 && eye.Y > top)
                {
                    near = Math.Max(near, (eye.Y - top) / -direction.Y);
                }
                float t = near;
                int level = _levels.Count - 1;
                for (int steps = 0; steps < 20000 && t <= far; steps++)
                {
                    Vector3 at = eye + direction * t;
                    int size = 1 << level;
                    (int levelWidth, int levelHeight, float[] highest) = _levels[level];
                    int cx = Math.Clamp((int)(at.X / size), 0, levelWidth - 1);
                    int cz = Math.Clamp((int)(at.Z / size), 0, levelHeight - 1);
                    float exit = Math.Min(far, Exit(at, direction, cx * size, cz * size, size, t));
                    float lowest = Math.Min(at.Y, eye.Y + direction.Y * exit);
                    if (lowest > highest[cz * levelWidth + cx])
                    {
                        // over this square: on to the next, and try larger squares
                        t = exit + 1e-3f;
                        level = Math.Min(level + 1, _levels.Count - 1);
                        continue;
                    }
                    if (level > 0)
                    {
                        level--;
                        continue;
                    }
                    // an ogrid the ray may meet: follow it in small steps
                    if (March(eye, direction, t, exit) is float hit)
                    {
                        return hit;
                    }
                    t = exit + 1e-3f;
                }
                return null;
            }

            private float? March(Vector3 eye, Vector3 direction, float from, float to)
            {
                const float Step = 0.25f;
                float previous = from;
                float before = Above(eye, direction, from);
                if (before <= 0)
                {
                    return from;
                }
                for (float t = Math.Min(to, from + Step); ; t = Math.Min(to, t + Step))
                {
                    float above = Above(eye, direction, t);
                    if (above <= 0)
                    {
                        // between the two: where the ray crosses the ground
                        float a = previous, b = t;
                        for (int i = 0; i < 8; i++)
                        {
                            float middle = (a + b) / 2;
                            if (Above(eye, direction, middle) > 0)
                            {
                                a = middle;
                            }
                            else
                            {
                                b = middle;
                            }
                        }
                        return b;
                    }
                    previous = t;
                    if (t >= to)
                    {
                        return null;
                    }
                }
            }

            private float Above(Vector3 eye, Vector3 direction, float t)
            {
                Vector3 at = eye + direction * t;
                return at.Y - _heightmap.HeightAt(at.X, at.Z);
            }

            /// <summary>
            /// Where the ray leaves a square, from the distance it is at.
            /// </summary>
            private static float Exit(Vector3 at, Vector3 direction, float x0, float z0, float size, float t)
            {
                float exit = float.MaxValue;
                if (direction.X > 1e-9f)
                {
                    exit = Math.Min(exit, (x0 + size - at.X) / direction.X);
                }
                else if (direction.X < -1e-9f)
                {
                    exit = Math.Min(exit, (x0 - at.X) / direction.X);
                }
                if (direction.Z > 1e-9f)
                {
                    exit = Math.Min(exit, (z0 + size - at.Z) / direction.Z);
                }
                else if (direction.Z < -1e-9f)
                {
                    exit = Math.Min(exit, (z0 - at.Z) / direction.Z);
                }
                return t + Math.Max(0, exit);
            }

            /// <summary>
            /// Narrows the stretch of a ray to where one coordinate lies between 0 and the end.
            /// </summary>
            private static bool Clip(float start, float direction, float end, ref float near, ref float far)
            {
                if (MathF.Abs(direction) < 1e-9f)
                {
                    return start >= 0 && start <= end;
                }
                float a = (0 - start) / direction;
                float b = (end - start) / direction;
                near = Math.Max(near, Math.Min(a, b));
                far = Math.Min(far, Math.Max(a, b));
                return near <= far;
            }
        }
    }
}
