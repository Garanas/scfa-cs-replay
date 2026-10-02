using System.Globalization;
using System.Text;

namespace FAForever.Replay
{
    /// <summary>
    /// Renders <see cref="LuaData"/> as a compact, single-line, human-readable string,
    /// e.g. for inspecting sim callback payloads: {To=3, Msg={text='glhf', to='all'}}.
    /// Output is capped (depth, entries per table, string length) so arbitrarily large
    /// payloads stay displayable.
    /// </summary>
    public static class LuaDataFormatter
    {
        public static string Format(LuaData data, int maxDepth = 3, int maxEntriesPerTable = 8, int maxStringLength = 48)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, data, maxDepth, maxEntriesPerTable, maxStringLength);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, LuaData data, int remainingDepth, int maxEntriesPerTable, int maxStringLength)
        {
            switch (data)
            {
                case LuaData.Nil:
                    builder.Append("nil");
                    break;

                case LuaData.Bool boolean:
                    builder.Append(boolean.Value ? "true" : "false");
                    break;

                case LuaData.Number number:
                    builder.Append(number.Value.ToString("0.###", CultureInfo.InvariantCulture));
                    break;

                case LuaData.String text:
                    builder.Append('\'');
                    builder.Append(text.Value.Length > maxStringLength ? text.Value[..maxStringLength] + "…" : text.Value);
                    builder.Append('\'');
                    break;

                case LuaData.Table table when remainingDepth <= 0:
                    builder.Append(table.Value.Count == 0 ? "{}" : "{…}");
                    break;

                case LuaData.Table table:
                    builder.Append('{');
                    int written = 0;
                    foreach ((string key, LuaData value) in table.Value)
                    {
                        if (written == maxEntriesPerTable)
                        {
                            builder.Append(", …");
                            break;
                        }
                        if (written > 0)
                        {
                            builder.Append(", ");
                        }
                        builder.Append(key);
                        builder.Append('=');
                        Append(builder, value, remainingDepth - 1, maxEntriesPerTable, maxStringLength);
                        written++;
                    }
                    builder.Append('}');
                    break;

                default:
                    builder.Append('?');
                    break;
            }
        }
    }
}
