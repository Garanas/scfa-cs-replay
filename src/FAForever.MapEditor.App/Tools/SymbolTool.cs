using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The Sketch mode's Symbol tool (user story N-4): a click stamps the symbol in hand (a spawn, a mass
/// point, a hydrocarbon, an arrow, a cross or a question mark) in the colour and size in hand into the
/// chosen sheet. An arrow takes the way the mouse is dragged, else the Turn in the row. With Mirror the
/// symbol is repeated by the map's symmetry (an arrow turned with it). The symbol is shown under the
/// mouse while it is down and becomes one step of undo when it is released: the command
/// <c>sketch.symbol</c> makes it. Keys 1 to 6 pick the colour.
/// </summary>
public sealed partial class SymbolTool : ObservableObject, IMapTool
{
    private readonly MainViewModel _model;
    private bool _down;
    private Vector2 _at;
    private float _turn;
    private bool _turned;
    private float _pixel = 0.5f;

    public SymbolTool(MainViewModel model)
    {
        _model = model;
        Kinds = [.. Enum.GetValues<SketchSymbolKind>().Select(kind => new SketchSymbolChoice(kind, ChooseKind))];
        Colours = [.. Enum.GetValues<SketchColour>().Select(colour => new SketchColourChoice(colour, ChooseColour, "Symbol"))];
        Sizes = [.. Enum.GetValues<SketchSize>().Select(size => new SketchSizeChoice(size, ChooseSize, "symbols"))];
        Kinds[(int)Kind].IsOn = true;
        Colours[(int)Colour].IsOn = true;
        Sizes[(int)Size].IsOn = true;
        model.Sketch.ChosenChanged += (_, _) => OnPropertyChanged(nameof(SheetText));
    }

    /// <summary>
    /// The six symbols, in the order of <see cref="SketchSymbolKind"/>.
    /// </summary>
    public IReadOnlyList<SketchSymbolChoice> Kinds { get; }

    public IReadOnlyList<SketchColourChoice> Colours { get; }

    public IReadOnlyList<SketchSizeChoice> Sizes { get; }

    /// <summary>
    /// The symbol in hand.
    /// </summary>
    [ObservableProperty]
    public partial SketchSymbolKind Kind { get; set; } = SketchSymbolKind.Spawn;

    /// <summary>
    /// The colour in hand.
    /// </summary>
    [ObservableProperty]
    public partial SketchColour Colour { get; set; } = SketchColour.Red;

    /// <summary>
    /// How large the symbol is made: one of the three choices, or any number of ogrids a command gave.
    /// </summary>
    [ObservableProperty]
    public partial SketchSize Size { get; set; } = SketchSize.Medium;

    /// <summary>
    /// The size in ogrids a stamp has now: a size a command gave, else the choice's.
    /// </summary>
    public float Ogrids => Custom ?? SketchLook.SymbolOgrids(Size);

    /// <summary>
    /// A size in ogrids given by <c>sketch.setSymbol</c>; null while one of the choices is in hand.
    /// </summary>
    public float? Custom { get; set; }

    /// <summary>
    /// Whether a symbol is repeated by the map's symmetry (while it mirrors edits).
    /// </summary>
    [ObservableProperty]
    public partial bool Mirror { get; set; } = true;

    /// <summary>
    /// The way an arrow points when it is clicked rather than dragged, in degrees: 0 to +x, clockwise on the top view.
    /// </summary>
    [ObservableProperty]
    public partial float Turn { get; set; }

    /// <summary>
    /// The turn as the options row's field holds it.
    /// </summary>
    public string TurnText
    {
        get => Turn.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float turn) && float.IsFinite(turn))
            {
                Turn = SketchMarkEdits.Normalized(turn);
            }
            OnPropertyChanged();
        }
    }

    partial void OnTurnChanged(float value) => OnPropertyChanged(nameof(TurnText));

    partial void OnKindChanged(SketchSymbolKind value)
    {
        foreach (SketchSymbolChoice choice in Kinds)
        {
            choice.IsOn = choice.Kind == value;
        }
        OnPropertyChanged(nameof(IsArrow));
    }

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

    /// <summary>
    /// Whether the symbol in hand has a direction: the row shows the Turn field.
    /// </summary>
    public bool IsArrow => Kind == SketchSymbolKind.Arrow;

    private Task ChooseKind(SketchSymbolKind kind) => _model.RunWithAsync("sketch.setSymbol", new JsonObject { ["kind"] = kind.ToString() });

    private Task ChooseColour(SketchColour colour) => _model.RunWithAsync("sketch.setSymbol", new JsonObject { ["colour"] = colour.ToString() });

    private Task ChooseSize(SketchSize size) => _model.RunWithAsync("sketch.setSymbol", new JsonObject { ["size"] = size.ToString() });

    /// <summary>
    /// What the options row says of the sheet stamped into.
    /// </summary>
    public string SheetText => _model.Sketch.Chosen is string sheet ? $"Sheet: {sheet}" : "No sheet yet: the first symbol makes one";

    public double CursorRadius => 0;

    public bool Mirrors => Mirror;

    /// <summary>
    /// Whether a symbol is being placed (the mouse is down).
    /// </summary>
    public bool IsPlacing => _down;

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        if (_model.ShownDocument is null)
        {
            return;
        }
        if (input.View is { IsTopDown: false })
        {
            _model.Status = "Stamp in the top view: the 3D view does not take the Symbol tool yet";
            return;
        }
        _down = true;
        _at = new Vector2(x, z);
        _turn = Turn;
        _turned = false;
        _pixel = input.OgridsPerPixel;
        Show();
    }

    public void Drag(float x, float z) => Drag(x, z, ToolInput.Default);

    public void Drag(float x, float z, ToolInput input)
    {
        if (!_down)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("tool.sketch-symbol");
        Vector2 way = new Vector2(x, z) - _at;
        // an arrow follows the mouse once it is a few pixels from where it was put; the others stay put under it
        if (Kind == SketchSymbolKind.Arrow && way.Length() > 4 * _pixel)
        {
            _turn = SketchMarkEdits.Normalized(MathF.Atan2(way.Y, way.X) * 180 / MathF.PI);
            _turned = true;
            Show();
        }
    }

    public void Release()
    {
        if (!_down)
        {
            return;
        }
        _down = false;
        JsonObject arguments = new JsonObject
        {
            ["kind"] = Kind.ToString(),
            ["x"] = Math.Round(_at.X, 2),
            ["z"] = Math.Round(_at.Y, 2),
            ["colour"] = Colour.ToString(),
            ["size"] = Math.Round(Ogrids, 2),
            ["turn"] = Math.Round(_turn, 1),
            ["mirror"] = Mirror,
        };
        // the command makes the symbol; the one shown meanwhile goes when it is part of the sketch
        Task ran = _model.RunWithAsync("sketch.symbol", arguments);
        if (ran.IsCompleted)
        {
            _model.Sketch.SetPendingSymbols([]);
        }
        else
        {
            SynchronizationContext? ui = SynchronizationContext.Current;
            _ = ran.ContinueWith(_ => UiThread.Run(ui, () => _model.Sketch.SetPendingSymbols([])), TaskScheduler.Default);
        }
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape" && _down)
        {
            _down = false;
            _model.Sketch.SetPendingSymbols([]);
            _model.Status = "Cancelled the symbol";
            return true;
        }
        if (modifiers != ToolModifiers.None)
        {
            return false;
        }
        string digit = key.Length == 2 && key[0] == 'D' ? key[1..] : key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 ? key[6..] : "";
        if (digit.Length == 1 && digit[0] is >= '1' and <= '6')
        {
            _ = _model.RunWithAsync("sketch.setSymbol", new JsonObject { ["colour"] = (int)(digit[0] - '0') });
            return true;
        }
        return false;
    }

    /// <summary>
    /// The symbols a stamp at a place makes, with its images by the symmetry, as the command makes them.
    /// </summary>
    internal IReadOnlyList<SketchSymbol> Symbols(MapDocument document, Vector2 place, float turn, bool mirror, SketchSymbolKind kind, SketchColour colour, float size) =>
        SketchMarkEdits.WithImages(new SketchSymbol(kind, place, colour, size, turn), mirror ? _model.EditTransforms(document) : []);

    // the symbol being placed, with its images, for the views
    private void Show()
    {
        if (_model.ShownDocument is not MapDocument document)
        {
            return;
        }
        _model.Sketch.SetPendingSymbols(Symbols(document, _at, _turn, Mirror, Kind, Colour, Ogrids));
    }

    /// <summary>
    /// What the tool says for automation: whether the way of an arrow was dragged.
    /// </summary>
    public JsonObject Describe() => new JsonObject { ["placing"] = _down, ["turned"] = _turned };
}
