using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The version 60 sky (<see cref="Skybox"/>) drawn on the processor into the six faces of a cube
    /// map, as the game's <c>effects/sky.fx</c> (in <c>effects.scd</c>) draws its dome: the
    /// atmosphere, the sky objects (planets, stars) and the cirrus layers, plus the sun as a disc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The atmosphere</b> (<c>AtmospherePS</c>): the dome is a sphere of the skybox's
    /// <see cref="Skybox.Scale"/> around its <see cref="Skybox.Position"/>; a direction meets it at a
    /// height, and <c>tv = (height - HorizonHeight) / (ZenithHeight - HorizonHeight)</c>, clamped,
    /// mixes the horizon's colour into the zenith's. The engine shapes <c>tv</c> through a lookup
    /// texture it makes itself; this takes it as it is (and through <see cref="Skybox.MidColor"/>
    /// halfway when that colour is not black). Below the horizon the dome shows the horizon's colour.
    /// </para>
    /// <para>
    /// <b>The sky objects</b> (<c>DecalVS</c>, <c>DecalAlbedoPS</c>, <c>DecalGlowPS</c>): each planet
    /// is a square facing the eye at its position, <see cref="Planet.Scale"/> from its middle to its
    /// sides, turned by <see cref="Planet.Rotation"/> (radians), showing the part
    /// <see cref="Planet.Uv"/> (x, y, width, height; y from the atlas's bottom) of the albedo atlas,
    /// blended by its alpha in the order listed. The game writes the glow atlas's alpha times
    /// <see cref="Skybox.DecalGlowMultiplier"/> into the frame's alpha for its bloom; here it brightens
    /// the planet by as much.
    /// </para>
    /// <para>
    /// <b>The cirrus</b> (<c>DomeVS</c>, <c>CirrusPS</c>): each of four layers reads one channel (red,
    /// green, blue, alpha) of the cirrus texture at the place on the dome turned into the layer's
    /// direction and times its frequency (at time 0, so the speed does not move them); the product of
    /// the four times <see cref="Skybox.CirrusMultiplier"/> blends <see cref="Skybox.CirrusColor"/> over
    /// the sky. A layer the skybox lacks counts as 1.
    /// </para>
    /// </remarks>
    public sealed class SkyboxSky
    {
        // the sun's disc: its radius as the cosine of its angle (about 1.3 degrees) and how far its rim fades
        private const float SunDiscCos = 0.99974f;
        private const float SunRimCos = 0.99955f;

        private readonly Skybox _skybox;
        private readonly Vector3 _eye;
        private readonly Vector3? _sun;
        private readonly Vector3 _sunColor;
        private readonly Picture? _albedo;
        private readonly Picture? _glow;
        private readonly Picture? _cirrus;

        /// <param name="skybox">The sky; <see cref="SkyboxDefaults.FromLighting"/> for a map without one.</param>
        /// <param name="eye">Where the faces are seen from, in the map's units.</param>
        /// <param name="texture">The texture of a game path (the atlas, the glow, the cirrus); null
        /// when it cannot be read, which leaves out what it draws.</param>
        /// <param name="sun">The direction towards the sun for its disc; null for none.</param>
        /// <param name="sunColor">The sun's colour (the lighting's), its disc white-hot towards the middle.</param>
        public SkyboxSky(Skybox skybox, Vector3 eye, Func<string, DecodedTexture?> texture, Vector3? sun = null, Vector3 sunColor = default)
        {
            _skybox = skybox;
            _eye = eye;
            _sun = sun is Vector3 s && s.LengthSquared() > 1e-12f ? Vector3.Normalize(s) : null;
            _sunColor = sunColor;
            _albedo = skybox.Planets.IsEmpty ? null : Picture.Of(Read(texture, skybox.Albedo));
            _glow = skybox.Planets.IsEmpty || skybox.DecalGlowMultiplier <= 0 ? null : Picture.Of(Read(texture, skybox.Glow));
            _cirrus = skybox.CirrusMultiplier > 0 && !skybox.CirrusLayers.IsEmpty ? Picture.Of(Read(texture, skybox.CirrusTexture)) : null;
        }

        /// <summary>
        /// Which of the sky's textures could be read: the atlas, the glow, the cirrus (each null when
        /// the sky does not use it).
        /// </summary>
        public (bool? Albedo, bool? Glow, bool? Cirrus) TexturesRead => (
            _skybox.Planets.IsEmpty ? null : _albedo is not null,
            _skybox.Planets.IsEmpty || _skybox.DecalGlowMultiplier <= 0 ? null : _glow is not null,
            _skybox.CirrusMultiplier > 0 && !_skybox.CirrusLayers.IsEmpty ? _cirrus is not null : null);

        private static DecodedTexture? Read(Func<string, DecodedTexture?> texture, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            try
            {
                return texture(path);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ImageMagick.MagickException)
            {
                return null;
            }
        }

        /// <summary>
        /// The six faces as a strip (<see cref="CubeMaps"/>), <paramref name="size"/> pixels square each, opaque.
        /// </summary>
        public DecodedTexture Render(int size, CancellationToken cancel = default)
        {
            byte[] rgba = new byte[6 * size * size * 4];
            for (int face = 0; face < 6; face++)
            {
                cancel.ThrowIfCancellationRequested();
                Vector3[] colors = new Vector3[size * size];
                Parallel.For(0, size, new ParallelOptions { CancellationToken = cancel }, row =>
                {
                    for (int column = 0; column < size; column++)
                    {
                        colors[row * size + column] = Atmosphere(CubeFaces.PixelDirection(face, column, row, size));
                    }
                });
                DrawPlanets(face, size, colors, cancel);
                Parallel.For(0, size, new ParallelOptions { CancellationToken = cancel }, row =>
                {
                    for (int column = 0; column < size; column++)
                    {
                        Vector3 direction = CubeFaces.PixelDirection(face, column, row, size);
                        Vector3 color = Cirrus(direction, colors[row * size + column]);
                        color = Sun(direction, color);
                        int at = (row * 6 * size + face * size + column) * 4;
                        rgba[at] = ToByte(color.X);
                        rgba[at + 1] = ToByte(color.Y);
                        rgba[at + 2] = ToByte(color.Z);
                        rgba[at + 3] = 255;
                    }
                });
            }
            return new DecodedTexture(6 * size, size, rgba);
        }

        private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);

        /// <summary>
        /// Where a direction from the eye meets the dome.
        /// </summary>
        private Vector3 OnDome(Vector3 direction)
        {
            float radius = _skybox.Scale > 0 ? _skybox.Scale : 1000;
            Vector3 from = _eye - _skybox.Position;
            float b = Vector3.Dot(from, direction);
            float c = from.LengthSquared() - radius * radius;
            float discriminant = b * b - c;
            // the eye outside the dome: as far as its radius
            float t = discriminant >= 0 && -b + MathF.Sqrt(discriminant) > 0 ? -b + MathF.Sqrt(discriminant) : radius;
            return _eye + direction * t;
        }

        /// <summary>
        /// The atmosphere's colour in a direction (<c>AtmospherePS</c>).
        /// </summary>
        public Vector3 Atmosphere(Vector3 direction)
        {
            if (direction.Y < 0)
            {
                return _skybox.HorizonColor;
            }
            float height = OnDome(direction).Y;
            float span = _skybox.ZenithHeight - _skybox.HorizonHeight;
            float tv = span == 0 ? (height >= _skybox.ZenithHeight ? 1 : 0) : Math.Clamp((height - _skybox.HorizonHeight) / span, 0, 1);
            Color32 mid = _skybox.MidColor;
            if (mid.Red == 0 && mid.Green == 0 && mid.Blue == 0)
            {
                return Vector3.Lerp(_skybox.HorizonColor, _skybox.ZenithColor, tv);
            }
            Vector3 middle = new Vector3(mid.Red, mid.Green, mid.Blue) / 255f;
            return tv < 0.5f
                ? Vector3.Lerp(_skybox.HorizonColor, middle, tv * 2)
                : Vector3.Lerp(middle, _skybox.ZenithColor, tv * 2 - 1);
        }

        /// <summary>
        /// The cirrus layers over a colour (<c>CirrusPS</c>).
        /// </summary>
        private Vector3 Cirrus(Vector3 direction, Vector3 color)
        {
            if (_cirrus is null || direction.Y < 0)
            {
                return color;
            }
            Vector3 place = OnDome(direction);
            float alpha = _skybox.CirrusMultiplier;
            for (int layer = 0; layer < 4 && layer < _skybox.CirrusLayers.Length; layer++)
            {
                CirrusLayer cirrus = _skybox.CirrusLayers[layer];
                Vector2 heading = cirrus.Direction.LengthSquared() > 1e-12f ? Vector2.Normalize(cirrus.Direction) : new Vector2(1, 0);
                // mul(position, float2x2(direction, (direction.y, -direction.x))): position as a row
                Vector2 turned = new Vector2(place.X * heading.X + place.Z * heading.Y, place.X * heading.Y - place.Z * heading.X);
                Vector2 at = cirrus.Frequency * turned;
                alpha *= _cirrus.Sample(at.X, at.Y, 0, wrap: true)[layer];
            }
            return Vector3.Lerp(color, _skybox.CirrusColor, Math.Clamp(alpha, 0, 1));
        }

        private Vector3 Sun(Vector3 direction, Vector3 color)
        {
            if (_sun is not Vector3 sun)
            {
                return color;
            }
            float cos = Vector3.Dot(direction, sun);
            if (cos <= 0.98f)
            {
                return color;
            }
            Vector3 hot = Vector3.Clamp(_sunColor, Vector3.Zero, new Vector3(4));
            Vector3 disc = Vector3.Lerp(Vector3.Min(Vector3.One, hot), Vector3.One, 0.6f);
            // a faint glow around the disc
            float glow = MathF.Pow(Math.Max(0, (cos - 0.98f) / 0.02f), 6) * 0.35f;
            color += Vector3.Min(Vector3.One, hot) * glow;
            float edge = Math.Clamp((cos - SunRimCos) / (SunDiscCos - SunRimCos), 0, 1);
            return Vector3.Lerp(color, disc, edge);
        }

        /// <summary>
        /// The sky objects of one face, each over the pixels it may cover, in the order listed.
        /// </summary>
        private void DrawPlanets(int face, int size, Vector3[] colors, CancellationToken cancel)
        {
            if (_albedo is null)
            {
                return;
            }
            List<PlanetOnFace> shown = [];
            foreach (Planet planet in _skybox.Planets)
            {
                if (PlanetOnFace.Of(planet, _eye, face, size, _albedo) is PlanetOnFace on)
                {
                    shown.Add(on);
                }
            }
            if (shown.Count == 0)
            {
                return;
            }
            float glowMultiplier = _glow is null ? 0 : _skybox.DecalGlowMultiplier;
            Parallel.For(0, size, new ParallelOptions { CancellationToken = cancel }, row =>
            {
                foreach (PlanetOnFace planet in shown)
                {
                    if (row < planet.Top || row > planet.Bottom)
                    {
                        continue;
                    }
                    for (int column = planet.Left; column <= planet.Right; column++)
                    {
                        Vector3 direction = CubeFaces.PixelDirection(face, column, row, size);
                        if (planet.TexelOf(direction) is not (float u, float v))
                        {
                            continue;
                        }
                        Vector4 texel = _albedo.Sample(u, v, planet.Level, wrap: false);
                        if (texel.W <= 0)
                        {
                            continue;
                        }
                        Vector3 albedo = new Vector3(texel.X, texel.Y, texel.Z);
                        if (glowMultiplier > 0)
                        {
                            albedo *= 1 + glowMultiplier * _glow!.Sample(u, v, planet.Level, wrap: false).W;
                        }
                        int at = row * size + column;
                        colors[at] = Vector3.Lerp(colors[at], albedo, texel.W);
                    }
                }
            });
        }

        /// <summary>
        /// A planet as one face sees it: the square facing the eye, and the pixels of the face it may cover.
        /// </summary>
        private sealed class PlanetOnFace
        {
            private Vector3 _middle;
            private Vector3 _forward;
            private Vector3 _right;
            private Vector3 _up;
            private Vector3 _eye;
            private Planet _planet = null!;

            public int Left { get; private set; }
            public int Right { get; private set; }
            public int Top { get; private set; }
            public int Bottom { get; private set; }

            /// <summary>
            /// The level of the atlas's mipmaps whose texels are about as large as a pixel of the face.
            /// </summary>
            public int Level { get; private set; }

            public static PlanetOnFace? Of(Planet planet, Vector3 eye, int face, int size, Picture atlas)
            {
                Vector3 toward = planet.Position - eye;
                float distance = toward.Length();
                float extent = MathF.Sqrt(planet.Scale.X * planet.Scale.X + planet.Scale.Y * planet.Scale.Y);
                if (distance <= 1e-3f || extent <= 0 || planet.Uv.Z == 0 || planet.Uv.W == 0)
                {
                    return null;
                }
                Vector3 forward = toward / distance;
                Vector3 up = Vector3.UnitY;
                Vector3 right = Vector3.Cross(up, forward);
                right = right.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(right);
                up = Vector3.Cross(forward, right);
                PlanetOnFace on = new PlanetOnFace { _middle = planet.Position, _forward = forward, _right = right, _up = up, _eye = eye, _planet = planet };

                // the face's pixels the square's corners (and its middle) fall on; all of the face
                // when a corner lies behind it
                float minS = float.MaxValue, maxS = float.MinValue, minT = float.MaxValue, maxT = float.MinValue;
                bool whole = false;
                for (int i = 0; i < 9; i++)
                {
                    float a = i % 3 - 1, b = i / 3 - 1;
                    Vector3 corner = planet.Position + (right * a + up * b) * extent - eye;
                    if (!OnFace(face, corner, out float s, out float t))
                    {
                        whole = true;
                        break;
                    }
                    minS = Math.Min(minS, s);
                    maxS = Math.Max(maxS, s);
                    minT = Math.Min(minT, t);
                    maxT = Math.Max(maxT, t);
                }
                if (!whole && (maxS < -1 || minS > 1 || maxT < -1 || minT > 1))
                {
                    return null;
                }
                if (whole)
                {
                    // a planet behind the face's axis altogether is not on it
                    if (Vector3.Dot(forward, AxisOf(face)) < -0.75f)
                    {
                        return null;
                    }
                    (minS, maxS, minT, maxT) = (-1, 1, -1, 1);
                }
                on.Left = Math.Clamp((int)MathF.Floor((minS + 1) / 2 * size) - 1, 0, size - 1);
                on.Right = Math.Clamp((int)MathF.Ceiling((maxS + 1) / 2 * size) + 1, 0, size - 1);
                on.Top = Math.Clamp((int)MathF.Floor((minT + 1) / 2 * size) - 1, 0, size - 1);
                on.Bottom = Math.Clamp((int)MathF.Ceiling((maxT + 1) / 2 * size) + 1, 0, size - 1);

                // texels across the square against pixels across it
                float pixels = Math.Max(1e-3f, 2 * Math.Max(planet.Scale.X, planet.Scale.Y) / distance * size / 2);
                float texels = Math.Max(MathF.Abs(planet.Uv.Z) * atlas.Width, MathF.Abs(planet.Uv.W) * atlas.Height);
                on.Level = Math.Clamp((int)MathF.Floor(MathF.Log2(Math.Max(1, texels / pixels))), 0, atlas.Levels - 1);
                return on;
            }

            private static Vector3 AxisOf(int face) => face switch
            {
                CubeFaces.PositiveX => Vector3.UnitX,
                CubeFaces.NegativeX => -Vector3.UnitX,
                CubeFaces.PositiveY => Vector3.UnitY,
                CubeFaces.NegativeY => -Vector3.UnitY,
                CubeFaces.PositiveZ => Vector3.UnitZ,
                _ => -Vector3.UnitZ,
            };

            /// <summary>
            /// Where a direction falls on a face's plane, false when it points away from the face.
            /// </summary>
            private static bool OnFace(int face, Vector3 direction, out float s, out float t)
            {
                Vector3 axis = AxisOf(face);
                float along = Vector3.Dot(direction, axis);
                s = t = 0;
                if (along <= 1e-6f)
                {
                    return false;
                }
                Vector3 d = direction / along;
                (s, t) = face switch
                {
                    CubeFaces.PositiveX => (-d.Z, -d.Y),
                    CubeFaces.NegativeX => (d.Z, -d.Y),
                    CubeFaces.PositiveY => (d.X, d.Z),
                    CubeFaces.NegativeY => (d.X, -d.Z),
                    CubeFaces.PositiveZ => (d.X, -d.Y),
                    _ => (-d.X, -d.Y),
                };
                return true;
            }

            /// <summary>
            /// The place in the atlas (0 to 1, its first row at v 0) a direction from the eye sees on
            /// the square; null when it misses the square.
            /// </summary>
            public (float U, float V)? TexelOf(Vector3 direction)
            {
                float facing = Vector3.Dot(direction, _forward);
                if (facing <= 1e-6f)
                {
                    return null;
                }
                float t = Vector3.Dot(_middle - _eye, _forward) / facing;
                Vector3 local = _eye + direction * t - _middle;
                float a = Vector3.Dot(local, _right);
                float b = Vector3.Dot(local, _up);
                float cos = MathF.Cos(_planet.Rotation);
                float sin = MathF.Sin(_planet.Rotation);
                // DecalVS turns the corner by the rotation: undo it
                float cx = (a * cos + b * sin) / _planet.Scale.X;
                float cy = (-a * sin + b * cos) / _planet.Scale.Y;
                if (cx < -1 || cx > 1 || cy < -1 || cy > 1)
                {
                    return null;
                }
                Vector4 uv = _planet.Uv;
                float u = uv.X + 0.5f * uv.Z * (cx + 1);
                float v = 1 - (uv.Y + 0.5f * uv.W * (cy + 1));
                return (u, v);
            }
        }

        /// <summary>
        /// A texture with its mipmaps, sampled between its four nearest texels as colours from 0 to 1.
        /// </summary>
        internal sealed class Picture
        {
            private readonly List<(int Width, int Height, byte[] Rgba)> _levels;

            private Picture(List<(int, int, byte[])> levels) => _levels = levels;

            public int Width => _levels[0].Width;

            public int Height => _levels[0].Height;

            public int Levels => _levels.Count;

            public static Picture? Of(DecodedTexture? texture)
            {
                if (texture is null || texture.Width <= 0 || texture.Height <= 0)
                {
                    return null;
                }
                List<(int, int, byte[])> levels = [(texture.Width, texture.Height, texture.Rgba)];
                (int w, int h, byte[] pixels) = (texture.Width, texture.Height, texture.Rgba);
                while ((w > 1 || h > 1) && levels.Count < 12)
                {
                    (w, h, pixels) = DxtEncoder.Halve(w, h, pixels);
                    levels.Add((w, h, pixels));
                }
                return new Picture(levels);
            }

            /// <summary>
            /// The colour at (u, v), 0 to 1 over the texture, its first row at v 0.
            /// </summary>
            public Vector4 Sample(float u, float v, int level, bool wrap)
            {
                (int width, int height, byte[] rgba) = _levels[Math.Clamp(level, 0, _levels.Count - 1)];
                float x = u * width - 0.5f;
                float y = v * height - 0.5f;
                int x0 = (int)MathF.Floor(x);
                int y0 = (int)MathF.Floor(y);
                float fx = x - x0;
                float fy = y - y0;
                Vector4 Texel(int tx, int ty)
                {
                    if (wrap)
                    {
                        tx = ((tx % width) + width) % width;
                        ty = ((ty % height) + height) % height;
                    }
                    else
                    {
                        tx = Math.Clamp(tx, 0, width - 1);
                        ty = Math.Clamp(ty, 0, height - 1);
                    }
                    int at = (ty * width + tx) * 4;
                    return new Vector4(rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) / 255f;
                }
                Vector4 top = Vector4.Lerp(Texel(x0, y0), Texel(x0 + 1, y0), fx);
                Vector4 bottom = Vector4.Lerp(Texel(x0, y0 + 1), Texel(x0 + 1, y0 + 1), fx);
                return Vector4.Lerp(top, bottom, fy);
            }
        }
    }
}
