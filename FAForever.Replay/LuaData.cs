
namespace FAForever.Replay
{
    /// <summary>
    /// Represents a Lua data type.
    /// </summary>
    public interface LuaData
    {
        /// <summary>
        /// Represents a Lua nil value.
        /// </summary>
        public record Nil() : LuaData;

        /// <summary>
        /// Represents a Lua boolean value.
        /// </summary>
        /// <param name="Value">The boolean value.</param>
        public record Bool(bool Value) : LuaData;

        /// <summary>
        /// Represents a Lua number value.
        /// </summary>
        /// <param name="Value">The number value.</param>
        public record Number(double Value) : LuaData;

        /// <summary>
        /// Represents a Lua string value.
        /// </summary>
        /// <param name="Value">The string value.</param>
        public record String(string Value) : LuaData;

        /// <summary>
        /// Represents a Lua table value.
        /// </summary>
        /// <param name="Value">The table value, represented as a dictionary of key-value pairs.</param>
        public record Table(Dictionary<string, LuaData> Value) : LuaData
        {
            /// <summary>
            /// Retrieves the string value associated with the specified key in the table.
            /// </summary>
            /// <param name="key">The key to look up.</param>
            /// <param name="value">The string value associated with the key, or the default value if not found.</param>
            /// <returns>True if the key was found, false otherwise.</returns>
            public bool TryGetStringValue(string key, out string? value)
            {
                // Check if the table contains the key
                if (Value.TryGetValue(key, out LuaData? luaData) && luaData is LuaData.String stringValue)
                {
                    value = stringValue.Value;
                    return true;
                }
                value = null;
                return false;
            }

            /// <summary>
            /// Retrieves the number value associated with the specified key in the table.
            /// </summary>
            /// <param name="key">The key to look up.</param>
            /// <param name="value">The number value associated with the key, or the default value if not found.</param>
            /// <returns>True if the key was found, false otherwise.</returns>
            public bool TryGetNumberValue(string key, out double? value)
            {
                // Check if the table contains the key
                if (Value.TryGetValue(key, out LuaData? luaData) && luaData is LuaData.Number numberValue)
                {
                    value = numberValue.Value;
                    return true;
                }
                value = null;
                return false;
            }

            /// <summary>
            /// Retrieves the boolean value associated with the specified key in the table.
            /// </summary>
            /// <param name="key">The key to look up.</param>
            /// <param name="value">The boolean value associated with the key, or the default value if not found.</param>
            /// <returns>True if the key was found, false otherwise.</returns>
            public bool TryGetBooleanValue(string key, out bool? value)
            {
                // Check if the table contains the key
                if (Value.TryGetValue(key, out LuaData? luaData) && luaData is LuaData.Bool boolValue)
                {
                    value = boolValue.Value;
                    return true;
                }
                value = null;
                return false;
            }

            /// <summary>
            /// Retrieves the table value associated with the specified key in the table.
            /// </summary>
            /// <param name="key">The key to look up.</param>
            /// <param name="value">The table value associated with the key, or the default value if not found.</param>
            /// <returns>True if the key was found, false otherwise.</returns>
            public bool TryGetTableValue(string key, out LuaData.Table? value)
            {
                // Check if the table contains the key
                if (Value.TryGetValue(key, out LuaData? luaData) && luaData is LuaData.Table tableValue)
                {
                    value = tableValue;
                    return true;
                }
                value = null;
                return false;
            }
        }
    }
}
