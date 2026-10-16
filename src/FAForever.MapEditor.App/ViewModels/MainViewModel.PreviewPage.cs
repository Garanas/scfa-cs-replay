using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The map's preview page (user story M-11): generated into a folder the map maker chooses, from the
/// map as it is, with the workspace's textures and blueprints.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The folder the preview page was generated into last in this session; the folder dialog starts there.
    /// </summary>
    public string? PreviewPageFolder { get; private set; }

    /// <returns>The page's file.</returns>
    public async Task<string> GeneratePreviewPageAsync(string folder)
    {
        MapDocument document = RequireMap().Document;
        Workspace? workspace = Setup.Workspace;
        AssetCatalog catalog = Assets.Catalog;
        EditorStamp stamp = EditorStamp.Now();
        string page = "";
        await BusyAsync("previewPage", "Generating the preview page...", async () =>
        {
            page = await Task.Run(() =>
            {
                using Performance.Span measure = Performance.Measure("model.preview-page");
                return PreviewPageFiles.Write(document, folder, workspace, catalog, stamp);
            });
        });
        PreviewPageFolder = Path.GetFullPath(folder);
        Status = catalog.Entries.Count == 0
            ? $"Generated the preview page {page}, without reclaim: the workspace has no assets"
            : $"Generated the preview page {page}";
        return page;
    }
}
