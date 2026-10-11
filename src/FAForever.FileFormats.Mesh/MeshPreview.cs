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
    /// The textures a mesh is drawn with, as a blueprint's level of detail names them; any may be
    /// missing.
    /// </summary>
    /// <param name="Albedo">The colour (<c>_albedo</c>); for Seraphim units its alpha marks the team colour.</param>
    /// <param name="Normals">The normal map (<c>_normalsTS</c>): the normal's x in green and y in alpha,
    /// along the binormal and tangent.</param>
    /// <param name="Specular">The units' material (<c>_SpecTeam</c>): what each faction's shader reads
    /// in its channels; the team colour's mask in alpha, glow in blue.</param>
    public sealed record PreviewTextures(RgbaImage? Albedo = null, RgbaImage? Normals = null, RgbaImage? Specular = null);

    /// <summary>
    /// How a mesh is shaded: the pixel shaders of the game's <c>effects/mesh.fx</c> (FAF's) that
    /// the blueprints' shader names use.
    /// </summary>
    public enum PreviewShader
    {
        /// <summary>
        /// Lit by the vertices' normals (<c>VertexNormalPS</c>): far levels of detail, and the shaders
        /// the file does not know.
        /// </summary>
        VertexNormal,

        /// <summary>
        /// Lit by the normal map, with the specular map's highlights (<c>NormalMappedPS</c>,
        /// <c>NormalMappedTerrainPS</c>): props.
        /// </summary>
        NormalMapped,

        /// <summary>
        /// UEF units (<c>PBR_UEF</c>): metal, roughness and team colour from the specular map.
        /// </summary>
        Uef,

        /// <summary>
        /// Aeon units (<c>PBR_Aeon</c>).
        /// </summary>
        Aeon,

        /// <summary>
        /// Cybran units (<c>PBR_Cybran</c>, the shader <c>Insect</c>).
        /// </summary>
        Cybran,

        /// <summary>
        /// Seraphim units (<c>PBR_Seraphim</c>): silver from the albedo's blue, team colour where its
        /// alpha is.
        /// </summary>
        Seraphim,
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

        public PreviewShader Shader { get; init; } = PreviewShader.VertexNormal;

        /// <summary>
        /// The albedo's alpha (0 to 1) a pixel must exceed to be drawn, as the shader's alpha test;
        /// null draws every pixel.
        /// </summary>
        public float? AlphaTest { get; init; }

        /// <summary>
        /// The army's colour, where the textures mark the team colour.
        /// </summary>
        public Vector3 TeamColour { get; init; } = new Vector3(0.18f, 0.38f, 0.85f);

        /// <summary>
        /// Samples per pixel along each side, averaged to smooth the edges.
        /// </summary>
        public int Samples { get; init; } = 2;
    }

    /// <summary>
    /// Draws a mesh into a small picture without a graphics card: a still of it from above and in
    /// front, on a transparent background, the mesh filling the picture, shaded as the game's
    /// shader for it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The projection is orthographic and the mesh is drawn in its rest pose (a mesh's vertices are
    /// where the rest pose has them). Both sides of a triangle are drawn and lit, as a leaf seen
    /// from behind is still a leaf.
    /// </para>
    /// <para>
    /// The light is that of the game's map editor by default, which many maps keep (a light
    /// multiplier of 1.5, ambient 0.2, a white sun, a shadow fill of 0.7, 0.7, 0.75), with the sun
    /// above the camera's left shoulder. The game reflects an environment cube map; a gradient
    /// fitted to its default one (<c>textures/environment/DefaultEnvCube.dds</c>: light blue at the
    /// horizon, darker up high, dark grey below the horizon and black straight down) takes its
    /// place, and an analytic fit takes the place of its lookup texture of the reflected light.
    /// There are no shadows.
    /// </para>
    /// </remarks>
    public static class MeshPreview
    {
        private const float LightMultiplier = 1.5f;
        private static readonly Vector3 SunDiffuse = Vector3.One;
        private static readonly Vector3 SunAmbient = new Vector3(0.2f);
        private static readonly Vector3 ShadowFill = new Vector3(0.7f, 0.7f, 0.75f);
        private static readonly Vector3 PhongCoefficient = new Vector3(0.6f, 0.8f, 0.9f);
        private static readonly Vector3 Zenith = new Vector3(0.40f, 0.46f, 0.61f);
        private static readonly Vector3 Horizon = new Vector3(0.55f, 0.62f, 0.79f);
        private static readonly Vector3 Ground = new Vector3(0.12f, 0.125f, 0.135f);
        private const float Margin = 0.06f;

        /// <summary>
        /// Draws the mesh with its textures (light grey without an albedo).
        /// </summary>
        /// <exception cref="ArgumentException">The size or samples are less than 1, or an index points
        /// past the last vertex.</exception>
        public static RgbaImage Render(Scm mesh, PreviewTextures textures, MeshPreviewOptions? options = null)
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
            Vector3 sun = Vector3.Normalize(toCamera + 1.2f * up - 0.6f * right);

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

            Raster raster = new Raster(size, textures, options, toCamera, sun);
            for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                raster.Triangle(mesh, screen, mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2]);
            }
            return Downsample(raster.Colour, size, options.Samples);
        }

        /// <summary>
        /// The legacy shader names the engine renames before it looks the technique up in
        /// <c>effects/mesh.fx</c> (faf-re, Draiget/faf-re commit 2ba55b2,
        /// <c>src/sdk/moho/mesh/ShaderDictionary.cpp</c>, the constructor).
        /// </summary>
        private static readonly Dictionary<string, string> LegacyShaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TMeshNoLighting"] = "Flat",
            ["TMeshNoNormals"] = "VertexNormal",
            ["TMeshAlpha"] = "NormalMappedAlpha",
            ["TMeshGlow"] = "NormalMappedGlow",
            ["TMeshTerrain"] = "NormalMappedTerrain",
            ["Simple"] = "Unit",
            ["Team"] = "Unit",
            ["TMeshAlphaGlowFade"] = "UnitBuild",
            ["TMeshMetalBuild"] = "AeonBuild",
            ["TMeshShield"] = "Shield",
            ["TMeshZFill"] = "ShieldFill",
            ["TMeshAdd"] = "Effect",
            ["TMeshExplosion"] = "Explosion",
            ["TMeshCloud"] = "Cloud",
            ["TMeshOuterCloud"] = "OuterCloud",
            ["TMeshEMPNuke"] = "NukeEMP",
            ["TMeshQuantumNuke"] = "NukeQuantum",
            ["TMeshTemporalBubble"] = "TemporalBubble",
        };

        /// <summary>
        /// The technique of <c>effects/mesh.fx</c> the engine draws a level of detail with, from the
        /// <c>ShaderName</c> its blueprint gives: no name (or an empty one) is <c>Unit</c>, a legacy
        /// name is renamed (<c>TMeshAlpha</c> is <c>NormalMappedAlpha</c>, <c>Simple</c> and <c>Team</c>
        /// are <c>Unit</c>, ...), any other name stays as it is (<c>ResolveShaderAnnotationName</c> in
        /// faf-re's <c>ShaderDictionary.cpp</c>). The engine's lookup is case-sensitive; this one
        /// ignores case.
        /// </summary>
        public static string ShaderName(string? name) =>
            string.IsNullOrEmpty(name) ? "Unit" : LegacyShaders.TryGetValue(name, out string? renamed) ? renamed : name;

        /// <summary>
        /// How the game shades a blueprint's level of detail by its shader's name (resolved as the
        /// engine does by <see cref="ShaderName"/>), and the alpha its albedo must exceed (the
        /// techniques of <c>effects/mesh.fx</c>: <c>AlphaRef = 0x80</c> for the
        /// <c>...NormalMappedAlpha</c> ones, <c>0x23</c> for <c>VertexNormal</c>, <c>0x03</c> for
        /// clutter).
        /// </summary>
        public static (PreviewShader Shader, float? AlphaTest) ShaderOf(string? name) => ShaderName(name).ToLowerInvariant() switch
        {
            "unit" or "uefnavy" or "wreckage" => (PreviewShader.Uef, null),
            "aeon" or "aeonnavy" or "aeonczar" => (PreviewShader.Aeon, null),
            "insect" or "cybrannavy" => (PreviewShader.Cybran, null),
            "seraphim" or "seraphimnavy" => (PreviewShader.Seraphim, null),
            "vertexnormal" => (PreviewShader.VertexNormal, 0x23 / 255f),
            "clutter" or "undulatingclutter" or "underwaterclutter" => (PreviewShader.VertexNormal, 0x03 / 255f),
            string other when other.EndsWith("normalmappedalpha", StringComparison.Ordinal) => (PreviewShader.NormalMapped, 0x80 / 255f),
            string other when other.Contains("normalmapped", StringComparison.Ordinal) => (PreviewShader.NormalMapped, null),
            _ => (PreviewShader.VertexNormal, null),
        };

        /// <summary>
        /// The picture being drawn, at the samples' resolution, with its depth.
        /// </summary>
        private sealed class Raster(int size, PreviewTextures textures, MeshPreviewOptions options, Vector3 toCamera, Vector3 sun)
        {
            public Vector4[] Colour { get; } = new Vector4[size * size];

            private readonly float[] _depth = Enumerable.Repeat(float.MinValue, size * size).ToArray();

            public void Triangle(Scm mesh, Vector3[] screen, int a, int b, int c)
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
                        if (z <= _depth[pixel])
                        {
                            continue;
                        }
                        Vector2 uv0 = w0 * v0.Uv0 + w1 * v1.Uv0 + w2 * v2.Uv0;
                        Vector4 albedo = textures.Albedo is null ? new Vector4(0.75f, 0.75f, 0.75f, 1) : Sample(textures.Albedo, uv0);
                        if (options.AlphaTest is float alphaTest && albedo.W <= alphaTest)
                        {
                            continue;
                        }
                        Frame frame = new Frame(
                            w0 * Direction(v0.Normal) + w1 * Direction(v1.Normal) + w2 * Direction(v2.Normal),
                            w0 * Direction(v0.Tangent) + w1 * Direction(v1.Tangent) + w2 * Direction(v2.Tangent),
                            w0 * Direction(v0.Binormal) + w1 * Direction(v1.Binormal) + w2 * Direction(v2.Binormal),
                            toCamera);
                        Vector2 uv1 = w0 * v0.Uv1 + w1 * v1.Uv1 + w2 * v2.Uv1;
                        _depth[pixel] = z;
                        Colour[pixel] = new Vector4(Vector3.Clamp(Shade(albedo, uv0, uv1, frame), Vector3.Zero, Vector3.One), 1);
                    }
                }
            }

            private Vector3 Shade(Vector4 albedo, Vector2 uv0, Vector2 uv1, Frame frame)
            {
                if (options.Shader == PreviewShader.VertexNormal)
                {
                    return new Vector3(albedo.X, albedo.Y, albedo.Z) * Light(Vector3.Dot(sun, frame.Normal));
                }
                // the game samples the normal map at the second texture coordinates (texcoord0.zw)
                Vector3 normal = textures.Normals is null ? frame.Normal : frame.Apply(Sample(textures.Normals, uv1));
                Vector4 specular = textures.Specular is null ? Vector4.Zero : Sample(textures.Specular, uv0);
                return options.Shader switch
                {
                    PreviewShader.Uef => Uef(albedo, specular, normal),
                    PreviewShader.Aeon => Aeon(albedo, specular, normal),
                    PreviewShader.Cybran => Cybran(albedo, specular, normal),
                    PreviewShader.Seraphim => Seraphim(albedo, specular, normal),
                    _ => NormalMapped(albedo, specular, normal),
                };
            }

            /// <summary>
            /// <c>ComputeLight</c> without shadows.
            /// </summary>
            private static Vector3 Light(float dotLightNormal)
            {
                Vector3 light = SunDiffuse * Saturate(dotLightNormal) + SunAmbient;
                return LightMultiplier * light + (Vector3.One - light) * ShadowFill;
            }

            /// <summary>
            /// <c>NormalMappedPS</c>: lit albedo, the environment where the specular map's red is, a
            /// highlight where its green is.
            /// </summary>
            private Vector3 NormalMapped(Vector4 albedo, Vector4 specular, Vector3 normal)
            {
                Vector3 colour = new Vector3(albedo.X, albedo.Y, albedo.Z);
                float phong = Saturate(Vector3.Dot(Vector3.Reflect(-sun, normal), toCamera));
                Vector3 additive = PhongCoefficient * phong * phong * specular.Y;
                Vector3 multiplicative = 2 * Environment(Vector3.Reflect(-toCamera, normal)) * specular.X;
                return colour * (Light(Vector3.Dot(sun, normal)) + multiplicative) + additive;
            }

            private Vector3 Uef(Vector4 albedoTexel, Vector4 specular, Vector3 normal)
            {
                Vector3 albedo = new Vector3(albedoTexel.X, albedoTexel.Y, albedoTexel.Z);
                float ao = 0.5f + Logistic(albedo.Length() / MathF.Sqrt(3), 0.1f, 40, 0.5f, 2);
                float teamColour = MathF.Min(MathF.Pow(specular.W * 1.1f, 0.6f), 1);
                float metallic = MathF.Max(1 - teamColour * 2.2f, 0);
                albedo = Vector3.Lerp(albedo, albedo * 1.9f, metallic);
                albedo = Vector3.Lerp(albedo, options.TeamColour * 0.6f, teamColour);
                float cockpit = Saturate((specular.X - 0.65f) * 3);
                albedo += new Vector3(cockpit);
                float roughness = specular.Y * 0.6f + 0.35f + Saturate(MathF.Pow(specular.W * 3.7f, 0.6f)) * 0.3f;
                roughness += cockpit - specular.Z * 3;
                roughness = Saturate(1 - roughness);
                Vector3 colour = Pbr(albedo, metallic, roughness, normal, 0.04f, ao);
                return colour + specular.Z * 0.8f * albedo;
            }

            private Vector3 Aeon(Vector4 albedoTexel, Vector4 specular, Vector3 normal)
            {
                Vector3 albedo = new Vector3(albedoTexel.X, albedoTexel.Y, albedoTexel.Z);
                float fullTeamColour = Saturate(specular.W * 2.5f);
                float metallic = specular.X < 0.4f
                    ? Saturate(MapRange(specular.X, 0.2267f, 0.3643f, 0, 1))
                    : MathF.Min(MathF.Max(MapRange(specular.X, 0.4129f, 0.5384f, 1, 0), 0.3f), 1);
                metallic = Saturate(metallic - fullTeamColour);
                float gloss = specular.Y < 0.45f
                    ? 0.022f * (MathF.Exp(6 * specular.Y) - 1) + 0.023f + specular.W * 0.2f
                    : 0.762f * specular.Y - 0.014f;
                float border = Saturate(MapRange(specular.W, 0.54f, 0.6f, 0, 1));
                float darkAreas = Saturate(Saturate(MapRange(specular.X, 0.3f, 0.44f, 1, 0)) - albedo.Length());
                float roughness = Saturate(float.Lerp(gloss, 0.03f, border) + darkAreas);
                albedo *= (specular.X + 0.3f) * 1.25f;
                float power = 1 - metallic * 0.95f;
                albedo = new Vector3(MathF.Pow(albedo.X, power), MathF.Pow(albedo.Y, power), MathF.Pow(albedo.Z, power));
                albedo = Vector3.Lerp(albedo, options.TeamColour * specular.W * 0.4f, fullTeamColour);
                float facing = float.Lerp(float.Lerp(0.08f, 0, darkAreas), 0.04f, fullTeamColour);
                Vector3 colour = Pbr(albedo, metallic, roughness, normal, facing, 1);
                return colour + new Vector3(specular.Z) + specular.W * options.TeamColour * 0.5f;
            }

            private Vector3 Cybran(Vector4 albedoTexel, Vector4 specular, Vector3 normal)
            {
                Vector3 albedo = new Vector3(albedoTexel.X, albedoTexel.Y, albedoTexel.Z);
                float metallic = Saturate(specular.X + Saturate(specular.Y - 0.1f) * 0.87f - specular.W * 2.2f);
                float roughness = float.Lerp(0.8f * (1 - specular.Y), float.Lerp(0.5f, 0.25f, specular.Y), metallic);
                albedo += new Vector3(specular.X * 0.1f);
                albedo = Vector3.Min(Vector3.Lerp(albedo, albedo * 2.3f, MathF.Pow(metallic, 2.5f)), new Vector3(0.9f));
                albedo = Vector3.Lerp(albedo, options.TeamColour, specular.W);
                Vector3 colour = Pbr(albedo, metallic, roughness, normal, 0.04f, 1);
                return colour + MathF.Sqrt(MathF.Max(specular.Z - 0.04f, 0)) * albedo;
            }

            private Vector3 Seraphim(Vector4 albedoTexel, Vector4 specular, Vector3 normal)
            {
                float dotView = Saturate(Vector3.Dot(toCamera, normal));
                dotView = 2 * MathF.Pow(dotView, 6) - 2 * dotView + 1.5f;
                Vector3 teamColour = dotView * options.TeamColour;
                Vector3 whiteness = Vector3.Max(new Vector3(albedoTexel.X, albedoTexel.Y, albedoTexel.Z) - new Vector3(0.4f), Vector3.Zero);
                float mask = albedoTexel.W;
                Vector3 albedo = (new Vector3(albedoTexel.Z, albedoTexel.Z, albedoTexel.Y) + new Vector3(0.4f, 0.43f, 0.47f)) * 0.8f;
                albedo = Vector3.Lerp(albedo, teamColour, mask);
                float roughness = Saturate((1 - MathF.Sqrt(specular.Y) + 0.3f) * 0.7f);
                Vector3 colour = Pbr(albedo, 1, roughness, normal, 0.04f, 1);
                colour = Vector3.Lerp(colour, teamColour * 0.5f, mask - 0.2f);
                Vector3 emission = new Vector3(Saturate(specular.Z - 0.1f)) + teamColour * mask + whiteness * 1.5f;
                float glow = Saturate(Saturate(specular.Z * 2) - mask);
                colour = Vector3.Lerp(colour, new Vector3(specular.Z * 2), glow);
                return colour + emission * albedo;
            }

            /// <summary>
            /// The game's <c>PBR</c>: Cook-Torrance light of the sun, and the environment's light
            /// (here the sky) reflected and diffused.
            /// </summary>
            private Vector3 Pbr(Vector3 albedo, float metallic, float roughness, Vector3 n, float facingSpecular, float ao)
            {
                Vector3 v = toCamera, l = sun;
                Vector3 reflection = Vector3.Reflect(-v, n);
                Vector3 environmentReflection = Environment(Vector3.Normalize(Vector3.Lerp(reflection, n, roughness * roughness)));
                Vector3 environmentIrradiance = Environment(n);
                (float scaleBrdf, float biasBrdf) = EnvironmentBrdf(MathF.Abs(Vector3.Dot(n, v)), roughness);
                biasBrdf *= 0.5f;

                Vector3 f0 = Vector3.Lerp(new Vector3(facingSpecular), albedo, metallic);
                Vector3 h = Vector3.Normalize(v + l);
                float nDotL = MathF.Max(Vector3.Dot(n, l), 0);
                float nDotV = MathF.Abs(Vector3.Dot(n, v)) + 0.001f;
                Vector3 sunLight = SunDiffuse * LightMultiplier;

                Vector3 f = f0 + (Vector3.One - f0) * MathF.Pow(1 - MathF.Max(Vector3.Dot(h, v), 0), 5);
                float a2 = roughness * roughness;
                float nDotH = MathF.Max(Vector3.Dot(n, h), 0);
                float denominatorNdf = nDotH * nDotH * (a2 - 1) + 1;
                float ndf = a2 / (MathF.PI * denominatorNdf * denominatorNdf);
                float k = (roughness + 1) * (roughness + 1) / 8;
                float g = nDotV / (nDotV * (1 - k) + k) * (nDotL / (nDotL * (1 - k) + k));
                Vector3 reflected = MathF.PI * ndf * g * f / (4 * nDotV * nDotL + 0.0001f);
                Vector3 kD = (Vector3.One - f) * (1 - metallic);
                Vector3 colour = (kD * albedo + reflected) * sunLight * nDotL;

                Vector3 kS = f0 + (Vector3.Max(new Vector3(1 - roughness), f0) - f0) * MathF.Pow(Saturate(1 - nDotV), 5);
                kD = (Vector3.One - kS) * (1 - metallic);
                Vector3 shadowColour = (Vector3.One - (SunDiffuse * nDotL + SunAmbient)) * ShadowFill;
                Vector3 ambient = SunAmbient * LightMultiplier + shadowColour;
                float correction = float.Lerp(Saturate((ambient.X + ambient.Y + ambient.Z) / 3), 1, nDotL);
                environmentIrradiance = environmentIrradiance * correction + ambient * 0.5f;
                environmentReflection = environmentReflection * correction + ambient * 0.15f;
                Vector3 specular = environmentReflection * (kS * scaleBrdf + new Vector3(biasBrdf));
                return colour + (kD * environmentIrradiance * albedo + specular) * ao;
            }

            /// <summary>
            /// The default environment cube by the height of a direction: its means above and below
            /// the horizon, straight up and straight down.
            /// </summary>
            private static Vector3 Environment(Vector3 direction)
            {
                float y = Math.Clamp(direction.Y, -1, 1);
                Vector3 above = Vector3.Lerp(Horizon, Zenith, y);
                Vector3 below = Vector3.Lerp(Ground, Vector3.Zero, -y);
                // the horizon is soft: blurred, as the shaders read the cube for rough surfaces
                float t = Saturate(y / 0.2f + 0.5f);
                return Vector3.Lerp(below, above, t * t * (3 - 2 * t));
            }

            /// <summary>
            /// The scale and bias of the reflected light by the angle and roughness: Karis' analytic
            /// fit of the lookup texture the game reads.
            /// </summary>
            private static (float Scale, float Bias) EnvironmentBrdf(float nDotV, float roughness)
            {
                Vector4 r = roughness * new Vector4(-1, -0.0275f, -0.572f, 0.022f) + new Vector4(1, 0.0425f, 1.04f, -0.04f);
                float a004 = MathF.Min(r.X * r.X, MathF.Pow(2, -9.28f * nDotV)) * r.X + r.Y;
                return (-1.04f * a004 + r.Z, 1.04f * a004 + r.W);
            }
        }

        /// <summary>
        /// The normal, tangent and binormal at a pixel, turned to face the camera: a triangle seen
        /// from behind is lit as its front.
        /// </summary>
        private readonly struct Frame
        {
            public Frame(Vector3 normal, Vector3 tangent, Vector3 binormal, Vector3 toCamera)
            {
                Vector3 unit = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : toCamera;
                float side = Vector3.Dot(unit, toCamera) < 0 ? -1 : 1;
                Normal = unit * side;
                Tangent = tangent * side;
                Binormal = binormal * side;
            }

            public Vector3 Normal { get; }

            public Vector3 Tangent { get; }

            public Vector3 Binormal { get; }

            /// <summary>
            /// <c>ComputeNormal</c>: x from the normal map's green along the binormal, y from its
            /// alpha along the tangent, z what makes it of length 1 along the normal.
            /// </summary>
            public Vector3 Apply(Vector4 texel)
            {
                float x = 2 * texel.Y - 1, y = 2 * texel.W - 1;
                float z = MathF.Sqrt(MathF.Max(0, 1 - x * x - y * y));
                Vector3 normal = x * Binormal + y * Tangent + z * Normal;
                return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Normal;
            }
        }

        private static float Saturate(float value) => Math.Clamp(value, 0, 1);

        private static float MapRange(float value, float min1, float max1, float min2, float max2) =>
            (value - min1) / (max1 - min1) * (max2 - min2) + min2;

        private static float Logistic(float x, float x0, float k, float l, float m) => l / (1 + MathF.Pow(2.71828f, -k * (m * x - x0)));

        /// <summary>
        /// Twice the signed area of the triangle (a, b, point).
        /// </summary>
        private static float Edge(Vector3 a, Vector3 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

        /// <summary>
        /// A direction at length 1, or zero where the mesh has none that can be used.
        /// </summary>
        private static Vector3 Direction(Vector3 direction)
        {
            float length = direction.LengthSquared();
            return float.IsFinite(length) && length > 1e-12f ? Vector3.Normalize(direction) : Vector3.Zero;
        }

        /// <summary>
        /// A texture at a texture coordinate, between its four nearest pixels, repeating beyond 0 and 1.
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
