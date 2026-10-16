using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Copies files into the map's own files (user story D-1), one step of undo: a map maker's own decals
/// and decal sets (<c>rock01_albedo.png</c>, <c>rock01_normals.png</c>, <c>rock01_mask.png</c>), which
/// the shelf and the picker then list under the source This map, ready to place. The project keeps
/// them in its <c>files/</c> folder; the export writes them into the map's folder (a decal set's masks
/// excepted) and converts pictures to DDS.
/// </summary>
/// <param name="Files">The files without asking; left out, a dialog asks for them.</param>
/// <param name="Folder">The folder in the map's folder they go to; left out, <c>env/decals</c> for pictures
/// and the map's folder itself for anything else.</param>
/// <returns>The files as the map's folder names them (<c>env/decals/rock01_albedo.png</c>).</returns>
[EditorCommand("map.addFiles", "Add files to the map...", Story = "D-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddMapFiles(string[]? Files = null, string? Folder = null) : IRequest<IReadOnlyList<string>>;

public sealed class AddMapFilesHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<AddMapFiles, IReadOnlyList<string>>
{
    /// <summary>
    /// The folder of a map's own decals, where pictures go unless told otherwise.
    /// </summary>
    public const string DecalsFolder = "env/decals";

    /// <summary>
    /// What the dialog offers: pictures, and the Lua and blueprint files a map's script may load.
    /// </summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".dds", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".lua", ".bp"];

    public async Task<IReadOnlyList<string>> Handle(AddMapFiles request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string? folder = request.Folder is string given ? FolderOf(given) : null;
        IReadOnlyList<string> paths = request.Files is { Length: > 0 } named
            ? named
            : await dialogs.PickFilesAsync("Add files to the map: decals, decal sets with their masks, ...", Extensions, model.Map?.ProjectFolder);
        if (paths.Count == 0)
        {
            model.Status = "Added no files";
            return [];
        }
        Dictionary<string, byte[]> added = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                throw new ArgumentException($"There is no file {path}");
            }
            string into = folder ?? (MapOwnAssets.IsPicture(path) ? DecalsFolder : "");
            string name = Path.GetFileName(path);
            added[into.Length == 0 ? name : $"{into}/{name}"] = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        int replaced = added.Keys.Count(path => document.Files.Keys.Any(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase)));
        model.EditMap(added.Count == 1 ? $"Add {added.Keys.First()} to the map" : $"Add {added.Count} files to the map", d =>
        {
            var files = d.Files;
            foreach ((string path, byte[] bytes) in added)
            {
                // a file of that name in another case is replaced, as the game's folders know no case
                if (files.Keys.FirstOrDefault(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase)) is string old)
                {
                    files = files.Remove(old);
                }
                files = files.SetItem(path, System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(bytes));
            }
            return d with { Files = files };
        });
        model.Logger("Files").LogInformation("Added {Count} files to the map's own files: {Files}", added.Count, string.Join(", ", added.Keys));
        model.Status = $"Added {(added.Count == 1 ? added.Keys.First() : $"{added.Count} files")} to the map's own files"
            + (replaced > 0 ? $" ({replaced} replaced)" : "")
            + (added.Keys.Any(MapOwnAssets.IsPicture) ? "; the shelf lists its decals under This map" : "");
        return [.. added.Keys];
    }

    /// <summary>
    /// A folder in the map's folder as the map names it: relative, with <c>/</c> between folders.
    /// </summary>
    /// <exception cref="ArgumentException">A folder outside the map's folder.</exception>
    private static string FolderOf(string folder)
    {
        string normalized = folder.Replace('\\', '/').Trim().Trim('/');
        if (Path.IsPathRooted(folder) || normalized.Split('/').Any(part => part is ".." or "."))
        {
            throw new ArgumentException($"The folder is one in the map's folder, such as {DecalsFolder}, not {folder}");
        }
        return normalized;
    }
}
