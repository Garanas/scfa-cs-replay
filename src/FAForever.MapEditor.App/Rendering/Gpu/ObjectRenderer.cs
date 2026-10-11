using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Mesh;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Silk.NET.OpenGL;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// What the 3D view shows of the objects on the map in a frame.
/// </summary>
/// <param name="Catalog">The workspace's assets, where the models of the blueprints come from.</param>
/// <param name="Props">Whether the props show (the overlay <c>props</c>).</param>
/// <param name="Units">Whether the units show (<c>units</c>).</param>
/// <param name="Markers">Whether the spawns, deposits, AI markers and weather show (<c>markers</c>).</param>
/// <param name="PathNodes">Whether the path nodes show (<c>pathNodes</c>).</param>
/// <param name="Highlighted">The objects drawn highlighted (the selection); null for none.</param>
/// <param name="Eye">Where the camera is: the markers keep their size on screen from afar.</param>
/// <param name="HighlightColor">The highlight's colour, from the theme.</param>
/// <param name="MissingColor">The colour of the box that stands in for a model the workspace does
/// not have, from the theme.</param>
/// <param name="MarkerKinds">The kinds of markers that show, of those <paramref name="Markers"/> and <paramref name="PathNodes"/> show (user story R-6).</param>
/// <param name="Hovered">The object under the mouse, drawn lightly highlighted; null for none.</param>
/// <param name="EffectColor">The colour of the diamond drawn for a prop the game shows as an effect, from the theme.</param>
internal sealed record ObjectFrame(
    AssetCatalog Catalog,
    bool Props,
    bool Units,
    bool Markers,
    bool PathNodes,
    IReadOnlySet<ObjectRef>? Highlighted,
    Vector3 Eye,
    Vector3 HighlightColor,
    Vector3 MissingColor,
    MarkerKinds MarkerKinds = MarkerKinds.All,
    ObjectRef? Hovered = null,
    IReadOnlySet<ObjectRef>? Isolated = null,
    Vector3 EffectColor = default)
{
    /// <summary>
    /// Whether the thing is drawn: every thing, or only those of the isolated selection.
    /// </summary>
    public bool Shows(ObjectRef thing) => Isolated?.Contains(thing) != false;

    /// <summary>
    /// How strongly an object is highlighted: 1 selected, less under the mouse, else 0.
    /// </summary>
    public float HighlightOf(ObjectRef thing) => Highlighted?.Contains(thing) == true ? 1 : Hovered == thing ? HoverStrength : 0;

    /// <summary>
    /// How strongly the object under the mouse is highlighted.
    /// </summary>
    public const float HoverStrength = 0.45f;
}

/// <summary>
/// Draws the objects on the map: the props and units with their models (<see cref="ObjectModels"/>),
/// instanced, one draw per blueprint; a box for a blueprint the workspace has no model of; and the
/// markers as diamonds in their kind's colour, one draw for all. The instances are made again
/// only when the objects (or what is highlighted) change; the models and their albedo textures are
/// read on threads of the pool and go on the card with the next frame.
/// </summary>
internal sealed unsafe class ObjectRenderer : IDisposable
{
    // four rows of a placement: sixteen floats an instance
    private const int ModelStride = 16 * sizeof(float);

    // place and size, colour and highlight: eight floats a marker
    private const int MarkerStride = 8 * sizeof(float);

    private readonly GL _gl;
    private readonly ObjectModels _models;
    private readonly GpuTextureCache _textures;
    private readonly TerrainRenderer.Program _model;
    private readonly TerrainRenderer.Program _marker;
    private readonly uint _sampler;
    private readonly uint _white;
    private readonly GpuMesh _box;
    private readonly GpuMesh _diamond;
    private readonly Dictionary<(string Model, int Lod), GpuMesh> _meshes = [];

    private readonly Instances _props;
    private readonly Instances _units;
    private readonly uint _markerInstances;
    private int _markerCount;
    private object? _markersOf;

    public ObjectRenderer(GL gl, bool es, ObjectModels models, Action textureLoaded)
    {
        _gl = gl;
        _models = models;
        _textures = new GpuTextureCache(gl, textureLoaded);
        _model = new TerrainRenderer.Program(gl, ObjectShaders.ModelVertex(es), ObjectShaders.ModelFragment(es));
        _marker = new TerrainRenderer.Program(gl, ObjectShaders.MarkerVertex(es), ObjectShaders.MarkerFragment(es));

        _sampler = gl.GenSampler();
        gl.SamplerParameter(_sampler, SamplerParameterI.WrapS, (int)GLEnum.Repeat);
        gl.SamplerParameter(_sampler, SamplerParameterI.WrapT, (int)GLEnum.Repeat);
        gl.SamplerParameter(_sampler, SamplerParameterI.MinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.SamplerParameter(_sampler, SamplerParameterI.MagFilter, (int)GLEnum.Linear);
        _white = gl.GenTexture();
        gl.ActiveTexture(TextureUnit.Texture1);
        GpuTextureCache.UploadRgba(gl, _white, 1, 1, [255, 255, 255, 255], mipmaps: false);

        _box = new GpuMesh(gl, BoxVertices(), BoxIndices());
        (float[] diamond, ushort[] diamondIndices) = Diamond();
        _diamond = new GpuMesh(gl, diamond, diamondIndices);
        _props = new Instances(gl);
        _units = new Instances(gl);
        _markerInstances = gl.GenBuffer();
    }

    /// <summary>
    /// The props, units and markers drawn in the last frame, the draws they took, the models on
    /// the card, the blueprints drawn as a box for want of a model, and the triangles of the models.
    /// </summary>
    public (int Props, int Units, int Markers, int Draws, int Meshes, int Missing, long Triangles) State { get; private set; }

    /// <summary>
    /// The models' albedos being read, not on the card yet.
    /// </summary>
    public int TexturesLoading => _textures.Loading;

    private long _triangles;

    /// <summary>
    /// The objects that lie on the terrain and hide what is behind them: the props and units.
    /// Call after the terrain, with the depth test on and depth writes on.
    /// </summary>
    /// <param name="sweep">Whether textures this draw did not use are let go (false while several views of one frame share them).</param>
    public void DrawModels(MapDocument document, Matrix4x4 viewProjection, ObjectFrame frame, bool sweep = true)
    {
        // uploads bind to the active unit: the albedo's own
        _gl.ActiveTexture(TextureUnit.Texture1);
        _textures.Upload();

        int draws = 0;
        int props = 0;
        int units = 0;
        int missing = 0;
        _triangles = 0;
        _model.Use();
        _model.Set("uViewProjection", viewProjection);
        SetLighting(_model, document);
        _model.Set("uHighlight", frame.HighlightColor.X, frame.HighlightColor.Y, frame.HighlightColor.Z);
        _model.Set("uAlbedo", 1);

        if (frame.Props)
        {
            using Performance.Span measure = Performance.Measure("3d.draw-props");
            // the thing under the mouse changes as the mouse moves: only a prop under it makes the props again
            _props.Update((document.Props, frame.Highlighted, frame.Hovered is { Kind: ObjectKind.Prop } hoveredProp ? hoveredProp : (ObjectRef?)null, frame.Isolated), () => document.Props.Where(prop => frame.Shows(ObjectRef.Prop(prop.Id))).Select(prop =>
                (prop.Blueprint, ObjectPlacement.Prop(prop), frame.HighlightOf(ObjectRef.Prop(prop.Id)), 0f)));
            _props.Arrange(blueprint => _models.Get(blueprint, frame.Catalog, out _), viewProjection, frame.Eye, _models.Loaded);
            (int drawn, int runs, int boxes) = DrawRuns(_props, frame);
            props = drawn;
            draws += runs;
            missing += boxes;
        }
        if (frame.Units)
        {
            using Performance.Span measure = Performance.Measure("3d.draw-units");
            _units.Update((document.Armies, frame.Highlighted, frame.Hovered is { Kind: ObjectKind.Unit } hoveredUnit ? hoveredUnit : (ObjectRef?)null, frame.Isolated), () =>
            {
                // wrecks are drawn grey and dark (U-5)
                ImmutableHashSet<string> wrecks = document.Wrecks();
                return ObjectPlacement.Units(document).Where(placed => frame.Shows(ObjectRef.Unit(placed.Unit.Name))).Select(placed =>
                    (placed.Unit.Blueprint, ObjectPlacement.Unit(placed.Unit), frame.HighlightOf(ObjectRef.Unit(placed.Unit.Name)), wrecks.Contains(placed.Unit.Name) ? 1f : 0f));
            });
            _units.Arrange(blueprint => _models.Get(blueprint, frame.Catalog, out _), viewProjection, frame.Eye, _models.Loaded);
            (int drawn, int runs, int boxes) = DrawRuns(_units, frame);
            units = drawn;
            draws += runs;
            missing += boxes;
        }
        _gl.BindVertexArray(0);
        _gl.BindSampler(1, 0);
        if (sweep)
        {
            _textures.Sweep();
        }
        State = State with { Props = props, Units = units, Draws = draws, Meshes = _meshes.Count, Missing = missing, Triangles = _triangles };
    }

    /// <summary>
    /// The markers' symbols. Call after the water: they stand on the terrain, in front of it.
    /// </summary>
    public void DrawMarkers(MapDocument document, Matrix4x4 viewProjection, ObjectFrame frame)
    {
        if (!frame.Markers && !frame.PathNodes)
        {
            State = State with { Markers = 0 };
            return;
        }
        using Performance.Span measure = Performance.Measure("3d.draw-markers");
        UpdateMarkers(document, frame);
        if (_markerCount == 0)
        {
            State = State with { Markers = 0 };
            return;
        }
        _marker.Use();
        _marker.Set("uViewProjection", viewProjection);
        _marker.Set("uEye", frame.Eye.X, frame.Eye.Y, frame.Eye.Z);
        _marker.Set("uNear", ObjectPlacement.MarkerNear);
        Vector3 sun = SunOf(document);
        _marker.Set("uSunDirection", sun.X, sun.Y, sun.Z);
        _marker.Set("uHighlight", frame.HighlightColor.X, frame.HighlightColor.Y, frame.HighlightColor.Z);
        _diamond.Bind();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _markerInstances);
        for (uint attribute = 3; attribute <= 4; attribute++)
        {
            _gl.EnableVertexAttribArray(attribute);
            _gl.VertexAttribPointer(attribute, 4, VertexAttribPointerType.Float, false, MarkerStride, (void*)((attribute - 3) * 4 * sizeof(float)));
            _gl.VertexAttribDivisor(attribute, 1);
        }
        _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)_diamond.IndexCount, DrawElementsType.UnsignedShort, null, (uint)_markerCount);
        _gl.BindVertexArray(0);
        State = State with { Markers = _markerCount, Draws = State.Draws + 1 };
    }

    private (int Drawn, int Draws, int Missing) DrawRuns(Instances instances, ObjectFrame frame)
    {
        int drawn = 0;
        int draws = 0;
        int missing = 0;
        foreach (Run run in instances.Runs)
        {
            ObjectModels.Model? model = _models.Get(run.Blueprint, frame.Catalog, out bool absent);
            if (model is null && !absent)
            {
                // being read: it shows a moment later
                continue;
            }
            GpuMesh mesh;
            uint texture = _white;
            float alphaTest = -1;
            if (model is { IsEffect: true })
            {
                // the game shows an effect (steam, sand, mist) where the blueprint stands, not its marker mesh
                mesh = _diamond;
                _model.Set("uColor", frame.EffectColor.X, frame.EffectColor.Y, frame.EffectColor.Z);
                _model.Set("uShading", 0);
            }
            else if (model is not null)
            {
                int level = Math.Min(run.Lod, model.Lods.Length - 1);
                ObjectModels.Lod lod = model.Lods[level];
                mesh = MeshOf(model, level);
                texture = lod.AlphaTest is float cut ? _textures.GetCutOut(lod.Albedo, _white, cut) : _textures.Get(lod.Albedo, _white);
                alphaTest = lod.AlphaTest ?? -1;
                _model.Set("uColor", 1, 1, 1);
                _model.Set("uShading", lod.Shader switch
                {
                    PreviewShader.Uef or PreviewShader.Aeon or PreviewShader.Cybran => 1,
                    PreviewShader.Seraphim => 2,
                    _ => 0,
                });
            }
            else
            {
                mesh = _box;
                missing++;
                _model.Set("uColor", frame.MissingColor.X, frame.MissingColor.Y, frame.MissingColor.Z);
                _model.Set("uShading", 0);
            }
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, texture);
            _gl.BindSampler(1, _sampler);
            _model.Set("uAlphaTest", alphaTest);

            mesh.Bind();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, instances.Buffer);
            nint offset = run.First * ModelStride;
            for (uint attribute = 3; attribute <= 6; attribute++)
            {
                _gl.EnableVertexAttribArray(attribute);
                _gl.VertexAttribPointer(attribute, 4, VertexAttribPointerType.Float, false, ModelStride, (void*)(offset + (attribute - 3) * 4 * sizeof(float)));
                _gl.VertexAttribDivisor(attribute, 1);
            }
            _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)mesh.IndexCount, DrawElementsType.UnsignedShort, null, (uint)run.Count);
            drawn += run.Count;
            _triangles += (long)run.Count * mesh.IndexCount / 3;
            draws++;
        }
        return (drawn, draws, missing);
    }

    /// <summary>
    /// A level of a model on the card, put there the first time it is drawn.
    /// </summary>
    private GpuMesh MeshOf(ObjectModels.Model model, int level)
    {
        if (!_meshes.TryGetValue((model.Key, level), out GpuMesh? mesh) || !ReferenceEquals(mesh.Of, model))
        {
            using Performance.Span measure = Performance.Measure("3d.upload-model");
            mesh?.Dispose();
            mesh = new GpuMesh(_gl, model.Lods[level].Vertices, model.Lods[level].Indices) { Of = model };
            _meshes[(model.Key, level)] = mesh;
        }
        return mesh;
    }

    private void UpdateMarkers(MapDocument document, ObjectFrame frame)
    {
        object key = (document.Markers, document.Terrain.Heightmap, frame.Markers, frame.PathNodes, frame.Highlighted, frame.MarkerKinds, frame.Hovered, frame.Isolated);
        if (Equals(key, _markersOf))
        {
            return;
        }
        _markersOf = key;
        using Performance.Span measure = Performance.Measure("3d.update-markers");
        List<float> data = [];
        foreach (Marker marker in ObjectPlacement.Markers(document, frame.Markers, frame.PathNodes, frame.MarkerKinds).Where(marker => frame.Shows(ObjectRef.Marker(marker.Id))))
        {
            Vector3 place = ObjectPlacement.MarkerPlace(marker, document.Terrain.Heightmap);
            Color32 color = ViewModels.MarkersViewModel.ShownColour(marker);
            data.AddRange([place.X, place.Y, place.Z, ObjectPlacement.MarkerBase(marker),
                color.Red / 255f, color.Green / 255f, color.Blue / 255f, frame.HighlightOf(ObjectRef.Marker(marker.Id))]);
        }
        _markerCount = data.Count / 8;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _markerInstances);
        float[] array = data.Count > 0 ? [.. data] : [0f];
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, array, BufferUsageARB.StaticDraw);
    }

    private static Vector3 SunOf(MapDocument document) =>
        document.Lighting.SunDirection.LengthSquared() > 0 ? Vector3.Normalize(document.Lighting.SunDirection) : Vector3.UnitY;

    private static void SetLighting(TerrainRenderer.Program program, MapDocument document)
    {
        Lighting lighting = document.Lighting;
        Vector3 sun = SunOf(document);
        program.Set("uSunDirection", sun.X, sun.Y, sun.Z);
        program.Set("uSunColor", lighting.SunColor.X, lighting.SunColor.Y, lighting.SunColor.Z);
        program.Set("uSunAmbience", lighting.SunAmbience.X, lighting.SunAmbience.Y, lighting.SunAmbience.Z);
        program.Set("uShadowFill", lighting.ShadowFillColor.X, lighting.ShadowFillColor.Y, lighting.ShadowFillColor.Z);
        program.Set("uLightMultiplier", lighting.Multiplier);
    }

    #region Shapes

    /// <summary>
    /// A box of one ogrid standing on the ground (<see cref="ObjectBounds.Box"/>), a normal per face.
    /// </summary>
    private static float[] BoxVertices()
    {
        List<float> vertices = [];
        Vector3[] normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        foreach (Vector3 normal in normals)
        {
            Vector3 side = MathF.Abs(normal.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 other = Vector3.Cross(normal, side);
            foreach ((float a, float b) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                Vector3 corner = (normal + side * a + other * b) * 0.5f + new Vector3(0, 0.5f, 0);
                vertices.AddRange([corner.X, corner.Y, corner.Z, normal.X, normal.Y, normal.Z, (a + 1) / 2, (b + 1) / 2]);
            }
        }
        return [.. vertices];
    }

    private static ushort[] BoxIndices()
    {
        List<ushort> indices = [];
        for (int face = 0; face < 6; face++)
        {
            ushort first = (ushort)(face * 4);
            indices.AddRange([first, (ushort)(first + 1), (ushort)(first + 2), first, (ushort)(first + 2), (ushort)(first + 3)]);
        }
        return [.. indices];
    }

    /// <summary>
    /// An upright diamond one unit high, its lower tip at the origin, a normal per facet.
    /// </summary>
    private static (float[] Vertices, ushort[] Indices) Diamond()
    {
        const float waist = 0.55f;
        const float radius = 0.32f;
        Vector3 bottom = Vector3.Zero;
        Vector3 top = new Vector3(0, 1, 0);
        Vector3[] ring = [new Vector3(radius, waist, 0), new Vector3(0, waist, radius), new Vector3(-radius, waist, 0), new Vector3(0, waist, -radius)];
        List<float> vertices = [];
        List<ushort> indices = [];
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = ring[i];
            Vector3 b = ring[(i + 1) % 4];
            foreach ((Vector3 p, Vector3 q, Vector3 r) in new[] { (top, b, a), (bottom, a, b) })
            {
                Vector3 normal = Vector3.Normalize(Vector3.Cross(q - p, r - p));
                if (Vector3.Dot(normal, (p + q + r) / 3 - new Vector3(0, waist, 0)) < 0)
                {
                    normal = -normal;
                }
                foreach (Vector3 corner in new[] { p, q, r })
                {
                    indices.Add((ushort)(vertices.Count / 8));
                    vertices.AddRange([corner.X, corner.Y, corner.Z, normal.X, normal.Y, normal.Z, 0, 0]);
                }
            }
        }
        return ([.. vertices], [.. indices]);
    }

    #endregion

    public void Dispose()
    {
        _textures.Dispose();
        _model.Dispose();
        _marker.Dispose();
        _box.Dispose();
        _diamond.Dispose();
        foreach (GpuMesh mesh in _meshes.Values)
        {
            mesh.Dispose();
        }
        _meshes.Clear();
        _props.Dispose();
        _units.Dispose();
        _gl.DeleteBuffer(_markerInstances);
        _gl.DeleteSampler(_sampler);
        _gl.DeleteTexture(_white);
    }

    /// <summary>
    /// A run of instances with one blueprint and level of detail: one draw.
    /// </summary>
    private sealed record Run(string Blueprint, int Lod, int First, int Count);

    /// <summary>
    /// The placements of the objects of one kind, grouped by blueprint, made again when what they
    /// are made of changes; and on the card, per frame, the ones the camera sees, in runs by
    /// blueprint and level of detail.
    /// </summary>
    private sealed class Instances(GL gl) : IDisposable
    {
        private sealed class Group(string blueprint)
        {
            public string Blueprint { get; } = blueprint;

            // sixteen floats a placement: its four rows, the first row's fourth the highlight
            public List<float> Building { get; } = [];

            public float[] Rows { get; set; } = [];

            public int Count => Rows.Length / 16;

            // the sphere around each instance, for the bounds it was worked out for
            public Vector3[] Centers { get; private set; } = [];

            public float[] Radii { get; private set; } = [];

            private ObjectBounds? _spheresOf;

            /// <summary>
            /// Works out the sphere around each instance again when the model's bounds changed
            /// (the model was read).
            /// </summary>
            public void Spheres(ObjectBounds bounds)
            {
                if (_spheresOf == bounds)
                {
                    return;
                }
                _spheresOf = bounds;
                Vector3 center = (bounds.Min + bounds.Max) / 2;
                float radius = (bounds.Max - bounds.Min).Length() / 2;
                Centers = new Vector3[Count];
                Radii = new float[Count];
                for (int i = 0; i < Count; i++)
                {
                    int r = i * 16;
                    Vector3 x = new Vector3(Rows[r], Rows[r + 1], Rows[r + 2]);
                    Vector3 y = new Vector3(Rows[r + 4], Rows[r + 5], Rows[r + 6]);
                    Vector3 z = new Vector3(Rows[r + 8], Rows[r + 9], Rows[r + 10]);
                    Centers[i] = center.X * x + center.Y * y + center.Z * z + new Vector3(Rows[r + 12], Rows[r + 13], Rows[r + 14]);
                    Radii[i] = radius * MathF.Sqrt(Math.Max(x.LengthSquared(), Math.Max(y.LengthSquared(), z.LengthSquared())));
                }
            }
        }

        private readonly List<Group> _groups = [];
        private object? _of;
        private object? _arrangedFor;
        private float[] _data = new float[16];
        private int _capacity;

        public uint Buffer { get; } = gl.GenBuffer();

        public List<Run> Runs { get; } = [];

        /// <param name="of">What the instances are made of; they are made again when it changes.</param>
        /// <param name="objects">Each object's blueprint, placement, highlight and whether it is a wreck (0 or 1).</param>
        public void Update(object of, Func<IEnumerable<(string Blueprint, Matrix4x4 Placement, float Highlighted, float Wreck)>> objects)
        {
            if (Equals(of, _of))
            {
                return;
            }
            _of = of;
            _arrangedFor = null;
            using Performance.Span measure = Performance.Measure("3d.update-objects");
            Dictionary<string, Group> byBlueprint = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            _groups.Clear();
            foreach ((string blueprint, Matrix4x4 m, float highlighted, float wreck) in objects())
            {
                if (!byBlueprint.TryGetValue(blueprint, out Group? group))
                {
                    group = new Group(blueprint);
                    byBlueprint[blueprint] = group;
                    _groups.Add(group);
                }
                group.Building.AddRange([m.M11, m.M12, m.M13, highlighted, m.M21, m.M22, m.M23, wreck, m.M31, m.M32, m.M33, 0, m.M41, m.M42, m.M43, 1]);
            }
            foreach (Group group in _groups)
            {
                group.Rows = [.. group.Building];
                group.Building.Clear();
            }
        }

        /// <summary>
        /// Puts on the card the instances inside the view, each with the level of detail its
        /// distance to the eye asks for; again only when the camera, the instances or the models
        /// read changed.
        /// </summary>
        /// <param name="modelOf">A blueprint's model, or null while there is none (a box, or nothing yet).</param>
        /// <param name="modelsRead">How many models are read: another number arranges them again.</param>
        public void Arrange(Func<string, ObjectModels.Model?> modelOf, Matrix4x4 viewProjection, Vector3 eye, int modelsRead)
        {
            object key = (viewProjection, eye, modelsRead);
            if (Equals(key, _arrangedFor))
            {
                return;
            }
            _arrangedFor = key;
            using Performance.Span measure = Performance.Measure("3d.arrange-objects");
            Vector4[] planes = FrustumOf(viewProjection);
            Runs.Clear();
            int total = _groups.Sum(group => group.Count);
            if (_data.Length < total * 16)
            {
                _data = new float[Math.Max(16, total * 16)];
            }
            int at = 0;
            List<int>[] byLod = [];
            foreach (Group group in _groups)
            {
                ObjectModels.Model? model = modelOf(group.Blueprint);
                group.Spheres(model?.Bounds ?? ObjectBounds.Box);
                int levels = model?.Lods.Length ?? 1;
                if (byLod.Length < levels)
                {
                    byLod = [.. Enumerable.Range(0, levels).Select(_ => new List<int>())];
                }
                for (int level = 0; level < levels; level++)
                {
                    byLod[level].Clear();
                }

                float[] rows = group.Rows;
                Vector3[] centers = group.Centers;
                float[] radii = group.Radii;
                for (int i = 0; i < centers.Length; i++)
                {
                    if (Inside(planes, centers[i], radii[i]))
                    {
                        byLod[model?.LodAt(Vector3.Distance(eye, centers[i])) ?? 0].Add(i * 16);
                    }
                }
                for (int level = 0; level < levels; level++)
                {
                    List<int> chosen = byLod[level];
                    if (chosen.Count == 0)
                    {
                        continue;
                    }
                    Runs.Add(new Run(group.Blueprint, level, at / 16, chosen.Count));
                    foreach (int r in chosen)
                    {
                        rows.AsSpan(r, 16).CopyTo(_data.AsSpan(at));
                        at += 16;
                    }
                }
            }

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, Buffer);
            if (_capacity < _data.Length)
            {
                gl.BufferData<float>(BufferTargetARB.ArrayBuffer, _data, BufferUsageARB.DynamicDraw);
                _capacity = _data.Length;
            }
            else if (at > 0)
            {
                gl.BufferSubData<float>(BufferTargetARB.ArrayBuffer, 0, _data.AsSpan(0, at));
            }
        }

        /// <summary>
        /// The six planes of what the camera sees (left, right, bottom, top, near, far), each as a
        /// normal pointing inwards and a distance, normalized.
        /// </summary>
        private static Vector4[] FrustumOf(Matrix4x4 m)
        {
            Vector4 column1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
            Vector4 column2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
            Vector4 column3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
            Vector4 column4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
            Vector4[] planes = [column4 + column1, column4 - column1, column4 + column2, column4 - column2, column3, column4 - column3];
            for (int i = 0; i < planes.Length; i++)
            {
                float length = new Vector3(planes[i].X, planes[i].Y, planes[i].Z).Length();
                planes[i] = length > 0 ? planes[i] / length : planes[i];
            }
            return planes;
        }

        private static bool Inside(Vector4[] planes, Vector3 center, float radius)
        {
            foreach (Vector4 plane in planes)
            {
                if (plane.X * center.X + plane.Y * center.Y + plane.Z * center.Z + plane.W < -radius)
                {
                    return false;
                }
            }
            return true;
        }

        public void Dispose() => gl.DeleteBuffer(Buffer);
    }

    /// <summary>
    /// A model on the card: eight floats a vertex (position, normal, texture coordinates) and
    /// 16-bit indices.
    /// </summary>
    private sealed class GpuMesh : IDisposable
    {
        private readonly GL _gl;
        private readonly uint _vao;
        private readonly uint _vertices;
        private readonly uint _indices;

        public GpuMesh(GL gl, float[] vertices, ushort[] indices)
        {
            _gl = gl;
            _vao = gl.GenVertexArray();
            _vertices = gl.GenBuffer();
            _indices = gl.GenBuffer();
            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
            gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);
            const uint stride = 8 * sizeof(float);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, null);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indices);
            gl.BufferData<ushort>(BufferTargetARB.ElementArrayBuffer, indices, BufferUsageARB.StaticDraw);
            gl.BindVertexArray(0);
            IndexCount = indices.Length;
        }

        public int IndexCount { get; }

        /// <summary>
        /// The model it was made of.
        /// </summary>
        public ObjectModels.Model? Of { get; init; }

        public void Bind() => _gl.BindVertexArray(_vao);

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vertices);
            _gl.DeleteBuffer(_indices);
        }
    }
}
