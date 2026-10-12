using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The projected images in the top view (user story N-2): each picture turned and scaled in map
/// coordinates, at its opacity, without colour when set. The images under the sketch are drawn
/// right after the terrain, those over it after the sketch; the 3D view never draws them.
/// </summary>
public sealed partial class HeightmapView
{
    private ProjectedPictures? _pictures;

    /// <summary>
    /// How many images the last frame drew, for automation's <c>state.images</c>.
    /// </summary>
    public int ImagesDrawn { get; private set; }

    /// <summary>
    /// Draws the shown images that lie under the sketch (<paramref name="aboveSketch"/> false) or over it.
    /// </summary>
    private void DrawImages(DrawingContext context, MapDocument document, bool aboveSketch)
    {
        if (!aboveSketch)
        {
            ImagesDrawn = 0;
        }
        if (_model is not { ShowImages: true } || document.Images.IsEmpty)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("top.images");
        _pictures ??= new ProjectedPictures(() => _model.Logger("Rendering"));
        Rect view = new Rect(Bounds.Size);
        foreach (ProjectedImage image in document.Images)
        {
            if (!image.Shown || image.AboveSketch != aboveSketch || _pictures.Of(image) is not Bitmap bitmap)
            {
                continue;
            }
            Point centre = MapToControl(image.CentreX, image.CentreZ);
            // the corners' box is outside the view: nothing of it shows
            double radius = Math.Sqrt(image.Width * image.Width + image.Height * image.Height) / 2 * _zoom;
            if (!view.Inflate(radius).Contains(centre))
            {
                continue;
            }
            double width = image.Width * _zoom;
            double height = image.Height * _zoom;
            Matrix place = Matrix.CreateRotation(image.Turn * Math.PI / 180) * Matrix.CreateTranslation(centre.X, centre.Y);
            // a picture shown larger than it is gets hard pixels, as the terrain does
            bool magnified = width > bitmap.PixelSize.Width * 2;
            using (context.PushTransform(place))
            using (context.PushOpacity(Math.Clamp(image.Opacity, 0, 1)))
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = magnified ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
            {
                context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), new Rect(-width / 2, -height / 2, width, height));
            }
            ImagesDrawn++;
        }
    }
}
