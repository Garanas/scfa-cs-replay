using System.Numerics;

namespace FAForever.FileFormats.Mesh
{
    /// <summary>
    /// A picture as 8-bit red, green, blue and alpha per pixel, rows from the top, the alpha not
    /// multiplied in.
    /// </summary>
    public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
    {
        /// <summary>
        /// An image of <paramref name="width"/> by <paramref name="height"/>, every pixel transparent.
        /// </summary>
        public static RgbaImage Transparent(int width, int height) => new RgbaImage(width, height, new byte[width * height * 4]);
    }

    /// <summary>
    /// What the alpha of a mesh's albedo means.
    /// </summary>
    public enum PreviewAlpha
    {
        /// <summary>
        /// Nothing about its shape: the mesh is solid (the units' shaders put other data there).
        /// </summary>
        Opaque,

        /// <summary>
        /// Where the mesh is: a pixel is drawn where the alpha is at least a half (the leaves of
        /// trees, the shaders whose names end in <c>Alpha</c>).
        /// </summary>
        Cutout,
    }

    /// <summary>
    /// How <see cref="MeshPreview.Render"/> draws a mesh.
    /// </summary>
    public sealed record MeshPreviewOptions
    {
        /// <summary>
        /// The width and height of the picture, in pixels.
        /// </summary>
        public int Size { get; init; } = 128;

        /// <summary>
        /// How far the camera is turned around the mesh from its front (+z), in degrees,
        /// counter-clockwise seen from above.
        /// </summary>
        public float Yaw { get; init; } = -35;

        /// <summary>
        /// How far the camera looks down on the mesh, in degrees.
        /// </summary>
        public float Pitch { get; init; } = 30;

        public PreviewAlpha Alpha { get; init; } = PreviewAlpha.Opaque;

        /// <summary>
        /// Samples per pixel along each side, averaged to smooth the edges.
        /// </summary>
        public int Samples { get; init; } = 2;

        /// <summary>
        /// The mean brightness (0 to 1) a darker mesh is brightened to, at most four times; null
        /// keeps the lit albedo as it is. The units' albedos are dark: the game brightens them
        /// with specular light, reflections and the team colour.
        /// </summary>
        public float? Exposure { get; init; } = 0.4f;
    }

    /// <summary>
    /// Draws a mesh into a small picture without a graphics card: a still of it from above and in
    /// front, its albedo lit by one light, on a transparent background, the mesh filling the picture.
    /// </summary>
    /// <remarks>
    /// The projection is orthographic and the mesh is drawn in its rest pose (a mesh's vertices are
    /// where the rest pose has them). Both sides of a triangle are drawn and lit, as a leaf seen
    /// from behind is still a leaf. The game's team colour and the other textures are left out.
    /// </remarks>
    public static class MeshPreview
    {
        private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.5f, 0.8f, 0.6f));

        private const float Ambient = 0.4f;
        private const float Diffuse = 0.7f;
        private const float Margin = 0.06f;

        /// <summary>
        /// Draws the mesh with its albedo (light grey without one).
        /// </summary>
        /// <exception cref="ArgumentException">The size or samples are less than 1, or an index points
        /// past the last vertex.</exception>
        public static RgbaImage Render(Scm mesh, RgbaImage? albedo, MeshPreviewOptions? options = null)
        {
            options ??= new MeshPreviewOptions();
            if (options.Size < 1 || options.Samples < 1)
            {
                throw new ArgumentException("A picture needs a size and samples of at least 1", nameof(options));
            }
            if (mesh.Indices.Any(index => index >= mesh.Vertices.Count))
            {
                throw new ArgumentException("An index is past the last vertex", nameof(mesh));
            }

            // the camera's axes: towards the camera, to its right and up on the screen
            float yaw = options.Yaw * MathF.PI / 180, pitch = options.Pitch * MathF.PI / 180;
            Vector3 toCamera = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
            Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toCamera));
            Vector3 up = Vector3.Cross(toCamera, right);

            int size = options.Size * options.Samples;
            int count = mesh.Vertices.Count;
            Vector3[] screen = new Vector3[count];
            float left = float.MaxValue, top = float.MinValue, rightmost = float.MinValue, bottom = float.MaxValue;
            foreach (ushort index in mesh.Indices)
            {
                Vector3 position = mesh.Vertices[index].Position;
                float x = Vector3.Dot(position, right), y = Vector3.Dot(position, up);
                left = MathF.Min(left, x);
                rightmost = MathF.Max(rightmost, x);
                bottom = MathF.Min(bottom, y);
                top = MathF.Max(top, y);
            }
            float extent = MathF.Max(rightmost - left, top - bottom);
            if (!float.IsFinite(extent))
            {
                return RgbaImage.Transparent(options.Size, options.Size);
            }
            float scale = extent > 0 ? size * (1 - 2 * Margin) / extent : 1;
            float middleX = (left + rightmost) / 2, middleY = (bottom + top) / 2;
            for (int i = 0; i < count; i++)
            {
                Vector3 position = mesh.Vertices[i].Position;
                screen[i] = new Vector3(
                    size / 2f + (Vector3.Dot(position, right) - middleX) * scale,
                    size / 2f - (Vector3.Dot(position, up) - middleY) * scale,
                    Vector3.Dot(position, toCamera));
            }

            float[] depth = new float[size * size];
            Array.Fill(depth, float.MinValue);
            Vector4[] colour = new Vector4[size * size];
            for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                Triangle(mesh, screen, mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2], albedo, options.Alpha, toCamera, size, depth, colour);
            }
            if (options.Exposure is float exposure)
            {
                Expose(colour, exposure);
            }
            return Downsample(colour, size, options.Samples);
        }

        private static void Expose(Vector4[] colour, float target)
        {
            Vector3 luma = new Vector3(0.299f, 0.587f, 0.114f);
            float sum = 0;
            int drawn = 0;
            foreach (Vector4 sample in colour)
            {
                if (sample.W > 0)
                {
                    sum += Vector3.Dot(new Vector3(sample.X, sample.Y, sample.Z), luma);
                    drawn++;
                }
            }
            float mean = drawn == 0 ? 0 : sum / drawn;
            if (mean <= 0 || mean >= target)
            {
                return;
            }
            float gain = MathF.Min(target / mean, 4);
            for (int i = 0; i < colour.Length; i++)
            {
                Vector4 sample = colour[i];
                colour[i] = new Vector4(Vector3.Min(new Vector3(sample.X, sample.Y, sample.Z) * gain, Vector3.One), sample.W);
            }
        }

        /// <summary>
        /// Whether a shader of a blueprint's level of detail cuts its mesh out by the albedo's alpha:
        /// the game's shaders whose names end in <c>Alpha</c> (<c>NormalMappedAlpha</c>, <c>TMeshAlpha</c>,
        /// <c>UndulatingNormalMappedAlpha</c>: trees and bushes), named in the blueprint's <c>Display.Mesh.LODs</c>.
        /// </summary>
        public static PreviewAlpha AlphaOf(string? shader) =>
            shader is not null && shader.EndsWith("Alpha", StringComparison.OrdinalIgnoreCase) ? PreviewAlpha.Cutout : PreviewAlpha.Opaque;

        private static void Triangle(Scm mesh, Vector3[] screen, int a, int b, int c, RgbaImage? albedo, PreviewAlpha alpha, Vector3 toCamera, int size, float[] depth, Vector4[] colour)
        {
            Vector3 p0 = screen[a], p1 = screen[b], p2 = screen[c];
            float area = Edge(p0, p1, p2.X, p2.Y);
            if (MathF.Abs(area) < 1e-9f)
            {
                return;
            }
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
            int maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
            int maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
            ScmVertex v0 = mesh.Vertices[a], v1 = mesh.Vertices[b], v2 = mesh.Vertices[c];
            Vector3 n0 = Direction(v0.Normal), n1 = Direction(v1.Normal), n2 = Direction(v2.Normal);
            for (int y = minY; y <= maxY; y++)
            {
                float sampleY = y + 0.5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float sampleX = x + 0.5f;
                    // the weights of the corners: all of one sign inside, whichever way the triangle winds
                    float w0 = Edge(p1, p2, sampleX, sampleY) / area;
                    float w1 = Edge(p2, p0, sampleX, sampleY) / area;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                    {
                        continue;
                    }
                    float z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z;
                    int pixel = y * size + x;
                    if (z <= depth[pixel])
                    {
                        continue;
                    }
                    Vector4 texel = albedo is null
                        ? new Vector4(0.75f, 0.75f, 0.75f, 1)
                        : Sample(albedo, w0 * v0.Uv0 + w1 * v1.Uv0 + w2 * v2.Uv0);
                    if (alpha == PreviewAlpha.Cutout && texel.W < 0.5f)
                    {
                        continue;
                    }
                    Vector3 normal = w0 * n0 + w1 * n1 + w2 * n2;
                    normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : toCamera;
                    if (Vector3.Dot(normal, toCamera) < 0)
                    {
                        normal = -normal;
                    }
                    float light = Ambient + Diffuse * MathF.Max(0, Vector3.Dot(normal, Light));
                    depth[pixel] = z;
                    colour[pixel] = new Vector4(Vector3.Min(new Vector3(texel.X, texel.Y, texel.Z) * light, Vector3.One), 1);
                }
            }
        }

        /// <summary>
        /// Twice the signed area of the triangle (a, b, point).
        /// </summary>
        private static float Edge(Vector3 a, Vector3 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

        /// <summary>
        /// A normal at length 1, or zero where the mesh has none that can be used.
        /// </summary>
        private static Vector3 Direction(Vector3 normal)
        {
            float length = normal.LengthSquared();
            return float.IsFinite(length) && length > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero;
        }

        /// <summary>
        /// The albedo at a texture coordinate, between its four nearest pixels, repeating beyond 0 and 1.
        /// </summary>
        private static Vector4 Sample(RgbaImage image, Vector2 uv)
        {
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
            {
                uv = Vector2.Zero;
            }
            float x = uv.X * image.Width - 0.5f, y = uv.Y * image.Height - 0.5f;
            float floorX = MathF.Floor(x), floorY = MathF.Floor(y);
            float fx = x - floorX, fy = y - floorY;
            int x0 = Wrap((long)floorX, image.Width), x1 = Wrap((long)floorX + 1, image.Width);
            int y0 = Wrap((long)floorY, image.Height), y1 = Wrap((long)floorY + 1, image.Height);
            Vector4 top = Vector4.Lerp(Texel(image, x0, y0), Texel(image, x1, y0), fx);
            Vector4 bottom = Vector4.Lerp(Texel(image, x0, y1), Texel(image, x1, y1), fx);
            return Vector4.Lerp(top, bottom, fy);
        }

        private static int Wrap(long value, int length) => (int)(((value % length) + length) % length);

        private static Vector4 Texel(RgbaImage image, int x, int y)
        {
            int i = (y * image.Width + x) * 4;
            return new Vector4(image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]) / 255f;
        }

        /// <summary>
        /// Averages each square of samples into one pixel; the colours by their coverage, so that an
        /// edge blends into the transparency around it.
        /// </summary>
        private static RgbaImage Downsample(Vector4[] colour, int size, int samples)
        {
            int width = size / samples;
            byte[] pixels = new byte[width * width * 4];
            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector4 sum = Vector4.Zero;
                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            Vector4 sample = colour[(y * samples + sy) * size + x * samples + sx];
                            sum += new Vector4(new Vector3(sample.X, sample.Y, sample.Z) * sample.W, sample.W);
                        }
                    }
                    int i = (y * width + x) * 4;
                    if (sum.W > 0)
                    {
                        pixels[i] = Byte(sum.X / sum.W);
                        pixels[i + 1] = Byte(sum.Y / sum.W);
                        pixels[i + 2] = Byte(sum.Z / sum.W);
                        pixels[i + 3] = Byte(sum.W / (samples * samples));
                    }
                }
            }
            return new RgbaImage(width, width, pixels);
        }

        private static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
    }
}
