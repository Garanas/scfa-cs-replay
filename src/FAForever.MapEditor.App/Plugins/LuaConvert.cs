using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lua;

namespace FAForever.MapEditor.App.Plugins;

/// <summary>
/// Values between Lua and the editor: a command's arguments and results are JSON, which a plugin
/// reads and writes as tables (user story F-15).
/// </summary>
public static class LuaConvert
{
    /// <summary>
    /// How deep tables may nest before a value is refused: a table that holds itself would never end.
    /// </summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// A JSON value as Lua: objects and arrays become tables (arrays from 1), numbers numbers, null nil.
    /// </summary>
    public static LuaValue ToLua(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return LuaValue.Nil;
            case JsonObject obj:
            {
                LuaTable table = new LuaTable(0, obj.Count);
                foreach ((string key, JsonNode? value) in obj)
                {
                    if (value is not null)
                    {
                        table[key] = ToLua(value);
                    }
                }
                return table;
            }
            case JsonArray array:
            {
                LuaTable table = new LuaTable(array.Count, 0);
                for (int i = 0; i < array.Count; i++)
                {
                    table[i + 1] = ToLua(array[i]);
                }
                return table;
            }
            case JsonValue value:
                return value.GetValueKind() switch
                {
                    JsonValueKind.String => value.GetValue<string>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => value.GetValue<double>(),
                    _ => LuaValue.Nil,
                };
            default:
                return LuaValue.Nil;
        }
    }

    /// <summary>
    /// What a command answered (a <see cref="JsonNode"/>, a text, a number, a list) as Lua.
    /// </summary>
    public static LuaValue ToLua(object? value) => value switch
    {
        null => LuaValue.Nil,
        JsonNode node => ToLua(node),
        string text => text,
        bool flag => flag,
        double or float or int or long or decimal => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => ToLua(JsonSerializer.SerializeToNode(value, value.GetType(), JsonSerializerOptions.Web)),
    };

    /// <summary>
    /// A Lua value as JSON, for a command's arguments: a table whose keys are 1 to n is an array,
    /// any other table an object (its keys as text); functions and the like are refused.
    /// </summary>
    /// <exception cref="ArgumentException">The value cannot be JSON.</exception>
    public static JsonNode? ToJson(LuaValue value, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            throw new ArgumentException($"Tables nest deeper than {MaxDepth}");
        }
        switch (value.Type)
        {
            case LuaValueType.Nil:
                return null;
            case LuaValueType.Boolean:
                return JsonValue.Create(value.Read<bool>());
            case LuaValueType.Number:
            {
                double number = value.Read<double>();
                return Math.Abs(number) < 1e15 && number == Math.Floor(number) ? JsonValue.Create((long)number) : JsonValue.Create(number);
            }
            case LuaValueType.String:
                return JsonValue.Create(value.Read<string>());
            case LuaValueType.Table:
            {
                LuaTable table = value.Read<LuaTable>();
                if (table.HashMapCount == 0 && table.ArrayLength > 0)
                {
                    JsonArray array = [];
                    for (int i = 1; i <= table.ArrayLength; i++)
                    {
                        array.Add(ToJson(table[i], depth + 1));
                    }
                    return array;
                }
                JsonObject obj = [];
                foreach (KeyValuePair<LuaValue, LuaValue> pair in table)
                {
                    string key = pair.Key.Type == LuaValueType.Number
                        ? pair.Key.Read<double>().ToString(CultureInfo.InvariantCulture)
                        : pair.Key.ToString();
                    obj[key] = ToJson(pair.Value, depth + 1);
                }
                return obj;
            }
            default:
                throw new ArgumentException($"A {value.TypeToString()} cannot be given to a command");
        }
    }

    /// <summary>
    /// A field of a table, or nil.
    /// </summary>
    public static LuaValue Field(LuaTable table, string key) => table.TryGetValue(key, out LuaValue value) ? value : LuaValue.Nil;

    public static string? Text(LuaTable table, string key) => Field(table, key) is { Type: LuaValueType.String } value ? value.Read<string>() : null;

    public static double? Number(LuaTable table, string key) => Field(table, key) is { Type: LuaValueType.Number } value ? value.Read<double>() : null;

    public static LuaFunction? Function(LuaTable table, string key) => Field(table, key) is { Type: LuaValueType.Function } value ? value.Read<LuaFunction>() : null;

    /// <summary>
    /// A list of texts: one text, or a table of them.
    /// </summary>
    public static IReadOnlyList<string> Texts(LuaValue value) => value.Type switch
    {
        LuaValueType.String => [value.Read<string>()],
        LuaValueType.Table => [.. Items(value.Read<LuaTable>()).Select(item => item.Type == LuaValueType.String
            ? item.Read<string>()
            : throw new ArgumentException($"Expected a list of texts, found a {item.TypeToString()}"))],
        LuaValueType.Nil => [],
        _ => throw new ArgumentException($"Expected a text or a list of texts, found a {value.TypeToString()}"),
    };

    /// <summary>
    /// The items 1 to n of a table.
    /// </summary>
    public static IEnumerable<LuaValue> Items(LuaTable table)
    {
        for (int i = 1; i <= table.ArrayLength; i++)
        {
            yield return table[i];
        }
    }

    /// <summary>
    /// A list as a Lua table, from 1.
    /// </summary>
    public static LuaTable List(IEnumerable<LuaValue> items)
    {
        LuaTable table = new LuaTable();
        int i = 1;
        foreach (LuaValue item in items)
        {
            table[i++] = item;
        }
        return table;
    }
}
