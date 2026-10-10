using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// Mass and energy, each after its icon (<c>Icon.Mass</c> in <c>MassBrush</c>, <c>Icon.Energy</c>
/// in <c>EnergyBrush</c>), at the size of its text: a unit's build cost, a prop's reclaim, a total
/// of reclaim (user story F-8). Its look is the control theme in <c>Styles/Controls.axaml</c>;
/// a screen reader hears it in words (<see cref="Label"/>), e.g. <c>5,220 mass, 47,400 energy</c>.
/// </summary>
public sealed class EconomyAmount : TemplatedControl
{
    public static readonly StyledProperty<double> MassProperty = AvaloniaProperty.Register<EconomyAmount, double>(nameof(Mass));

    public static readonly StyledProperty<double> EnergyProperty = AvaloniaProperty.Register<EconomyAmount, double>(nameof(Energy));

    public static readonly StyledProperty<bool> IsShortProperty = AvaloniaProperty.Register<EconomyAmount, bool>(nameof(IsShort));

    public static readonly StyledProperty<string?> PrefixProperty = AvaloniaProperty.Register<EconomyAmount, string?>(nameof(Prefix));

    public static readonly StyledProperty<string?> SuffixProperty = AvaloniaProperty.Register<EconomyAmount, string?>(nameof(Suffix));

    public static readonly StyledProperty<string?> EmptyTextProperty = AvaloniaProperty.Register<EconomyAmount, string?>(nameof(EmptyText));

    public static readonly DirectProperty<EconomyAmount, string> MassTextProperty =
        AvaloniaProperty.RegisterDirect<EconomyAmount, string>(nameof(MassText), amount => amount.MassText);

    public static readonly DirectProperty<EconomyAmount, string> EnergyTextProperty =
        AvaloniaProperty.RegisterDirect<EconomyAmount, string>(nameof(EnergyText), amount => amount.EnergyText);

    public static readonly DirectProperty<EconomyAmount, bool> ShowsValuesProperty =
        AvaloniaProperty.RegisterDirect<EconomyAmount, bool>(nameof(ShowsValues), amount => amount.ShowsValues);

    public static readonly DirectProperty<EconomyAmount, bool> ShowsEmptyProperty =
        AvaloniaProperty.RegisterDirect<EconomyAmount, bool>(nameof(ShowsEmpty), amount => amount.ShowsEmpty);

    private string _massText = "0";
    private string _energyText = "0";
    private bool _showsValues = true;
    private bool _showsEmpty;

    public EconomyAmount()
    {
        Update();
    }

    /// <summary>
    /// The mass.
    /// </summary>
    public double Mass
    {
        get => GetValue(MassProperty);
        set => SetValue(MassProperty, value);
    }

    /// <summary>
    /// The energy.
    /// </summary>
    public double Energy
    {
        get => GetValue(EnergyProperty);
        set => SetValue(EnergyProperty, value);
    }

    /// <summary>
    /// Whether the values are shown in a few characters (<c>5.2k</c>, a tile's) or in full (<c>5,220</c>).
    /// </summary>
    public bool IsShort
    {
        get => GetValue(IsShortProperty);
        set => SetValue(IsShortProperty, value);
    }

    /// <summary>
    /// A text before the values, e.g. an area's name.
    /// </summary>
    public string? Prefix
    {
        get => GetValue(PrefixProperty);
        set => SetValue(PrefixProperty, value);
    }

    /// <summary>
    /// A text after the values, e.g. <c>in 45 props</c>.
    /// </summary>
    public string? Suffix
    {
        get => GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    /// <summary>
    /// What it shows instead of the values when both are zero (<c>No cost</c>); null shows the zeros.
    /// </summary>
    public string? EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public string MassText { get => _massText; private set => SetAndRaise(MassTextProperty, ref _massText, value); }

    public string EnergyText { get => _energyText; private set => SetAndRaise(EnergyTextProperty, ref _energyText, value); }

    public bool ShowsValues { get => _showsValues; private set => SetAndRaise(ShowsValuesProperty, ref _showsValues, value); }

    public bool ShowsEmpty { get => _showsEmpty; private set => SetAndRaise(ShowsEmptyProperty, ref _showsEmpty, value); }

    /// <summary>
    /// What it shows, as text: <c>5.2k 47.4k</c> (what automation's <c>ui</c> reads).
    /// </summary>
    public string Text => Join(Prefix, ShowsEmpty ? EmptyText : $"{MassText} {EnergyText}", Suffix);

    /// <summary>
    /// What a screen reader says: <c>5,220 mass, 47,400 energy</c>, always in full.
    /// </summary>
    public string Label => Join(Prefix, ShowsEmpty ? EmptyText : Words(Mass, Energy), Suffix);

    /// <summary>
    /// Mass and energy in words, in full: <c>5,220 mass, 47,400 energy</c>.
    /// </summary>
    public static string Words(double mass, double energy) => $"{Full(mass)} mass, {Full(energy)} energy";

    /// <summary>
    /// A value in full: <c>5,220</c>, <c>0.27</c>.
    /// </summary>
    public static string Full(double value) => value.ToString("#,0.##", CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : "0";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MassProperty || change.Property == EnergyProperty || change.Property == IsShortProperty
            || change.Property == PrefixProperty || change.Property == SuffixProperty || change.Property == EmptyTextProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        MassText = IsShort ? AssetSearch.Short(Mass) : Full(Mass);
        EnergyText = IsShort ? AssetSearch.Short(Energy) : Full(Energy);
        ShowsEmpty = EmptyText is { Length: > 0 } && Mass == 0 && Energy == 0;
        ShowsValues = !ShowsEmpty;
        // a name of its own set in a view wins; otherwise the values in words
        if (!IsSet(AutomationProperties.NameProperty) || GetValue(AutomationProperties.NameProperty) == _label)
        {
            _label = Label;
            SetCurrentValue(AutomationProperties.NameProperty, _label);
        }
    }

    private string? _label;

    private static string Join(params string?[] parts) => string.Join(" ", parts.Where(part => part is { Length: > 0 }));
}
