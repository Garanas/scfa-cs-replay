using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// Reporting a problem, over the window (Help, Report a problem).
/// </summary>
public sealed partial class ReportProblemPage : UserControl
{
    public ReportProblemPage()
    {
        InitializeComponent();
        // the description is what one types first
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && IsVisible)
            {
                Description.Focus();
            }
        };
    }
}
