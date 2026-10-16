using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The projected images in <c>state.images</c> (user story N-2): the list with each image's place and
/// look, whether the overlay shows them, how many the top view drew, and where the chosen image's
/// handles are in the top view's pixels, to aim the mouse at (<c>mouse view=top x=.. y=..</c>).
/// </summary>
public sealed partial class AutomationServer
{
    private JsonObject ImagesState()
    {
        JsonObject state = model.Images.Describe();
        state["shown"] = model.ShowImages;
        state["drawnTop"] = window.MapView.ImagesDrawn;
        state["tool"] = model.Tool<ImageTool>().Describe();
        if (model.Images.Chosen is ProjectedImage image)
        {
            JsonNode? At(double x, double z)
            {
                Avalonia.Point point = window.MapView.MapToControl(x, z);
                return new JsonArray(Math.Round(point.X, 1), Math.Round(point.Y, 1));
            }
            (double X, double Z)[] corners = ProjectedImages.Corners(image);
            // the knob lies this many pixels beyond the top edge, as the tool draws it
            (double kx, double kz) = ProjectedImages.Knob(image, ImageTool.KnobPixels / window.MapView.Zoom);
            state["points"] = new JsonObject
            {
                ["centre"] = At(image.CentreX, image.CentreZ),
                ["topLeft"] = At(corners[0].X, corners[0].Z),
                ["topRight"] = At(corners[1].X, corners[1].Z),
                ["bottomRight"] = At(corners[2].X, corners[2].Z),
                ["bottomLeft"] = At(corners[3].X, corners[3].Z),
                ["knob"] = At(kx, kz),
            };
        }
        return state;
    }
}
