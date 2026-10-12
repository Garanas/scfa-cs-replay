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
    internal const string Heights = """
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
    internal const string Lighting = """
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

    /// <summary>
    /// A corner of the terrain's lattice (whole cells from its top left) as a place of the terrain:
    /// the terrain and the brush both make their vertices with it, invariantly, so that the brush
    /// lies exactly on the terrain.
    /// </summary>
    private const string Lattice = """
        uniform mat4 uViewProjection;
        uniform vec2 uMapSize;
        uniform vec2 uCells;
        invariant gl_Position;

        vec3 latticePlace(vec2 corner)
        {
            vec2 p = corner / uCells * uMapSize;
            return vec3(p.x, heightAt(p), p.y);
        }

        """;

    public static string TerrainVertex(bool es) => Header(es) + Heights + Lattice + """
        layout(location = 0) in vec2 aCorner;
        out vec3 vWorld;

        void main()
        {
            vWorld = latticePlace(aCorner);
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
        // the terrain shader's way of blending (TerrainShaderCatalog): x 1 when a mask counts over its
        // whole range (else from its middle up), y the strata it blends (4 or 8), z 1 when stratum 8
        // is a texture (else map-wide data), w 1 when the upper layer is a texture
        uniform vec4 uBlend;

        void main()
        {
            vec2 p = vWorld.xz;
            vec2 maskUv = p / uMapSize;
            vec4 mask0 = texture(uMask0, maskUv);
            vec4 mask1 = texture(uMask1, maskUv);
            if (uBlend.x < 0.5)
            {
                mask0 = clamp(mask0 * 2.0 - 1.0, 0.0, 1.0);
                mask1 = clamp(mask1 * 2.0 - 1.0, 0.0, 1.0);
            }
            if (uBlend.y < 4.5)
            {
                mask1 = vec4(0.0);
            }
            mask1.w *= uBlend.z;

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
            albedo.rgb = mix(albedo.rgb, upper.rgb, upper.a * uBlend.w);

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
    /// floor's decals lie under it). With its ramp, as the game: the ramp's colour at the depth
    /// (down to the abyss elevation) over the ground, then the surface colour; without one its
    /// opacity and darkness grow with the depth, in the surface colour. It reflects its cube map.
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
        // what the water reflects (its cube map, else the sky's), as strong as the water's sky
        // reflection (y) and the Fresnel term say; uReflect.x 0 for none
        uniform samplerCube uReflection;
        uniform vec4 uReflect;
        uniform vec3 uEye;
        // the water ramp (its colour by depth) and whether there is one; the surface colour's near and far
        uniform sampler2D uRamp;
        uniform int uHasRamp;
        uniform vec2 uColorLerp;

        void main()
        {
            float depth = uWater.x - heightAt(vPlace);
            if (depth <= 0.0)
            {
                discard;
            }
            float t = clamp(depth / max(uWater.x - uWater.y, 1.0), 0.0, 1.0);
            vec3 surface = clamp(uWaterColor, 0.0, 1.0);
            // premultiplied, over the terrain
            vec4 water;
            if (uHasRamp == 1)
            {
                // as the game: the ramp's colour at the depth over the terrain by its alpha (terrain.fx),
                // then the surface colour by the depth, clamped to its near and far (water2.fx)
                vec4 ramp = texture(uRamp, vec2(t, 0.5));
                float f = clamp(t, min(uColorLerp.x, uColorLerp.y), max(uColorLerp.x, uColorLerp.y));
                water = vec4(ramp.rgb * ramp.a * (1.0 - f) + surface * f, 1.0 - (1.0 - ramp.a) * (1.0 - f));
            }
            else
            {
                float alpha = clamp(0.25 + 0.75 * sqrt(t), 0.0, 0.92);
                water = vec4(mix(surface * 0.55, surface * 0.12, t) * alpha, alpha);
            }
            if (uReflect.x > 0.5)
            {
                vec3 view = normalize(vec3(vPlace.x, uWater.x, vPlace.y) - uEye);
                vec3 reflected = reflect(view, vec3(0.0, 1.0, 0.0));
                // Schlick's approximation with FAF's F0 of 0.08 (water2.fx), the surface flat
                float fresnel = 0.08 + 0.92 * pow(1.0 - clamp(-view.y, 0.0, 1.0), 5.0);
                float strength = clamp(fresnel * uReflect.y, 0.0, 1.0);
                water = water * (1.0 - strength) + vec4(texture(uReflection, reflected).rgb * strength, strength);
            }
            outColor = water;
        }
        """;

    /// <summary>
    /// The sky (user story L-1): behind everything, the map's sky cube map in the direction of
    /// each pixel, from the inverse of the camera's matrix.
    /// </summary>
    public static string SkyVertex(bool es) => Header(es) + """
        layout(location = 0) in vec2 aGrid;
        out vec2 vNdc;

        void main()
        {
            vNdc = aGrid * 2.0 - 1.0;
            gl_Position = vec4(vNdc, 1.0, 1.0);
        }
        """;

    /// <summary>
    /// The sky's cube map in the direction of each pixel. A cube map of the game shows its upper half
    /// only, as the game shows it: below the horizon (where the game has the map and its skirt) the
    /// horizon's colour fading within a few degrees into a haze of the sky low above the horizon, so
    /// the lower half of a cube map (the default's photo landscape) never wraps the map. A cube map of
    /// the map's own (<c>uWhole</c>: one generated from the map, whose lower half is the map itself)
    /// shows whole, so what lies across its horizon (a planet low in the sky) is not cut off.
    /// </summary>
    public static string SkyFragment(bool es) => Header(es) + """
        in vec2 vNdc;
        out vec4 outColor;
        uniform mat4 uInverseViewProjection;
        uniform samplerCube uSky;
        uniform int uWhole;

        void main()
        {
            vec4 near = uInverseViewProjection * vec4(vNdc, -1.0, 1.0);
            vec4 far = uInverseViewProjection * vec4(vNdc, 1.0, 1.0);
            vec3 direction = normalize(far.xyz / far.w - near.xyz / near.w);
            vec3 sky;
            if (direction.y >= 0.0 || uWhole == 1)
            {
                sky = texture(uSky, direction).rgb;
            }
            else
            {
                // the horizon's own colour for the first few degrees, then a haze: the sky low above
                // the horizon in that direction, blurred to its broad colour
                vec3 horizon = normalize(vec3(direction.x, 0.01, direction.z));
                vec3 haze = textureLod(uSky, normalize(vec3(direction.x, 0.15, direction.z)), 7.0).rgb;
                sky = mix(textureLod(uSky, horizon, 0.0).rgb, haze, smoothstep(0.0, 0.05, -direction.y));
            }
            outColor = vec4(sky, 1.0);
        }
        """;

    /// <summary>
    /// The active tool's brush under the mouse: the cells of the terrain's lattice under it, made
    /// as the terrain makes them, so the two are one surface; where a point lies in the brush's
    /// square (turned with its picture) is worked out per pixel.
    /// </summary>
    public static string BrushVertex(bool es) => Header(es) + Heights + Lattice + """
        layout(location = 0) in vec2 aCorner;
        // the patch's first corner in the lattice
        uniform vec2 uOrigin;
        // the brush's centre, radius, and the sine and cosine of its turn
        uniform vec2 uCenter;
        uniform float uRadius;
        uniform vec2 uTurn;
        // -1 flips the picture left to right before it is turned (an image in a mirrored part)
        uniform float uFlip;
        out vec2 vUv;

        void main()
        {
            vec3 world = latticePlace(uOrigin + aCorner);
            vec2 d = world.xz - uCenter;
            // turned back into the square, from (0, 0) at its top left to (1, 1)
            vec2 c = vec2(d.x * uTurn.y + d.y * uTurn.x, d.y * uTurn.y - d.x * uTurn.x) / uRadius;
            c.x *= uFlip;
            vUv = c * 0.5 + 0.5;
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    /// <summary>
    /// How strongly the brush works (the round falloff, or the picture's values) as a ramp from
    /// near black to the overlay's colour over the terrain, so that it reads on light and dark
    /// ground alike. Its edge is a line: the circle of the radius, or the picture's square.
    /// </summary>
    public static string BrushFragment(bool es) => Header(es) + """
        in vec2 vUv;
        out vec4 outColor;
        uniform sampler2D uShape;
        uniform int uRound;
        // the square's side over the picture's width and height: a shorter side is centred
        uniform vec2 uShapeScale;
        uniform vec3 uColor;

        void main()
        {
            vec2 c = vUv * 2.0 - 1.0;
            float weight;
            float edge;
            bool inside;
            if (uRound == 1)
            {
                float d = length(c);
                inside = d < 1.0;
                weight = inside ? 0.5 * (1.0 + cos(3.14159265 * d)) : 0.0;
                edge = abs(d - 1.0) / max(fwidth(d), 1e-5);
            }
            else
            {
                vec2 uv = (vUv - 0.5) * uShapeScale + 0.5;
                inside = all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)));
                weight = inside ? texture(uShape, uv).r : 0.0;
                // pixels from the square's edge, on either side of it
                vec2 e = (1.0 - abs(c)) / max(fwidth(c), vec2(1e-5));
                edge = abs(min(e.x, e.y));
            }
            float line = 1.0 - clamp(edge - 0.75, 0.0, 1.0);
            // premultiplied: near black at no weight, the overlay's colour at full weight; the
            // square root shows where a picture works at all, which its low values hide
            float tint = inside ? 0.55 : 0.0;
            vec4 color = vec4(mix(vec3(0.05), uColor, sqrt(weight)) * tint, tint);
            color = mix(color, vec4(uColor * 0.9, 0.9), line);
            if (color.a < 0.004)
            {
                discard;
            }
            outColor = color;
        }
        """;

    /// <summary>
    /// A decal: the cells of the terrain's lattice under its rectangle, made as the terrain makes
    /// them, so the two are one surface whatever the distance. Each instance is one decal, placed by
    /// its corner (the decal's position) and its two edges; its patch of the lattice has
    /// <c>uPatchCells</c> cells a side, the corners it does not need folded onto its far edge, and a
    /// decal wider than that takes every second (or more) corner. Where a point lies in the decal is
    /// its place turned back by the edges; the fragment drops what lies outside.
    /// </summary>
    public static string DecalVertex(bool es) => Header(es) + Heights + Lattice + """
        layout(location = 0) in vec2 aCorner;
        layout(location = 1) in vec2 iCorner;
        layout(location = 2) in vec2 iEdgeU;
        layout(location = 3) in vec2 iEdgeV;
        uniform float uPatchCells;
        out vec2 vUv;
        out vec3 vWorld;
        out vec2 vTangent;

        void main()
        {
            vec2 cell = uMapSize / uCells;
            vec2 b = iCorner + iEdgeU;
            vec2 c = iCorner + iEdgeV;
            vec2 d = b + iEdgeV;
            vec2 low = min(min(iCorner, b), min(c, d));
            vec2 high = max(max(iCorner, b), max(c, d));
            vec2 origin = clamp(floor(low / cell), vec2(0.0), uCells);
            vec2 need = clamp(ceil(high / cell), vec2(0.0), uCells) - origin;
            float stride = max(1.0, ceil(max(need.x, need.y) / uPatchCells));
            vWorld = latticePlace(origin + min(aCorner * stride, need));
            vUv = inverse(mat2(iEdgeU, iEdgeV)) * (vWorld.xz - iCorner);
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
            if (any(lessThan(vUv, vec2(0.0))) || any(greaterThan(vUv, vec2(1.0))))
            {
                discard;
            }
            vec4 color = texture(uTexture, vUv);
            if (color.a < 0.003)
            {
                discard;
            }
            outColor = vec4(lightOf(normalAt(vWorld.xz)) * color.rgb * color.a, color.a);
        }
        """;

    /// <summary>
    /// A highlighted decal (the selection): a light tint of <c>uColor</c> over it and an outline
    /// two pixels wide along its edges, premultiplied.
    /// </summary>
    public static string DecalHighlightFragment(bool es) => Header(es) + Heights + Lighting + """
        in vec2 vUv;
        in vec3 vWorld;
        in vec2 vTangent;
        out vec4 outColor;
        uniform vec3 uColor;

        void main()
        {
            if (any(lessThan(vUv, vec2(0.0))) || any(greaterThan(vUv, vec2(1.0))))
            {
                discard;
            }
            vec2 pixels = (min(vUv, 1.0 - vUv)) / max(fwidth(vUv), vec2(1e-6));
            float alpha = min(pixels.x, pixels.y) < 2.0 ? 0.9 : 0.18;
            outColor = vec4(uColor * alpha, alpha);
        }
        """;

    /// <summary>
    /// A normal decal (DXT5nm: x in alpha, z in green, opacity in red). As the game's terrain shader
    /// reads it (<c>effects/terrain.fx</c>, <c>DecalsNormalsPS</c>): the normal is the decal's own,
    /// up in its frame and turned only by the decal's turn about the vertical axis (the engine's
    /// <c>TangentMatrix</c>), and it replaces the terrain's normal by the red channel's weight; so a
    /// map-wide normals decal holds the terrain's normals themselves. The view draws no deferred
    /// lighting, so the decal changes the lit colour below it by how much its normal changes the
    /// light: it outputs half that factor, blended as <c>2 × source × destination</c>.
    /// </summary>
    public static string DecalNormalFragment(bool es) => Header(es) + Heights + Lighting + """
        in vec2 vUv;
        in vec3 vWorld;
        in vec2 vTangent;
        out vec4 outColor;
        uniform sampler2D uTexture;

        void main()
        {
            if (any(lessThan(vUv, vec2(0.0))) || any(greaterThan(vUv, vec2(1.0))))
            {
                discard;
            }
            vec4 raw = texture(uTexture, vUv);
            float opacity = raw.r;
            if (opacity < 0.004)
            {
                discard;
            }
            vec2 xy = vec2(raw.a, raw.g) * 2.0 - 1.0;
            float z = sqrt(max(0.0, 1.0 - dot(xy, xy)));

            vec3 normal = normalAt(vWorld.xz);
            // the decal's own axes on the ground: x along its width, z along its length
            vec3 tangent = normalize(vec3(vTangent.x, 0.0, vTangent.y));
            vec3 bitangent = vec3(-tangent.z, 0.0, tangent.x);
            vec3 decalNormal = normalize(tangent * xy.x + bitangent * xy.y + vec3(0.0, z, 0.0));

            vec3 factor = lightOf(decalNormal) / max(lightOf(normal), vec3(0.02));
            outColor = vec4(clamp(mix(vec3(1.0), factor, opacity) * 0.5, 0.0, 1.0), 1.0);
        }
        """;

    /// <summary>
    /// What the 3D view lays over the terrain (user story V-3), drawn on the whole lattice of the
    /// terrain, made as the terrain makes it, so the two are one surface at any distance.
    /// </summary>
    public static string OverlayVertex(bool es) => Header(es) + Heights + Lattice + """
        layout(location = 0) in vec2 aCorner;
        out vec3 vWorld;

        void main()
        {
            vWorld = latticePlace(aCorner);
            gl_Position = uViewProjection * vec4(vWorld, 1.0);
        }
        """;

    /// <summary>
    /// The overlays, premultiplied, each over the one before: the shade outside the playable area,
    /// walkability (one texel an ogrid, its colours made on the processor), the grid being painted
    /// or the mask for tools (its colours made on the processor), the areas' outlines (two and a half
    /// pixels wide for the playable area and the selected ones, else one and a half) (the selected ones in the selection's colour, the one under the mouse lighter), and the
    /// symmetry's guides, dashed.
    /// </summary>
    public static string OverlayFragment(bool es) => Header(es) + """
        in vec3 vWorld;
        out vec4 outColor;
        uniform vec2 uMapSize;
        uniform int uWalk;
        uniform sampler2D uWalkTexture;
        uniform int uGrid;
        uniform sampler2D uGridTexture;
        uniform int uAreaCount;
        uniform vec4 uAreas[32];
        // 0 an area, 1 selected, 2 under the mouse
        uniform float uAreaStates[32];
        uniform vec4 uAreaColor;
        uniform vec4 uSelectionColor;
        uniform vec4 uHoverColor;
        uniform vec4 uOutside;
        uniform int uShade;
        uniform int uGuideCount;
        uniform vec4 uGuides[8];
        uniform vec4 uGuideColor;
        uniform vec4 uShadowColor;
        uniform vec3 uEye;
        // ogrids a pixel at a distance of one ogrid from the eye
        uniform float uPixelScale;
        // the grid's lines (V-3): their spacing in ogrids, 0 for none, and their colour
        uniform float uLineSpacing;
        uniform vec4 uLineColor;

        vec4 over(vec4 below, vec4 above)
        {
            return above + below * (1.0 - above.a);
        }

        void main()
        {
            vec2 p = vWorld.xz;
            // how large a pixel is on the map here: the lines keep their width on screen
            vec2 pixel = vec2(max(distance(uEye, vWorld) * uPixelScale, 1e-5));
            vec4 color = vec4(0.0);
            if (uShade == 1 && uAreaCount > 0)
            {
                vec4 a = uAreas[0];
                if (p.x < min(a.x, a.z) || p.x > max(a.x, a.z) || p.y < min(a.y, a.w) || p.y > max(a.y, a.w))
                {
                    color = over(color, vec4(uOutside.rgb * uOutside.a, uOutside.a));
                }
            }
            if (uWalk == 1)
            {
                ivec2 size = textureSize(uWalkTexture, 0);
                color = over(color, texelFetch(uWalkTexture, clamp(ivec2(floor(p)), ivec2(0), size - 1), 0));
            }
            if (uGrid == 1)
            {
                color = over(color, texture(uGridTexture, p / uMapSize));
            }
            for (int i = 0; i < uAreaCount; i++)
            {
                vec4 a = uAreas[i];
                vec2 low = min(a.xy, a.zw);
                vec2 high = max(a.xy, a.zw);
                vec2 inside = min(p - low, high - p) / pixel;
                float d = min(inside.x, inside.y);
                vec2 outside = max(max(low - p, p - high), vec2(0.0)) / pixel;
                float outer = length(outside);
                float distance = d >= 0.0 ? d : outer;
                // half the line's width: it lies on both sides of the edge
                float width = uAreaStates[i] > 0.5 || i == 0 ? 1.25 : 0.75;
                if (distance < width + 1.0)
                {
                    vec4 line = uAreaStates[i] > 1.5 ? uHoverColor : uAreaStates[i] > 0.5 ? uSelectionColor : uAreaColor;
                    float alpha = line.a * (distance < width ? 1.0 : 0.6);
                    // a dark edge outside the line, so it shows on light ground
                    vec4 shown = distance < width ? vec4(line.rgb * alpha, alpha) : vec4(uShadowColor.rgb * uShadowColor.a * 0.6, uShadowColor.a * 0.6);
                    color = over(color, shown);
                }
            }
            if (uLineSpacing > 0.0)
            {
                // a line a pixel wide every spacing ogrids, faded where they would lie closer than a few pixels
                vec2 cell = p / uLineSpacing;
                vec2 away = abs(cell - floor(cell + 0.5)) * uLineSpacing / pixel;
                float fade = clamp((uLineSpacing / pixel.x - 4.0) / 8.0, 0.0, 1.0);
                if (min(away.x, away.y) < 0.75 && fade > 0.0)
                {
                    color = over(color, vec4(uLineColor.rgb * uLineColor.a * fade, uLineColor.a * fade));
                }
            }
            for (int i = 0; i < uGuideCount; i++)
            {
                vec2 a = uGuides[i].xy;
                vec2 b = uGuides[i].zw;
                vec2 ab = b - a;
                float t = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-6), 0.0, 1.0);
                vec2 d = (p - (a + ab * t)) / pixel;
                float distance = length(d);
                float along = t * length(ab) / max(pixel.x, pixel.y);
                if (distance < 2.5)
                {
                    bool dash = mod(along, 14.0) < 8.0;
                    vec4 line = distance < 1.0 && dash ? vec4(uGuideColor.rgb * uGuideColor.a, uGuideColor.a) : vec4(uShadowColor.rgb * uShadowColor.a, uShadowColor.a);
                    color = over(color, line);
                }
            }
            if (color.a < 0.004)
            {
                discard;
            }
            outColor = color;
        }
        """;
}
