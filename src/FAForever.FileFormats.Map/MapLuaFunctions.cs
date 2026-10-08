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

        /// <summary>
        /// The reverse of <see cref="All"/>, for <see cref="LuaSourceWriter"/>: a string, number or
        /// boolean in its wrapper (<c>STRING( 'Mass' )</c>, <c>FLOAT( 100 )</c>, <c>BOOLEAN( true )</c>)
        /// and a tagged table as its call (<c>VECTOR3( 1, 2, 3 )</c>), as the map editors write the
        /// fields of markers and areas. Other values are left to the writer.
        /// </summary>
        public static string? Wrap(LuaData value) => value switch
        {
            LuaData.String text => $"STRING( {LuaSourceWriter.Literal(text.Value)} )",
            LuaData.Number number => $"FLOAT( {LuaSourceWriter.Literal(number.Value)} )",
            LuaData.Bool boolean => $"BOOLEAN( {LuaSourceWriter.Literal(boolean.Value)} )",
            LuaData.Table table when TaggedCall(table) is string call => call,
            _ => null,
        };

        /// <summary>
        /// <c>VECTOR3( x, y, z )</c> for a position or orientation, each number as its shortest float.
        /// </summary>
        public static string Vector3(System.Numerics.Vector3 vector) =>
            $"VECTOR3( {LuaSourceWriter.Literal(vector.X)}, {LuaSourceWriter.Literal(vector.Y)}, {LuaSourceWriter.Literal(vector.Z)} )";

        /// <summary>
        /// The call that made a table of <see cref="Tagged"/>, or null for any other table.
        /// </summary>
        private static string? TaggedCall(LuaData.Table table)
        {
            if (!table.TryGetStringValue("type", out string? type) || type is not ("VECTOR2" or "VECTOR3" or "RECTANGLE"))
            {
                return null;
            }

            List<string> arguments = new List<string>();
            for (int i = 1; table.Value.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out LuaData? argument); i++)
            {
                if (argument is not LuaData.Number number)
                {
                    return null;
                }
                arguments.Add(LuaSourceWriter.Literal(number.Value));
            }
            return arguments.Count + 1 == table.Value.Count ? $"{type}( {string.Join(", ", arguments)} )" : null;
        }

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
