using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Places decals on the terrain (user story D-1). The decal is chosen with the picker
/// (<c>decals.choose</c>, the options' Choose button; Ctrl+P in the Decals mode) or on the shelf. A
/// click places it centred on the mouse, <see cref="Width"/> wide and as long as its texture's
/// shape makes it, turned by <see cref="Heading"/>; dragging from the click spans its width from the
/// press to the mouse (its centre half way) and turns it so its width lies along the drag. Each decal is one step
/// of undo and becomes the selection, so the inspector shows it. A decal dropped from the shelf on
/// a view is placed there with these settings (<see cref="IAssetDropTarget"/>).
/// </summary>
public sealed partial class DecalPlaceTool(MainViewModel model) : ObservableObject, IMapTool, IAssetDropTarget
{
    /// <summary>
    /// How far, in pixels, the mouse moves before a press becomes a drag.
    /// </summary>
    private const float DragPixels = 3;

    private EditTransaction? _place;
    private MapDocument? _before;
    private (float X, float Z) _from;
    private (float X, float Z) _dragCentre;
    private ToolInput _input = ToolInput.Default;
    private bool _dragging;
    private ObjectRef? _added;
    private System.Collections.Immutable.ImmutableArray<ObjectRef> _all = [];
    private float _dragWidth;
    private float _dragHeading;

    /// <summary>
    /// The key of the decal it places (an asset of the workspace, <c>env/common/decals/erosion018_normals.dds</c>),
    /// or the name of a decal set (<c>env/rocky/decals/rock01</c>, placed as one decal with all its
    /// textures); null before one is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Image), nameof(HasDecal))]
    public partial string? Decal { get; set; }

    /// <summary>
    /// The chosen decal as the workspace has it, or null when it has none of that key.
    /// </summary>
    public AssetEntry? Entry { get; private set; }

    /// <summary>
    /// The decal set it places when <see cref="Decal"/> names one; null for a single texture.
    /// </summary>
    public DecalSetAssets? Set { get; private set; }

    public bool HasDecal => Decal is not null;

    /// <summary>
    /// What the options bar calls the chosen decal.
    /// </summary>
    public string Title => Set is DecalSetAssets set ? $"{Path.GetFileName(set.Name)} (set)"
        : Entry?.Name ?? (Decal is string key ? Path.GetFileNameWithoutExtension(key) : "No decal chosen");

    /// <summary>
    /// The chosen decal's picture, for the options' thumbnail.
    /// </summary>
    public string? Image => Entry?.Image;

    /// <summary>
    /// How much longer than wide its texture is (height over width of the picture); 1 when unknown.
    /// </summary>
    public float Aspect { get; private set; } = 1;

    /// <summary>
    /// A new decal's width (along its own x axis), in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Width { get; set; } = 16;

    /// <summary>
    /// A new decal's turn in degrees, clockwise from +x in the top view.
    /// </summary>
    [ObservableProperty]
    public partial double Heading { get; set; }

    /// <summary>
    /// The distance beyond which a new decal is not drawn in the game.
    /// </summary>
    [ObservableProperty]
    public partial double CutOffLod { get; set; } = DecalEdits.DefaultCutOffLod;

    public double CursorRadius => 0;

    /// <summary>
    /// What the options bar says about using it.
    /// </summary>
    public string Help => "Click to place the decal, drag to size and turn it; Ctrl+P or Choose picks the decal, or choose one on the shelf or drag it onto the map";

    partial void OnDecalChanged(string? value)
    {
        Entry = value is null ? null : model.Assets.Catalog.Find(value);
        Set = value is not null && Entry is null ? DecalSets.Find(model.Assets.Catalog.Entries, value) : null;
        if (Set?.First is string first)
        {
            Entry = model.Assets.Catalog.Find(first);
        }
        OnPropertyChanged(nameof(Set));
        Aspect = Entry?.Image is string image ? AspectOf(image, model.Logger("Assets")) : 1;
        OnPropertyChanged(nameof(Entry));
    }

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = (x, z);
        _dragging = false;
        _added = null;
        if (model.ShownDocument is null || model.StrokeInProgress is not null)
        {
            return;
        }
        if (Decal is not string key)
        {
            model.Status = "Choose a decal to place first: Choose in the options, Ctrl+P, or a decal on the shelf";
            return;
        }
        _dragWidth = (float)Width;
        _dragHeading = (float)(Heading * Math.PI / 180);
        _dragCentre = (x, z);
        _place = model.StartStroke($"Place the decal {Path.GetFileNameWithoutExtension(key)}");
        _before = _place.Document;
        Show(key);
    }

    public void Drag(float x, float z, ToolInput input)
    {
        if (_place is null || Decal is not string key)
        {
            return;
        }
        float dx = x - _from.X;
        float dz = z - _from.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (!_dragging)
        {
            if (distance < DragPixels * _input.OgridsPerPixel)
            {
                return;
            }
            _dragging = true;
        }
        // the decal spans the drag: from the press to the mouse, its centre half way
        _dragWidth = MathF.Max(distance, DecalEdits.SmallestSide);
        _dragHeading = MathF.Atan2(dz, dx);
        _dragCentre = (_from.X + dx / 2, _from.Z + dz / 2);
        Show(key);
    }

    public void Release()
    {
        if (_place is EditTransaction place)
        {
            model.EndStroke(place);
            if (_added is ObjectRef added && model.ShownDocument?.Exists(added) == true)
            {
                model.Selection.Set(_all.Where(model.ShownDocument.Exists));
                model.Status = $"Placed {Title}";
            }
        }
        _place = null;
        _before = null;
        _dragging = false;
    }

    public bool Accepts(AssetEntry entry) => entry.Kind == AssetKind.Decal;

    /// <summary>
    /// A decal dropped from the shelf becomes the chosen one and is placed where it fell.
    /// </summary>
    public void Drop(AssetEntry entry, float x, float z)
    {
        Decal = entry.Key;
        _ = model.RunWithAsync("decals.place", new System.Text.Json.Nodes.JsonObject { ["key"] = entry.Key, ["x"] = x, ["z"] = z });
    }

    /// <summary>
    /// A new decal of the tool's settings, centred on a place, as <c>decals.place</c> makes it.
    /// </summary>
    /// <param name="width">Left out, the tool's <see cref="Width"/>.</param>
    /// <param name="length">Left out, the width times the texture's <see cref="Aspect"/> (of the
    /// chosen decal; 1 for another).</param>
    /// <param name="heading">In radians; left out, the tool's <see cref="Heading"/>.</param>
    public Decal Make(MapDocument document, string key, float x, float z, float? width = null, float? length = null, float? heading = null, float? cutOffLod = null)
    {
        float wide = width ?? (float)Width;
        float aspect = key == Decal ? Aspect : 1;
        float turn = heading ?? (float)(Heading * Math.PI / 180);
        float cutOff = cutOffLod ?? (float)CutOffLod;
        // a decal set's name (no texture of that key): one decal carrying all the set's textures
        DecalSetAssets? set = key == Decal ? Set
            : model.Assets.Catalog.Find(key) is null ? DecalSets.Find(model.Assets.Catalog.Entries, key) : null;
        return set is not null
            ? DecalEdits.NewDecalSet(document.Terrain.Heightmap, set, x, z, wide, length ?? wide * aspect, turn, cutOff)
            : DecalEdits.NewDecal(document.Terrain.Heightmap, key, x, z, wide, length ?? wide * aspect, turn, cutOff);
    }

    /// <summary>
    /// The decal being placed, from the document as it was at the press, so the step holds only it.
    /// </summary>
    private void Show(string key)
    {
        using Performance.Span measure = Performance.Measure("tool.place-decal");
        if (_place is null || model.StrokeInProgress != _place)
        {
            return;
        }
        MapDocument before = _before!;
        Decal decal = Make(before, key, _dragCentre.X, _dragCentre.Z, _dragWidth, null, _dragHeading);
        ObjectRef added = default;
        System.Collections.Immutable.ImmutableArray<ObjectRef> all = [];
        // with the symmetry, its images follow the size and turn of the drag (F-12)
        _place.Apply(_ => model.MirrorPlaced(before.PlaceDecal(decal, out added), [added], out all));
        _added = added;
        _all = all;
        model.ShowStroke();
    }

    /// <summary>
    /// The height over the width of a PNG, from its header; 1 when it cannot be read.
    /// </summary>
    private static float AspectOf(string png, ILogger log)
    {
        try
        {
            using FileStream stream = File.OpenRead(png);
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]);
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..]);
            return width > 0 && height > 0 ? (float)height / width : 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(exception, "The size of the decal's picture {File} cannot be read: {Reason}", png, exception.Message);
            return 1;
        }
    }
}
