namespace FAForever.Replay
{
    /// <summary>
    /// Typed access to a blueprint table, used to build the blueprint records. Every accessor
    /// returns null (scalars, optional sections) or an empty collection when the key is missing
    /// or holds a value of another type.
    /// </summary>
    internal readonly struct BlueprintTableReader(LuaData.Table table)
    {
        public LuaData.Table Table => table;

        private LuaData? Get(string key) => table.Value.TryGetValue(key, out LuaData? value) ? value : null;

        public double? Number(string key) => Get(key) is LuaData.Number number ? number.Value : null;

        public int? Integer(string key) => Get(key) is LuaData.Number number ? (int)number.Value : null;

        public string? String(string key) => Get(key) is LuaData.String text ? text.Value : null;

        public bool? Bool(string key) => Get(key) is LuaData.Bool boolean ? boolean.Value : null;

        /// <summary>
        /// A nested table read as a record, or null when the table is missing.
        /// </summary>
        public T? Section<T>(string key, Func<BlueprintTableReader, T> read) where T : class =>
            Get(key) is LuaData.Table section ? read(new BlueprintTableReader(section)) : null;

        /// <summary>
        /// A nested table read as a record; a missing table reads as an empty one.
        /// </summary>
        public T SectionOrEmpty<T>(string key, Func<BlueprintTableReader, T> read) =>
            read(new BlueprintTableReader(Get(key) as LuaData.Table ?? new LuaData.Table(new Dictionary<string, LuaData>())));

        /// <summary>
        /// The positional entries 1, 2, 3, ... (like Lua's ipairs) of a nested table.
        /// </summary>
        private IEnumerable<LuaData> Positional(string key)
        {
            if (Get(key) is not LuaData.Table list)
            {
                yield break;
            }
            for (int index = 1; list.Value.TryGetValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture), out LuaData? item); index++)
            {
                yield return item;
            }
        }

        public IReadOnlyList<T> List<T>(string key, Func<BlueprintTableReader, T> read) =>
            Positional(key).OfType<LuaData.Table>().Select(item => read(new BlueprintTableReader(item))).ToList();

        public IReadOnlyList<string> Strings(string key) =>
            Positional(key).OfType<LuaData.String>().Select(item => item.Value).ToList();

        public IReadOnlyList<double> Numbers(string key) =>
            Positional(key).OfType<LuaData.Number>().Select(item => item.Value).ToList();

        /// <summary>
        /// The keys of a nested table whose value is true, e.g. <c>CommandCaps = { RULEUCC_Move = true, ... }</c>.
        /// </summary>
        public IReadOnlySet<string> Flags(string key) =>
            Get(key) is LuaData.Table flags
                ? flags.Value.Where(entry => entry.Value is LuaData.Bool { Value: true }).Select(entry => entry.Key).ToHashSet()
                : new HashSet<string>();

        /// <summary>
        /// The entries of a nested table whose value is a table, read as records.
        /// </summary>
        public IReadOnlyDictionary<string, T> Dictionary<T>(string key, Func<BlueprintTableReader, T> read, Func<string, bool>? include = null) =>
            Get(key) is LuaData.Table entries
                ? entries.Value
                    .Where(entry => entry.Value is LuaData.Table && (include is null || include(entry.Key)))
                    .ToDictionary(entry => entry.Key, entry => read(new BlueprintTableReader((LuaData.Table)entry.Value)))
                : new Dictionary<string, T>();

        /// <summary>
        /// The string entries of a nested table, e.g. <c>FireTargetLayerCapsTable = { Land = 'Land|Water' }</c>.
        /// </summary>
        public IReadOnlyDictionary<string, string> StringDictionary(string key) =>
            Get(key) is LuaData.Table entries
                ? entries.Value.Where(entry => entry.Value is LuaData.String).ToDictionary(entry => entry.Key, entry => ((LuaData.String)entry.Value).Value)
                : new Dictionary<string, string>();

        public IReadOnlyDictionary<string, BlueprintSound> Sounds(string key) => Dictionary(key, BlueprintSound.Read);
    }
}
