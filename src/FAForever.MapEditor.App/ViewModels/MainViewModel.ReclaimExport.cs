using System.ComponentModel;
using System.Globalization;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The export step that writes the props' reclaim into the scenario (<c>ScenarioInfo.Reclaim</c>,
/// the totals the lobby shows), in the version the export writes only (<see cref="PropReclaim.IntoScenario"/>),
/// and the Map panel's read-only line saying what it will write. The totals are the props' only:
/// units and wrecks are not counted (the owner's decision, October 2026).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The id of the export step that writes the props' reclaim into the scenario.
    /// </summary>
    public const string ReclaimExportStep = "reclaim";

    /// <summary>
    /// How many of the props the workspace does not know a note names.
    /// </summary>
    private const int ReclaimUnknownNamed = 3;

    private void InitializeReclaimExport()
    {
        Export.Register(new ExportStep(ReclaimExportStep, "Write the props' reclaim into the scenario", ExportStep.GameFilesOrder)
        {
            Applies = run =>
            {
                if (Assets.Catalog.Entries.Count == 0)
                {
                    run.Note = "the workspace has no assets: the map's own totals are kept";
                    return false;
                }
                return true;
            },
            Run = run =>
            {
                run.Document = PropReclaim.IntoScenario(run.Document, Assets.Catalog, out ReclaimTotal total);
                IReadOnlyList<string> unknown = total.Unknown == 0 ? [] : PropReclaim.UnknownBlueprints(run.Document.Props, Assets.Catalog);
                run.Note = ReclaimNote(total, unknown);
                if (total.Unknown > 0)
                {
                    Logger("Export").LogWarning("{Unknown} props have a blueprint the workspace has not and count as 0 in the reclaim: {Blueprints}",
                        total.Unknown, string.Join(", ", unknown));
                }
                return Task.CompletedTask;
            },
        });
        Props.PropertyChanged += OnPropsChangedForReclaim;
        MapInformation.ShowReclaim(Map is null ? null : Props.Reclaim);
    }

    private void OnPropsChangedForReclaim(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PropsViewModel.ReclaimText))
        {
            MapInformation.ShowReclaim(Map is null ? null : Props.Reclaim);
        }
    }

    /// <summary>
    /// What the step says it wrote, e.g. <c>52,400 mass, 410,000 energy from 1,234 props; 2 props
    /// the workspace does not know count as 0: rock07_prop, tree12_prop</c>.
    /// </summary>
    public static string ReclaimNote(ReclaimTotal total, IReadOnlyList<string> unknownBlueprints)
    {
        if (total.Props == 0)
        {
            return "no props: no reclaim written";
        }
        string note = string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(total.Mass):#,0} mass, {Math.Round(total.Energy):#,0} energy from {total.Props:#,0} {(total.Props == 1 ? "prop" : "props")}");
        if (total.Unknown == 0)
        {
            return note;
        }
        string names = string.Join(", ", unknownBlueprints.Take(ReclaimUnknownNamed).Select(Path.GetFileNameWithoutExtension));
        string more = unknownBlueprints.Count > ReclaimUnknownNamed ? $" and {unknownBlueprints.Count - ReclaimUnknownNamed} more" : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{note}; {total.Unknown:#,0} {(total.Unknown == 1 ? "prop" : "props")} the workspace does not know count as 0: {names}{more}");
    }
}
