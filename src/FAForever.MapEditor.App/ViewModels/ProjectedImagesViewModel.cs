using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// An image of the Sketch mode's list of projected images (user story N-2): its name with the eye
/// that shows or hides it, and how it looks. A tick runs <c>images.set</c>.
/// </summary>
public sealed partial class ProjectedImageItem(ProjectedImage image, Func<string, JsonObject, Task> run) : ObservableObject
{
    public ProjectedImage Image { get; private set; } = image;

    public string Name => Image.Name;

    /// <summary>
    /// Whether the top view draws it (the eye); a change runs <c>images.set</c>.
    /// </summary>
    public bool Shown
    {
        get => Image.Shown;
        set
        {
            if (value != Image.Shown)
            {
                _ = run("images.set", new JsonObject { ["name"] = Name, ["shown"] = value });
            }
        }
    }

    public string ShownLabel => $"Show the image {Name}";

    /// <summary>
    /// E.g. <c>55%</c>.
    /// </summary>
    public string OpacityText => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(Image.Opacity * 100)}%");

    /// <summary>
    /// E.g. <c>1600 × 1200 · locked</c>: the picture's pixels, and what keeps the mouse from moving it.
    /// </summary>
    public string Detail => string.Create(CultureInfo.InvariantCulture,
        $"{Image.PixelWidth} × {Image.PixelHeight}{(Image.Locked ? " · locked" : "")}{(Image.AboveSketch ? " · above the sketch" : "")}");

    /// <summary>
    /// Shows the image as the document now has it.
    /// </summary>
    public void Update(ProjectedImage image)
    {
        if (image == Image)
        {
            return;
        }
        Image = image;
        OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(OpacityText));
        OnPropertyChanged(nameof(Detail));
    }
}

/// <summary>
/// The map's projected images (user story N-2) as the Sketch mode shows them: the list in the
/// panel, the image chosen, and the chosen image's fields in the inspector. The panel says what
/// there is, the inspector edits what is chosen: every field runs <c>images.set</c> (a slider
/// dragged is one step of undo). The images themselves live in the document
/// (<see cref="MapDocument.Images"/>); the top view draws them (<c>HeightmapView.Images</c>) and the
/// Image tool moves, scales and turns the chosen one (<c>Tools/ImageTool</c>).
/// </summary>
public sealed partial class ProjectedImagesViewModel : ObservableObject
{
    private const string Below = "Below the sketch";
    private const string Above = "Above the sketch";

    private static readonly ProjectedImage Nothing = new ProjectedImage { Name = "", Picture = [], PixelWidth = 1, PixelHeight = 1 };

    private readonly Func<string, JsonObject, Task> _run;
    private MapDocument? _document;
    private string? _chosenName;
    private System.Collections.Immutable.ImmutableArray<byte> _chosenPicture;
    private bool _rebuilding;

    public ProjectedImagesViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
        JsonObject Chosen() => new JsonObject { ["name"] = _chosenName };
        ProjectedImage Of(MapDocument document) => _chosenName is string name ? document.FindImage(name) ?? Nothing : Nothing;

        Opacity = new NumberSetting("Opacity", "images.set", "opacity", d => Of(d).Opacity * 100, run, Chosen)
        {
            Minimum = 0, Maximum = 100, HasSlider = true, Help = "How much of the map shows through, in percent",
        };
        NameField = new TextSetting("Name", "images.set", "rename", d => Of(d).Name, run, Chosen)
        {
            Help = "Also the name of its file in the project's sketch/images folder: letters, digits, spaces, dashes and underscores",
        };
        CentreX = new NumberSetting("Centre x", "images.set", "centreX", d => Of(d).CentreX, run, Chosen) { Help = "Where the picture's middle lies, in ogrids" };
        CentreZ = new NumberSetting("Centre z", "images.set", "centreZ", d => Of(d).CentreZ, run, Chosen) { Help = "Where the picture's middle lies, in ogrids" };
        Width = new NumberSetting("Width", "images.set", "width", d => Of(d).Width, run, Chosen) { Help = "In ogrids, before it is turned; with Keep proportions the height follows" };
        Height = new NumberSetting("Height", "images.set", "height", d => Of(d).Height, run, Chosen) { Help = "In ogrids, before it is turned; with Keep proportions the width follows" };
        Turn = new NumberSetting("Turn", "images.set", "turn", d => Of(d).Turn, run, Chosen)
        {
            Minimum = -180, Maximum = 180, HasSlider = true, Help = "Degrees clockwise about its middle; the Image tool's knob turns it too (Shift: steps of 15 degrees)",
        };
        KeepProportions = new BoolSetting("Keep proportions", "images.set", "keepProportions", d => Of(d).KeepProportions, run, Chosen)
        {
            Help = "A corner scales both sides together, and a fit to the map keeps the picture's proportions",
        };
        Greyscale = new BoolSetting("Greyscale", "images.set", "greyscale", d => Of(d).Greyscale, run, Chosen) { Help = "Draw the picture without colour" };
        Locked = new BoolSetting("Locked (no moving by mistake)", "images.set", "locked", d => Of(d).Locked, run, Chosen)
        {
            Help = "The mouse does not move, scale or turn it; the fields still do",
        };
        Order = new ChoiceSetting("Order", "images.set", "aboveSketch", [Below, Above], d => Of(d).AboveSketch ? Above : Below, run, Chosen)
        {
            Value = choice => choice == Above, Help = "Whether it lies under the sketch's strokes or over them",
        };
        Place = new SettingGroup("Place", [NameField, Opacity, CentreX, CentreZ, Width, Height, Turn]);
        Look = new SettingGroup("Look", [KeepProportions, Greyscale, Locked, Order]);
    }

    /// <summary>
    /// The settings of the chosen image's inspector: its name, opacity, place, size and turn.
    /// </summary>
    public SettingGroup Place { get; }

    /// <summary>
    /// What the chosen image looks like and how the mouse treats it.
    /// </summary>
    public SettingGroup Look { get; }

    public TextSetting NameField { get; }

    public NumberSetting Opacity { get; }

    public NumberSetting CentreX { get; }

    public NumberSetting CentreZ { get; }

    public NumberSetting Width { get; }

    public NumberSetting Height { get; }

    public NumberSetting Turn { get; }

    public BoolSetting KeepProportions { get; }

    public BoolSetting Greyscale { get; }

    public BoolSetting Locked { get; }

    public ChoiceSetting Order { get; }

    /// <summary>
    /// The images in the order they are drawn, the first lowest.
    /// </summary>
    public ObservableCollection<ProjectedImageItem> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// The name of the chosen image: the one the inspector shows and the Image tool handles; null for none.
    /// </summary>
    public string? ChosenName => _chosenName;

    /// <summary>
    /// The chosen image as the document has it, or null.
    /// </summary>
    public ProjectedImage? Chosen => _chosenName is string name ? _document?.FindImage(name) : null;

    public bool HasChosen => Chosen is not null;

    /// <summary>
    /// The list's chosen entry; choosing one in the list chooses its image.
    /// </summary>
    public ProjectedImageItem? ChosenItem
    {
        get => Items.FirstOrDefault(item => item.Name == _chosenName);
        set
        {
            // the list clears its choice while it is made again
            if (!_rebuilding && value?.Name != _chosenName)
            {
                Choose(value?.Name);
            }
        }
    }

    /// <summary>
    /// What the inspector says about the chosen image's picture: its size, and where it is kept.
    /// </summary>
    public string PictureText => Chosen is { } image
        ? string.Create(CultureInfo.InvariantCulture, $"{image.PixelWidth} × {image.PixelHeight} pixels, copied into the project (sketch/images/{image.Name}.png). Never exported.")
        : "";

    /// <summary>
    /// What the panel says while there are no images.
    /// </summary>
    public string EmptyText => "None yet: project a picture of your own, a drawing or a screenshot of another map, to trace it or build towards it. It is copied into the project and shows in the top view only.";

    /// <summary>
    /// Raised when another image is chosen, or none.
    /// </summary>
    public event EventHandler? ChosenChanged;

    /// <summary>
    /// Chooses an image by name (in any case), or none.
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such image.</exception>
    public void Choose(string? name)
    {
        string? chosen = null;
        if (name is not null)
        {
            chosen = _document?.FindImage(name)?.Name ?? throw new ArgumentException($"The map has no image {name}");
        }
        if (chosen == _chosenName)
        {
            return;
        }
        _chosenName = chosen;
        _chosenPicture = chosen is null ? default : _document!.FindImage(chosen)!.Picture;
        Changed();
        Place.Refresh(_document);
        Look.Refresh(_document);
        ChosenChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the images of a version of the document: the list again when the images changed, the
    /// chosen image's fields. An image that went away (an undo) is no longer chosen.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        _document = document;
        System.Collections.Immutable.ImmutableArray<ProjectedImage> images = document?.Images ?? [];
        bool same = images.Length == Items.Count && images.Zip(Items).All(pair => pair.First.Name == pair.Second.Name);
        if (same)
        {
            for (int i = 0; i < images.Length; i++)
            {
                Items[i].Update(images[i]);
            }
        }
        else
        {
            _rebuilding = true;
            Items.Clear();
            foreach (ProjectedImage image in images)
            {
                Items.Add(new ProjectedImageItem(image, _run));
            }
            _rebuilding = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
        bool lost = false;
        if (_chosenName is not null)
        {
            if (document?.FindImage(_chosenName) is { } found)
            {
                // a name in another case is the same image
                _chosenName = found.Name;
                _chosenPicture = found.Picture;
            }
            else if (document?.Images.FirstOrDefault(image => image.Picture == _chosenPicture) is { } renamed)
            {
                // renamed (or the rename undone): the image that holds the same picture
                _chosenName = renamed.Name;
            }
            else
            {
                _chosenName = null;
                lost = true;
            }
        }
        Changed();
        Place.Refresh(document);
        Look.Refresh(document);
        if (lost)
        {
            ChosenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(ChosenName));
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(HasChosen));
        OnPropertyChanged(nameof(ChosenItem));
        OnPropertyChanged(nameof(PictureText));
    }

    /// <summary>
    /// What automation's <c>state.images</c> says: the chosen image and each image's place and look.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["count"] = Items.Count,
        ["chosen"] = _chosenName,
        ["items"] = new JsonArray([.. Items.Select(item =>
        {
            ProjectedImage image = item.Image;
            return (JsonNode)new JsonObject
            {
                ["name"] = image.Name,
                ["shown"] = image.Shown,
                ["opacity"] = Math.Round(image.Opacity * 100, 2),
                ["centre"] = new JsonArray(Math.Round(image.CentreX, 3), Math.Round(image.CentreZ, 3)),
                ["size"] = new JsonArray(Math.Round(image.Width, 3), Math.Round(image.Height, 3)),
                ["turn"] = Math.Round(image.Turn, 3),
                ["pixels"] = new JsonArray(image.PixelWidth, image.PixelHeight),
                ["greyscale"] = image.Greyscale,
                ["locked"] = image.Locked,
                ["aboveSketch"] = image.AboveSketch,
                ["keepProportions"] = image.KeepProportions,
            };
        })]),
    };
}
