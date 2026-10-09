using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// The Help menu: what goes well and wrong while the editor runs (user story F-17).
/// </summary>
public sealed class HelpController(MainViewModel model)
{
    [EditorCommand("help.diagnostics", "Diagnostics...", Menu = "Help", Story = "F-17")]
    public void Diagnostics() => model.OpenDiagnostics();

    [EditorCommand("help.diagnosticsFolder", "Show the diagnostics folder", Menu = "Help", Story = "F-17")]
    public void DiagnosticsFolder() => model.ShowDiagnosticsFolder();

    [Incomplete("F-17", "Describe a problem and bundle it with the recent sessions' logs and metrics into a file to attach to an issue, or send it")]
    [EditorCommand("help.reportProblem", "Report a problem...", Menu = "Help", Story = "F-17", SeparatorBefore = true)]
    public void ReportProblem()
    {
    }
}
