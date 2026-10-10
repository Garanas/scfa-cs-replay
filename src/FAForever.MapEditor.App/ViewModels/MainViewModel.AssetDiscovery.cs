using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Asset discovery (user story F-8): the shelf's missing assets lead to their problems.
/// </summary>
public sealed partial class MainViewModel
{
    private void InitializeAssetDiscovery() => Assets.ShowProblemRequested += missing => ShowMissingAsset(missing);

    /// <summary>
    /// Shows the problem a missing asset is found by (<c>missing-texture</c>, <c>missing-model</c>)
    /// in the Problems tab and goes to it.
    /// </summary>
    /// <returns>The problem's line, or -1 when the checks list none (they have not run since).</returns>
    public int ShowMissingAsset(MissingAsset missing)
    {
        Dock = DockTab.Problems;
        string path = missing.Path;
        for (int i = 0; i < Problems.Items.Count; i++)
        {
            Project.Checks.Problem problem = Problems.Items[i].Problem;
            if (problem.Check == missing.Check && problem.Message.Contains(path, StringComparison.OrdinalIgnoreCase))
            {
                ShowProblem(i);
                return i;
            }
        }
        Status = $"The workspace and the map lack {path}: see the Problems tab";
        return -1;
    }
}
