using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Diagnostics;

/// <summary>
/// What a problem report holds besides the telemetry (user story F-17).
/// </summary>
/// <param name="Description">What went wrong, in the user's words; may be empty.</param>
/// <param name="Sessions">How many of the newest sessions' logs and metrics go in, this one first.</param>
/// <param name="ProjectFolder">The open map's project to put in, or null to leave it out (the user's choice).</param>
/// <param name="MapName">The open map's name, if any.</param>
/// <param name="UnsavedChanges">Whether the open map has changes the project on disk lacks.</param>
public sealed record ProblemReportContent(
    string Description,
    int Sessions = ProblemReport.DefaultSessions,
    string? ProjectFolder = null,
    string? MapName = null,
    bool UnsavedChanges = false);

/// <summary>
/// A problem report (user story F-17): one zip with the recent sessions' logs and metrics (the
/// files of <see cref="Telemetry"/>), the editor's version and the operating system, the user's
/// description, and the open map's project when the user chooses to add it. It is written into a
/// folder the user chooses, to attach to an issue; nothing is sent anywhere.
/// </summary>
public static class ProblemReport
{
    public const int DefaultSessions = 5;

    /// <summary>
    /// The folders of a project that are left out: git's own, and what other tools build.
    /// </summary>
    private static readonly string[] LeftOut = [".git/", "procedural/temp/"];

    /// <summary>
    /// The name of a report written at <paramref name="time"/>.
    /// </summary>
    public static string FileName(DateTimeOffset time) => $"faf-map-editor-report-{time:yyyyMMdd-HHmmss}.zip";

    /// <summary>
    /// Writes the report into <paramref name="folder"/>.
    /// </summary>
    /// <param name="telemetryFolder">Where the sessions' files are; null when nothing is recorded.</param>
    /// <returns>The zip's path.</returns>
    /// <exception cref="IOException">The folder cannot be written.</exception>
    public static string Write(string folder, string? telemetryFolder, ProblemReportContent content, DateTimeOffset now)
    {
        // one kind of slash in the path the editor reports, however the folder was written
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName(now));
        string[] sessions = SessionFiles(telemetryFolder, content.Sessions);
        List<string> projectFiles = content.ProjectFolder is string project && Directory.Exists(project) ? ProjectFiles(project) : [];

        using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            JsonObject report = new JsonObject
            {
                ["created"] = now.ToString("O"),
                ["editorVersion"] = AssetConverter.EditorVersion,
                ["os"] = RuntimeInformation.OSDescription,
                ["architecture"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["description"] = content.Description.Trim(),
                ["sessions"] = new JsonArray([.. sessions.Select(file => (JsonNode)JsonValue.Create(Path.GetFileName(file)))]),
                ["map"] = content.MapName,
                ["project"] = projectFiles.Count == 0 ? null : new JsonObject
                {
                    ["files"] = projectFiles.Count,
                    ["unsavedChanges"] = content.UnsavedChanges
                        ? "the map had unsaved changes: the project is as it was last saved"
                        : null,
                },
            };
            ZipArchiveEntry entry = zip.CreateEntry("report.json", CompressionLevel.Optimal);
            using (Stream json = entry.Open())
            {
                JsonSerializer.Serialize(json, report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            foreach (string file in sessions)
            {
                Add(zip, file, "telemetry/" + Path.GetFileName(file));
            }
            foreach (string relative in projectFiles)
            {
                Add(zip, Path.Combine(content.ProjectFolder!, relative), "project/" + relative);
            }
        }
        return path;
    }

    /// <summary>
    /// The logs and metrics of the newest sessions, newest first; a session's two files share the
    /// start of their name (<c>&lt;start&gt;-&lt;session&gt;</c>).
    /// </summary>
    public static string[] SessionFiles(string? telemetryFolder, int sessions)
    {
        if (telemetryFolder is null || !Directory.Exists(telemetryFolder) || sessions <= 0)
        {
            return [];
        }
        return [.. Directory.EnumerateFiles(telemetryFolder, "*.jsonl")
            .GroupBy(file => Path.GetFileName(file).Split('.')[0], StringComparer.Ordinal)
            .OrderByDescending(session => session.Key, StringComparer.Ordinal)
            .Take(sessions)
            .SelectMany(session => session.Order(StringComparer.Ordinal))];
    }

    private static List<string> ProjectFiles(string project) => [.. Directory.EnumerateFiles(project, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(project, file).Replace('\\', '/'))
        .Where(relative => !LeftOut.Any(folder => relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
        .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Adds a file, also one the editor is still writing (this session's logs).
    /// </summary>
    private static void Add(ZipArchive zip, string file, string name)
    {
        using FileStream source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using Stream target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        source.CopyTo(target);
    }
}
