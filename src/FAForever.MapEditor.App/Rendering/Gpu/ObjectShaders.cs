namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// The shaders of the objects on the map in the 3D view: the models of props and units, lit by the
/// map's sun as the terrain is, and the markers' symbols.
/// </summary>
internal static class ObjectShaders
{
    /// <summary>
    /// A model, drawn once per object: each instance is the object's placement as four rows of a
    /// matrix (a point is <c>x · row0 + y · row1 + z · row2 + row3</c>); the first row's fourth
    /// value is how strongly it is highlighted (0 or 1), the second row's whether it is a wreck
    /// (user story U-5: 1, drawn grey and dark), both of which a placement leaves 0.
    /// </summary>
    public static string ModelVertex(bool es) => TerrainShaders.Header(es) + """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;
        layout(location = 3) in vec4 iRow0;
        layout(location = 4) in vec4 iRow1;
        layout(location = 5) in vec4 iRow2;
        layout(location = 6) in vec4 iRow3;
        uniform mat4 uViewProjection;
        out vec3 vNormal;
        out vec2 vUv;
        out float vHighlight;
        out float vWreck;

        void main()
        {
            vec3 world = aPosition.x * iRow0.xyz + aPosition.y * iRow1.xyz + aPosition.z * iRow2.xyz + iRow3.xyz;
            vNormal = aNormal.x * iRow0.xyz + aNormal.y * iRow1.xyz + aNormal.z * iRow2.xyz;
            vUv = aUv;
            vHighlight = iRow0.w;
            vWreck = iRow1.w;
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    /// <summary>
    /// A model's pixel: its albedo (cut out below the shader's alpha reference) lit as the terrain,
    /// from both sides (leaves and thin parts show their back; cut-out foliage with its normals
    /// leaning up, as a crown takes the light), and mixed with the highlight's
    /// colour when highlighted. A wreck is grey and darker, as the game's burnt wreckage.
    /// </summary>
    public static string ModelFragment(bool es) => TerrainShaders.Header(es) + TerrainShaders.Lighting + """
        in vec3 vNormal;
        in vec2 vUv;
        in float vHighlight;
        in float vWreck;
        out vec4 outColor;
        uniform sampler2D uAlbedo;
        // the alpha below which a pixel is cut out; below 0 for none
        uniform float uAlphaTest;
        uniform vec3 uHighlight;
        // what the albedo is multiplied with: white for a model, the theme's colour for the box of a missing one
        uniform vec3 uColor;
        // how the albedo reads: 0 as it is (props), 1 the metal of UEF, Aeon and Cybran units, which
        // the game lights brighter with its environment, 2 Seraphim units (silver from blue and green)
        uniform int uShading;

        void main()
        {
            vec4 albedo = texture(uAlbedo, vUv);
            if (uAlphaTest >= 0.0)
            {
                // the smaller mipmaps average a leaf texture's holes into alpha below the cut, which
                // would leave a tree seen from afar as its bare trunk: the cut reads the alpha from
                // the first or second level whatever the distance
                vec2 texel = vUv * vec2(textureSize(uAlbedo, 0));
                vec2 dx = dFdx(texel);
                vec2 dy = dFdy(texel);
                float level = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
                if (textureLod(uAlbedo, vUv, clamp(level, 0.0, 1.0)).a < uAlphaTest)
                {
                    discard;
                }
            }
            vec3 normal = normalize(vNormal);
            if (!gl_FrontFacing)
            {
                normal = -normal;
            }
            if (uAlphaTest >= 0.0)
            {
                // foliage: cards of leaves stand at every angle, and lit by their own normals most face
                // away from a high sun and show black; a crown takes the light as a whole, so its
                // normals lean half way up
                normal = normalize(normal + vec3(0.0, 1.0, 0.0));
            }
            vec3 base = albedo.rgb;
            if (uShading == 1)
            {
                base = min(base * 1.8, vec3(1.0));
            }
            else if (uShading == 2)
            {
                base = (albedo.bbg + vec3(0.4, 0.43, 0.47)) * 0.8;
            }
            vec3 color = lightOf(normal) * base * uColor;
            color = mix(color, vec3(dot(color, vec3(0.3, 0.59, 0.11))) * vec3(0.5, 0.47, 0.45), vWreck);
            color = mix(color, uHighlight, vHighlight * 0.55);
            outColor = vec4(color, 1.0);
        }
        """;

    /// <summary>
    /// A marker's symbol, drawn once per marker: an upright diamond whose lower tip stands on the
    /// marker's place. Each instance is its place, its size close by and its colour (the fourth
    /// value: highlighted, 0 or 1). From <see cref="ObjectPlacement.MarkerNear"/> on it grows with
    /// the distance to the eye, so that it keeps its size on screen
    /// (<see cref="ObjectPlacement.MarkerHeight"/>, which picking uses).
    /// </summary>
    public static string MarkerVertex(bool es) => TerrainShaders.Header(es) + """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 3) in vec4 iPlace;
        layout(location = 4) in vec4 iColor;
        uniform mat4 uViewProjection;
        uniform vec3 uEye;
        uniform float uNear;
        out vec3 vNormal;
        out vec3 vColor;
        out float vHighlight;

        void main()
        {
            float height = iPlace.w * max(1.0, distance(iPlace.xyz, uEye) / uNear);
            vec3 world = iPlace.xyz + aPosition * height;
            vNormal = aNormal;
            vColor = iColor.rgb;
            vHighlight = iColor.a;
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    /// <summary>
    /// A marker's pixel: its kind's colour, shaded by the sun enough to show the diamond's facets,
    /// and lighter with the highlight's colour when highlighted.
    /// </summary>
    public static string MarkerFragment(bool es) => TerrainShaders.Header(es) + """
        in vec3 vNormal;
        in vec3 vColor;
        in float vHighlight;
        out vec4 outColor;
        uniform vec3 uSunDirection;
        uniform vec3 uHighlight;

        void main()
        {
            float light = 0.55 + 0.45 * max(dot(normalize(vNormal), uSunDirection), 0.0);
            vec3 color = vColor * light;
            color = mix(color, uHighlight, vHighlight * 0.6);
            outColor = vec4(color, 1.0);
        }
        """;
}
