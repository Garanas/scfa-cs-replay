namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// The commands a tour never runs for the person (user story H-1): those that leave the map, reach
/// outside the project or send something away. A tour's actions run without a person, so they
/// ask nothing; a tour about exporting rings the button and explains it, and the person presses it.
/// </summary>
public static class TourRefusals
{
    /// <summary>
    /// Command ids a tour may not run, by their start: <c>file.</c> opens, imports, creates, saves, exports,
    /// uploads and pushes; <c>workspace.</c> converts and sets up; <c>diagnostics.</c> sends the session's files;
    /// <c>help.</c> folders open the file manager, <c>help.reportProblem</c> sends a report; <c>help.tour</c> starts or
    /// steers a tour (a tour does not run tours); the plugins' reload, switch, fields and checks read and run the workspace's scripts.
    /// </summary>
    private static readonly string[] Prefixes =
    [
        "file.", "workspace.", "diagnostics.", "help.reportProblem", "help.tour", "help.tours",
        "help.workspaceFolder", "help.projectsFolder", "help.projectFolder", "help.presetsFolder", "help.assetsFolder",
        "help.gameFolder", "help.fafDataFolder", "help.exportFolder", "help.diagnosticsFolder",
        "assets.brushesFolder", "plugins.reload", "plugins.enable", "plugins.setField", "plugins.check", "plugins.showFolder",
    ];

    /// <summary>
    /// Why a command may not run in a tour; null when it may.
    /// </summary>
    public static string? Why(string id)
    {
        if (id.StartsWith("plugin.", StringComparison.Ordinal))
        {
            return "a plugin's command runs the workspace's script, which a tour may not start";
        }
        foreach (string prefix in Prefixes)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal))
            {
                return "tours may not run commands that leave the map, reach outside the project or send something away";
            }
        }
        return null;
    }
}
