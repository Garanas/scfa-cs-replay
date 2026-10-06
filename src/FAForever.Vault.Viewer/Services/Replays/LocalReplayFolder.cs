using System.Text.Json;

namespace FAForever.Vault.Viewer.Services.Replays;

/// <summary>
/// The replay folder the user picked on <c>replays/folder</c>, kept while the app runs, so going to a
/// replay and back does not ask for the folder again. The files themselves stay in the browser
/// (js/app.js, <c>folderFiles</c>); this holds what the list shows: the name, date and the JSON
/// metadata line a .fafreplay starts with. A reload clears both: the folder is picked again.
/// </summary>
public sealed class LocalReplayFolder
{
    /// <summary>The folder's name, as the browser reports it ("replays"); null before a folder was picked.</summary>
    public string? FolderName { get; private set; }

    public IReadOnlyList<LocalReplayEntry> Entries { get; private set; } = [];

    public void Set(string? folderName, string entriesJson)
    {
        FolderName = folderName ?? "replays";
        Entries = [.. Parse(entriesJson).OrderByDescending(entry => entry.PlayedAt ?? entry.LastModified)];
    }

    private static IEnumerable<LocalReplayEntry> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            string name = element.GetProperty("name").GetString() ?? "";
            LocalReplayEntry entry = new(
                element.GetProperty("index").GetInt32(),
                name,
                element.GetProperty("size").GetInt64(),
                DateTimeOffset.FromUnixTimeMilliseconds((long)element.GetProperty("lastModified").GetDouble()));

            if (element.TryGetProperty("metadata", out JsonElement line) && line.ValueKind == JsonValueKind.String)
            {
                entry = WithMetadata(entry, line.GetString()!);
            }
            else if (IdFromFileName(name) is int id)
            {
                entry = entry with { GameId = id };
            }

            yield return entry;
        }
    }

    /// <summary>
    /// The FAF client names its replays "{game id}-{player}.fafreplay" (seen 2026-10-06 in
    /// C:\ProgramData\FAForever\replays); the metadata line has the id too, this is for files without one.
    /// </summary>
    private static int? IdFromFileName(string name)
        => name.IndexOf('-') is > 0 and int dash && int.TryParse(name.AsSpan(0, dash), out int id) && id > 0 ? id : null;

    /// <summary>
    /// The fields of the metadata line the list needs. Players come from "teams", a map of team to
    /// player names (<c>{"1=[32238]":["Jip"]}</c>); a key of 1 is FFA, as in the lobby.
    /// </summary>
    private static LocalReplayEntry WithMetadata(LocalReplayEntry entry, string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;

            List<string> players = [];
            if (root.TryGetProperty("teams", out JsonElement teams) && teams.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty team in teams.EnumerateObject())
                {
                    if (team.Value.ValueKind == JsonValueKind.Array)
                    {
                        players.AddRange(team.Value.EnumerateArray().Select(player => player.GetString()).OfType<string>());
                    }
                }
            }

            return entry with
            {
                GameId = Number(root, "uid") is double uid && uid > 0 ? (int)uid : IdFromFileName(entry.FileName),
                Title = Text(root, "title"),
                MapFolder = Text(root, "mapname") is { } map && !map.Equals("None", StringComparison.OrdinalIgnoreCase) ? map : null,
                PlayedAt = Number(root, "launched_at") is double start && start > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(start * 1000)) : null,
                Duration = Number(root, "launched_at") is double from && Number(root, "game_end") is double to && to > from ? TimeSpan.FromSeconds(to - from) : null,
                FeaturedMod = Text(root, "featured_mod"),
                Recorder = Text(root, "recorder"),
                Players = players,
            };
        }
        catch (JsonException)
        {
            return entry with { GameId = IdFromFileName(entry.FileName) };
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static double? Number(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}

/// <summary>A replay file in the picked folder, with what its metadata line says (when it has one).</summary>
public sealed record LocalReplayEntry(int Index, string FileName, long Size, DateTimeOffset LastModified)
{
    /// <summary>The game's id in the FAF vault, so the vault page (which can be shared) can be linked.</summary>
    public int? GameId { get; init; }

    public string? Title { get; init; }

    /// <summary>The map's vault folder, e.g. <c>osiris.v0006</c>, or a code for the original maps.</summary>
    public string? MapFolder { get; init; }

    public DateTimeOffset? PlayedAt { get; init; }

    /// <summary>Wall clock time from launch to end, as the server recorded it.</summary>
    public TimeSpan? Duration { get; init; }

    public string? FeaturedMod { get; init; }

    public string? Recorder { get; init; }

    public IReadOnlyList<string> Players { get; init; } = [];

    /// <summary>Whether the text (title, map, player or file name) contains <paramref name="search"/>.</summary>
    public bool Matches(string search)
        => search.Length == 0
           || FileName.Contains(search, StringComparison.OrdinalIgnoreCase)
           || Title?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
           || MapFolder?.Contains(search.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase) == true
           || Players.Any(player => player.Contains(search, StringComparison.OrdinalIgnoreCase));
}
