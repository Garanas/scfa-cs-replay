using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// Which unit data file holds each game version: the content of <c>units/index.json</c>, next to
    /// the data files (<see cref="UnitData"/>). Releases that change no unit share a file, so a data
    /// file is named after the first version that has its data, e.g. 3838 and 3839 both use
    /// <c>3837.json</c> when neither changed a unit. Maintained by <c>tools/generate-unit-data.cs</c>.
    /// </summary>
    /// <param name="Versions">The data file of each game version.</param>
    public sealed record UnitDataIndex(IReadOnlyDictionary<int, string> Versions)
    {
        /// <summary>The newest game version with data.</summary>
        [JsonIgnore]
        public int? Latest => Versions.Count > 0 ? Versions.Keys.Max() : null;

        /// <summary>The game versions, newest first.</summary>
        [JsonIgnore]
        public IEnumerable<int> Newest => Versions.Keys.OrderDescending();

        /// <summary>
        /// The version whose data to use for a game played on <paramref name="gameVersion"/>: that
        /// version when it has data, otherwise the newest older one, otherwise the oldest there is.
        /// Without a version (not a FAF game), the latest. Null only when the index is empty.
        /// </summary>
        public int? Resolve(int? gameVersion)
        {
            if (Versions.Count == 0)
            {
                return null;
            }

            if (gameVersion is not { } version)
            {
                return Latest;
            }

            return Versions.Keys.Where(candidate => candidate <= version).DefaultIfEmpty(Versions.Keys.Min()).Max();
        }

        /// <summary>
        /// The index with a version added, pointing at <paramref name="file"/>.
        /// </summary>
        public UnitDataIndex With(int gameVersion, string file) =>
            new UnitDataIndex(new Dictionary<int, string>(Versions) { [gameVersion] = file });

        public static UnitDataIndex Empty { get; } = new UnitDataIndex(new Dictionary<int, string>());

        /// <summary>
        /// Writes the index with one version per line, newest first, so a diff shows the added version.
        /// </summary>
        public string Serialize()
        {
            string[] lines = Newest.Select(version => $"  \"{version}\": {JsonSerializer.Serialize(Versions[version], UnitDataIndexJsonContext.Default.String)}").ToArray();
            return "{\"versions\":{\n" + string.Join(",\n", lines) + "\n}}\n";
        }

        /// <exception cref="JsonException">The text is not a unit data index.</exception>
        public static UnitDataIndex Deserialize(string json) =>
            JsonSerializer.Deserialize(json, UnitDataIndexJsonContext.Default.UnitDataIndex)
            ?? throw new JsonException("The unit data index is empty.");

        /// <exception cref="JsonException">The stream does not hold a unit data index.</exception>
        public static async Task<UnitDataIndex> DeserializeAsync(Stream json, CancellationToken cancellationToken = default) =>
            await JsonSerializer.DeserializeAsync(json, UnitDataIndexJsonContext.Default.UnitDataIndex, cancellationToken)
            ?? throw new JsonException("The unit data index is empty.");
    }

    /// <summary>
    /// Source-generated JSON for the unit data index (see <see cref="UnitDataJsonContext"/>).
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(UnitDataIndex))]
    [JsonSerializable(typeof(string))]
    internal sealed partial class UnitDataIndexJsonContext : JsonSerializerContext
    {
    }
}
