using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// Every game version with unit data: the content of <c>units/index.json</c>, next to the data
    /// files (<see cref="UnitData"/>). Each version says which file holds its units, where they come
    /// from and what changed since the version before. Releases that change no unit share a file, so
    /// a data file is named after the first version that has its units. Maintained by
    /// <c>tools/generate-unit-data.cs</c>.
    /// </summary>
    /// <param name="Versions">The entry of each game version.</param>
    public sealed record UnitDataIndex(IReadOnlyDictionary<int, UnitDataIndex.Entry> Versions)
    {
        /// <summary>
        /// One game version in the index.
        /// </summary>
        /// <param name="File">The data file with its units, e.g. <c>3837.json</c>.</param>
        /// <param name="UnitCount">How many units the data holds.</param>
        /// <param name="ReusedFrom">When the file belongs to another version: that version. Its units
        /// are exactly the same, so this release changed no unit (see <paramref name="Changes"/>).</param>
        /// <param name="Changes">What changed since the previous version in the index; null for the first.</param>
        /// <param name="Commit">The commit of the release tag in the FA repository, when known.</param>
        /// <param name="Released">The date of that commit, when known.</param>
        /// <param name="Generated">The day this entry was generated.</param>
        public sealed record Entry(
            string File,
            int UnitCount,
            int? ReusedFrom,
            UnitChanges? Changes,
            string? Commit,
            DateOnly? Released,
            DateOnly? Generated);

        /// <summary>
        /// The units changed, added and removed since <paramref name="Previous"/>, by blueprint id.
        /// </summary>
        public sealed record UnitChanges(int Previous, IReadOnlyList<string> Changed, IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
        {
            [JsonIgnore]
            public bool IsEmpty => Changed.Count == 0 && Added.Count == 0 && Removed.Count == 0;

            public static UnitChanges From(int previous, UnitData.Difference difference) =>
                new UnitChanges(previous, difference.Changed, difference.Added, difference.Removed);
        }

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

        /// <summary>The newest version before <paramref name="gameVersion"/>, or null.</summary>
        public int? Previous(int gameVersion) =>
            Versions.Keys.Where(candidate => candidate < gameVersion).Select(candidate => (int?)candidate).Max();

        /// <summary>The index with a version added or replaced.</summary>
        public UnitDataIndex With(int gameVersion, Entry entry) =>
            new UnitDataIndex(new Dictionary<int, Entry>(Versions) { [gameVersion] = entry });

        public static UnitDataIndex Empty { get; } = new UnitDataIndex(new Dictionary<int, Entry>());

        /// <summary>
        /// Writes the index with one version per line, newest first, so a diff shows the added version.
        /// Properties are camel case; null values are left out.
        /// </summary>
        public string Serialize()
        {
            string[] lines = Newest
                .Select(version => $"\"{version}\":{JsonSerializer.Serialize(Versions[version], UnitDataIndexJsonContext.Default.Entry)}")
                .ToArray();
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
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(UnitDataIndex))]
    internal sealed partial class UnitDataIndexJsonContext : JsonSerializerContext
    {
    }
}
