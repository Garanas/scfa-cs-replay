using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// The units of a game version: the version the index resolved to, and its data. Versions that
/// share a data file share the same <see cref="UnitData"/>.
/// </summary>
public sealed record UnitDataVersion(int GameVersion, UnitData Data);

/// <summary>
/// A stretch of game versions in which a unit stayed the same: from <paramref name="From"/> to
/// <paramref name="To"/>, both versions in the index.
/// </summary>
public sealed record UnitState(int From, int To, UnitSummary Unit);

/// <summary>
/// The unit data of every supported game version (<c>wwwroot/data/units/</c>, generated from the FA
/// repository by <c>tools/generate-unit-data.cs</c>): <c>index.json</c> says which file holds each
/// version. The index and every file are fetched once, on first use, and shared by the whole app.
/// </summary>
public sealed class UnitDatabase(HttpClient http)
{
    public const string Folder = "data/units/";

    /// <summary>
    /// The oldest game version in the data: where <c>tools/backfill-unit-data.ps1 -From</c> started.
    /// Change it together with the data.
    /// </summary>
    public const int FirstVersion = 3801;

    private Task<UnitDataIndex>? index;
    private readonly Dictionary<string, Task<UnitData>> files = [];

    /// <summary>Which data file holds each game version.</summary>
    public Task<UnitDataIndex> LoadIndexAsync() => index ??= Retry(LoadIndexCoreAsync(), () => index = null);

    /// <summary>
    /// The units for a game played on <paramref name="gameVersion"/>: that version's, or the nearest
    /// there is (<see cref="UnitDataIndex.Resolve"/>); the latest without a version. The result says
    /// which version it resolved to.
    /// </summary>
    public async Task<UnitDataVersion> LoadAsync(int? gameVersion = null)
    {
        UnitDataIndex loadedIndex = await LoadIndexAsync();
        int version = loadedIndex.Resolve(gameVersion) ?? throw new InvalidOperationException("The unit data index is empty.");

        string file = loadedIndex.Versions[version].File;
        if (!files.TryGetValue(file, out Task<UnitData>? loading))
        {
            loading = Retry(LoadFileCoreAsync(file), () => files.Remove(file));
            files[file] = loading;
        }

        return new UnitDataVersion(version, await loading);
    }

    /// <summary>
    /// Every state of a unit across the game versions, oldest first: a new state starts where the
    /// index says the unit was added or changed, and ends where it was removed. Only the versions
    /// where it changed are read, and of each only the unit's own line (<see cref="UnitData.ReadUnit"/>).
    /// Empty when no version has the unit.
    /// </summary>
    public async Task<IReadOnlyList<UnitState>> LoadHistoryAsync(string blueprintId)
    {
        string id = blueprintId.ToLowerInvariant();
        UnitDataIndex loadedIndex = await LoadIndexAsync();
        int[] versions = loadedIndex.Versions.Keys.Order().ToArray();

        List<UnitState> states = [];
        UnitState? current = null;
        foreach (int version in versions)
        {
            UnitDataIndex.UnitChanges? changes = loadedIndex.Versions[version].Changes;
            bool starts = changes is null
                ? true // the oldest version: read it to see whether the unit exists
                : changes.Changed.Contains(id) || changes.Added.Contains(id);
            bool ends = changes?.Removed.Contains(id) == true;

            if (current is not null && (starts || ends))
            {
                states.Add(current);
                current = null;
            }

            if (starts && await LoadUnitAsync(version, id) is { } unit)
            {
                current = new UnitState(version, version, unit);
            }
            else if (current is not null && !ends)
            {
                current = current with { To = version };
            }
        }

        if (current is not null)
        {
            states.Add(current);
        }

        return states;
    }

    /// <summary>One unit of one game version, read from its line only, unless the whole file is already in.</summary>
    private async Task<UnitSummary?> LoadUnitAsync(int version, string blueprintId)
    {
        string file = (await LoadIndexAsync()).Versions[version].File;
        if (files.TryGetValue(file, out Task<UnitData>? parsed) && parsed.IsCompletedSuccessfully)
        {
            return parsed.Result.GetOrNull(blueprintId);
        }

        if (!texts.TryGetValue(file, out Task<string>? text))
        {
            text = Retry(http.GetStringAsync(Folder + file), () => texts.Remove(file));
            texts[file] = text;
        }

        return UnitData.ReadUnit(await text, blueprintId);
    }

    private readonly Dictionary<string, Task<string>> texts = [];

    private async Task<UnitDataIndex> LoadIndexCoreAsync()
    {
        await using Stream json = await http.GetStreamAsync(Folder + "index.json");
        return await UnitDataIndex.DeserializeAsync(json);
    }

    private async Task<UnitData> LoadFileCoreAsync(string file)
    {
        await using Stream json = await http.GetStreamAsync(Folder + file);
        return await UnitData.DeserializeAsync(json);
    }

    // A failed load is forgotten, so a later card tries again instead of keeping the failure.
    private static async Task<T> Retry<T>(Task<T> task, Action forget)
    {
        try
        {
            return await task;
        }
        catch
        {
            forget();
            throw;
        }
    }
}
