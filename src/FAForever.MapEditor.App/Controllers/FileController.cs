using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// The File menu: opening, saving and exporting maps.
/// </summary>
public sealed class FileController(MainViewModel model)
{
    public bool CanStart => model.CanStart;

    public bool CanSave => model.CanSave;

    [Incomplete("M-2", "Make a map from scratch: size, starting height, water, biome or template")]
    [EditorCommand("file.new", "New map...", Menu = "File", Gesture = "Ctrl+N", Story = "M-2")]
    public void NewMap()
    {
    }

    [EditorCommand("file.open", "Open project...", Menu = "File", Gesture = "Ctrl+O", Story = "M-7", CanRun = nameof(CanStart))]
    public Task Open() => model.OpenProjectCommand.ExecuteAsync(null);

    [EditorCommand("file.import", "Import map folder...", Menu = "File", Gesture = "Ctrl+I", Story = "M-7", CanRun = nameof(CanStart))]
    public Task Import() => model.ImportMapCommand.ExecuteAsync(null);

    [EditorCommand("file.save", "Save", Menu = "File", Gesture = "Ctrl+S", Story = "M-7", CanRun = nameof(CanSave), SeparatorBefore = true)]
    public Task Save() => model.SaveProjectCommand.ExecuteAsync(null);

    [EditorCommand("file.saveAs", "Save as project...", Menu = "File", Gesture = "Ctrl+Shift+S", Story = "M-7", CanRun = nameof(CanSave))]
    public Task SaveAs() => model.SaveProjectAsCommand.ExecuteAsync(null);

    [EditorCommand("file.export", "Export to the vault", Menu = "File", Gesture = "Ctrl+E", Story = "M-7", CanRun = nameof(CanSave), SeparatorBefore = true)]
    public Task Export() => model.ExportMapCommand.ExecuteAsync(null);

    [Incomplete("M-8", "Render the map's preview image")]
    [EditorCommand("file.renderPreview", "Render the preview", Menu = "File", Story = "M-8")]
    public void RenderPreview()
    {
    }

    [Incomplete("M-8", "Upload the exported map to the FAF vault")]
    [EditorCommand("file.upload", "Upload to the vault...", Menu = "File", Story = "M-8")]
    public void Upload()
    {
    }

    [Incomplete("M-6", "Open the map's script in the user's editor")]
    [EditorCommand("file.openScript", "Open the script", Menu = "File", Story = "M-6", SeparatorBefore = true)]
    public void OpenScript()
    {
    }
}
