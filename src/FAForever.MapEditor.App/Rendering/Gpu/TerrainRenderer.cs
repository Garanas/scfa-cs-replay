using FAForever.MapEditor.App.Diagnostics;
using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// Draws a map with OpenGL: the terrain with its layers, then the albedo decals, then the normal
/// decals. It keeps on the card what it drew last and updates only what a new version of the
/// document changed: the tiles of the heightmap and masks a brush touched, the decals when the
/// list changed. Textures come from a <see cref="GpuTextureCache"/>, one per file.
/// </summary>
internal sealed unsafe class TerrainRenderer : IDisposable
{
    // the grid of the terrain has at most this many cells along a side; the heights are sampled per pixel for the light
    private const int MaxGridCells = 1024;

    // decal grids by size class: small decals need few cells, large ones many to follow the ground
    private static readonly int[] DecalGridCells = [8, 32, 96];

    // corner, edge u, edge v: six floats per decal
    private const int DecalStride = 6 * sizeof(float);

    private readonly GL _gl;
    private readonly GpuTextureCache _textures;
    private readonly Program _terrain;
    private readonly Program _decalAlbedo;
    private readonly Program _decalNormal;
    private readonly Program _water;
    private readonly uint _repeat;
    private readonly uint _clamp;
    private readonly uint _masks;
    private readonly uint _white;
    private readonly uint _clear;

    private uint _heights;
    private TiledBuffer<ushort>? _heightsOf;
    private uint _mask0;
    private uint _mask1;
    private ImmutableArray<TiledBuffer<byte>> _masksOf;

    private readonly Mesh _grid;
    private readonly Mesh _waterQuad;
    private (int X, int Z) _gridCells;
    private readonly Mesh[] _decalGrids;
    private readonly uint _decalInstances;
    private ImmutableArray<Decal> _decalsOf;
    private readonly List<DecalRun> _runs = [];

    private sealed record DecalRun(bool Normals, string Texture, int SizeClass, int First, int Count);

    public TerrainRenderer(GL gl, bool es, Action textureLoaded)
    {
        _gl = gl;
        _textures = new GpuTextureCache(gl, textureLoaded);
        _terrain = new Program(gl, TerrainShaders.TerrainVertex(es), TerrainShaders.TerrainFragment(es));
        _decalAlbedo = new Program(gl, TerrainShaders.DecalVertex(es), TerrainShaders.DecalAlbedoFragment(es));
        _decalNormal = new Program(gl, TerrainShaders.DecalVertex(es), TerrainShaders.DecalNormalFragment(es));
        _water = new Program(gl, TerrainShaders.WaterVertex(es), TerrainShaders.WaterFragment(es));

        _repeat = Sampler(GLEnum.Repeat);
        _clamp = Sampler(GLEnum.ClampToEdge);
        // the masks have no mipmaps, and a mipmapped filter on a texture without them reads as black
        _masks = Sampler(GLEnum.ClampToEdge, mipmaps: false);
        _white = gl.GenTexture();
        GpuTextureCache.UploadRgba(gl, _white, 1, 1, [255, 255, 255, 255], mipmaps: false);
        _clear = gl.GenTexture();
        GpuTextureCache.UploadRgba(gl, _clear, 1, 1, [0, 0, 0, 0], mipmaps: false);

        _grid = new Mesh(gl);
        _waterQuad = new Mesh(gl);
        _waterQuad.SetGrid(1, 1);
        _decalGrids = [.. DecalGridCells.Select(cells =>
        {
            Mesh mesh = new Mesh(gl);
            mesh.SetGrid(cells, cells);
            return mesh;
        })];
        _decalInstances = gl.GenBuffer();
    }

    /// <summary>
    /// The textures on the card, and the last one that could not be read.
    /// </summary>
    public (int Textures, string? Error) TextureState => (_textures.Count, _textures.LastError);

    /// <summary>
    /// The decals drawn, the draws they take (a run of decals with one texture is one draw) and
    /// the textures they use.
    /// </summary>
    public (int Decals, int Draws, int Textures) DecalState =>
        (_runs.Sum(run => run.Count), _runs.Count, _runs.Select(run => run.Texture.ToLowerInvariant()).Distinct().Count());

    /// <summary>
    /// What the terrain shows: 0 the picture, 1 the albedo, 2 the light, 3 the normals, 4 the heights.
    /// </summary>
    public int Debug { get; set; }

    public void Render(MapDocument document, Matrix4x4 viewProjection, Func<string, TextureSource?> findTexture)
    {
        using (Performance.Measure("3d.upload-textures"))
        {
            _textures.Upload();
        }
        using (Performance.Measure("3d.update-buffers"))
        {
            Update(document);
        }

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        using (Performance.Measure("3d.draw-terrain"))
        {
            DrawTerrain(document, viewProjection, findTexture);
        }

        // decals lie on the terrain: no depth writes, and pulled towards the camera so the ground does not hide them
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(-1, -4);
        _gl.Enable(EnableCap.Blend);
        using (Performance.Measure("3d.draw-decals"))
        {
            DrawDecals(document, viewProjection, findTexture, normals: false);
            DrawDecals(document, viewProjection, findTexture, normals: true);
        }
        _gl.Disable(EnableCap.PolygonOffsetFill);
        DrawWater(document, viewProjection);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);

        for (int unit = 0; unit < 16; unit++)
        {
            _gl.BindSampler((uint)unit, 0);
        }
        _textures.Sweep();
    }

    private void DrawTerrain(MapDocument document, Matrix4x4 viewProjection, Func<string, TextureSource?> findTexture)
    {
        Program program = _terrain;
        program.Use();
        SetCommon(program, document, viewProjection);
        program.Set("uMapSize", document.Width, document.Height);
        program.Set("uDebug", Debug);

        ImmutableArray<TerrainLayer> layers = document.Terrain.Layers;
        int last = layers.Length - 1;
        (string Name, int Layer)[] slots =
        [
            ("uLower", 0), ("uStratum1", 1), ("uStratum2", 2), ("uStratum3", 3), ("uStratum4", 4),
            ("uStratum5", 5), ("uStratum6", 6), ("uStratum7", 7), ("uStratum8", 8), ("uUpper", last),
        ];
        float[] scales = new float[10];
        for (int slot = 0; slot < slots.Length; slot++)
        {
            // the strata lie between the lower and the upper layer: four in version 53, eight after
            int layer = slots[slot].Layer;
            bool exists = slot == 0 ? layers.Length > 0 : slot == 9 ? last > 0 : layer < last;
            uint fallback = slot == 9 ? _clear : _white;
            uint texture = exists ? _textures.Get(findTexture(layers[layer].AlbedoPath), fallback) : fallback;
            // unit 0 holds the heights (SetCommon), 1 the decal texture
            Bind(2 + slot, texture, _repeat);
            program.Set(slots[slot].Name, 2 + slot);
            scales[slot] = exists && layers[layer].AlbedoScale > 0 ? layers[layer].AlbedoScale : 4;
        }
        program.Set("uScalesA", scales[0], scales[1], scales[2], scales[3]);
        program.Set("uScalesB", scales[4], scales[5], scales[6], scales[7]);
        program.Set("uScalesC", scales[8], scales[9]);

        Bind(12, _mask0, _masks);
        program.Set("uMask0", 12);
        Bind(13, _mask1, _masks);
        program.Set("uMask1", 13);

        _grid.Draw();
    }

    private void DrawWater(MapDocument document, Matrix4x4 viewProjection)
    {
        Water water = document.Water;
        if (!water.HasWater)
        {
            return;
        }
        Program program = _water;
        program.Use();
        program.Set("uViewProjection", viewProjection);
        Bind(0, _heights, 0);
        program.Set("uHeights", 0);
        program.Set("uHeightScale", document.Terrain.Heightmap.Scale);
        program.Set("uMapSize", document.Width, document.Height);
        program.Set("uElevation", water.Elevation);
        program.Set("uWater", water.Elevation, water.ElevationAbyss);
        program.Set("uWaterColor", water.SurfaceColor.X, water.SurfaceColor.Y, water.SurfaceColor.Z);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _waterQuad.Draw();
    }

    private void DrawDecals(MapDocument document, Matrix4x4 viewProjection, Func<string, TextureSource?> findTexture, bool normals)
    {
        Program program = normals ? _decalNormal : _decalAlbedo;
        program.Use();
        SetCommon(program, document, viewProjection);
        program.Set("uTexture", 1);
        if (normals)
        {
            // 2 × source × destination: the shader outputs half the factor, so it can brighten too
            _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
        }
        else
        {
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        }

        foreach (DecalRun run in _runs)
        {
            if (run.Normals != normals)
            {
                continue;
            }
            uint texture = _textures.Get(findTexture(run.Texture), _clear);
            if (texture == _clear)
            {
                continue;
            }
            Bind(1, texture, _clamp);
            Mesh mesh = _decalGrids[run.SizeClass];
            mesh.Bind();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _decalInstances);
            nint offset = run.First * DecalStride;
            for (uint attribute = 1; attribute <= 3; attribute++)
            {
                _gl.EnableVertexAttribArray(attribute);
                _gl.VertexAttribPointer(attribute, 2, VertexAttribPointerType.Float, false, DecalStride, (void*)(offset + (attribute - 1) * 2 * sizeof(float)));
                _gl.VertexAttribDivisor(attribute, 1);
            }
            _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)mesh.IndexCount, DrawElementsType.UnsignedInt, null, (uint)run.Count);
        }
    }

    private void SetCommon(Program program, MapDocument document, Matrix4x4 viewProjection)
    {
        program.Set("uViewProjection", viewProjection);
        Bind(0, _heights, 0);
        program.Set("uHeights", 0);
        program.Set("uHeightScale", document.Terrain.Heightmap.Scale);

        Lighting lighting = document.Lighting;
        Vector3 sun = lighting.SunDirection.LengthSquared() > 0 ? Vector3.Normalize(lighting.SunDirection) : Vector3.UnitY;
        program.Set("uSunDirection", sun.X, sun.Y, sun.Z);
        program.Set("uSunColor", lighting.SunColor.X, lighting.SunColor.Y, lighting.SunColor.Z);
        program.Set("uSunAmbience", lighting.SunAmbience.X, lighting.SunAmbience.Y, lighting.SunAmbience.Z);
        program.Set("uShadowFill", lighting.ShadowFillColor.X, lighting.ShadowFillColor.Y, lighting.ShadowFillColor.Z);
        program.Set("uLightMultiplier", lighting.Multiplier);
    }

    private void Bind(int unit, uint texture, uint sampler)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.BindSampler((uint)unit, sampler);
    }

    #region Keeping the card up to date

    private void Update(MapDocument document)
    {
        UpdateHeights(document.Terrain.Heightmap.Samples);
        UpdateMasks(document.Terrain.StratumMasks.Layers);

        int cellsX = Math.Min(MaxGridCells, document.Width);
        int cellsZ = Math.Min(MaxGridCells, document.Height);
        if (_gridCells != (cellsX, cellsZ))
        {
            _grid.SetGrid(cellsX, cellsZ);
            _gridCells = (cellsX, cellsZ);
        }

        if (_decalsOf != document.Decals)
        {
            UpdateDecals(document.Decals);
            _decalsOf = document.Decals;
        }
    }

    /// <summary>
    /// The heights as 16-bit integers, read with <c>texelFetch</c>: all of them for another
    /// heightmap, else the tiles that differ.
    /// </summary>
    private void UpdateHeights(TiledBuffer<ushort> heights)
    {
        using Performance.Span measure = Performance.Measure("3d.update-heights");
        if (ReferenceEquals(heights, _heightsOf))
        {
            return;
        }
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        if (_heightsOf is null || _heightsOf.Width != heights.Width || _heightsOf.Height != heights.Height)
        {
            if (_heights != 0)
            {
                _gl.DeleteTexture(_heights);
            }
            _heights = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _heights);
            ushort[] all = heights.ToArray();
            _gl.TexImage2D<ushort>(TextureTarget.Texture2D, 0, InternalFormat.R16ui, (uint)heights.Width, (uint)heights.Height, 0, PixelFormat.RedInteger, PixelType.UnsignedShort, all);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        }
        else
        {
            _gl.BindTexture(TextureTarget.Texture2D, _heights);
            foreach ((int tileX, int tileY) in heights.ChangedTiles(_heightsOf))
            {
                (int x, int y, int width, int height) = TileBounds(heights.Width, heights.Height, tileX, tileY);
                ushort[] tile = new ushort[width * height];
                for (int row = 0; row < height; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        tile[row * width + column] = heights[x + column, y + row];
                    }
                }
                _gl.TexSubImage2D<ushort>(TextureTarget.Texture2D, 0, x, y, (uint)width, (uint)height, PixelFormat.RedInteger, PixelType.UnsignedShort, tile);
            }
        }
        _heightsOf = heights;
    }

    /// <summary>
    /// The masks as two colour textures, four layers each (as the game keeps them).
    /// </summary>
    private void UpdateMasks(ImmutableArray<TiledBuffer<byte>> masks)
    {
        using Performance.Span measure = Performance.Measure("3d.update-masks");
        if (masks == _masksOf)
        {
            return;
        }
        int width = masks.Length > 0 ? masks[0].Width : 1;
        int height = masks.Length > 0 ? masks[0].Height : 1;
        bool whole = _masksOf.IsDefault || _masksOf.Length != masks.Length || _masksOf.Length == 0 || _masksOf[0].Width != width || _masksOf[0].Height != height;

        for (int half = 0; half < 2; half++)
        {
            ref uint texture = ref half == 0 ? ref _mask0 : ref _mask1;
            if (whole)
            {
                if (texture != 0)
                {
                    _gl.DeleteTexture(texture);
                }
                texture = _gl.GenTexture();
                GpuTextureCache.UploadRgba(_gl, texture, width, height, Interleave(masks, half, 0, 0, width, height), mipmaps: false);
                continue;
            }

            HashSet<(int, int)> tiles = [];
            for (int layer = half * 4; layer < Math.Min(masks.Length, half * 4 + 4); layer++)
            {
                if (!ReferenceEquals(masks[layer], _masksOf[layer]))
                {
                    tiles.UnionWith(masks[layer].ChangedTiles(_masksOf[layer]));
                }
            }
            _gl.BindTexture(TextureTarget.Texture2D, texture);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            foreach ((int tileX, int tileY) in tiles)
            {
                (int x, int y, int w, int h) = TileBounds(width, height, tileX, tileY);
                _gl.TexSubImage2D<byte>(TextureTarget.Texture2D, 0, x, y, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, Interleave(masks, half, x, y, w, h));
            }
        }
        _masksOf = masks;
    }

    private static byte[] Interleave(ImmutableArray<TiledBuffer<byte>> masks, int half, int x, int y, int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int channel = 0; channel < 4; channel++)
        {
            int layer = half * 4 + channel;
            if (layer >= masks.Length)
            {
                continue;
            }
            TiledBuffer<byte> mask = masks[layer];
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    rgba[(row * width + column) * 4 + channel] = mask[x + column, y + row];
                }
            }
        }
        return rgba;
    }

    private static (int X, int Y, int Width, int Height) TileBounds(int width, int height, int tileX, int tileY)
    {
        int x = tileX * TiledBuffer<byte>.TileSize;
        int y = tileY * TiledBuffer<byte>.TileSize;
        return (x, y, Math.Min(TiledBuffer<byte>.TileSize, width - x), Math.Min(TiledBuffer<byte>.TileSize, height - y));
    }

    /// <summary>
    /// The decals as instances, in the map's order (it decides which lies on top), split into runs
    /// that share a texture and size class: each run is one draw.
    /// </summary>
    /// <remarks>
    /// A decal's position is a corner of its rectangle; it turns around that corner by its
    /// rotation about the vertical axis (the FAF map editor's <c>OzoneDecal.PivotPointLocal</c>).
    /// </remarks>
    private void UpdateDecals(ImmutableArray<Decal> decals)
    {
        using Performance.Span measure = Performance.Measure("3d.update-decals");
        _runs.Clear();
        List<float> instances = [];
        foreach (Decal decal in decals)
        {
            bool normals = decal.Type is DecalType.Normals or DecalType.AlphaNormals;
            if ((decal.Type != DecalType.Albedo && !normals) || decal.Textures.IsDefaultOrEmpty || decal.Textures[0].Length == 0)
            {
                continue;
            }
            (float sin, float cos) = MathF.SinCos(decal.Rotation.Y);
            float size = Math.Max(Math.Abs(decal.Scale.X), Math.Abs(decal.Scale.Z));
            int sizeClass = size <= 24 ? 0 : size <= 128 ? 1 : 2;
            int index = instances.Count / 6;
            instances.AddRange([decal.Position.X, decal.Position.Z, decal.Scale.X * cos, decal.Scale.X * sin, -decal.Scale.Z * sin, decal.Scale.Z * cos]);

            string texture = decal.Textures[0];
            if (_runs.Count > 0 && _runs[^1] is DecalRun last && last.Normals == normals && last.SizeClass == sizeClass && string.Equals(last.Texture, texture, StringComparison.OrdinalIgnoreCase))
            {
                _runs[^1] = last with { Count = last.Count + 1 };
            }
            else
            {
                _runs.Add(new DecalRun(normals, texture, sizeClass, index, 1));
            }
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _decalInstances);
        float[] data = instances.Count > 0 ? [.. instances] : [0f];
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data, BufferUsageARB.StaticDraw);
    }

    private uint Sampler(GLEnum wrap, bool mipmaps = true)
    {
        uint sampler = _gl.GenSampler();
        _gl.SamplerParameter(sampler, SamplerParameterI.WrapS, (int)wrap);
        _gl.SamplerParameter(sampler, SamplerParameterI.WrapT, (int)wrap);
        _gl.SamplerParameter(sampler, SamplerParameterI.MinFilter, (int)(mipmaps ? GLEnum.LinearMipmapLinear : GLEnum.Linear));
        _gl.SamplerParameter(sampler, SamplerParameterI.MagFilter, (int)GLEnum.Linear);
        return sampler;
    }

    #endregion

    public void Dispose()
    {
        _textures.Dispose();
        _terrain.Dispose();
        _decalAlbedo.Dispose();
        _decalNormal.Dispose();
        _water.Dispose();
        _waterQuad.Dispose();
        _grid.Dispose();
        foreach (Mesh mesh in _decalGrids)
        {
            mesh.Dispose();
        }
        _gl.DeleteBuffer(_decalInstances);
        _gl.DeleteSampler(_repeat);
        _gl.DeleteSampler(_clamp);
        _gl.DeleteSampler(_masks);
        _gl.DeleteTexture(_white);
        _gl.DeleteTexture(_clear);
        _gl.DeleteTexture(_heights);
        _gl.DeleteTexture(_mask0);
        _gl.DeleteTexture(_mask1);
    }

    /// <summary>
    /// A square grid of cells over 0 to 1, as positions and two triangles per cell.
    /// </summary>
    private sealed class Mesh(GL gl) : IDisposable
    {
        private readonly uint _vao = gl.GenVertexArray();
        private readonly uint _vertices = gl.GenBuffer();
        private readonly uint _indices = gl.GenBuffer();

        public int IndexCount { get; private set; }

        public void SetGrid(int cellsX, int cellsZ)
        {
            float[] positions = new float[(cellsX + 1) * (cellsZ + 1) * 2];
            int i = 0;
            for (int z = 0; z <= cellsZ; z++)
            {
                for (int x = 0; x <= cellsX; x++)
                {
                    positions[i++] = (float)x / cellsX;
                    positions[i++] = (float)z / cellsZ;
                }
            }
            uint[] indices = new uint[cellsX * cellsZ * 6];
            i = 0;
            for (int z = 0; z < cellsZ; z++)
            {
                for (int x = 0; x < cellsX; x++)
                {
                    uint corner = (uint)(z * (cellsX + 1) + x);
                    uint below = corner + (uint)(cellsX + 1);
                    indices[i++] = corner;
                    indices[i++] = corner + 1;
                    indices[i++] = below;
                    indices[i++] = corner + 1;
                    indices[i++] = below + 1;
                    indices[i++] = below;
                }
            }

            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
            gl.BufferData<float>(BufferTargetARB.ArrayBuffer, positions, BufferUsageARB.StaticDraw);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), null);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indices);
            gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, indices, BufferUsageARB.StaticDraw);
            gl.BindVertexArray(0);
            IndexCount = indices.Length;
        }

        public void Bind() => gl.BindVertexArray(_vao);

        public void Draw()
        {
            gl.BindVertexArray(_vao);
            gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount, DrawElementsType.UnsignedInt, null);
            gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            gl.DeleteVertexArray(_vao);
            gl.DeleteBuffer(_vertices);
            gl.DeleteBuffer(_indices);
        }
    }

    /// <summary>
    /// A linked pair of shaders and its uniforms by name.
    /// </summary>
    private sealed class Program : IDisposable
    {
        private readonly GL _gl;
        private readonly uint _id;
        private readonly Dictionary<string, int> _locations = new Dictionary<string, int>(StringComparer.Ordinal);

        public Program(GL gl, string vertex, string fragment)
        {
            _gl = gl;
            uint vs = Compile(ShaderType.VertexShader, vertex);
            uint fs = Compile(ShaderType.FragmentShader, fragment);
            _id = gl.CreateProgram();
            gl.AttachShader(_id, vs);
            gl.AttachShader(_id, fs);
            gl.LinkProgram(_id);
            gl.GetProgram(_id, ProgramPropertyARB.LinkStatus, out int linked);
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);
            if (linked == 0)
            {
                throw new InvalidOperationException($"Linking a shader program failed: {gl.GetProgramInfoLog(_id)}");
            }
        }

        private uint Compile(ShaderType type, string source)
        {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);
            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
            if (compiled == 0)
            {
                throw new InvalidOperationException($"The {type} did not compile: {_gl.GetShaderInfoLog(shader)}");
            }
            return shader;
        }

        public void Use() => _gl.UseProgram(_id);

        private int Location(string name)
        {
            if (!_locations.TryGetValue(name, out int location))
            {
                location = _gl.GetUniformLocation(_id, name);
                _locations[name] = location;
            }
            return location;
        }

        public void Set(string name, int value) => _gl.Uniform1(Location(name), value);

        public void Set(string name, float value) => _gl.Uniform1(Location(name), value);

        public void Set(string name, float x, float y) => _gl.Uniform2(Location(name), x, y);

        public void Set(string name, float x, float y, float z) => _gl.Uniform3(Location(name), x, y, z);

        public void Set(string name, float x, float y, float z, float w) => _gl.Uniform4(Location(name), x, y, z, w);

        public void Set(string name, Matrix4x4 matrix) => _gl.UniformMatrix4(Location(name), 1, false, &matrix.M11);

        public void Dispose() => _gl.DeleteProgram(_id);
    }
}
