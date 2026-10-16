using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// Version control of the project, over the window (File, Commit): its setup, then the commit.
/// </summary>
public sealed partial class VersionControlPage : UserControl
{
    public VersionControlPage()
    {
        InitializeComponent();
        // the message is what one types next
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && IsVisible && Message.IsEffectivelyVisible)
            {
                Message.Focus();
            }
        };
    }
}
