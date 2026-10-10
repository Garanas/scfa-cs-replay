using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// What the 3D view lays over the terrain in a frame (user story V-3); colours with their opacity.
/// </summary>
/// <param name="Walkability">Where units of the shown layer cannot go and what changed, one
/// premultiplied colour (<c>0xAARRGGBB</c>) an ogrid, made again only when the result changes;
/// null for none. <paramref name="WalkabilityOf"/> names it.</param>
/// <param name="Grid">The grid being painted or the mask for tools; null for none.</param>
/// <param name="GridPalette">A colour per code for a grid of codes (the terrain types); null for a
/// mask, shown in <paramref name="MaskColor"/> as strong as it is full.</param>
/// <param name="Areas">The areas, the first the playable area; null when the overlay is off.</param>
/// <param name="AreaStates">Per area: 0, 1 selected, 2 under the mouse.</param>
/// <param name="Guides">The symmetry's guides; empty for none.</param>
/// <param name="Eye">Where the camera is, and <paramref name="PixelScale"/> the ogrids a pixel
/// covers an ogrid from it: the lines keep their width on screen.</param>
internal sealed record TerrainOverlays(
    object? WalkabilityOf,
    Func<uint[]>? Walkability,
    int WalkabilityWidth,
    int WalkabilityHeight,
    TiledBuffer<byte>? Grid,
    IReadOnlyList<uint>? GridPalette,
    Vector4 MaskColor,
    IReadOnlyList<Area>? Areas,
    IReadOnlyList<float> AreaStates,
    IReadOnlyList<(Vector2 From, Vector2 To)> Guides,
    Vector4 AreaColor,
    Vector4 SelectionColor,
    Vector4 HoverColor,
    Vector4 OutsideColor,
    Vector4 GuideColor,
    Vector4 ShadowColor,
    Vector3 Eye,
    float PixelScale)
{
    public bool IsEmpty => Walkability is null && Grid is null && (Areas is null || Areas.Count == 0) && Guides.Count == 0;
}

/// <summary>
/// Draws a map with OpenGL: the terrain with its layers, the props and units
/// (<see cref="ObjectRenderer"/>), then the albedo decals, then the normal decals, the water, the
/// markers, and the active tool's brush under the mouse. It keeps on the card what it drew last and updates only what a new version of the
/// document changed: the tiles of the heightmap and masks a brush touched, the decals when the
/// list changed. Textures come from a <see cref="GpuTextureCache"/>, one per file.
/// </summary>
internal sealed unsafe class TerrainRenderer : IDisposable
{
    // the grid of the terrain has at most this many cells along a side; the heights are sampled per pixel for the light
    private const int MaxGridCells = 1024;

    // decal patches of the terrain's lattice by size class, in cells a side: the smallest that
    // covers a decal; one wider than the largest takes every second (or more) corner
    private static readonly int[] DecalGridCells = [4, 16, 64, 256];

    // corner, edge u, edge v: six floats per decal
    private const int DecalStride = 6 * sizeof(float);

    private readonly GL _gl;
    private readonly GpuTextureCache _textures;
    private readonly Program _terrain;
    private readonly Program _decalAlbedo;
    private readonly Program _decalNormal;
    private readonly Program _decalHighlight;
    private readonly Program _water;
    private readonly Program _brush;
    private readonly Program _overlay;
    private uint _walkTexture;
    private object? _walkOf;
    private uint _gridTexture;
    private TiledBuffer<byte>? _gridOf;
    private IReadOnlyList<uint>? _gridPaletteOf;
    private Vector4 _gridColorOf;
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
    private readonly Mesh _brushGrid;
    private (int X, int Z) _brushCells;
    private uint _brushPicture;
    private BrushShape? _brushPictureOf;
    private (int X, int Z) _gridCells;
    private readonly Mesh[] _decalGrids;
    private readonly uint _decalInstances;
    private readonly uint _highlightInstances;
    private ImmutableArray<Decal> _decalsOf;
    private IReadOnlySet<int>? _hiddenOf;
    private IReadOnlySet<int>? _hidden;
    private readonly List<DecalRun> _runs = [];
    private ImmutableArray<Decal> _highlightOf;
    private IReadOnlySet<int>? _highlightedOf;
    private IReadOnlySet<int>? _highlighted;
    private readonly List<DecalRun> _highlightRuns = [];
    private (int Width, int Height) _mapSize;

    private sealed record DecalRun(bool Normals, string Texture, int SizeClass, int First, int Count);

    public TerrainRenderer(GL gl, bool es, Action textureLoaded)
    {
        _gl = gl;
        _textures = new GpuTextureCache(gl, textureLoaded);
        _terrain = new Program(gl, TerrainShaders.TerrainVertex(es), TerrainShaders.TerrainFragment(es));
        _decalAlbedo = new Program(gl, TerrainShaders.DecalVertex(es), TerrainShaders.DecalAlbedoFragment(es));
        _decalNormal = new Program(gl, TerrainShaders.DecalVertex(es), TerrainShaders.DecalNormalFragment(es));
        _decalHighlight = new Program(gl, TerrainShaders.DecalVertex(es), TerrainShaders.DecalHighlightFragment(es));
        _water = new Program(gl, TerrainShaders.WaterVertex(es), TerrainShaders.WaterFragment(es));
        _brush = new Program(gl, TerrainShaders.BrushVertex(es), TerrainShaders.BrushFragment(es));
        _overlay = new Program(gl, TerrainShaders.OverlayVertex(es), TerrainShaders.OverlayFragment(es));

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
        _brushGrid = new Mesh(gl);
        _decalGrids = [.. DecalGridCells.Select(cells =>
        {
            Mesh mesh = new Mesh(gl);
            mesh.SetGrid(cells, cells, corners: true);
            return mesh;
        })];
        _decalInstances = gl.GenBuffer();
        _highlightInstances = gl.GenBuffer();
    }

    /// <summary>
    /// The textures on the card, and the last one that could not be read.
    /// </summary>
    public (int Textures, string? Error) TextureState => (_textures.Count, _textures.LastError);

    /// <summary>
    /// The textures being read, not on the card yet.
    /// </summary>
    public int TexturesLoading => _textures.Loading;

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

    /// <param name="brushes">The active tool's brush under the mouse and its images in the other
    /// parts of a symmetric map; null or empty for none.</param>
    /// <param name="brushColor">Its colour, from the theme's overlay token.</param>
    /// <param name="objects">The props, units and markers, and what of them shows: the models after
    /// the terrain (decals do not cover them), the markers after the water; null for none.</param>
    /// <param name="decals">The ids of the decals not drawn (those of hidden groups) and of those
    /// drawn highlighted (the selection), with the highlight's colour; null for none.</param>
    public void Render(MapDocument document, Matrix4x4 viewProjection, Func<string, TextureSource?> findTexture, IReadOnlyList<BrushPreview>? brushes = null, Vector3 brushColor = default, (ObjectRenderer Renderer, ObjectFrame Frame)? objects = null,
        (IReadOnlySet<int>? Hidden, IReadOnlySet<int>? Highlighted, Vector3 Color)? decals = null, TerrainOverlays? overlays = null)
    {
        _hidden = decals?.Hidden;
        _highlighted = decals?.Highlighted;
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
        objects?.Renderer.DrawModels(document, viewProjection, objects.Value.Frame);

        // decals lie on the terrain: no depth writes, and pulled towards the camera so the ground does not hide them
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(-1, -4);
        _gl.Enable(EnableCap.Blend);
        using (Performance.Measure("3d.draw-decals"))
        {
            DrawDecals(document, viewProjection, findTexture, normals: false);
            DrawDecals(document, viewProjection, findTexture, normals: true);
            DrawHighlights(document, viewProjection, decals?.Color ?? Vector3.One);
        }
        _gl.Disable(EnableCap.PolygonOffsetFill);
        DrawWater(document, viewProjection);
        if (objects is { } shown)
        {
            // the symbols are solid: they hide what is behind them, the brush included
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
            shown.Renderer.DrawMarkers(document, viewProjection, shown.Frame);
            _gl.DepthMask(false);
            _gl.Enable(EnableCap.Blend);
        }
        if (overlays is { IsEmpty: false })
        {
            using (Performance.Measure("3d.draw-overlays"))
            {
                // on the terrain's own lattice, made invariantly as the terrain's: the same depth, which
                // the depth test (less or equal) passes without an offset, so what stands on the terrain
                // (trees far away included) stays in front; over the decals and the water
                DrawOverlays(document, viewProjection, overlays);
            }
        }
        if (brushes is { Count: > 0 })
        {
            using (Performance.Measure("3d.draw-brush"))
            {
                // over the decals and the water, pulled towards the camera so the ground does not hide it
                _gl.Enable(EnableCap.PolygonOffsetFill);
                _gl.PolygonOffset(-2, -16);
                foreach (BrushPreview brush in brushes)
                {
                    DrawBrush(document, viewProjection, brush, brushColor);
                }
                _gl.Disable(EnableCap.PolygonOffsetFill);
            }
        }
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
        program.Set("uCells", _gridCells.X, _gridCells.Z);
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

    private void DrawOverlays(MapDocument document, Matrix4x4 viewProjection, TerrainOverlays overlays)
    {
        // uploads bind to the active unit: the overlays' own
        _gl.ActiveTexture(TextureUnit.Texture15);
        UpdateWalkability(overlays);
        UpdateGrid(overlays);
        Program program = _overlay;
        program.Use();
        program.Set("uViewProjection", viewProjection);
        Bind(0, _heights, 0);
        program.Set("uHeights", 0);
        program.Set("uHeightScale", document.Terrain.Heightmap.Scale);
        program.Set("uMapSize", document.Width, document.Height);
        program.Set("uCells", _gridCells.X, _gridCells.Z);

        // unit 13 (a mask's for the terrain, bound again every frame) holds walkability, 15 the grid
        program.Set("uWalk", overlays.Walkability is not null && _walkTexture != 0 ? 1 : 0);
        Bind(13, _walkTexture, _masks);
        program.Set("uWalkTexture", 13);
        program.Set("uGrid", overlays.Grid is not null && _gridTexture != 0 ? 1 : 0);
        Bind(15, _gridTexture, _masks);
        program.Set("uGridTexture", 15);

        IReadOnlyList<Area> areas = overlays.Areas ?? [];
        int count = Math.Min(areas.Count, 32);
        program.Set("uAreaCount", count);
        for (int i = 0; i < count; i++)
        {
            program.Set($"uAreas[{i}]", (float)areas[i].X0, (float)areas[i].Z0, (float)areas[i].X1, (float)areas[i].Z1);
            program.Set($"uAreaStates[{i}]", i < overlays.AreaStates.Count ? overlays.AreaStates[i] : 0);
        }
        program.Set("uShade", count > 0 ? 1 : 0);
        Set(program, "uAreaColor", overlays.AreaColor);
        Set(program, "uSelectionColor", overlays.SelectionColor);
        Set(program, "uHoverColor", overlays.HoverColor);
        Set(program, "uOutside", overlays.OutsideColor);
        Set(program, "uGuideColor", overlays.GuideColor);
        Set(program, "uShadowColor", overlays.ShadowColor);
        int guides = Math.Min(overlays.Guides.Count, 8);
        program.Set("uEye", overlays.Eye.X, overlays.Eye.Y, overlays.Eye.Z);
        program.Set("uPixelScale", overlays.PixelScale);
        program.Set("uGuideCount", guides);
        for (int i = 0; i < guides; i++)
        {
            (Vector2 from, Vector2 to) = overlays.Guides[i];
            program.Set($"uGuides[{i}]", from.X, from.Y, to.X, to.Y);
        }
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _grid.Draw();
    }

    private static void Set(Program program, string name, Vector4 color) => program.Set(name, color.X, color.Y, color.Z, color.W);

    /// <summary>
    /// Walkability's colours on the card, again only for another result, layer or theme.
    /// </summary>
    private void UpdateWalkability(TerrainOverlays overlays)
    {
        if (overlays.Walkability is null || Equals(overlays.WalkabilityOf, _walkOf))
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("3d.update-walkability");
        uint[] pixels = overlays.Walkability();
        byte[] rgba = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            uint argb = pixels[i];
            rgba[i * 4] = (byte)(argb >> 16);
            rgba[i * 4 + 1] = (byte)(argb >> 8);
            rgba[i * 4 + 2] = (byte)argb;
            rgba[i * 4 + 3] = (byte)(argb >> 24);
        }
        if (_walkTexture == 0)
        {
            _walkTexture = _gl.GenTexture();
        }
        GpuTextureCache.UploadRgba(_gl, _walkTexture, overlays.WalkabilityWidth, overlays.WalkabilityHeight, rgba, mipmaps: false);
        _walkOf = overlays.WalkabilityOf;
    }

    /// <summary>
    /// The grid's colours on the card, premultiplied: all of them for another grid or palette, else
    /// the tiles a stroke changed.
    /// </summary>
    private void UpdateGrid(TerrainOverlays overlays)
    {
        if (overlays.Grid is not TiledBuffer<byte> grid || (ReferenceEquals(grid, _gridOf) && ReferenceEquals(overlays.GridPalette, _gridPaletteOf) && overlays.MaskColor == _gridColorOf))
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("3d.update-grid");
        bool whole = _gridTexture == 0 || _gridOf is null || _gridOf.Width != grid.Width || _gridOf.Height != grid.Height
            || !ReferenceEquals(overlays.GridPalette, _gridPaletteOf) || overlays.MaskColor != _gridColorOf;
        if (whole)
        {
            if (_gridTexture == 0)
            {
                _gridTexture = _gl.GenTexture();
            }
            GpuTextureCache.UploadRgba(_gl, _gridTexture, grid.Width, grid.Height, GridColours(grid, overlays, 0, 0, grid.Width, grid.Height), mipmaps: false);
        }
        else
        {
            _gl.BindTexture(TextureTarget.Texture2D, _gridTexture);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            foreach ((int tileX, int tileY) in grid.ChangedTiles(_gridOf!))
            {
                (int x, int y, int w, int h) = TileBounds(grid.Width, grid.Height, tileX, tileY);
                _gl.TexSubImage2D<byte>(TextureTarget.Texture2D, 0, x, y, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, GridColours(grid, overlays, x, y, w, h));
            }
        }
        _gridOf = grid;
        _gridPaletteOf = overlays.GridPalette;
        _gridColorOf = overlays.MaskColor;
    }

    private static byte[] GridColours(TiledBuffer<byte> grid, TerrainOverlays overlays, int x, int y, int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        IReadOnlyList<uint>? palette = overlays.GridPalette;
        Vector4 mask = overlays.MaskColor;
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                byte value = grid[x + column, y + row];
                int i = (row * width + column) * 4;
                float r, g, b, a;
                if (palette is not null)
                {
                    uint argb = palette[value];
                    a = (argb >> 24) / 255f * 0.6f;
                    r = ((argb >> 16) & 0xff) / 255f;
                    g = ((argb >> 8) & 0xff) / 255f;
                    b = (argb & 0xff) / 255f;
                }
                else
                {
                    a = value / 255f * 0.7f * mask.W;
                    (r, g, b) = (mask.X, mask.Y, mask.Z);
                }
                rgba[i] = (byte)(r * a * 255);
                rgba[i + 1] = (byte)(g * a * 255);
                rgba[i + 2] = (byte)(b * a * 255);
                rgba[i + 3] = (byte)(a * 255);
            }
        }
        return rgba;
    }

    private void DrawBrush(MapDocument document, Matrix4x4 viewProjection, BrushPreview brush, Vector3 color)
    {
        if (brush.Shape is BrushShape picture)
        {
            // before the heights are bound: uploading binds the picture to the active unit
            UpdateBrushPicture(picture);
        }
        Program program = _brush;
        program.Use();
        program.Set("uViewProjection", viewProjection);
        Bind(0, _heights, 0);
        program.Set("uHeights", 0);
        program.Set("uHeightScale", document.Terrain.Heightmap.Scale);
        program.Set("uMapSize", document.Width, document.Height);
        program.Set("uCells", _gridCells.X, _gridCells.Z);

        // the cells of the terrain's lattice under the square around the radius, turned clockwise
        // in the top view (x east, z south): its corners reach the radius times the root of two
        (float sin, float cos) = MathF.SinCos(brush.Shape is null ? 0 : brush.Rotation * MathF.PI / 180);
        float reach = brush.Radius * (brush.Shape is null ? 1 : MathF.Sqrt(2));
        float cellX = (float)document.Width / _gridCells.X;
        float cellZ = (float)document.Height / _gridCells.Z;
        int x0 = Math.Clamp((int)MathF.Floor((brush.X - reach) / cellX), 0, _gridCells.X);
        int x1 = Math.Clamp((int)MathF.Ceiling((brush.X + reach) / cellX), 0, _gridCells.X);
        int z0 = Math.Clamp((int)MathF.Floor((brush.Z - reach) / cellZ), 0, _gridCells.Z);
        int z1 = Math.Clamp((int)MathF.Ceiling((brush.Z + reach) / cellZ), 0, _gridCells.Z);
        if (x1 <= x0 || z1 <= z0)
        {
            return;
        }
        if (_brushCells != (x1 - x0, z1 - z0))
        {
            _brushCells = (x1 - x0, z1 - z0);
            _brushGrid.SetGrid(_brushCells.X, _brushCells.Z, corners: true);
        }
        program.Set("uOrigin", x0, z0);
        program.Set("uCenter", brush.X, brush.Z);
        program.Set("uRadius", brush.Radius);
        program.Set("uTurn", sin, cos);
        program.Set("uFlip", brush.Mirrored && brush.Shape is not null ? -1f : 1f);
        program.Set("uColor", color.X, color.Y, color.Z);

        if (brush.Shape is BrushShape shape)
        {
            Bind(14, _brushPicture, _clamp);
            float longest = Math.Max(shape.Width, shape.Height);
            program.Set("uShapeScale", longest / shape.Width, longest / shape.Height);
            program.Set("uRound", 0);
        }
        else
        {
            program.Set("uRound", 1);
        }
        program.Set("uShape", 14);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _brushGrid.Draw();
    }

    /// <summary>
    /// The brush's picture on the card, its value in every channel; again only for another shape.
    /// </summary>
    private void UpdateBrushPicture(BrushShape shape)
    {
        if (ReferenceEquals(shape, _brushPictureOf))
        {
            return;
        }
        if (_brushPicture == 0)
        {
            _brushPicture = _gl.GenTexture();
        }
        ReadOnlySpan<float> values = shape.Values;
        byte[] rgba = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            byte value = (byte)Math.Clamp(MathF.Round(values[i] * 255), 0, 255);
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = rgba[i * 4 + 3] = value;
        }
        GpuTextureCache.UploadRgba(_gl, _brushPicture, shape.Width, shape.Height, rgba, mipmaps: true);
        _brushPictureOf = shape;
    }

    private void DrawDecals(MapDocument document, Matrix4x4 viewProjection, Func<string, TextureSource?> findTexture, bool normals)
    {
        Program program = normals ? _decalNormal : _decalAlbedo;
        program.Use();
        SetCommon(program, document, viewProjection);
        program.Set("uMapSize", document.Width, document.Height);
        program.Set("uCells", _gridCells.X, _gridCells.Z);
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
            program.Set("uPatchCells", (float)DecalGridCells[run.SizeClass]);
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
        _mapSize = (document.Width, document.Height);

        int cellsX = Math.Min(MaxGridCells, document.Width);
        int cellsZ = Math.Min(MaxGridCells, document.Height);
        if (_gridCells != (cellsX, cellsZ))
        {
            _grid.SetGrid(cellsX, cellsZ, corners: true);
            _gridCells = (cellsX, cellsZ);
            // the decals' patches are counted in the lattice's cells
            _decalsOf = default;
        }

        if (_decalsOf != document.Decals || _hiddenOf != _hidden)
        {
            UpdateDecals(document.Decals);
            _decalsOf = document.Decals;
            _hiddenOf = _hidden;
            _highlightOf = default;
        }
        if (_highlightOf != document.Decals || _highlightedOf != _highlighted)
        {
            UpdateHighlights(document.Decals);
            _highlightOf = document.Decals;
            _highlightedOf = _highlighted;
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

    /// <summary>
    /// Four layers of masks as the channels of one picture, over a part of the map: tile by tile from
    /// the masks' own tiles (an 81 km map's masks are 16 million values a layer), the rows of
    /// tiles in parallel when the part is larger than a tile.
    /// </summary>
    private static byte[] Interleave(ImmutableArray<TiledBuffer<byte>> masks, int half, int x, int y, int width, int height)
    {
        const int size = TiledBuffer<byte>.TileSize;
        byte[] rgba = new byte[width * height * 4];
        int firstRow = y / size, lastRow = (y + height - 1) / size;
        int firstColumn = x / size, lastColumn = (x + width - 1) / size;
        void Row(int tileY)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                int layer = half * 4 + channel;
                if (layer >= masks.Length)
                {
                    continue;
                }
                TiledBuffer<byte> mask = masks[layer];
                for (int tileX = firstColumn; tileX <= lastColumn; tileX++)
                {
                    ReadOnlySpan<byte> tile = mask.Tile(tileX, tileY);
                    int x0 = Math.Max(x, tileX * size), x1 = Math.Min(x + width, Math.Min(mask.Width, (tileX + 1) * size));
                    int y0 = Math.Max(y, tileY * size), y1 = Math.Min(y + height, Math.Min(mask.Height, (tileY + 1) * size));
                    for (int row = y0; row < y1; row++)
                    {
                        int from = (row - tileY * size) * size - tileX * size;
                        int to = ((row - y) * width - x) * 4 + channel;
                        for (int column = x0; column < x1; column++)
                        {
                            rgba[to + column * 4] = tile[from + column];
                        }
                    }
                }
            }
        }
        if (lastRow == firstRow)
        {
            Row(firstRow);
        }
        else
        {
            Parallel.For(firstRow, lastRow + 1, Row);
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
            if ((decal.Type != DecalType.Albedo && !normals) || decal.Textures.IsDefaultOrEmpty || decal.Textures[0].Length == 0
                || _hidden?.Contains(decal.Id) == true)
            {
                continue;
            }
            (float sin, float cos) = MathF.SinCos(decal.Rotation.Y);
            Vector2 corner = new Vector2(decal.Position.X, decal.Position.Z);
            Vector2 edgeU = new Vector2(decal.Scale.X * cos, decal.Scale.X * sin);
            Vector2 edgeV = new Vector2(-decal.Scale.Z * sin, decal.Scale.Z * cos);
            int sizeClass = SizeClassOf(corner, edgeU, edgeV);
            int index = instances.Count / 6;
            instances.AddRange([corner.X, corner.Y, edgeU.X, edgeU.Y, edgeV.X, edgeV.Y]);

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

    /// <summary>
    /// The highlighted decals that show, as instances of their own, in runs of one size class each
    /// (their textures do not matter: the highlight is a tint with an outline).
    /// </summary>
    private void UpdateHighlights(ImmutableArray<Decal> decals)
    {
        _highlightRuns.Clear();
        if (_highlighted is not { Count: > 0 } highlighted)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("3d.update-decal-highlights");
        List<(int SizeClass, float[] Instance)> found = [];
        foreach (Decal decal in decals)
        {
            if (!highlighted.Contains(decal.Id) || _hidden?.Contains(decal.Id) == true)
            {
                continue;
            }
            (float sin, float cos) = MathF.SinCos(decal.Rotation.Y);
            Vector2 corner = new Vector2(decal.Position.X, decal.Position.Z);
            Vector2 edgeU = new Vector2(decal.Scale.X * cos, decal.Scale.X * sin);
            Vector2 edgeV = new Vector2(-decal.Scale.Z * sin, decal.Scale.Z * cos);
            found.Add((SizeClassOf(corner, edgeU, edgeV), [corner.X, corner.Y, edgeU.X, edgeU.Y, edgeV.X, edgeV.Y]));
        }
        List<float> instances = [];
        foreach (IGrouping<int, (int SizeClass, float[] Instance)> sizeClass in found.GroupBy(item => item.SizeClass))
        {
            int first = instances.Count / 6;
            foreach ((int _, float[] instance) in sizeClass)
            {
                instances.AddRange(instance);
            }
            _highlightRuns.Add(new DecalRun(false, "", sizeClass.Key, first, sizeClass.Count()));
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _highlightInstances);
        float[] data = instances.Count > 0 ? [.. instances] : [0f];
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data, BufferUsageARB.StaticDraw);
    }

    /// <summary>
    /// The highlighted decals, over the others: a light tint of the highlight's colour with an outline.
    /// </summary>
    private void DrawHighlights(MapDocument document, Matrix4x4 viewProjection, Vector3 color)
    {
        if (_highlightRuns.Count == 0)
        {
            return;
        }
        Program program = _decalHighlight;
        program.Use();
        SetCommon(program, document, viewProjection);
        program.Set("uMapSize", document.Width, document.Height);
        program.Set("uCells", _gridCells.X, _gridCells.Z);
        program.Set("uColor", color.X, color.Y, color.Z);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        foreach (DecalRun run in _highlightRuns)
        {
            Mesh mesh = _decalGrids[run.SizeClass];
            program.Set("uPatchCells", (float)DecalGridCells[run.SizeClass]);
            mesh.Bind();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _highlightInstances);
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

    /// <summary>
    /// The smallest patch of the lattice that covers a decal's rectangle, as the decal shader
    /// measures it.
    /// </summary>
    private int SizeClassOf(Vector2 corner, Vector2 edgeU, Vector2 edgeV)
    {
        Vector2 b = corner + edgeU;
        Vector2 c = corner + edgeV;
        Vector2 d = b + edgeV;
        Vector2 low = Vector2.Min(Vector2.Min(corner, b), Vector2.Min(c, d));
        Vector2 high = Vector2.Max(Vector2.Max(corner, b), Vector2.Max(c, d));
        Vector2 cell = new Vector2((float)_mapSize.Width / _gridCells.X, (float)_mapSize.Height / _gridCells.Z);
        Vector2 cells = new Vector2(_gridCells.X, _gridCells.Z);
        Vector2 origin = Vector2.Clamp(new Vector2(MathF.Floor(low.X / cell.X), MathF.Floor(low.Y / cell.Y)), Vector2.Zero, cells);
        Vector2 need = Vector2.Clamp(new Vector2(MathF.Ceiling(high.X / cell.X), MathF.Ceiling(high.Y / cell.Y)), Vector2.Zero, cells) - origin;
        float widest = Math.Max(need.X, need.Y);
        int sizeClass = Array.FindIndex(DecalGridCells, cellsOfClass => cellsOfClass >= widest);
        return sizeClass < 0 ? DecalGridCells.Length - 1 : sizeClass;
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
        _decalHighlight.Dispose();
        _water.Dispose();
        _waterQuad.Dispose();
        _brush.Dispose();
        _brushGrid.Dispose();
        if (_brushPicture != 0)
        {
            _gl.DeleteTexture(_brushPicture);
        }
        _grid.Dispose();
        foreach (Mesh mesh in _decalGrids)
        {
            mesh.Dispose();
        }
        _gl.DeleteBuffer(_decalInstances);
        _gl.DeleteBuffer(_highlightInstances);
        _gl.DeleteSampler(_repeat);
        _gl.DeleteSampler(_clamp);
        _gl.DeleteSampler(_masks);
        _gl.DeleteTexture(_white);
        _gl.DeleteTexture(_clear);
        _gl.DeleteTexture(_heights);
        _gl.DeleteTexture(_mask0);
        _gl.DeleteTexture(_mask1);
        _overlay.Dispose();
        if (_walkTexture != 0)
        {
            _gl.DeleteTexture(_walkTexture);
        }
        if (_gridTexture != 0)
        {
            _gl.DeleteTexture(_gridTexture);
        }
    }

    /// <summary>
    /// A grid of cells over 0 to 1 (or over whole corners), as positions and two triangles per cell.
    /// </summary>
    private sealed class Mesh(GL gl) : IDisposable
    {
        private readonly uint _vao = gl.GenVertexArray();
        private readonly uint _vertices = gl.GenBuffer();
        private readonly uint _indices = gl.GenBuffer();

        public int IndexCount { get; private set; }

        /// <param name="corners">The positions as whole corners (0 to the cells), the lattice the
        /// terrain and the brush share; else from 0 to 1.</param>
        public void SetGrid(int cellsX, int cellsZ, bool corners = false)
        {
            float[] positions = new float[(cellsX + 1) * (cellsZ + 1) * 2];
            int i = 0;
            for (int z = 0; z <= cellsZ; z++)
            {
                for (int x = 0; x <= cellsX; x++)
                {
                    positions[i++] = corners ? x : (float)x / cellsX;
                    positions[i++] = corners ? z : (float)z / cellsZ;
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
    internal sealed class Program : IDisposable
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
