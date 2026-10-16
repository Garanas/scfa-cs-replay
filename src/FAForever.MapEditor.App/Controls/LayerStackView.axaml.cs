using Avalonia;
using Avalonia.Controls;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The reusable stack control: a <see cref="LayerStackViewModel"/> as a list of layers with a
/// preview each (<see cref="ImagePreview"/>), show and lock boxes, buttons to add a layer by
/// source, move and remove the chosen one, and, with <see cref="ShowsSettings"/>, the chosen
/// layer's settings under the list (in an inspector). The heightmap's layers in the Terrain panel
/// are its first user (their settings are in the inspector).
/// </summary>
public sealed partial class LayerStackView : UserControl
{
    public static readonly StyledProperty<string?> ListHelpProperty = AvaloniaProperty.Register<LayerStackView, string?>(nameof(ListHelp));

    public static readonly StyledProperty<string?> EmptyTextProperty = AvaloniaProperty.Register<LayerStackView, string?>(nameof(EmptyText));

    public static readonly StyledProperty<string?> RemoveHelpProperty = AvaloniaProperty.Register<LayerStackView, string?>(nameof(RemoveHelp));

    public static readonly StyledProperty<bool> CanAddProperty = AvaloniaProperty.Register<LayerStackView, bool>(nameof(CanAdd), true);

    public static readonly StyledProperty<bool> ShowsSettingsProperty = AvaloniaProperty.Register<LayerStackView, bool>(nameof(ShowsSettings));

    public static readonly DirectProperty<LayerStackView, bool> ShowsChosenSettingsProperty =
        AvaloniaProperty.RegisterDirect<LayerStackView, bool>(nameof(ShowsChosenSettings), control => control.ShowsChosenSettings);

    private bool _showsChosenSettings;
    private LayerStackViewModel? _stack;

    public LayerStackView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The list's help for screen readers: what the chosen layer is for.
    /// </summary>
    public string? ListHelp
    {
        get => GetValue(ListHelpProperty);
        set => SetValue(ListHelpProperty, value);
    }

    /// <summary>
    /// What shows while the stack has no layers.
    /// </summary>
    public string? EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>
    /// The help of the remove button.
    /// </summary>
    public string? RemoveHelp
    {
        get => GetValue(RemoveHelpProperty);
        set => SetValue(RemoveHelpProperty, value);
    }

    /// <summary>
    /// Whether the add buttons can be used (a map is open).
    /// </summary>
    public bool CanAdd
    {
        get => GetValue(CanAddProperty);
        set => SetValue(CanAddProperty, value);
    }

    /// <summary>
    /// Whether the chosen layer's settings show under the list.
    /// </summary>
    public bool ShowsSettings
    {
        get => GetValue(ShowsSettingsProperty);
        set => SetValue(ShowsSettingsProperty, value);
    }

    /// <summary>
    /// Whether the settings show now: asked for, and there is a layer.
    /// </summary>
    public bool ShowsChosenSettings
    {
        get => _showsChosenSettings;
        private set => SetAndRaise(ShowsChosenSettingsProperty, ref _showsChosenSettings, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowsSettingsProperty)
        {
            Update();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_stack is not null)
        {
            _stack.PropertyChanged -= OnStackChanged;
        }
        _stack = DataContext as LayerStackViewModel;
        if (_stack is not null)
        {
            _stack.PropertyChanged += OnStackChanged;
        }
        Update();
    }

    private void OnStackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerStackViewModel.HasLayers) || e.PropertyName == nameof(LayerStackViewModel.Items))
        {
            Update();
        }
    }

    private void Update() => ShowsChosenSettings = ShowsSettings && _stack?.HasLayers == true;
}
