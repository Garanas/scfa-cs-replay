using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A file of the map's folder, as the Map panel lists it: its path in the folder and its size.
/// </summary>
public sealed record MapFileItem(string Path, string Size);

/// <summary>
/// The map's script and the other files of its folder (user story M-6): the script opens in the
/// person's own editor (<c>file.openScript</c>), and what that editor saves is read back into the
/// map; the files (textures, Lua files the script loads, ...) are listed, and their folder shows in
/// the system's file manager (<c>file.showMapFiles</c>).
/// </summary>
public sealed partial class ScriptFilesViewModel : ObservableObject
{
    private ImmutableArray<byte>? _script;
    private ImmutableSortedDictionary<string, ImmutableArray<byte>>? _files;

    /// <summary>
    /// E.g. <c>script.lua, 42 lines</c>, or that the map has none.
    /// </summary>
    [ObservableProperty]
    public partial string Script { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    public partial IReadOnlyList<MapFileItem> Files { get; private set; } = [];

    public bool HasFiles => Files.Count > 0;

    /// <summary>
    /// E.g. <c>3 files, 1.2 MB</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>
    /// Whether the map is saved as a project: the script is a file to open only then.
    /// </summary>
    [ObservableProperty]
    public partial bool IsProject { get; private set; }

    /// <summary>
    /// A number of bytes as people read it: <c>812 bytes</c>, <c>14 KB</c>, <c>1.2 MB</c>.
    /// </summary>
    public static string SizeOf(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
    };

    public void Refresh(MapDocument? document, bool isProject)
    {
        IsProject = isProject;
        if (document is null)
        {
            _script = null;
            _files = null;
            Script = "";
            Files = [];
            Summary = "";
            return;
        }
        if (!Equals(document.Script, _script) || Script.Length == 0)
        {
            _script = document.Script;
            Script = document.Script is ImmutableArray<byte> script
                ? string.Create(CultureInfo.InvariantCulture, $"script.lua, {script.Count(b => b == '\n') + 1} lines, {SizeOf(script.Length)}")
                : "The map has no script yet: opening it makes one, the script of a skirmish map";
        }
        if (!ReferenceEquals(document.Files, _files))
        {
            _files = document.Files;
            Files = [.. document.Files.Select(file => new MapFileItem(file.Key, SizeOf(file.Value.Length)))];
            long total = document.Files.Sum(file => (long)file.Value.Length);
            Summary = document.Files.Count == 0
                ? "The map's folder holds no other files"
                : string.Create(CultureInfo.InvariantCulture, $"{document.Files.Count} {(document.Files.Count == 1 ? "file" : "files")}, {SizeOf(total)}");
        }
    }
}
