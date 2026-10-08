namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// The shaders of the GPU view, in the GLSL that OpenGL ES 3.0 and OpenGL 3.3 share. The terrain
/// follows the game's <c>TTerrainXP</c> as the FAF map editor ports it
/// (<c>Assets/GFX/Shaders/FaTerrainShader.shader</c>, <c>Deferred/Internal-DeferredShading.shader</c>):
/// a lower albedo, the strata blended over it by their masks, the upper albedo by its own alpha, and
/// the game's lighting.
/// </summary>
internal static class TerrainShaders
{
    /// <summary>
    /// The first lines of every shader, for OpenGL ES (ANGLE on Windows) or desktop OpenGL.
    /// </summary>
    public static string Header(bool es) => es
        ? "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp usampler2D;\nprecision highp sampler2D;\n"
        : "#version 330 core\n";

    /// <summary>
    /// The heightmap as 16-bit samples, one per ogrid corner, read with interpolation in between;
    /// normals from the slope around a point.
    /// </summary>
    private const string Heights = """
        uniform usampler2D uHeights;
        uniform float uHeightScale;

        float sampleHeight(ivec2 i)
        {
            ivec2 size = textureSize(uHeights, 0);
            return float(texelFetch(uHeights, clamp(i, ivec2(0), size - 1), 0).r) * uHeightScale;
        }

        float heightAt(vec2 p)
        {
            vec2 f = fract(p);
            ivec2 i = ivec2(floor(p));
            return mix(mix(sampleHeight(i), sampleHeight(i + ivec2(1, 0)), f.x),
                       mix(sampleHeight(i + ivec2(0, 1)), sampleHeight(i + ivec2(1, 1)), f.x), f.y);
        }

        vec3 normalAt(vec2 p)
        {
            float l = heightAt(p - vec2(1.0, 0.0));
            float r = heightAt(p + vec2(1.0, 0.0));
            float u = heightAt(p - vec2(0.0, 1.0));
            float d = heightAt(p + vec2(0.0, 1.0));
            return normalize(vec3(l - r, 2.0, u - d));
        }

        """;

    /// <summary>
    /// <c>CalculateXPLighting</c> without the specular part: the sun on the side that faces it,
    /// the ambience everywhere, and the shadow fill colour where little light falls.
    /// </summary>
    private const string Lighting = """
        uniform vec3 uSunDirection;
        uniform vec3 uSunColor;
        uniform vec3 uSunAmbience;
        uniform vec3 uShadowFill;
        uniform float uLightMultiplier;

        vec3 lightOf(vec3 normal)
        {
            vec3 light = uSunColor * max(dot(uSunDirection, normal), 0.0) + uSunAmbience;
            return uLightMultiplier * light + uShadowFill * (1.0 - light);
        }

        """;

    public static string TerrainVertex(bool es) => Header(es) + Heights + """
        layout(location = 0) in vec2 aGrid;
        uniform mat4 uViewProjection;
        uniform vec2 uMapSize;
        out vec3 vWorld;

        void main()
        {
            vec2 p = aGrid * uMapSize;
            vWorld = vec3(p.x, heightAt(p), p.y);
            gl_Position = uViewProjection * vec4(vWorld, 1.0);
        }
        """;

    public static string TerrainFragment(bool es) => Header(es) + Heights + Lighting + """
        in vec3 vWorld;
        out vec4 outColor;

        uniform vec2 uMapSize;
        uniform sampler2D uMask0;
        uniform sampler2D uMask1;
        uniform sampler2D uLower;
        uniform sampler2D uStratum1;
        uniform sampler2D uStratum2;
        uniform sampler2D uStratum3;
        uniform sampler2D uStratum4;
        uniform sampler2D uStratum5;
        uniform sampler2D uStratum6;
        uniform sampler2D uStratum7;
        uniform sampler2D uStratum8;
        uniform sampler2D uUpper;
        // ogrids per repeat of each texture: lower, strata 1 to 8, upper
        uniform vec4 uScalesA;
        uniform vec4 uScalesB;
        uniform vec2 uScalesC;
        // 0 the picture, 1 the albedo alone, 2 the light alone, 3 the normal, 4 the height
        uniform int uDebug;

        void main()
        {
            vec2 p = vWorld.xz;
            vec2 maskUv = p / uMapSize;
            vec4 mask0 = clamp(texture(uMask0, maskUv) * 2.0 - 1.0, 0.0, 1.0);
            vec4 mask1 = clamp(texture(uMask1, maskUv) * 2.0 - 1.0, 0.0, 1.0);

            vec4 albedo = texture(uLower, p / uScalesA.x);
            albedo = mix(albedo, texture(uStratum1, p / uScalesA.y), mask0.x);
            albedo = mix(albedo, texture(uStratum2, p / uScalesA.z), mask0.y);
            albedo = mix(albedo, texture(uStratum3, p / uScalesA.w), mask0.z);
            albedo = mix(albedo, texture(uStratum4, p / uScalesB.x), mask0.w);
            albedo = mix(albedo, texture(uStratum5, p / uScalesB.y), mask1.x);
            albedo = mix(albedo, texture(uStratum6, p / uScalesB.z), mask1.y);
            albedo = mix(albedo, texture(uStratum7, p / uScalesB.w), mask1.z);
            albedo = mix(albedo, texture(uStratum8, p / uScalesC.x), mask1.w);
            vec4 upper = texture(uUpper, p / uScalesC.y);
            albedo.rgb = mix(albedo.rgb, upper.rgb, upper.a);

            vec3 color = lightOf(normalAt(p)) * albedo.rgb;
            if (uDebug == 1) color = albedo.rgb;
            if (uDebug == 2) color = lightOf(normalAt(p)) * 0.5;
            if (uDebug == 3) color = normalAt(p) * 0.5 + 0.5;
            if (uDebug == 4) color = vec3(vWorld.y / 256.0);
            if (uDebug == 5) color = texture(uLower, p / uScalesA.x).rgb;
            if (uDebug == 6) color = mask0.rgb;
            if (uDebug == 7) color = texture(uMask0, maskUv).rgb;
            outColor = vec4(color, 1.0);
        }
        """;

    /// <summary>
    /// The water: the map's rectangle at the water's elevation, drawn after the decals (the sea
    /// floor's decals lie under it). Its opacity and darkness grow with the depth, down to the
    /// abyss elevation, in the water's surface colour.
    /// </summary>
    public static string WaterVertex(bool es) => Header(es) + """
        layout(location = 0) in vec2 aGrid;
        uniform mat4 uViewProjection;
        uniform vec2 uMapSize;
        uniform float uElevation;
        out vec2 vPlace;

        void main()
        {
            vPlace = aGrid * uMapSize;
            gl_Position = uViewProjection * vec4(vPlace.x, uElevation, vPlace.y, 1.0);
        }
        """;

    public static string WaterFragment(bool es) => Header(es) + Heights + """
        in vec2 vPlace;
        out vec4 outColor;
        // elevation, abyss elevation
        uniform vec2 uWater;
        uniform vec3 uWaterColor;

        void main()
        {
            float depth = uWater.x - heightAt(vPlace);
            if (depth <= 0.0)
            {
                discard;
            }
            float t = clamp(depth / max(uWater.x - uWater.y, 1.0), 0.0, 1.0);
            vec3 surface = clamp(uWaterColor, 0.0, 1.0);
            vec3 color = mix(surface * 0.55, surface * 0.12, t);
            float alpha = clamp(0.25 + 0.75 * sqrt(t), 0.0, 0.92);
            outColor = vec4(color * alpha, alpha);
        }
        """;

    /// <summary>
    /// A decal: a grid over its rectangle that follows the terrain. Each instance is one decal,
    /// placed by its corner (the decal's position) and its two edges.
    /// </summary>
    public static string DecalVertex(bool es) => Header(es) + Heights + """
        layout(location = 0) in vec2 aUv;
        layout(location = 1) in vec2 iCorner;
        layout(location = 2) in vec2 iEdgeU;
        layout(location = 3) in vec2 iEdgeV;
        uniform mat4 uViewProjection;
        out vec2 vUv;
        out vec3 vWorld;
        out vec2 vTangent;

        void main()
        {
            vec2 p = iCorner + aUv.x * iEdgeU + aUv.y * iEdgeV;
            vWorld = vec3(p.x, heightAt(p), p.y);
            vUv = aUv;
            vTangent = normalize(iEdgeU);
            gl_Position = uViewProjection * vec4(vWorld, 1.0);
        }
        """;

    /// <summary>
    /// An albedo decal, lit as the terrain under it and blended by its alpha (premultiplied).
    /// </summary>
    public static string DecalAlbedoFragment(bool es) => Header(es) + Heights + Lighting + """
        in vec2 vUv;
        in vec3 vWorld;
        in vec2 vTangent;
        out vec4 outColor;
        uniform sampler2D uTexture;

        void main()
        {
            vec4 color = texture(uTexture, vUv);
            if (color.a < 0.003)
            {
                discard;
            }
            outColor = vec4(lightOf(normalAt(vWorld.xz)) * color.rgb * color.a, color.a);
        }
        """;

    /// <summary>
    /// A normal decal (DXT5nm: x in alpha, y in green, opacity in red). The view draws no
    /// deferred lighting, so the decal changes the lit colour below it by how much its normal
    /// changes the light: it outputs half that factor, blended as <c>2 × source × destination</c>.
    /// </summary>
    public static string DecalNormalFragment(bool es) => Header(es) + Heights + Lighting + """
        in vec2 vUv;
        in vec3 vWorld;
        in vec2 vTangent;
        out vec4 outColor;
        uniform sampler2D uTexture;

        void main()
        {
            vec4 raw = texture(uTexture, vUv);
            float opacity = raw.r;
            if (opacity < 0.004)
            {
                discard;
            }
            vec2 xy = vec2(raw.a, raw.g) * 2.0 - 1.0;
            float z = sqrt(max(0.0, 1.0 - dot(xy, xy)));

            vec3 normal = normalAt(vWorld.xz);
            vec3 tangent = normalize(vec3(vTangent.x, 0.0, vTangent.y));
            tangent = normalize(tangent - normal * dot(normal, tangent));
            vec3 bitangent = cross(normal, tangent);
            vec3 decalNormal = normalize(tangent * xy.x + bitangent * xy.y + normal * z);

            vec3 factor = lightOf(decalNormal) / max(lightOf(normal), vec3(0.02));
            outColor = vec4(clamp(mix(vec3(1.0), factor, opacity) * 0.5, 0.0, 1.0), 1.0);
        }
        """;
}
