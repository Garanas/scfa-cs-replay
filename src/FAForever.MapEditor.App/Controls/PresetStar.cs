using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The star on a unit preset's row (user story U-9): on while the preset is starred, a click turns it
/// (<c>presets.star</c>). Looks are <c>Button.chip.icononly</c> and <c>.on</c>.
/// </summary>
public sealed class PresetStar : Button
{
    public static readonly StyledProperty<Preset?> PresetProperty =
        AvaloniaProperty.Register<PresetStar, Preset?>(nameof(Preset));

    public static readonly StyledProperty<PresetsViewModel?> PresetsProperty =
        AvaloniaProperty.Register<PresetStar, PresetsViewModel?>(nameof(Presets));

    private PresetsViewModel? _watching;

    public PresetStar()
    {
        Classes.Add("chip");
        Classes.Add("icononly");
        Avalonia.Controls.Shapes.Path star = new Avalonia.Controls.Shapes.Path { Classes = { "icon", "small" } };
        if (Application.Current?.TryFindResource("Icon.Star", out object? geometry) == true && geometry is Geometry data)
        {
            star.Data = data;
        }
        Content = star;
        Show();
    }

    public Preset? Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public PresetsViewModel? Presets
    {
        get => GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PresetsProperty)
        {
            if (_watching is not null)
            {
                _watching.StarsChanged -= Show;
            }
            _watching = Presets;
            if (_watching is not null)
            {
                _watching.StarsChanged += Show;
            }
        }
        if (change.Property == PresetProperty || change.Property == PresetsProperty)
        {
            Show();
        }
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Preset is Preset preset && Presets is PresetsViewModel presets)
        {
            _ = presets.ToggleStar(preset);
        }
    }

    private void Show()
    {
        bool starred = Preset is Preset preset && Presets?.IsStarred(preset) == true;
        Classes.Set("on", starred);
        string name = Preset?.Name ?? "";
        string text = starred ? $"Take the star of the preset {name} away" : $"Star the preset {name}";
        AutomationProperties.SetName(this, text);
        ToolTip.SetTip(this, starred ? "Starred: the Starred filter shows it" : "Star it, to find it under Starred");
    }
}
