using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// A folder to choose: a heading, what the folder is for, its path and a button to browse for
/// it. The path box is named <see cref="Label"/> for screen readers, the button "Browse for"
/// it.
/// </summary>
public sealed partial class FolderField : UserControl
{
    public static readonly StyledProperty<string?> HeadingProperty = AvaloniaProperty.Register<FolderField, string?>(nameof(Heading));

    public static readonly StyledProperty<string?> HelpProperty = AvaloniaProperty.Register<FolderField, string?>(nameof(Help));

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<FolderField, string?>(nameof(Label));

    public static readonly StyledProperty<string?> FolderProperty =
        AvaloniaProperty.Register<FolderField, string?>(nameof(Folder), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> BrowseCommandProperty = AvaloniaProperty.Register<FolderField, ICommand?>(nameof(BrowseCommand));

    public FolderField()
    {
        InitializeComponent();
    }

    /// <summary>
    /// What the folder is, e.g. <c>Vault</c>.
    /// </summary>
    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    /// <summary>
    /// What the folder is for and holds.
    /// </summary>
    public string? Help
    {
        get => GetValue(HelpProperty);
        set => SetValue(HelpProperty, value);
    }

    /// <summary>
    /// The name a screen reader says for the path, e.g. <c>vault folder</c>.
    /// </summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Folder
    {
        get => GetValue(FolderProperty);
        set => SetValue(FolderProperty, value);
    }

    public ICommand? BrowseCommand
    {
        get => GetValue(BrowseCommandProperty);
        set => SetValue(BrowseCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
        {
            AutomationProperties.SetName(Path, Label is { Length: > 0 } label ? char.ToUpperInvariant(label[0]) + label[1..] : null);
            AutomationProperties.SetName(Browse, Label is { Length: > 0 } ? $"Browse for the {Label}" : null);
        }
    }
}
