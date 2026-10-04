using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// The unit summaries of the current game version (<c>wwwroot/data/units.json</c>, generated from
/// the FA repository by <c>tools/generate-unit-data.cs</c>). Fetched once, on first use, and shared
/// by every unit card on the page.
/// </summary>
public sealed class UnitDatabase(HttpClient http)
{
    public const string DataFile = "data/units.json";

    private Task<UnitData>? loading;

    public Task<UnitData> LoadAsync() => loading ??= LoadCoreAsync();

    private async Task<UnitData> LoadCoreAsync()
    {
        try
        {
            await using Stream json = await http.GetStreamAsync(DataFile);
            return await UnitData.DeserializeAsync(json);
        }
        catch
        {
            // Let a later card try again instead of caching the failure.
            loading = null;
            throw;
        }
    }
}
