using System.Globalization;
using System.Text.Json;

namespace FAForever.Replay
{
    /// <summary>
    /// Reads the JSON payload of a <c>GpgNetSend('JsonStats', …)</c> call into
    /// <see cref="ReplayArmyStats"/>. Tolerant by design: the payload comes from game code we
    /// do not control, so missing or mistyped fields read as 0 and anything that is not JSON
    /// at all yields null — it never throws.
    /// </summary>
    public static class ReplayGameStatsReader
    {
        /// <summary>The ModeratorEvent message prefix the GpgNetSend hook writes for this command.</summary>
        public const string MessagePrefix = "GpgNetSend with command 'JsonStats' and data '";

        /// <summary>
        /// Extracts the JSON from a ModeratorEvent message. The hook (lua/ui/globals/GpgNetSend.lua)
        /// formats it as <c>… and data '&lt;arg1&gt;,&lt;arg2&gt;,'</c>: every argument followed by a
        /// comma. JsonStats has a single argument.
        /// </summary>
        public static string? GetJson(string message)
        {
            if (!message.StartsWith(MessagePrefix, StringComparison.Ordinal))
            {
                return null;
            }

            ReadOnlySpan<char> data = message.AsSpan(MessagePrefix.Length).TrimEnd();
            if (data.EndsWith("'"))
            {
                data = data[..^1];
            }

            data = data.TrimEnd();
            if (data.EndsWith(","))
            {
                data = data[..^1];
            }

            return data.ToString();
        }

        /// <summary>Parses a JsonStats payload; null when it is not a usable stats object.</summary>
        public static IReadOnlyList<ReplayArmyStats>? Parse(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("stats", out JsonElement stats))
                {
                    return null;
                }

                bool mangledDecimals = HasMangledDecimals(stats);

                // dkson writes a Lua table with keys 1..n as an array, and one with holes (a
                // civilian army in the middle) as an object keyed by the army index.
                IEnumerable<JsonElement> entries = stats.ValueKind switch
                {
                    JsonValueKind.Array => stats.EnumerateArray().ToList(),
                    JsonValueKind.Object => stats.EnumerateObject()
                        .OrderBy(property => int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : int.MaxValue)
                        .Select(property => property.Value)
                        .ToList(),
                    _ => [],
                };

                List<ReplayArmyStats> armies = entries
                    .Where(entry => entry.ValueKind == JsonValueKind.Object)
                    .Select(entry => ReadArmy(entry, mangledDecimals))
                    .ToList();

                return armies.Count > 0 ? armies : null;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
            {
                return null;
            }
        }

        private static ReplayArmyStats ReadArmy(JsonElement army, bool mangled)
        {
            JsonElement general = Child(army, "general");
            JsonElement resources = Child(army, "resources");

            return new ReplayArmyStats(
                String(army, "name"),
                OptionalNumber(army, "faction", mangled) is { } faction ? (int)faction : null,
                String(army, "type"),
                OptionalNumber(army, "Defeated", mangled),
                new ReplayArmyGeneralStats(
                    Number(general, "score", mangled),
                    Number(general, "lastupdatetick", mangled),
                    Number(general, "currentunits", mangled),
                    Number(general, "currentcap", mangled),
                    ValueTally(Child(general, "kills"), mangled),
                    ValueTally(Child(general, "built"), mangled),
                    ValueTally(Child(general, "lost"), mangled)),
                Tallies(Child(army, "units"), mangled),
                Tallies(Child(army, "blueprints"), mangled),
                new ReplayArmyResourceStats(
                    Income(Child(resources, "massin"), mangled),
                    Expense(Child(resources, "massout"), mangled),
                    Income(Child(resources, "energyin"), mangled),
                    Expense(Child(resources, "energyout"), mangled),
                    Storage(Child(resources, "storage"), mangled)));
        }

        private static ReplayValueTally ValueTally(JsonElement tally, bool mangled)
            => new(Number(tally, "count", mangled), Number(tally, "mass", mangled), Number(tally, "energy", mangled));

        private static Dictionary<string, ReplayUnitTally> Tallies(JsonElement table, bool mangled)
        {
            Dictionary<string, ReplayUnitTally> tallies = new Dictionary<string, ReplayUnitTally>();
            if (table.ValueKind != JsonValueKind.Object)
            {
                return tallies;
            }

            foreach (JsonProperty property in table.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    tallies[property.Name.ToLowerInvariant()] = new ReplayUnitTally(
                        Number(property.Value, "built", mangled),
                        Number(property.Value, "kills", mangled),
                        Number(property.Value, "lost", mangled),
                        OptionalNumber(property.Value, "lowest_health", mangled));
                }
            }

            return tallies;
        }

        private static ReplayResourceIncome Income(JsonElement income, bool mangled)
            => new(Number(income, "total", mangled), Number(income, "rate", mangled),
                   Number(income, "reclaimed", mangled), Number(income, "reclaimRate", mangled));

        private static ReplayResourceExpense Expense(JsonElement expense, bool mangled)
            => new(Number(expense, "total", mangled), Number(expense, "rate", mangled), Number(expense, "excess", mangled));

        private static ReplayResourceStorage Storage(JsonElement storage, bool mangled)
            => new(Number(storage, "storedMass", mangled), Number(storage, "maxMass", mangled),
                   Number(storage, "storedEnergy", mangled), Number(storage, "maxEnergy", mangled));

        private static JsonElement Child(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement child) ? child : default;

        private static string? String(JsonElement element, string name)
            => Child(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

        private static double Number(JsonElement element, string name, bool mangled)
            => OptionalNumber(element, name, mangled) ?? 0;

        private static double? OptionalNumber(JsonElement element, string name, bool mangled)
        {
            JsonElement value = Child(element, name);
            string? text = value.ValueKind switch
            {
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.String => value.GetString(),
                _ => null,
            };

            if (text is null)
            {
                return null;
            }

            if (mangled)
            {
                text = Unmangle(text);
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
                ? number
                : null;
        }

        /// <summary>
        /// The game's dkson (lua/system/dkson.lua) writes numbers with
        /// <c>replace(tostring(num), ".", ".0")</c>, so every fractional number gets a spurious 0
        /// after the decimal point: 1415.1 is written as 1415.01. Integers are unaffected.
        /// </summary>
        private static string Unmangle(string number)
        {
            int point = number.IndexOf('.');
            return point >= 0 && point + 1 < number.Length && number[point + 1] == '0'
                ? number.Remove(point + 1, 1)
                : number;
        }

        /// <summary>
        /// Whether the payload was written by the dkson that inserts a 0 after the decimal point:
        /// true when there is at least one fractional number and all of them show that 0. A fixed
        /// encoder would write many fractional numbers without it, so they are then read as-is.
        /// </summary>
        private static bool HasMangledDecimals(JsonElement root)
        {
            int fractional = 0;
            int withZero = 0;
            Count(root);
            return fractional > 0 && fractional == withZero;

            void Count(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (JsonProperty property in element.EnumerateObject())
                        {
                            Count(property.Value);
                        }
                        break;
                    case JsonValueKind.Array:
                        foreach (JsonElement item in element.EnumerateArray())
                        {
                            Count(item);
                        }
                        break;
                    case JsonValueKind.Number:
                        string text = element.GetRawText();
                        int point = text.IndexOf('.');
                        if (point >= 0)
                        {
                            fractional++;
                            if (point + 1 < text.Length && text[point + 1] == '0')
                            {
                                withZero++;
                            }
                        }
                        break;
                }
            }
        }
    }
}
