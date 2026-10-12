using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// A tour file of the workspace's <c>tours</c> folder: the tour, or why it cannot be used.
/// </summary>
public sealed record TourEntry(string File, Tour? Tour, string? Error)
{
    public bool IsOk => Tour is not null;

    /// <summary>
    /// What the list shows: the tour's title, else the file's name.
    /// </summary>
    public string Title => Tour?.Title ?? System.IO.Path.GetFileName(File);
}

/// <summary>
/// The tours of a folder, read from every <c>*.tour.json</c> in it: the editor's own (written there,
/// see <see cref="EditorTours"/>) and a map maker's beside them. A file that cannot be read is an entry
/// with its error; it never stops the others.
/// </summary>
public static class TourCatalog
{
    /// <summary>
    /// Reads the folder's tours, in their order (<see cref="Tour.Order"/>, then by title); a file that fails after them.
    /// </summary>
    public static IReadOnlyList<TourEntry> Load(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }
        List<TourEntry> entries = [];
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(folder, "*" + TourScript.Suffix).Order(StringComparer.OrdinalIgnoreCase))
        {
            Tour? tour = TourScript.Read(file, out string? error);
            if (tour is not null && !ids.Add(tour.Id))
            {
                error = $"the id '{tour.Id}' is used by another file";
                tour = null;
            }
            entries.Add(new TourEntry(file, tour, error));
        }
        return [.. entries.Where(entry => entry.IsOk).OrderBy(entry => entry.Tour!.Order).ThenBy(entry => entry.Tour!.Title, StringComparer.CurrentCultureIgnoreCase),
            .. entries.Where(entry => !entry.IsOk)];
    }
}

/// <summary>
/// The tours that ship with the editor (<c>Tours/*.tour.json</c>, embedded): written into a workspace's
/// <c>tours</c> folder the way the example plugins are into <c>plugins</c>. A tour the person deletes is not
/// written again: the hidden file <c>.examples</c> names the ones written. A copy that is an earlier version
/// of the tour (<see cref="Earlier"/>), untouched, is brought up to date; a copy the person changed is theirs.
/// </summary>
public static class EditorTours
{
    private const string Prefix = "Tours/";
    private const string Written = ".examples";

    /// <summary>
    /// The SHA-256 of earlier versions of the tours' files (lines ending in a line feed), as
    /// <c>file:hash</c>: a copy still equal to one was not changed by the person and is replaced by the
    /// current version. A tour changed for a release adds its old hash here.
    /// </summary>
    private static readonly HashSet<string> Earlier = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "window.tour.json:589810bf33a41f0b9075f98be056355db553186d66ab640fdc689e510472215b",
        "terrain.tour.json:4594fa12d6c8b7fb42872ccda984f41b9f6820c6c98b8ec8b2208c7f5b0f5143",
        "masks.tour.json:11e507e8c87ca82aab583c705f3f33ed23313a8ac2786a34cde6d5ec28eaaaa3",
        "paint.tour.json:b8238a30c83261b58092bd2b558bbd01e657bd0b461e054db6b676b3a4027bc9",
    };

    /// <summary>
    /// The environment variable that makes <see cref="Ensure"/> write the shipped tours over the copies the
    /// workspace holds, whatever was changed in them: set to <c>1</c> by <c>faf-map editor start|restart --refresh-tours</c>,
    /// for a developer who edits the embedded tours and tries them in a scratch workspace.
    /// </summary>
    public const string RefreshVariable = "FAF_MAP_EDITOR_REFRESH_TOURS";

    /// <summary>
    /// Whether the environment asks for the shipped tours to replace the workspace's copies.
    /// </summary>
    public static bool RefreshRequested => Environment.GetEnvironmentVariable(RefreshVariable) == "1";

    /// <summary>
    /// The shipped tours' file names.
    /// </summary>
    public static IReadOnlyList<string> Names => [.. Resources().Select(resource => resource.File).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The text of a shipped tour's file.
    /// </summary>
    public static string Text(string file)
    {
        (string File, string Resource) resource = Resources().Single(each => each.File == file);
        using StreamReader reader = new StreamReader(typeof(EditorTours).Assembly.GetManifestResourceStream(resource.Resource)!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Writes the tours the folder never had and brings untouched copies of earlier versions up to date.
    /// </summary>
    /// <param name="folder">The workspace's <c>tours</c> folder.</param>
    /// <param name="refresh">Whether a copy that differs from the shipped tour is written over, also one the person changed (development: <see cref="RefreshVariable"/>); a tour the person deleted stays deleted.</param>
    /// <returns>The files written or brought up to date now.</returns>
    public static IReadOnlyList<string> Ensure(string folder, bool refresh = false)
    {
        Directory.CreateDirectory(folder);
        string written = Path.Combine(folder, Written);
        HashSet<string> had = new HashSet<string>(File.Exists(written) ? File.ReadAllLines(written) : [], StringComparer.OrdinalIgnoreCase);
        List<string> now = [];
        foreach ((string file, string _) in Resources())
        {
            string path = Path.Combine(folder, file);
            string shipped = Text(file);
            if (File.Exists(path))
            {
                string onDisk = File.ReadAllText(path);
                if (Normal(onDisk) != Normal(shipped) && (refresh || Earlier.Contains($"{file}:{Hash(onDisk)}")))
                {
                    File.WriteAllText(path, shipped);
                    now.Add(file);
                }
                continue;
            }
            if (had.Contains(file))
            {
                continue;
            }
            File.WriteAllText(path, shipped);
            now.Add(file);
        }
        if (now.Count > 0)
        {
            File.WriteAllLines(written, [.. had.Concat(now).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)]);
        }
        return now;
    }

    private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normal(text))));

    private static IEnumerable<(string File, string Resource)> Resources() =>
        typeof(EditorTours).Assembly.GetManifestResourceNames()
            .Where(resource => resource.Replace('\\', '/').StartsWith(Prefix, StringComparison.Ordinal))
            .Select(resource => (resource.Replace('\\', '/')[Prefix.Length..], resource));
}

/// <summary>
/// What the workspace remembers about the tours (<c>tours/.progress.json</c>, user story H-1): whether
/// the offer of the window's tour was answered, and when each tour was finished.
/// </summary>
public sealed record TourProgress
{
    /// <summary>
    /// The file's name in the tours folder.
    /// </summary>
    public const string File = ".progress.json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Set once the person answered the offer with Start or Don't ask again: it is not made again.
    /// </summary>
    public bool Offered { get; init; }

    /// <summary>
    /// The tours finished (their last step reached), by id, with when.
    /// </summary>
    public Dictionary<string, DateTimeOffset> Finished { get; init; } = [];

    /// <summary>
    /// Reads the folder's progress file; none, or one that cannot be read, is no progress.
    /// </summary>
    public static TourProgress Read(string folder, Action<string, Exception>? failed = null)
    {
        string path = Path.Combine(folder, File);
        try
        {
            return System.IO.File.Exists(path) ? JsonSerializer.Deserialize<TourProgress>(System.IO.File.ReadAllText(path), Options) ?? new TourProgress() : new TourProgress();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            failed?.Invoke(path, exception);
            return new TourProgress();
        }
    }

    /// <summary>
    /// Writes the progress into the folder.
    /// </summary>
    public void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(Path.Combine(folder, File), JsonSerializer.Serialize(this, Options));
    }
}
