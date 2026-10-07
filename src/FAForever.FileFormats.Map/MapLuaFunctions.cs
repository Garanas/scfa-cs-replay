using System.Globalization;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// The functions that the Lua files of a map call: <c>STRING( 'Mass' )</c>,
    /// <c>VECTOR3( 21.5, 81.2, 253.5 )</c>, <c>GROUP { ... }</c>, ... They mirror
    /// <c>lua/dataInit.lua</c> in the FA repository: the wrappers of plain values return the value,
    /// the others a table tagged with its <c>type</c>.
    /// </summary>
    public static class MapLuaFunctions
    {
        /// <summary>
        /// Every function, by name, for <see cref="LuaSourceParser.Execute"/>.
        /// </summary>
        public static IReadOnlyDictionary<string, LuaFunction> All { get; } = new Dictionary<string, LuaFunction>
        {
            ["BOOLEAN"] = First,
            ["INTEGER"] = First,
            ["FLOAT"] = First,
            ["STRING"] = First,
            ["VECTOR2"] = arguments => Tagged(arguments, 2, "VECTOR2"),
            ["VECTOR3"] = arguments => Tagged(arguments, 3, "VECTOR3"),
            ["RECTANGLE"] = arguments => Tagged(arguments, 4, "RECTANGLE"),
            ["GROUP"] = Group,
        };

        private static LuaData First(IReadOnlyList<LuaData> arguments) =>
            arguments.Count > 0 ? arguments[0] : new LuaData.Nil();

        private static LuaData Tagged(IReadOnlyList<LuaData> arguments, int count, string type)
        {
            Dictionary<string, LuaData> entries = new Dictionary<string, LuaData>();
            for (int i = 0; i < count && i < arguments.Count; i++)
            {
                if (arguments[i] is not LuaData.Nil)
                {
                    entries[(i + 1).ToString(CultureInfo.InvariantCulture)] = arguments[i];
                }
            }
            entries["type"] = new LuaData.String(type);
            return new LuaData.Table(entries);
        }

        private static LuaData Group(IReadOnlyList<LuaData> arguments)
        {
            if (arguments.Count == 0 || arguments[0] is not LuaData.Table table)
            {
                return new LuaData.Nil();
            }
            Dictionary<string, LuaData> entries = new Dictionary<string, LuaData>(table.Value)
            {
                ["type"] = new LuaData.String("GROUP"),
            };
            return new LuaData.Table(entries);
        }
    }
}
