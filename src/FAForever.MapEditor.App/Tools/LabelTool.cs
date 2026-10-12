using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The Sketch mode's Label tool (user story N-4): a click opens a text box over the view where the label
/// goes; Enter keeps it (written once in the chosen sheet, not repeated by the symmetry), Escape drops it,
/// and a click elsewhere keeps what is there and begins another. The label is drawn in a hand-written
/// style (the font token <c>HandFontFamily</c>), in the colour and size in hand; keys 1 to 6 pick the colour
/// (while the text box does not hold the keyboard). <c>sketch.label</c> makes the label.
/// </summary>
public sealed partial class LabelTool : ObservableObject, IMapTool
{
    private readonly MainViewModel _model;

    public LabelTool(MainViewModel model)
    {
        _model = model;
        Colours = [.. Enum.GetValues<SketchColour>().Select(colour => new SketchColourChoice(colour, ChooseColour, "Label"))];
        Sizes = [.. Enum.GetValues<SketchSize>().Select(size => new SketchSizeChoice(size, ChooseSize, "labels"))];
        Colours[(int)Colour].IsOn = true;
        Sizes[(int)Size].IsOn = true;
        model.Sketch.ChosenChanged += (_, _) => OnPropertyChanged(nameof(SheetText));
    }

    public IReadOnlyList<SketchColourChoice> Colours { get; }

    public IReadOnlyList<SketchSizeChoice> Sizes { get; }

    /// <summary>
    /// The colour in hand.
    /// </summary>
    [ObservableProperty]
    public partial SketchColour Colour { get; set; } = SketchColour.Red;

    /// <summary>
    /// How large the letters are: one of the three choices, or any number of ogrids a command gave.
    /// </summary>
    [ObservableProperty]
    public partial SketchSize Size { get; set; } = SketchSize.Medium;

    /// <summary>
    /// The height of the letters in ogrids a new label has now.
    /// </summary>
    public float Ogrids => Custom ?? SketchLook.LabelOgrids(Size);

    /// <summary>
    /// A size in ogrids given by <c>sketch.setLabel</c>; null while one of the choices is in hand.
    /// </summary>
    public float? Custom { get; set; }

    partial void OnColourChanged(SketchColour value)
    {
        foreach (SketchColourChoice choice in Colours)
        {
            choice.IsOn = choice.Colour == value;
        }
    }

    partial void OnSizeChanged(SketchSize value)
    {
        Custom = null;
        foreach (SketchSizeChoice choice in Sizes)
        {
            choice.IsOn = choice.Size == value;
        }
    }

    private Task ChooseColour(SketchColour colour) => _model.RunWithAsync("sketch.setLabel", new JsonObject { ["colour"] = colour.ToString() });

    private Task ChooseSize(SketchSize size) => _model.RunWithAsync("sketch.setLabel", new JsonObject { ["size"] = size.ToString() });

    public string SheetText => _model.Sketch.Chosen is string sheet ? $"Sheet: {sheet}" : "No sheet yet: the first label makes one";

    public double CursorRadius => 0;

    public bool Mirrors => false;

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        if (_model.ShownDocument is null)
        {
            return;
        }
        if (input.View is { IsTopDown: false })
        {
            _model.Status = "Write in the top view: the 3D view does not take the Label tool yet";
            return;
        }
        Vector2 screen = input.ViewAt(x, z).ToScreen(new Vector3(x, 0, z)) ?? Vector2.Zero;
        string sheet = _model.Sketch.Chosen ?? "";
        _ = _model.Sketch.BeginLabel(new SketchLabelDraft(new Vector2(x, z), screen, Colour, Ogrids, sheet, null), "");
    }

    public void Drag(float x, float z)
    {
    }

    public void Release()
    {
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (modifiers != ToolModifiers.None)
        {
            return false;
        }
        if (key == "Escape" && _model.Sketch.IsWriting)
        {
            _model.Sketch.Drop();
            _model.Status = "Dropped the label";
            return true;
        }
        string digit = key.Length == 2 && key[0] == 'D' ? key[1..] : key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 ? key[6..] : "";
        if (digit.Length == 1 && digit[0] is >= '1' and <= '6')
        {
            _ = _model.RunWithAsync("sketch.setLabel", new JsonObject { ["colour"] = (int)(digit[0] - '0') });
            return true;
        }
        return false;
    }
}
