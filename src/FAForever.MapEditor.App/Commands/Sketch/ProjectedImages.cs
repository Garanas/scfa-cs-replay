using System.Globalization;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// What the <c>images.*</c> commands share: the image a command names (in any case), or else the one chosen.
/// </summary>
internal static class ImageArguments
{
    /// <summary>
    /// The image named, else the chosen one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No map is open.</exception>
    /// <exception cref="ArgumentException">There is no such image, or none is named or chosen.</exception>
    public static ProjectedImage Image(MainViewModel model, string? name)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string? wanted = name?.Trim() is { Length: > 0 } text ? text : model.Images.ChosenName;
        if (wanted is null)
        {
            throw new ArgumentException("Name an image, or choose one in the Sketch mode's list of projected images");
        }
        return document.FindImage(wanted) ?? throw new ArgumentException($"The map has no image {wanted}");
    }

    /// <summary>
    /// The picture of a file the person chose, read and scaled down as the project keeps it.
    /// </summary>
    public static async Task<(System.Collections.Immutable.ImmutableArray<byte> Png, int Width, int Height)> ReadAsync(MainViewModel model, string path)
    {
        try
        {
            return await Task.Run(() => ProjectedImages.Read(path));
        }
        catch (MapProjectException exception)
        {
            model.Logger("Files").LogError(exception, "Projecting {File} failed: {Reason}", path, exception.Message);
            exception.Data["FAForever.MapEditor.Logged"] = true;
            throw;
        }
    }

    public static readonly IReadOnlyList<string> Extensions = ["png", "jpg", "jpeg", "webp", "bmp", "gif", "tif", "tiff", "tga", "dds", "psd"];
}

/// <summary>
/// Projects a picture of the map maker's own onto the map in the top view (user story N-2): a
/// drawing to trace, a screenshot of another map. The file may be any picture ImageMagick reads;
/// it is copied into the project as <c>sketch/images/&lt;name&gt;.png</c> (the longest side at most
/// 4096 pixels) and never exported. The image starts fitted to the map, in the middle, above the
/// images before it; one step of undo. It is chosen, and the Image tool takes over.
/// </summary>
/// <param name="Path">The picture; left out, a file is asked for.</param>
/// <param name="Name">What to call it: letters, digits, spaces, dashes and underscores; left out, the file's name.</param>
/// <returns>The image's name; null when no file was chosen.</returns>
[EditorCommand("images.project", "Project an image...", Story = "N-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ProjectImage(string? Path = null, string? Name = null) : IRequest<string?>;

public sealed class ProjectImageHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<ProjectImage, string?>
{
    public async Task<string?> Handle(ProjectImage request, CancellationToken cancellationToken)
    {
        if (model.ShownDocument is null)
        {
            throw new InvalidOperationException("No map is open");
        }
        string? path = request.Path ?? await dialogs.PickFileAsync("Project an image", ImageArguments.Extensions, model.Map?.ProjectFolder);
        if (path is null)
        {
            model.Status = "Chose no file";
            return null;
        }
        (System.Collections.Immutable.ImmutableArray<byte> png, int width, int height) = await ImageArguments.ReadAsync(model, path);
        string stem = request.Name?.Trim() is { Length: > 0 } wanted ? wanted : ProjectedImages.NameOfFile(path);
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (request.Name is not null && ProjectedImages.Problem(document, stem) is string problem)
        {
            throw new ArgumentException(problem);
        }
        ProjectedImage image = new ProjectedImage { Name = stem, Picture = png, PixelWidth = width, PixelHeight = height };
        ProjectedImage added = image;
        model.EditMap($"Project the image {stem}", d => d.AddImage(image, out added));
        model.Images.Choose(added.Name);
        if (model.ActiveTool?.Definition.Id != "sketch.image")
        {
            model.SelectTool("sketch.image");
        }
        model.Status = $"Projected {added.Name}: {width} by {height} pixels, fitted to the map; drag it to move it, a corner to scale it, the knob to turn it";
        return added.Name;
    }
}

/// <summary>
/// Chooses an image of the map (user story N-2): the inspector shows it and the Image tool takes
/// its handles. Not a step of undo.
/// </summary>
/// <param name="Name">The image; left out, none is chosen.</param>
[EditorCommand("images.choose", "Choose an image", Story = "N-2", Needs = Needs.Map)]
public sealed record ChooseImage(string? Name = null) : IRequest;

public sealed class ChooseImageHandler(MainViewModel model) : IRequestHandler<ChooseImage>
{
    public Task Handle(ChooseImage request, CancellationToken cancellationToken)
    {
        model.Images.Choose(request.Name?.Trim() is { Length: > 0 } name ? name : null);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Removes a projected image from the map (user story N-2), one step of undo; its picture leaves the
/// project's sketch/images folder when the project is saved.
/// </summary>
/// <param name="Name">The image; left out, the chosen one.</param>
[EditorCommand("images.remove", "Remove the image", Story = "N-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveImage(string? Name = null) : IRequest;

public sealed class RemoveImageHandler(MainViewModel model) : IRequestHandler<RemoveImage>
{
    public Task Handle(RemoveImage request, CancellationToken cancellationToken)
    {
        ProjectedImage image = ImageArguments.Image(model, request.Name);
        model.EditMap($"Remove the image {image.Name}", d => d.RemoveImage(image.Name));
        model.Status = $"Removed the image {image.Name}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts another picture in a projected image (user story N-2), one step of undo: it keeps its place,
/// width and turn; with Keep proportions its height follows the new picture's.
/// </summary>
/// <param name="Name">The image; left out, the chosen one.</param>
/// <param name="Path">The picture; left out, a file is asked for.</param>
/// <returns>The new picture's size in pixels; null when no file was chosen.</returns>
[EditorCommand("images.replace", "Replace the picture...", Story = "N-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ReplaceImagePicture(string? Name = null, string? Path = null) : IRequest<string?>;

public sealed class ReplaceImagePictureHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<ReplaceImagePicture, string?>
{
    public async Task<string?> Handle(ReplaceImagePicture request, CancellationToken cancellationToken)
    {
        ProjectedImage image = ImageArguments.Image(model, request.Name);
        string? path = request.Path ?? await dialogs.PickFileAsync($"A picture for {image.Name}", ImageArguments.Extensions, model.Map?.ProjectFolder);
        if (path is null)
        {
            model.Status = "Chose no file";
            return null;
        }
        (System.Collections.Immutable.ImmutableArray<byte> png, int width, int height) = await ImageArguments.ReadAsync(model, path);
        ProjectedImage picture = image with { Picture = png, PixelWidth = width, PixelHeight = height };
        model.EditMap($"Replace the picture of {image.Name}", d => d.ReplacePicture(image.Name, picture));
        model.Status = $"Replaced the picture of {image.Name}: {width} by {height} pixels";
        return string.Create(CultureInfo.InvariantCulture, $"{width} by {height}");
    }
}

/// <summary>
/// Fits a projected image to the whole map (user story N-2), one step of undo: in the middle, not
/// turned, as large as the map lets it be with the picture's proportions (Keep proportions), or
/// stretched over the whole map.
/// </summary>
/// <param name="Name">The image; left out, the chosen one.</param>
[EditorCommand("images.fitToMap", "Fit the image to the map", Story = "N-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record FitImageToMap(string? Name = null) : IRequest;

public sealed class FitImageToMapHandler(MainViewModel model) : IRequestHandler<FitImageToMap>
{
    public Task Handle(FitImageToMap request, CancellationToken cancellationToken)
    {
        ProjectedImage image = ImageArguments.Image(model, request.Name);
        model.EditMap($"Fit the image {image.Name} to the map", d => d.FitImageToMap(image.Name));
        model.Status = $"Fitted {image.Name} to the map";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Fits a projected image to the playable area (user story N-2; the whole map when it has no area),
/// as <c>images.fitToMap</c> does to the map.
/// </summary>
/// <param name="Name">The image; left out, the chosen one.</param>
[EditorCommand("images.fitToPlayableArea", "Fit the image to the playable area", Story = "N-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record FitImageToPlayableArea(string? Name = null) : IRequest;

public sealed class FitImageToPlayableAreaHandler(MainViewModel model) : IRequestHandler<FitImageToPlayableArea>
{
    public Task Handle(FitImageToPlayableArea request, CancellationToken cancellationToken)
    {
        ProjectedImage image = ImageArguments.Image(model, request.Name);
        model.EditMap($"Fit the image {image.Name} to the playable area", d => d.FitImageToPlayableArea(image.Name));
        model.Status = $"Fitted {image.Name} to the playable area";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes what a projected image is (user story N-2), one step of undo; steps that change the same
/// fields of the same image one after the other are one step (a slider dragged). What the
/// inspector's fields and the panel's eye run. With Keep proportions, a width given alone makes the
/// height follow (and the other way round) at the proportions it has now.
/// </summary>
/// <param name="Name">The image; left out, the chosen one.</param>
/// <param name="Rename">Its new name: letters, digits, spaces, dashes and underscores, which no other image has.</param>
/// <param name="Opacity">In percent, 0 to 100.</param>
/// <param name="CentreX">Where its middle lies, in ogrids.</param>
/// <param name="Width">In ogrids before it is turned, at least 1.</param>
/// <param name="Turn">Degrees clockwise about its middle.</param>
/// <param name="Greyscale">Drawn without colour.</param>
/// <param name="Locked">Whether the mouse may not move, scale or turn it.</param>
/// <param name="Shown">Whether the top view draws it.</param>
/// <param name="AboveSketch">Whether it lies over the sketch's strokes rather than under them.</param>
/// <param name="KeepProportions">Whether scaling and fitting keep the picture's proportions.</param>
[EditorCommand("images.set", "Change the image", Story = "N-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetImage(string? Name = null, string? Rename = null, double? Opacity = null, double? CentreX = null, double? CentreZ = null,
    double? Width = null, double? Height = null, double? Turn = null, bool? Greyscale = null, bool? Locked = null, bool? Shown = null,
    bool? AboveSketch = null, bool? KeepProportions = null) : IRequest;

public sealed class SetImageHandler(MainViewModel model) : IRequestHandler<SetImage>
{
    public Task Handle(SetImage request, CancellationToken cancellationToken)
    {
        ProjectedImage image = ImageArguments.Image(model, request.Name);
        MapDocument document = model.ShownDocument!;
        string? rename = request.Rename?.Trim();
        if (rename is not null && rename != image.Name && ProjectedImages.Problem(document, rename, except: image.Name) is string problem)
        {
            throw new ArgumentException(problem);
        }
        foreach ((string field, double? value) in new (string, double?)[] { ("centreX", request.CentreX), ("centreZ", request.CentreZ), ("width", request.Width), ("height", request.Height), ("turn", request.Turn), ("opacity", request.Opacity) })
        {
            if (value is double number && !double.IsFinite(number))
            {
                throw new ArgumentException($"The image's {field} is a number");
            }
        }
        if (request.Width is < ProjectedImages.MinimumSize || request.Height is < ProjectedImages.MinimumSize)
        {
            throw new ArgumentException($"An image is at least {ProjectedImages.MinimumSize} ogrid wide and high");
        }
        if (request.Opacity is < 0 or > 100)
        {
            throw new ArgumentException("An image's opacity lies between 0 and 100 percent");
        }
        string[] fields = [.. new (string, bool)[]
        {
            ("rename", request.Rename is not null), ("opacity", request.Opacity is not null), ("centreX", request.CentreX is not null), ("centreZ", request.CentreZ is not null),
            ("width", request.Width is not null), ("height", request.Height is not null), ("turn", request.Turn is not null), ("greyscale", request.Greyscale is not null),
            ("locked", request.Locked is not null), ("shown", request.Shown is not null), ("aboveSketch", request.AboveSketch is not null), ("keepProportions", request.KeepProportions is not null),
        }.Where(field => field.Item2).Select(field => field.Item1)];
        if (fields.Length == 0)
        {
            return Task.CompletedTask;
        }

        bool renamed = rename is not null && rename != image.Name;
        bool wasChosen = string.Equals(model.Images.ChosenName, image.Name, StringComparison.Ordinal);
        string name = image.Name;
        model.EditMap(renamed ? $"Rename the image {name} to {rename}" : $"Change the image {name}", d => d.UpdateImage(name, current =>
        {
            bool keep = request.KeepProportions ?? current.KeepProportions;
            double width = request.Width ?? current.Width;
            double height = request.Height ?? current.Height;
            if (keep && request.Width is not null && request.Height is null)
            {
                height = Math.Max(ProjectedImages.MinimumSize, current.Width > 0 ? width * current.Height / current.Width : height);
            }
            else if (keep && request.Height is not null && request.Width is null)
            {
                width = Math.Max(ProjectedImages.MinimumSize, current.Height > 0 ? height * current.Width / current.Height : width);
            }
            return current with
            {
                Name = rename ?? current.Name,
                Opacity = request.Opacity is double percent ? percent / 100 : current.Opacity,
                CentreX = request.CentreX ?? current.CentreX,
                CentreZ = request.CentreZ ?? current.CentreZ,
                Width = width,
                Height = height,
                Turn = request.Turn is double turn ? ProjectedImages.Normalized(turn) : current.Turn,
                Greyscale = request.Greyscale ?? current.Greyscale,
                Locked = request.Locked ?? current.Locked,
                Shown = request.Shown ?? current.Shown,
                AboveSketch = request.AboveSketch ?? current.AboveSketch,
                KeepProportions = keep,
            };
        }), $"images.set:{name}:{string.Join(",", fields)}");
        if (renamed && wasChosen)
        {
            model.Images.Choose(rename);
        }
        return Task.CompletedTask;
    }
}
