using System.Globalization;
using System.Text;

namespace FAForever.FileFormats.Lua
{
    /// <summary>
    /// Formats a value for <see cref="LuaSourceWriter"/>: returns the expression for a value it
    /// knows how to write (e.g. <c>VECTOR3( 1, 2, 3 )</c> for a tagged table), or null to write it
    /// as plain Lua.
    /// </summary>
    public delegate string? LuaInlineFormatter(LuaData value);

    /// <summary>
    /// Writes Lua source for data, the reverse of <see cref="LuaSourceParser"/>: what it writes reads
    /// back to the same <see cref="LuaData"/>. Lines are indented by four spaces per level and end
    /// in <c>\n</c>.
    /// </summary>
    /// <remarks>
    /// A <see cref="LuaData.Table"/> keeps its keys as strings, so the writer chooses the form of a
    /// key from its text: "1", "2", ... from the start are positional entries, other whole numbers
    /// and booleans are written as such (<c>[5]</c>, <c>[true]</c>), and the rest as names or quoted
    /// strings. A string key that looks like a number (<c>['5']</c>) therefore comes back as <c>[5]</c>,
    /// which <see cref="LuaSourceParser"/> reads to the same table.
    /// </remarks>
    public sealed class LuaSourceWriter
    {
        private const string Indent = "    ";

        private static readonly HashSet<string> Reserved =
        [
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
            "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
        ];

        private readonly StringBuilder _builder = new StringBuilder();
        private int _depth;

        /// <summary>
        /// Writes a line at the current indentation.
        /// </summary>
        public LuaSourceWriter Line(string text = "")
        {
            if (text.Length > 0)
            {
                for (int i = 0; i < _depth; i++)
                {
                    _builder.Append(Indent);
                }
            }
            _builder.Append(text).Append('\n');
            return this;
        }

        /// <summary>
        /// Writes a line that opens a table, e.g. <c>Scenario = {</c>, and indents what follows.
        /// </summary>
        public LuaSourceWriter Open(string text)
        {
            Line(text);
            _depth++;
            return this;
        }

        /// <summary>
        /// Ends the indentation of <see cref="Open"/> and writes the line that closes the table.
        /// </summary>
        public LuaSourceWriter Close(string text = "},")
        {
            if (_depth == 0)
            {
                throw new InvalidOperationException("Nothing to close");
            }
            _depth--;
            return Line(text);
        }

        /// <summary>
        /// Opens a table entry: <c>key = {</c>.
        /// </summary>
        public LuaSourceWriter OpenField(string key, string prefix = "") => Open($"{Key(key)} = {prefix}{{");

        /// <summary>
        /// Writes <c>key = expression,</c>, the expression as it is.
        /// </summary>
        public LuaSourceWriter Field(string key, string expression) => Line($"{Key(key)} = {expression},");

        /// <summary>
        /// Writes <c>key = value,</c>; a table is spread over lines.
        /// </summary>
        /// <param name="inline">Writes the values it knows, at every depth.</param>
        public LuaSourceWriter Field(string key, LuaData value, LuaInlineFormatter? inline = null)
        {
            WriteValue($"{Key(key)} = ", value, ",", inline);
            return this;
        }

        /// <summary>
        /// Writes the entries of a table, without its braces.
        /// </summary>
        public LuaSourceWriter Fields(LuaData.Table table, LuaInlineFormatter? inline = null)
        {
            int positional = PositionalCount(table);
            for (int i = 1; i <= positional; i++)
            {
                WriteValue("", table.Value[i.ToString(CultureInfo.InvariantCulture)], ",", inline);
            }
            foreach ((string key, LuaData value) in table.Value)
            {
                if (!IsPositional(key, positional))
                {
                    Field(key, value, inline);
                }
            }
            return this;
        }

        /// <summary>
        /// The source written so far.
        /// </summary>
        public override string ToString() => _builder.ToString();

        /// <summary>
        /// A value as a Lua expression; a table that holds tables is spread over lines.
        /// </summary>
        public static string Format(LuaData value, LuaInlineFormatter? inline = null)
        {
            LuaSourceWriter writer = new LuaSourceWriter();
            writer.WriteValue("", value, "", inline);
            return writer.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// A string in single quotes. Backslashes, quotes and control characters are escaped; other
        /// characters are written as they are.
        /// </summary>
        public static string Literal(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length + 2).Append('\'');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append(@"\\"); break;
                    case '\'': builder.Append(@"\'"); break;
                    case '\n': builder.Append(@"\n"); break;
                    case '\r': builder.Append(@"\r"); break;
                    case '\t': builder.Append(@"\t"); break;
                    case < ' ' or '\x7f':
                        // three digits, so that a digit after it is not read as part of the escape
                        builder.Append('\\').Append(((int)c).ToString("000", CultureInfo.InvariantCulture));
                        break;
                    default: builder.Append(c); break;
                }
            }
            return builder.Append('\'').ToString();
        }

        /// <summary>
        /// The shortest number that reads back to the same double.
        /// </summary>
        /// <exception cref="ArgumentException">The number is not finite: Lua has no literal for it.</exception>
        public static string Literal(double value) => double.IsFinite(value)
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : throw new ArgumentException($"Lua has no literal for {value}", nameof(value));

        /// <summary>
        /// The shortest number that reads back to the same float, e.g. <c>2.70329</c> where the double
        /// would be <c>2.7032899856567383</c>.
        /// </summary>
        /// <exception cref="ArgumentException">The number is not finite: Lua has no literal for it.</exception>
        public static string Literal(float value) => float.IsFinite(value)
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : throw new ArgumentException($"Lua has no literal for {value}", nameof(value));

        public static string Literal(bool value) => value ? "true" : "false";

        /// <summary>
        /// A scalar value as a literal: nil, a boolean, a number or a string.
        /// </summary>
        /// <exception cref="ArgumentException">The value is a table.</exception>
        public static string Literal(LuaData value) => value switch
        {
            LuaData.Nil => "nil",
            LuaData.Bool boolean => Literal(boolean.Value),
            LuaData.Number number => Literal(number.Value),
            LuaData.String text => Literal(text.Value),
            _ => throw new ArgumentException($"A {value.GetType().Name} has no literal", nameof(value)),
        };

        /// <summary>
        /// A table key as written before <c>=</c>: a name as it is (<c>size</c>), anything else in
        /// brackets (<c>['Mass 08']</c>, <c>[5]</c>, <c>[true]</c>).
        /// </summary>
        public static string Key(string key)
        {
            if (IsName(key))
            {
                return key;
            }
            if (key is "true" or "false" || IsWholeNumber(key))
            {
                return $"[{key}]";
            }
            return $"[{Literal(key)}]";
        }

        /// <summary>
        /// Whether the text is a Lua name: letters, digits and underscores, not starting with a digit,
        /// and not a reserved word.
        /// </summary>
        public static bool IsName(string text) =>
            text.Length > 0
            && (char.IsAsciiLetter(text[0]) || text[0] == '_')
            && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            && !Reserved.Contains(text);

        private static bool IsWholeNumber(string text) =>
            long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number)
            && number.ToString(CultureInfo.InvariantCulture) == text;

        /// <summary>
        /// How many entries from "1" on the table has without a gap.
        /// </summary>
        private static int PositionalCount(LuaData.Table table)
        {
            int count = 0;
            while (table.Value.ContainsKey((count + 1).ToString(CultureInfo.InvariantCulture)))
            {
                count++;
            }
            return count;
        }

        private static bool IsPositional(string key, int positional) =>
            int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index >= 1 && index <= positional
            && index.ToString(CultureInfo.InvariantCulture) == key;

        private void WriteValue(string prefix, LuaData value, string suffix, LuaInlineFormatter? inline)
        {
            if (inline?.Invoke(value) is string expression)
            {
                Line(prefix + expression + suffix);
                return;
            }

            if (value is not LuaData.Table table)
            {
                Line(prefix + Literal(value) + suffix);
                return;
            }

            if (table.Value.Count == 0)
            {
                Line(prefix + "{}" + suffix);
                return;
            }

            // a list of plain values fits on one line: { 256, 256 }, { 'ARMY_1', 'ARMY_2' }
            int count = table.Value.Count;
            if (PositionalCount(table) == count && table.Value.Values.All(entry => entry is not LuaData.Table && inline?.Invoke(entry) is null))
            {
                IEnumerable<string> literals = Enumerable.Range(1, count).Select(i => Literal(table.Value[i.ToString(CultureInfo.InvariantCulture)]));
                Line($"{prefix}{{ {string.Join(", ", literals)} }}{suffix}");
                return;
            }

            Open(prefix + "{");
            Fields(table, inline);
            Close("}" + suffix);
        }
    }
}
