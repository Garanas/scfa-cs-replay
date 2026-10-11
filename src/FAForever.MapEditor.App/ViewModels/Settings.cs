using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// One field of a panel that shows a setting of the map and changes it through a command (user
/// stories M-1, W-1, L-1, S-2): a number, a text, a colour, a choice, on or off, a texture. A
/// value given (Enter, leaving the field, a slider moved, a box ticked) runs the command with the
/// setting's argument; the command makes it a step of undo, and the steps of one field that follow
/// each other are one step (a slider dragged). <see cref="Refresh"/> shows the value of a version of
/// the document. The templates of the classes are in <c>Controls/SettingList.axaml</c>.
/// </summary>
public abstract partial class Setting : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private readonly string _command;
    private readonly Func<JsonObject>? _fixed;

    /// <param name="command">The command that changes it, e.g. <c>water.set</c>.</param>
    /// <param name="argument">Its argument for the value, e.g. <c>elevation</c>.</param>
    /// <param name="fixedArguments">Arguments the command takes besides the value, such as the layer.</param>
    protected Setting(string label, string command, string argument, Func<string, JsonObject, Task> run, Func<JsonObject>? fixedArguments)
    {
        Label = label;
        _command = command;
        Argument = argument;
        _run = run;
        _fixed = fixedArguments;
    }

    public string Label { get; }

    /// <summary>
    /// What it is for, when its label does not say it all; the field's tool tip and help for screen readers.
    /// </summary>
    public string? Help { get; init; }

    /// <summary>
    /// What the command is given for a value, when not the value itself (an offset worked out from
    /// an absolute height, a range from one of its ends); null gives the value.
    /// </summary>
    public Func<JsonNode?, JsonNode?>? ValueOf { get; init; }

    /// <summary>
    /// The command's argument the value goes to.
    /// </summary>
    public string Argument { get; }

    /// <summary>
    /// Whether the field can be used: a map is open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; private set; }

    /// <summary>
    /// Shows the value the setting has in a version of the document; null without a map.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        IsEnabled = document is not null;
        if (document is not null)
        {
            Show(document);
        }
    }

    protected abstract void Show(MapDocument document);

    /// <summary>
    /// Runs the command with the value.
    /// </summary>
    protected void Give(JsonNode? value)
    {
        JsonObject arguments = _fixed?.Invoke() ?? [];
        arguments[Argument] = ValueOf is null ? value : ValueOf(value);
        _ = _run(_command, arguments);
    }

    /// <summary>
    /// A number as a field shows it: up to four decimals, a point as the separator.
    /// </summary>
    public static string Format(double value) => EditorCulture.Format(value);

    /// <summary>
    /// A number typed into a field (<see cref="EditorCulture.TryParse(string?, out double)"/>): "12.5" or "12,5".
    /// </summary>
    public static bool TryParse(string text, out double value) => EditorCulture.TryParse(text, out value);
}

/// <summary>
/// A number: a field, and a slider when it has a range.
/// </summary>
public sealed partial class NumberSetting(string label, string command, string argument, Func<MapDocument, double> read,
    Func<string, JsonObject, Task> run, Func<JsonObject>? fixedArguments = null) : Setting(label, command, argument, run, fixedArguments)
{
    private string _text = "";
    private double _value;

    public double Minimum { get; init; }

    public double Maximum { get; init; } = 1;

    /// <summary>
    /// Whether a slider goes with the field (from <see cref="Minimum"/> to <see cref="Maximum"/>).
    /// </summary>
    public bool HasSlider { get; init; }

    /// <summary>
    /// The number as text; a number typed is given, anything else shows the value again.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (value == _text)
            {
                return;
            }
            if (!TryParse(value, out double number))
            {
                string shown = _text;
                _text = value;
                OnPropertyChanged();
                _text = shown;
                OnPropertyChanged();
                return;
            }
            Give(number);
        }
    }

    /// <summary>
    /// The number on the slider; moving it gives each value.
    /// </summary>
    public double Value
    {
        get => _value;
        set
        {
            if (Math.Abs(value - _value) < 1e-9)
            {
                return;
            }
            _value = value;
            OnPropertyChanged();
            Give(Math.Round(value, 4));
        }
    }

    protected override void Show(MapDocument document)
    {
        double value = read(document);
        string text = Format(value);
        if (text != _text)
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }
        if (Math.Abs(value - _value) > 1e-9)
        {
            _value = value;
            OnPropertyChanged(nameof(Value));
        }
    }
}

/// <summary>
/// A text, such as a name or a path.
/// </summary>
public sealed partial class TextSetting(string label, string command, string argument, Func<MapDocument, string> read,
    Func<string, JsonObject, Task> run, Func<JsonObject>? fixedArguments = null) : Setting(label, command, argument, run, fixedArguments)
{
    private string _text = "";

    /// <summary>
    /// Whether the field takes several lines, such as a description.
    /// </summary>
    public bool IsLong { get; init; }

    public string Text
    {
        get => _text;
        set
        {
            if (value != _text)
            {
                Give(value);
            }
        }
    }

    protected override void Show(MapDocument document)
    {
        string text = read(document);
        if (text != _text)
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }
    }
}

/// <summary>
/// On or off.
/// </summary>
public sealed partial class BoolSetting(string label, string command, string argument, Func<MapDocument, bool> read,
    Func<string, JsonObject, Task> run, Func<JsonObject>? fixedArguments = null) : Setting(label, command, argument, run, fixedArguments)
{
    private bool _isOn;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (value != _isOn)
            {
                Give(value);
            }
        }
    }

    protected override void Show(MapDocument document)
    {
        bool isOn = read(document);
        if (isOn != _isOn)
        {
            _isOn = isOn;
            OnPropertyChanged(nameof(IsOn));
        }
    }
}

/// <summary>
/// One of a few choices, given as the choice's text.
/// </summary>
public sealed partial class ChoiceSetting(string label, string command, string argument, IReadOnlyList<string> choices, Func<MapDocument, string> read,
    Func<string, JsonObject, Task> run, Func<JsonObject>? fixedArguments = null) : Setting(label, command, argument, run, fixedArguments)
{
    private string? _chosen;
    private IReadOnlyList<string> _choices = choices;

    /// <summary>
    /// The choices, with the map's value at the end when it is none of them (a weather marker's
    /// cloud type the editor does not know), so the field never shows empty.
    /// </summary>
    public IReadOnlyList<string> Choices => _choices;

    /// <summary>
    /// What the command is given for a choice, when not its text (a number: the file version).
    /// </summary>
    public Func<string, JsonNode?>? Value { get; init; }

    public string? Chosen
    {
        get => _chosen;
        set
        {
            if (value is not null && value != _chosen)
            {
                Give(Value is null ? value : Value(value));
            }
        }
    }

    protected override void Show(MapDocument document)
    {
        string value = read(document);
        string chosen = choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase)) ?? value;
        IReadOnlyList<string> shown = choices.Contains(chosen) || chosen.Length == 0 ? choices : [.. choices, chosen];
        if (!shown.SequenceEqual(_choices))
        {
            _choices = shown;
            OnPropertyChanged(nameof(Choices));
        }
        if (chosen != _chosen)
        {
            _chosen = chosen;
            OnPropertyChanged(nameof(Chosen));
        }
    }
}

/// <summary>
/// A colour of the map as three numbers, red, green and blue, from 0 to 1 and beyond (the game's
/// lighting goes past 1): typed as <c>0.95, 0.84, 0.76</c>, given as an array, with a swatch.
/// </summary>
public sealed partial class ColorSetting(string label, string command, string argument, Func<MapDocument, Vector3> read,
    Func<string, JsonObject, Task> run, Func<JsonObject>? fixedArguments = null) : Setting(label, command, argument, run, fixedArguments)
{
    private string _text = "";

    public string Text
    {
        get => _text;
        set
        {
            if (value == _text)
            {
                return;
            }
            if (Parse(value, allowNegative: !ShowsSwatch) is not Vector3 color)
            {
                string shown = _text;
                _text = value;
                OnPropertyChanged();
                _text = shown;
                OnPropertyChanged();
                return;
            }
            Give(new JsonArray(Math.Round(color.X, 4), Math.Round(color.Y, 4), Math.Round(color.Z, 4)));
        }
    }

    /// <summary>
    /// Whether it is a colour with a swatch; three numbers that are no colour (a direction, a
    /// velocity) have none, and may be negative.
    /// </summary>
    public bool ShowsSwatch { get; init; } = true;

    /// <summary>
    /// The colour as the swatch shows it, <c>#rrggbb</c>: each part up to 1 (a brighter colour
    /// shows at full strength).
    /// </summary>
    [ObservableProperty]
    public partial string Swatch { get; private set; } = "#000000";

    /// <summary>
    /// Three numbers between commas or spaces; null when the text is not that.
    /// </summary>
    public static Vector3? Parse(string text, bool allowNegative = false)
    {
        string[] parts = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return null;
        }
        double[] values = new double[3];
        for (int i = 0; i < 3; i++)
        {
            if (!TryParse(parts[i], out values[i]) || values[i] < 0 && !allowNegative)
            {
                return null;
            }
        }
        return new Vector3((float)values[0], (float)values[1], (float)values[2]);
    }

    public static string SwatchOf(Vector3 color)
    {
        static int Byte(float value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
        return $"#{Byte(color.X):x2}{Byte(color.Y):x2}{Byte(color.Z):x2}";
    }

    protected override void Show(MapDocument document)
    {
        Vector3 color = read(document);
        string text = $"{Format(color.X)}, {Format(color.Y)}, {Format(color.Z)}";
        if (text != _text)
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }
        Swatch = SwatchOf(color);
    }
}

/// <summary>
/// The path of a texture the map names, typed, or chosen in the picker when the textures of its
/// kind are assets of the workspace (the strata's).
/// </summary>
/// <param name="choose">What the Choose button runs: a command that opens the picker; null for none.</param>
public sealed partial class TextureSetting(string label, string command, string argument, Func<MapDocument, string> read,
    Func<string, JsonObject, Task> run, Func<JsonObject>? fixedArguments = null, Func<Task>? choose = null) : Setting(label, command, argument, run, fixedArguments)
{
    private string _text = "";

    public string Text
    {
        get => _text;
        set
        {
            if (value != _text)
            {
                Give(value.Trim());
            }
        }
    }

    /// <summary>
    /// Whether the picker can choose it.
    /// </summary>
    public bool CanChoose => choose is not null;

    [RelayCommand]
    private Task Choose() => choose?.Invoke() ?? Task.CompletedTask;

    protected override void Show(MapDocument document)
    {
        string text = read(document);
        if (text != _text)
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }
    }
}

/// <summary>
/// Settings under a heading, as a panel or the inspector lists them (<c>Controls/SettingList</c>).
/// </summary>
public sealed class SettingGroup(string title, IReadOnlyList<Setting> settings)
{
    public string Title => title;

    public IReadOnlyList<Setting> Settings => settings;

    public void Refresh(MapDocument? document)
    {
        foreach (Setting setting in settings)
        {
            setting.Refresh(document);
        }
    }
}
