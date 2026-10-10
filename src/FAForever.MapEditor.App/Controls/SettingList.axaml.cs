using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The fields of a <see cref="ViewModels.SettingGroup"/>, its data context: a field of class
/// <c>commit</c> gives its value when it loses the keyboard, and on Enter.
/// </summary>
public sealed partial class SettingList : UserControl
{
    public static readonly StyledProperty<bool> ShowsTitleProperty = AvaloniaProperty.Register<SettingList, bool>(nameof(ShowsTitle), true);

    public SettingList()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Whether the group's title shows above its fields (a panel's group has a heading of its own).
    /// </summary>
    public bool ShowsTitle
    {
        get => GetValue(ShowsTitleProperty);
        set => SetValue(ShowsTitleProperty, value);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is TextBox field && field.Classes.Contains("commit"))
        {
            BindingOperations.GetBindingExpressionBase(field, TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
