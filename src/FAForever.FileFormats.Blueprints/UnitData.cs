using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// The unit summaries of a game release: the content of a unit data file (<c>units/3837.json</c>),
    /// generated from the FA repository by <c>tools/generate-unit-data.cs</c> so the browser does not
    /// have to parse blueprints itself. It holds only the units: which game versions they belong to is
    /// up to the <see cref="UnitDataIndex"/>, since releases that change no unit share a file.
    /// </summary>
    /// <param name="units">The units, sorted by blueprint id.</param>
    public sealed class UnitData(IReadOnlyList<UnitSummary> units)
    {
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
        /// The units that can build the given one, sorted by id (the inverse of <see cref="UnitSummary.Builds"/>).
        /// </summary>
        public IReadOnlyList<UnitSummary> GetBuilders(string blueprintId)
        {
            builders ??= Units
                .SelectMany(builder => builder.Builds.Select(target => (Target: target, Builder: builder)))
                .GroupBy(pair => pair.Target, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<UnitSummary>)group.Select(pair => pair.Builder).ToList(), StringComparer.OrdinalIgnoreCase);
            return builders.GetValueOrDefault(blueprintId) ?? [];
        }

        private Dictionary<string, IReadOnlyList<UnitSummary>>? builders;

        /// <summary>
        /// The data of a release from its unit blueprints, with the build tree
        /// (<see cref="UnitBuildTree"/>) filled in.
        /// </summary>
        public static UnitData From(IEnumerable<BlueprintUnit> units)
        {
            List<BlueprintUnit> all = units.ToList();
            IReadOnlyDictionary<string, IReadOnlyList<string>> builds = UnitBuildTree.Builds(all);
            IReadOnlySet<string> buildable = UnitBuildTree.Buildable(all, builds);
            return new UnitData(
                all.Select(unit =>
                    {
                        UnitSummary summary = UnitSummary.From(unit);
                        return summary with
                        {
                            Buildable = buildable.Contains(summary.BlueprintId),
                            Builds = builds.GetValueOrDefault(summary.BlueprintId) ?? [],
                        };
                    })
                    .OrderBy(unit => unit.BlueprintId, StringComparer.Ordinal)
                    .ToList());
        }

        /// <summary>
        /// Whether both hold exactly the same units.
        /// </summary>
        public bool HasSameUnits(UnitData other) => UnitLines().SequenceEqual(other.UnitLines());

        /// <summary>What changed from one game version's units to another's, by blueprint id.</summary>
        public sealed record Difference(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
        {
            public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
        }

        /// <summary>
        /// The units added, removed and changed (any value of their summary) from <paramref name="older"/>
        /// to <paramref name="newer"/>, each sorted by id.
        /// </summary>
        public static Difference Compare(UnitData older, UnitData newer)
        {
            Dictionary<string, string> before = older.Units.ToDictionary(unit => unit.BlueprintId, Line);
            Dictionary<string, string> after = newer.Units.ToDictionary(unit => unit.BlueprintId, Line);
            return new Difference(
                after.Keys.Except(before.Keys).Order(StringComparer.Ordinal).ToList(),
                before.Keys.Except(after.Keys).Order(StringComparer.Ordinal).ToList(),
                after.Keys.Intersect(before.Keys).Where(id => after[id] != before[id]).Order(StringComparer.Ordinal).ToList());
        }

        private IEnumerable<string> UnitLines() => Units.Select(Line);

        private static string Line(UnitSummary unit) => JsonSerializer.Serialize(unit, UnitDataJsonContext.Default.UnitSummary);

        /// <summary>
        /// Writes the units as a unit data file, <c>{"units":[...]}</c> with one unit per line.
        /// Properties are camel case; null values are left out.
        /// </summary>
        public string Serialize()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"units\":[\n");
            for (int index = 0; index < Units.Count; index++)
            {
                builder.Append(Line(Units[index]));
                builder.Append(index < Units.Count - 1 ? ",\n" : "\n");
            }
            builder.Append("]}\n");
            return builder.ToString();
        }

        /// <summary>
        /// Reads one unit from the text of a unit data file without reading the others: <see cref="Serialize"/>
        /// writes one unit per line, starting with its id, so only that line is parsed. Falls back to
        /// reading the whole file when no line starts that way. Null when the file has no such unit.
        /// </summary>
        /// <exception cref="JsonException">The text is not a unit data file.</exception>
        public static UnitSummary? ReadUnit(string json, string blueprintId)
        {
            string start = $"{{\"blueprintId\":{JsonSerializer.Serialize(blueprintId.ToLowerInvariant(), UnitDataJsonContext.Default.String)},";
            foreach (ReadOnlySpan<char> line in json.AsSpan().EnumerateLines())
            {
                if (line.StartsWith(start, StringComparison.Ordinal))
                {
                    return JsonSerializer.Deserialize(line.TrimEnd(',').ToString(), UnitDataJsonContext.Default.UnitSummary);
                }
            }

            return Deserialize(json).GetOrNull(blueprintId);
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
    [JsonSerializable(typeof(string))]
    internal sealed partial class UnitDataJsonContext : JsonSerializerContext
    {
    }
}
