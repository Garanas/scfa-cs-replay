using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// The unit summaries of one game version: the content of a unit data file (<c>units.json</c>),
    /// generated from the FA repository by <c>tools/generate-unit-data.cs</c> so the browser does not
    /// have to parse blueprints itself.
    /// </summary>
    /// <param name="gameVersion">The FAF game version the data comes from, e.g. 3839 (<c>version</c>
    /// in the FA repository's <c>mod_info.lua</c>; also the release tag, and the last part of the
    /// version in a replay header such as "Supreme Commander v1.50.3839").</param>
    /// <param name="units">The units, sorted by blueprint id.</param>
    public sealed class UnitData(int gameVersion, IReadOnlyList<UnitSummary> units)
    {
        public int GameVersion { get; } = gameVersion;

        public IReadOnlyList<UnitSummary> Units { get; } = units;

        /// <summary>
        /// The unit with the given blueprint id (case-insensitive), or null.
        /// </summary>
        public UnitSummary? GetOrNull(string? blueprintId)
        {
            if (blueprintId is not { Length: > 0 })
            {
                return null;
            }

            return byId.GetValueOrDefault(blueprintId);
        }

        private readonly Dictionary<string, UnitSummary> byId = units
            .GroupBy(unit => unit.BlueprintId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The data of a game version from its unit blueprints.
        /// </summary>
        public static UnitData From(int gameVersion, IEnumerable<BlueprintUnit> units) => new UnitData(
            gameVersion,
            units.Select(UnitSummary.From).OrderBy(unit => unit.BlueprintId, StringComparer.Ordinal).ToList());

        /// <summary>
        /// Writes the data as JSON with one unit per line, so the diff after a game update shows
        /// which units changed. Properties are camel case; null values are left out.
        /// </summary>
        public string Serialize()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"gameVersion\":").Append(GameVersion).Append(",\"units\":[\n");
            for (int index = 0; index < Units.Count; index++)
            {
                builder.Append(JsonSerializer.Serialize(Units[index], UnitDataJsonContext.Default.UnitSummary));
                builder.Append(index < Units.Count - 1 ? ",\n" : "\n");
            }
            builder.Append("]}\n");
            return builder.ToString();
        }

        /// <summary>
        /// Reads a unit data file.
        /// </summary>
        /// <exception cref="JsonException">The text is not a unit data file.</exception>
        public static UnitData Deserialize(string json) =>
            JsonSerializer.Deserialize(json, UnitDataJsonContext.Default.UnitData)
            ?? throw new JsonException("The unit data file is empty.");

        /// <summary>
        /// Reads a unit data file.
        /// </summary>
        /// <exception cref="JsonException">The stream does not hold a unit data file.</exception>
        public static async Task<UnitData> DeserializeAsync(Stream json, CancellationToken cancellationToken = default) =>
            await JsonSerializer.DeserializeAsync(json, UnitDataJsonContext.Default.UnitData, cancellationToken)
            ?? throw new JsonException("The unit data file is empty.");
    }

    /// <summary>
    /// Source-generated JSON for the unit data file: no reflection, so it survives trimming in WebAssembly.
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(UnitData))]
    internal sealed partial class UnitDataJsonContext : JsonSerializerContext
    {
    }
}
