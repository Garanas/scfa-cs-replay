using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// The unit data of every supported game version (<c>wwwroot/data/units/</c>, generated from the FA
/// repository by <c>tools/generate-unit-data.cs</c>): <c>index.json</c> says which file holds each
/// version. The index and every file are fetched once, on first use, and shared by the whole app.
/// </summary>
public sealed class UnitDatabase(HttpClient http)
{
    public const string Folder = "data/units/";

    private Task<UnitDataIndex>? index;
    private readonly Dictionary<string, Task<UnitData>> files = [];
    private readonly Dictionary<int, UnitData> versions = [];

    /// <summary>Which data file holds each game version.</summary>
    public Task<UnitDataIndex> LoadIndexAsync() => index ??= Retry(LoadIndexCoreAsync(), () => index = null);

    /// <summary>
    /// The unit data for a game played on <paramref name="gameVersion"/>: that version's, or the
    /// nearest one there is (<see cref="UnitDataIndex.Resolve"/>); the latest without a version.
    /// <see cref="UnitData.GameVersion"/> says which version the data is of.
    /// </summary>
    public async Task<UnitData> LoadAsync(int? gameVersion = null)
    {
        UnitDataIndex loadedIndex = await LoadIndexAsync();
        int version = loadedIndex.Resolve(gameVersion) ?? throw new InvalidOperationException("The unit data index is empty.");
        if (versions.TryGetValue(version, out UnitData? known))
        {
            return known;
        }

        string file = loadedIndex.Versions[version].File;
        if (!files.TryGetValue(file, out Task<UnitData>? loading))
        {
            loading = Retry(LoadFileCoreAsync(file), () => files.Remove(file));
            files[file] = loading;
        }

        // A version without changes shares an earlier version's file: label it with its own version.
        UnitData data = await loading;
        return versions[version] = data.GameVersion == version ? data : data.WithGameVersion(version);
    }

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
