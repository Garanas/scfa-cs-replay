using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The badges after a unit preset in the Units mode's list (user story U-9): how often it is placed
/// on the open map (<c>×2 placed</c>) and how many of those instances are out of date
/// (<c>1 out of date</c>: the preset changed since). Nothing shows for a preset placed nowhere.
/// The looks are <c>Border.badge.instance</c> and <c>Border.badge.stale</c>.
/// </summary>
public sealed class PresetInstanceBadges : StackPanel
{
    public static readonly StyledProperty<string?> PresetProperty =
        AvaloniaProperty.Register<PresetInstanceBadges, string?>(nameof(Preset));

    public static readonly StyledProperty<IReadOnlyDictionary<string, PresetInstanceCount>?> CountsProperty =
        AvaloniaProperty.Register<PresetInstanceBadges, IReadOnlyDictionary<string, PresetInstanceCount>?>(nameof(Counts));

    private readonly Border _placed = new Border();
    private readonly Border _outOfDate = new Border();
    private readonly TextBlock _placedText = new TextBlock();
    private readonly TextBlock _outOfDateText = new TextBlock();

    public PresetInstanceBadges()
    {
        Orientation = Orientation.Horizontal;
        _placed.Classes.Add("badge");
        _placed.Classes.Add("instance");
        _placed.Child = _placedText;
        _outOfDate.Classes.Add("badge");
        _outOfDate.Classes.Add("stale");
        _outOfDate.Child = _outOfDateText;
        Children.Add(_outOfDate);
        Children.Add(_placed);
        Show();
    }

    /// <summary>
    /// The preset's name.
    /// </summary>
    public string? Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    /// <summary>
    /// The counts per preset name (<see cref="PresetsViewModel.Instances"/>).
    /// </summary>
    public IReadOnlyDictionary<string, PresetInstanceCount>? Counts
    {
        get => GetValue(CountsProperty);
        set => SetValue(CountsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PresetProperty || change.Property == CountsProperty)
        {
            Show();
        }
    }

    private void Show()
    {
        PresetInstanceCount count = Preset is string preset && Counts is not null && Counts.TryGetValue(preset, out PresetInstanceCount found) ? found : new PresetInstanceCount(0, 0);
        _placed.IsVisible = count.Placed > 0;
        _placedText.Text = $"×{count.Placed} placed";
        _outOfDate.IsVisible = count.OutOfDate > 0;
        _outOfDateText.Text = $"{count.OutOfDate} out of date";
        IsVisible = count.Placed > 0;
        AutomationProperties.SetName(this, count.Placed == 0 ? "Placed nowhere on this map"
            : $"Placed {count.Placed} {(count.Placed == 1 ? "time" : "times")} on this map" + (count.OutOfDate == 0 ? "" : $", {count.OutOfDate} out of date"));
    }
}
