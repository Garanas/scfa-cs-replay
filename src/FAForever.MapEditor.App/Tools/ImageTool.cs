using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Chooses, moves, scales and turns the map's projected images in the top view (user story N-2).
/// A click on an image chooses it (the top one under the mouse; a locked image only when no other
/// lies there), a click on nothing chooses none. The chosen image has handles: a square at each
/// corner scales it about the opposite corner (keeping its proportions unless Keep proportions is
/// off), a knob beyond the middle of its top edge turns it (Shift: steps of 15 degrees), and a drag
/// inside it moves it. A locked image has none of that. One drag is one step of undo, held from press
/// to release. The 3D view never shows the images, so a click there does nothing. Its keys: Delete
/// removes the chosen image, Escape chooses none.
/// </summary>
public sealed partial class ImageTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// How near a handle, in pixels, the mouse must be to take it.
    /// </summary>
    private const float HandlePixels = 8;

    /// <summary>
    /// How far beyond the top edge the turning knob lies, in pixels.
    /// </summary>
    public const float KnobPixels = 28;

    /// <summary>
    /// How far the mouse must go, in pixels, before a press becomes a drag.
    /// </summary>
    private const float DragPixels = 3;

    private ToolInput _input = ToolInput.Default;
    private (float X, float Z) _from;
    private bool _dragging;
    private string? _name;
    private ProjectedImage? _start;
    private ImageHandle _handle;
    private EditTransaction? _edit;
    private MapDocument? _before;

    public double CursorRadius => 0;

    /// <summary>
    /// The images and the chosen one's fields, for the options row.
    /// </summary>
    public ProjectedImagesViewModel Images => model.Images;

    /// <summary>
    /// What the options row says about using it, short enough for its row at 1280 pixels.
    /// </summary>
    public string Hint => "Drag: move · corners: scale · knob: turn (Shift: 15° steps)";

    /// <summary>
    /// All there is to say about using it: the tool tip of the hint.
    /// </summary>
    public string Help => "Click an image to choose it, drag it to move it. The corners scale it (keeping its proportions unless Keep proportions is off), the knob above its top edge turns it (Shift: steps of 15 degrees). A locked image does not move. Delete removes the chosen image.";

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = (x, z);
        _dragging = false;
        _name = null;
        _start = null;
        _handle = ImageHandle.None;
        if (input.View is { IsTopDown: false } || model.ShownDocument is not MapDocument document || !model.ShowImages)
        {
            return;
        }
        float reach = HandlePixels * input.OgridsPerPixel;
        float knob = KnobPixels * input.OgridsPerPixel;

        // the chosen image's own handles first: a corner or the knob lies outside its picture
        if (model.Images.Chosen is { Shown: true, Locked: false } chosen)
        {
            ImageHandle handle = ProjectedImages.HandleAt(chosen, x, z, reach, knob);
            if (handle is not (ImageHandle.None or ImageHandle.Move))
            {
                Take(chosen, handle);
                return;
            }
        }

        // else the top image under the mouse, an unlocked one before a locked one
        ProjectedImage? hit = Under(document, x, z, locked: false) ?? Under(document, x, z, locked: true);
        if (hit is null)
        {
            if (!input.Modifiers.HasFlag(ToolModifiers.Shift))
            {
                model.Images.Choose(null);
            }
            return;
        }
        model.Images.Choose(hit.Name);
        if (!hit.Locked)
        {
            Take(hit, ImageHandle.Move);
        }
    }

    public void Drag(float x, float z, ToolInput input)
    {
        if (_name is null || _start is null)
        {
            return;
        }
        if (!_dragging)
        {
            float distance = MathF.Sqrt((x - _from.X) * (x - _from.X) + (z - _from.Z) * (z - _from.Z));
            if (distance < DragPixels * _input.OgridsPerPixel)
            {
                return;
            }
            _dragging = true;
        }
        Reshape(x, z, input.Modifiers.HasFlag(ToolModifiers.Shift));
    }

    public void Release()
    {
        if (_edit is not null)
        {
            ProjectedImage? after = model.ShownDocument?.FindImage(_name!);
            model.EndStroke(_edit);
            if (after is not null)
            {
                model.Status = _handle switch
                {
                    ImageHandle.Turn => $"Turned {after.Name} to {after.Turn:0.#}°",
                    ImageHandle.Move => $"Moved {after.Name} to {after.CentreX:0.#}, {after.CentreZ:0.#}",
                    _ => $"Scaled {after.Name} to {after.Width:0.#} by {after.Height:0.#} ogrids",
                };
            }
        }
        _edit = null;
        _before = null;
        _name = null;
        _start = null;
        _handle = ImageHandle.None;
        _dragging = false;
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        switch (key)
        {
            case "Delete" when model.Images.HasChosen:
                _ = model.RunWithAsync("images.remove", []);
                return true;
            case "Escape" when model.Images.HasChosen:
                model.Images.Choose(null);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The chosen image's outline, its corner squares and its turning knob, at a fixed size on
    /// screen: what a person aims the mouse at. A locked image has the outline alone.
    /// </summary>
    public IReadOnlyList<ToolShape> Shapes(ToolView view)
    {
        if (!view.IsTopDown || !model.ShowImages || model.Images.Chosen is not { Shown: true } image)
        {
            return [];
        }
        (double X, double Z)[] corners = ProjectedImages.Corners(image);
        Vector2[] screen = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            if (view.ToScreen(new Vector3((float)corners[i].X, 0, (float)corners[i].Z)) is not Vector2 point)
            {
                return [];
            }
            screen[i] = point;
        }
        // the handle colour over a dark rim: nothing the sketch's own six colours (the yellow pencil) can be taken for
        List<ToolShape> shapes = [new ToolLine(screen, true, 4, ToolInk.Shadow), new ToolLine(screen, true, 2, ToolInk.Handle)];
        if (image.Locked)
        {
            return shapes;
        }
        float knobDistance = KnobPixels * view.OgridsPerPixelAt(new Vector3((float)image.CentreX, 0, (float)image.CentreZ));
        (double kx, double kz) = ProjectedImages.Knob(image, knobDistance);
        (double mx, double mz) = ProjectedImages.Knob(image, 0);
        if (view.ToScreen(new Vector3((float)kx, 0, (float)kz)) is Vector2 knob && view.ToScreen(new Vector3((float)mx, 0, (float)mz)) is Vector2 middle)
        {
            shapes.Add(new ToolLine([middle, knob], false, 3.5f, ToolInk.Shadow));
            shapes.Add(new ToolLine([middle, knob], false, 1.5f, ToolInk.Handle));
            shapes.Add(new ToolDisc(knob, 6, ToolInk.Handle, ToolInk.Shadow));
        }
        foreach (Vector2 corner in screen)
        {
            shapes.Add(new ToolSquare(corner, 5, ToolInk.Handle, ToolInk.Shadow));
        }
        return shapes;
    }

    /// <summary>
    /// Describes the tool for automation: what is chosen and what a drag now does.
    /// </summary>
    public System.Text.Json.Nodes.JsonObject Describe() => new System.Text.Json.Nodes.JsonObject
    {
        ["dragging"] = _edit is not null,
        ["handle"] = _handle.ToString(),
    };

    private void Take(ProjectedImage image, ImageHandle handle)
    {
        _name = image.Name;
        _start = image;
        _handle = handle;
    }

    /// <summary>
    /// The top image under a place: the last drawn, the images over the sketch after those under it.
    /// </summary>
    private static ProjectedImage? Under(MapDocument document, float x, float z, bool locked)
    {
        ProjectedImage? best = null;
        foreach (ProjectedImage image in document.Images)
        {
            if (image.Shown && image.Locked == locked && ProjectedImages.Contains(image, x, z) && (best is null || image.AboveSketch || !best.AboveSketch))
            {
                best = image;
            }
        }
        return best;
    }

    /// <summary>
    /// Makes the image what the handle makes of it with the mouse at a place: from the image as it was
    /// when the drag began, so the step of undo holds the whole drag.
    /// </summary>
    private void Reshape(float x, float z, bool snap)
    {
        using Performance.Span measure = Performance.Measure("tool.reshape-image");
        if (_edit is null)
        {
            if (model.StrokeInProgress is not null)
            {
                _name = null;
                return;
            }
            _edit = model.StartStroke(_handle switch
            {
                ImageHandle.Turn => $"Turn the image {_name}",
                ImageHandle.Move => $"Move the image {_name}",
                _ => $"Scale the image {_name}",
            });
            _before = _edit.Document;
        }
        if (model.StrokeInProgress != _edit)
        {
            return;
        }
        MapDocument before = _before!;
        ProjectedImage start = _start!;
        string name = _name!;
        ImageHandle handle = _handle;
        (float X, float Z) from = _from;
        _edit.Apply(_ => before.UpdateImage(name, _ => ProjectedImages.Rounded(handle switch
        {
            ImageHandle.Move => ProjectedImages.Moved(start, x - from.X, z - from.Z),
            ImageHandle.Turn => ProjectedImages.Turned(start, from.X, from.Z, x, z, snap),
            _ => ProjectedImages.Scaled(start, handle, x, z, start.KeepProportions),
        })));
        model.ShowStroke();
    }
}
