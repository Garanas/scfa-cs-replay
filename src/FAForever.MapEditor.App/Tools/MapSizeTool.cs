using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Grows or crops the map (user story M-3): its options are the new size, where the old map lies
/// in it (an anchor, or an offset), what the map gains (the edges' heights or a height) and whether
/// the playable area becomes the whole new map. The views draw the new map's rectangle over the
/// old one; dragging it moves the offset by 4 ogrids at a time. Apply runs <c>map.resize</c>, one
/// step of undo.
/// </summary>
public sealed partial class MapSizeTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// The step a drag moves the new map by, in ogrids: the game rounds a playable area to it, and
    /// it keeps the grids of half the map's size on whole pixels.
    /// </summary>
    public const int DragStep = 4;

    private (int X, int Z) _pressOffset;
    private (float X, float Z) _from;
    private bool _syncing;

    public double CursorRadius => 0;

    /// <summary>
    /// The game's sizes of a side, in ogrids.
    /// </summary>
    public IReadOnlyList<int> Sizes => NewMap.Sizes;

    public IReadOnlyList<MapAnchor> Anchors { get; } = Enum.GetValues<MapAnchor>();

    [ObservableProperty]
    public partial int NewWidth { get; set; } = 512;

    [ObservableProperty]
    public partial int NewHeight { get; set; } = 512;

    [ObservableProperty]
    public partial MapAnchor Anchor { get; set; } = MapAnchor.Centre;

    /// <summary>
    /// Where the old map's corner lands in the new one, in ogrids; it follows the anchor until it
    /// is typed or dragged.
    /// </summary>
    [ObservableProperty]
    public partial int OffsetX { get; set; }

    /// <inheritdoc cref="OffsetX"/>
    [ObservableProperty]
    public partial int OffsetZ { get; set; }

    /// <summary>
    /// Whether the offset was typed or dragged rather than taken from the anchor.
    /// </summary>
    [ObservableProperty]
    public partial bool CustomOffset { get; private set; }

    /// <summary>
    /// Whether the map gains terrain at <see cref="FillHeight"/>; otherwise the heights at its
    /// edges continue outwards.
    /// </summary>
    [ObservableProperty]
    public partial bool FillWithHeight { get; set; }

    [ObservableProperty]
    public partial float FillHeight { get; set; } = 35;

    [ObservableProperty]
    public partial bool FitPlayableArea { get; set; }

    /// <summary>
    /// E.g. <c>1024 × 512 ogrids (20 × 10 km), the old map at 256, 0</c>.
    /// </summary>
    public string Summary => $"{NewWidth} × {NewHeight} ogrids ({Kilometres(NewWidth)} × {Kilometres(NewHeight)} km), the old map at {OffsetX}, {OffsetZ}";

    public string Help => "Choose the new size and where the old map lies in it; drag the rectangle to move it. Apply resizes the map, one step of undo";

    /// <summary>
    /// The new map's rectangle in the old map's ogrids.
    /// </summary>
    public (float X0, float Z0, float X1, float Z1)? Box => model.ShownDocument is null
        ? null
        : (-OffsetX, -OffsetZ, -OffsetX + NewWidth, -OffsetZ + NewHeight);

    /// <summary>
    /// The size along the new map's top edge.
    /// </summary>
    public ToolMarks? Marks => Box is (float x0, float z0, float x1, _)
        ? new ToolMarks([], (x0, z0, x1, z0), $"{NewWidth} × {NewHeight}")
        : null;

    /// <summary>
    /// Takes the map's size and the anchor's offset again: a map opened, resized or undone.
    /// </summary>
    public void Reset()
    {
        if (model.ShownDocument is not MapDocument document)
        {
            return;
        }
        _syncing = true;
        NewWidth = document.Width;
        NewHeight = document.Height;
        CustomOffset = false;
        _syncing = false;
        FollowAnchor();
    }

    /// <summary>
    /// The arguments of <c>map.resize</c> for these settings.
    /// </summary>
    public JsonObject Arguments()
    {
        JsonObject arguments = new JsonObject
        {
            ["width"] = NewWidth,
            ["height"] = NewHeight,
            ["fitPlayableArea"] = FitPlayableArea,
        };
        if (CustomOffset)
        {
            arguments["offsetX"] = OffsetX;
            arguments["offsetZ"] = OffsetZ;
        }
        else
        {
            arguments["anchor"] = Anchor.ToString();
        }
        if (FillWithHeight)
        {
            arguments["fillHeight"] = FillHeight;
        }
        return arguments;
    }

    [RelayCommand]
    private Task Apply() => model.RunWithAsync("map.resize", Arguments());

    public void Press(float x, float z)
    {
        _from = (x, z);
        _pressOffset = (OffsetX, OffsetZ);
    }

    public void Drag(float x, float z)
    {
        int dx = (int)MathF.Round((x - _from.X) / DragStep) * DragStep;
        int dz = (int)MathF.Round((z - _from.Z) / DragStep) * DragStep;
        // the rectangle follows the mouse, so the old map moves the other way in it
        SetOffset(_pressOffset.X - dx, _pressOffset.Z - dz);
    }

    public void Release()
    {
    }

    /// <summary>
    /// Puts the old map at a place in the new one, which no longer follows the anchor.
    /// </summary>
    public void SetOffset(int x, int z)
    {
        _syncing = true;
        OffsetX = x;
        OffsetZ = z;
        _syncing = false;
        CustomOffset = true;
        Changed();
    }

    partial void OnNewWidthChanged(int value) => FollowAnchor();

    partial void OnNewHeightChanged(int value) => FollowAnchor();

    partial void OnAnchorChanged(MapAnchor value)
    {
        CustomOffset = false;
        FollowAnchor();
    }

    partial void OnOffsetXChanged(int value) => Typed();

    partial void OnOffsetZChanged(int value) => Typed();

    partial void OnFitPlayableAreaChanged(bool value) => Changed();

    private void Typed()
    {
        if (!_syncing)
        {
            CustomOffset = true;
            Changed();
        }
    }

    private void FollowAnchor()
    {
        if (_syncing || model.ShownDocument is not MapDocument document)
        {
            return;
        }
        if (!CustomOffset)
        {
            (int x, int z) = MapResize.OffsetOf(document.Width, document.Height, new MapResizeSettings { Width = NewWidth, Height = NewHeight, Anchor = Anchor });
            _syncing = true;
            OffsetX = x;
            OffsetZ = z;
            _syncing = false;
        }
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Box));
        OnPropertyChanged(nameof(Marks));
    }

    /// <summary>
    /// A side's length as the game names it: 5, 10, 20, 40 or 81 km.
    /// </summary>
    public static string Kilometres(int ogrids) => ogrids == 4096 ? "81" : (ogrids / 51.2).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
