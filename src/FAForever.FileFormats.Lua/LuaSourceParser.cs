using System.Globalization;
using System.Text;

namespace FAForever.FileFormats.Lua
{
    /// <summary>
    /// A function that Lua source can call by name, e.g. <c>Sound { Bank = 'UEL', ... }</c>.
    /// </summary>
    public delegate LuaData LuaFunction(IReadOnlyList<LuaData> arguments);

    /// <summary>
    /// Thrown when Lua source cannot be parsed or evaluated.
    /// </summary>
    public sealed class LuaSyntaxException(string message, int line)
        : FormatException($"Line {line}: {message}")
    {
        public int Line { get; } = line;
    }

    /// <summary>
    /// Parses and evaluates the subset of Lua that data files such as blueprints (<c>.bp</c>) are
    /// written in: top-level function calls (<c>UnitBlueprint { ... }</c>) and assignments, with
    /// constant expressions as arguments. Supported: nil, booleans, numbers (decimal, exponent, hex),
    /// strings ('', "", [[long]]), table constructors, variables and field access, calls to the
    /// functions supplied by the caller, and the arithmetic, concatenation, comparison and logical
    /// operators. Not supported: function definitions and control flow (if, for, while, ...).
    /// </summary>
    /// <remarks>
    /// Tables become <see cref="LuaData.Table"/>, whose keys are strings: positional entries get
    /// "1", "2", ... like tables read from a replay, and a boolean key becomes "true"/"false".
    /// Entries whose value is nil are left out, as in Lua.
    /// </remarks>
    public sealed class LuaSourceParser
    {
        private static readonly LuaData.Nil Nil = new LuaData.Nil();
        private static readonly LuaData.Bool True = new LuaData.Bool(true);
        private static readonly LuaData.Bool False = new LuaData.Bool(false);

        private readonly Lexer _lexer;
        private readonly IReadOnlyDictionary<string, LuaFunction> _functions;
        private readonly Dictionary<string, LuaData> _variables = new Dictionary<string, LuaData>();

        private LuaSourceParser(string source, IReadOnlyDictionary<string, LuaFunction> functions)
        {
            _lexer = new Lexer(source);
            _functions = functions;
        }

        /// <summary>
        /// Runs a chunk of Lua source: every top-level call invokes the matching function.
        /// </summary>
        /// <returns>The global variables the chunk assigned.</returns>
        public static IReadOnlyDictionary<string, LuaData> Execute(string source, IReadOnlyDictionary<string, LuaFunction> functions)
        {
            LuaSourceParser parser = new LuaSourceParser(source, functions);
            parser.ParseChunk();
            return parser._variables;
        }

        /// <summary>
        /// Evaluates a single expression, e.g. <c>{ 1, 2, Name = 'x' }</c>.
        /// </summary>
        public static LuaData ParseExpression(string source, IReadOnlyDictionary<string, LuaFunction>? functions = null)
        {
            LuaSourceParser parser = new LuaSourceParser(source, functions ?? new Dictionary<string, LuaFunction>());
            LuaData value = parser.ParseExpression();
            parser.Expect(TokenKind.EndOfFile);
            return value;
        }

        #region Statements

        private void ParseChunk()
        {
            while (_lexer.Current.Kind != TokenKind.EndOfFile)
            {
                if (Accept(";"))
                {
                    continue;
                }

                bool isLocal = _lexer.Current.Kind == TokenKind.Name && _lexer.Current.Text == "local";
                if (isLocal)
                {
                    _lexer.Next();
                }

                Token name = Expect(TokenKind.Name);
                if (Accept("="))
                {
                    LuaData value = ParseExpression();
                    if (value is LuaData.Nil)
                    {
                        _variables.Remove(name.Text);
                    }
                    else
                    {
                        _variables[name.Text] = value;
                    }
                }
                else if (isLocal)
                {
                    _variables.Remove(name.Text);
                }
                else if (!TryParseCall(name, out _))
                {
                    throw Error($"Expected a function call or an assignment after '{name.Text}'");
                }
            }
        }

        #endregion

        #region Expressions

        // Binary operator priorities (left, right), from the Lua 5.0 reference implementation.
        private static (int Left, int Right)? GetBinaryPriority(in Token token) => token.Kind switch
        {
            TokenKind.Symbol => token.Text switch
            {
                "+" or "-" => (6, 6),
                "*" or "/" or "%" => (7, 7),
                "^" => (10, 9),
                ".." => (5, 4),
                "==" or "~=" or "!=" or "<" or "<=" or ">" or ">=" => (3, 3),
                _ => null,
            },
            TokenKind.Name => token.Text switch
            {
                "and" => (2, 2),
                "or" => (1, 1),
                _ => null,
            },
            _ => null,
        };

        private const int UnaryPriority = 8;

        private LuaData ParseExpression(int limit = 0)
        {
            LuaData left;
            Token token = _lexer.Current;
            if (token.Is("-") || token.Is("not"))
            {
                _lexer.Next();
                LuaData operand = ParseExpression(UnaryPriority);
                left = token.Text == "not"
                    ? (IsTruthy(operand) ? False : True)
                    : new LuaData.Number(-ToNumber(operand, token));
            }
            else
            {
                left = ParseSimpleExpression();
            }

            while (GetBinaryPriority(_lexer.Current) is { } priority && priority.Left > limit)
            {
                Token op = _lexer.Current;
                _lexer.Next();
                LuaData right = ParseExpression(priority.Right);
                left = ApplyBinary(op, left, right);
            }

            return left;
        }

        private LuaData ParseSimpleExpression()
        {
            Token token = _lexer.Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    _lexer.Next();
                    return new LuaData.Number(token.Number);

                case TokenKind.String:
                    _lexer.Next();
                    return new LuaData.String(token.Text);

                case TokenKind.Name when token.Text == "nil":
                    _lexer.Next();
                    return Nil;

                case TokenKind.Name when token.Text == "true":
                    _lexer.Next();
                    return True;

                case TokenKind.Name when token.Text == "false":
                    _lexer.Next();
                    return False;

                case TokenKind.Name when token.Text == "function":
                    throw Error("Function definitions are not supported");

                case TokenKind.Name:
                    _lexer.Next();
                    return ParseSuffixes(TryParseCall(token, out LuaData result) ? result : LookupVariable(token));

                case TokenKind.Symbol when token.Text == "{":
                    return ParseTable();

                case TokenKind.Symbol when token.Text == "(":
                    _lexer.Next();
                    LuaData inner = ParseExpression();
                    Expect(")");
                    return ParseSuffixes(inner);

                default:
                    throw Error($"Unexpected {Describe(token)}");
            }
        }

        /// <summary>
        /// Parses a call to a (possibly dotted) function name, e.g. <c>Sound { ... }</c> or
        /// <c>math.sqrt(2)</c>. The name token has already been consumed. Returns false, without
        /// consuming anything, when the name is not followed by call arguments.
        /// </summary>
        private bool TryParseCall(Token name, out LuaData result)
        {
            // look ahead for `a.b.c` followed by call arguments, without consuming a field access
            string path = name.Text;
            int fields = 0;
            while (_lexer.Ahead(fields * 2).Is(".") && _lexer.Ahead(fields * 2 + 1).Kind == TokenKind.Name)
            {
                path += "." + _lexer.Ahead(fields * 2 + 1).Text;
                fields++;
            }

            Token start = _lexer.Ahead(fields * 2);
            if (!(start.Is("(") || start.Is("{") || start.Kind == TokenKind.String))
            {
                result = Nil;
                return false;
            }

            if (!_functions.TryGetValue(path, out LuaFunction? function))
            {
                throw Error($"Call to unknown function '{path}'");
            }

            for (int i = 0; i < fields * 2; i++)
            {
                _lexer.Next();
            }

            List<LuaData> arguments = new List<LuaData>();
            if (start.Kind == TokenKind.String)
            {
                _lexer.Next();
                arguments.Add(new LuaData.String(start.Text));
            }
            else if (start.Is("{"))
            {
                arguments.Add(ParseTable());
            }
            else
            {
                _lexer.Next();
                if (!Accept(")"))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    } while (Accept(","));
                    Expect(")");
                }
            }

            result = function(arguments);
            return true;
        }

        private LuaData ParseSuffixes(LuaData value)
        {
            while (true)
            {
                Token token = _lexer.Current;
                LuaData key;
                if (token.Is("."))
                {
                    _lexer.Next();
                    key = new LuaData.String(Expect(TokenKind.Name).Text);
                }
                else if (token.Is("["))
                {
                    _lexer.Next();
                    key = ParseExpression();
                    Expect("]");
                }
                else if (token.Is("(") || token.Is("{") || token.Kind == TokenKind.String)
                {
                    throw Error("Only named functions can be called");
                }
                else
                {
                    return value;
                }

                if (value is not LuaData.Table table)
                {
                    throw new LuaSyntaxException("Attempt to index a non-table value", token.Line);
                }
                value = table.Value.TryGetValue(ToKey(key, token), out LuaData? field) ? field : Nil;
            }
        }

        private LuaData LookupVariable(Token name) =>
            _variables.TryGetValue(name.Text, out LuaData? value) ? value : Nil;

        private LuaData.Table ParseTable()
        {
            Expect("{");
            Dictionary<string, LuaData> entries = new Dictionary<string, LuaData>();
            int index = 1;

            while (!Accept("}"))
            {
                Token token = _lexer.Current;
                string key;
                LuaData value;
                if (token.Is("["))
                {
                    _lexer.Next();
                    key = ToKey(ParseExpression(), token);
                    Expect("]");
                    Expect("=");
                    value = ParseExpression();
                }
                else if (token.Kind == TokenKind.Name && _lexer.Ahead(1).Is("="))
                {
                    _lexer.Next();
                    _lexer.Next();
                    key = token.Text;
                    value = ParseExpression();
                }
                else
                {
                    key = IntegerKey(index++);
                    value = ParseExpression();
                }

                if (value is LuaData.Nil)
                {
                    entries.Remove(key);
                }
                else
                {
                    entries[key] = value;
                }

                if (!Accept(",") && !Accept(";"))
                {
                    Expect("}");
                    break;
                }
            }

            return new LuaData.Table(entries);
        }

        #endregion

        #region Semantics

        private static readonly string[] IntegerKeys = Enumerable.Range(0, 257).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();

        private static string IntegerKey(int index) =>
            (uint)index < (uint)IntegerKeys.Length ? IntegerKeys[index] : index.ToString(CultureInfo.InvariantCulture);

        private static string ToKey(LuaData key, in Token at) => key switch
        {
            LuaData.String text => text.Value,
            LuaData.Number number when number.Value == Math.Floor(number.Value) && Math.Abs(number.Value) < int.MaxValue => IntegerKey((int)number.Value),
            LuaData.Number number => number.Value.ToString("R", CultureInfo.InvariantCulture),
            LuaData.Bool boolean => boolean.Value ? "true" : "false",
            LuaData.Nil => throw new LuaSyntaxException("Table index is nil", at.Line),
            _ => throw new LuaSyntaxException("Tables cannot be used as table keys", at.Line),
        };

        private static bool IsTruthy(LuaData value) => value is not (LuaData.Nil or LuaData.Bool { Value: false });

        private static double ToNumber(LuaData value, in Token at) => value switch
        {
            LuaData.Number number => number.Value,
            LuaData.String text when Lexer.TryParseNumber(text.Value.Trim(), out double parsed) => parsed,
            _ => throw new LuaSyntaxException($"Attempt to perform arithmetic on a {TypeName(value)} value", at.Line),
        };

        private static string ToConcatString(LuaData value, in Token at) => value switch
        {
            LuaData.String text => text.Value,
            LuaData.Number number => number.Value.ToString("G14", CultureInfo.InvariantCulture),
            _ => throw new LuaSyntaxException($"Attempt to concatenate a {TypeName(value)} value", at.Line),
        };

        private static string TypeName(LuaData value) => value switch
        {
            LuaData.Nil => "nil",
            LuaData.Bool => "boolean",
            LuaData.Number => "number",
            LuaData.String => "string",
            _ => "table",
        };

        private static LuaData ApplyBinary(in Token op, LuaData left, LuaData right)
        {
            switch (op.Text)
            {
                case "and":
                    return IsTruthy(left) ? right : left;
                case "or":
                    return IsTruthy(left) ? left : right;
                case "..":
                    return new LuaData.String(ToConcatString(left, op) + ToConcatString(right, op));
                case "==":
                    return Equals(left, right) ? True : False;
                case "~=":
                case "!=":
                    return Equals(left, right) ? False : True;
                case "<" or "<=" or ">" or ">=":
                    int comparison = (left, right) switch
                    {
                        (LuaData.Number a, LuaData.Number b) => a.Value.CompareTo(b.Value),
                        (LuaData.String a, LuaData.String b) => string.CompareOrdinal(a.Value, b.Value),
                        _ => throw new LuaSyntaxException($"Attempt to compare a {TypeName(left)} with a {TypeName(right)}", op.Line),
                    };
                    bool result = op.Text switch
                    {
                        "<" => comparison < 0,
                        "<=" => comparison <= 0,
                        ">" => comparison > 0,
                        _ => comparison >= 0,
                    };
                    return result ? True : False;
            }

            double x = ToNumber(left, op);
            double y = ToNumber(right, op);
            return new LuaData.Number(op.Text switch
            {
                "+" => x + y,
                "-" => x - y,
                "*" => x * y,
                "/" => x / y,
                "%" => x - Math.Floor(x / y) * y,
                _ => Math.Pow(x, y),
            });
        }

        #endregion

        #region Token helpers

        private bool Accept(string symbol)
        {
            if (_lexer.Current.Is(symbol))
            {
                _lexer.Next();
                return true;
            }
            return false;
        }

        private void Expect(string symbol)
        {
            if (!Accept(symbol))
            {
                throw Error($"Expected '{symbol}' but found {Describe(_lexer.Current)}");
            }
        }

        private Token Expect(TokenKind kind)
        {
            Token token = _lexer.Current;
            if (token.Kind != kind)
            {
                throw Error($"Expected {kind} but found {Describe(token)}");
            }
            _lexer.Next();
            return token;
        }

        private LuaSyntaxException Error(string message) => new LuaSyntaxException(message, _lexer.Current.Line);

        private static string Describe(in Token token) => token.Kind switch
        {
            TokenKind.EndOfFile => "the end of the file",
            TokenKind.String => "a string",
            TokenKind.Number => $"the number {token.Number.ToString(CultureInfo.InvariantCulture)}",
            _ => $"'{token.Text}'",
        };

        #endregion

        #region Lexer

        private enum TokenKind
        {
            EndOfFile,
            Name,
            Number,
            String,
            Symbol,
        }

        private readonly record struct Token(TokenKind Kind, string Text, double Number, int Line)
        {
            public bool Is(string text) => (Kind == TokenKind.Symbol || Kind == TokenKind.Name) && Text == text;
        }

        private sealed class Lexer
        {
            private readonly string _source;
            private int _position;
            private int _line = 1;
            private readonly List<Token> _lookahead = new List<Token>();

            public Token Current { get; private set; }

            public Lexer(string source)
            {
                _source = source;
                // skip a byte order mark
                if (_source.StartsWith('﻿'))
                {
                    _position = 1;
                }
                Current = Read();
            }

            public void Next()
            {
                if (_lookahead.Count > 0)
                {
                    Current = _lookahead[0];
                    _lookahead.RemoveAt(0);
                }
                else
                {
                    Current = Read();
                }
            }

            /// <summary>
            /// The token <paramref name="offset"/> positions from here: 0 is the current one.
            /// </summary>
            public Token Ahead(int offset)
            {
                if (offset == 0)
                {
                    return Current;
                }
                while (_lookahead.Count < offset)
                {
                    _lookahead.Add(Read());
                }
                return _lookahead[offset - 1];
            }

            private char At(int offset) => _position + offset < _source.Length ? _source[_position + offset] : '\0';

            private LuaSyntaxException Error(string message) => new LuaSyntaxException(message, _line);

            private Token Read()
            {
                SkipWhitespaceAndComments();
                if (_position >= _source.Length)
                {
                    return new Token(TokenKind.EndOfFile, "", 0, _line);
                }

                char c = _source[_position];
                int line = _line;

                if (char.IsAsciiLetter(c) || c == '_')
                {
                    int start = _position;
                    while (char.IsAsciiLetterOrDigit(At(0)) || At(0) == '_')
                    {
                        _position++;
                    }
                    return new Token(TokenKind.Name, _source[start.._position], 0, line);
                }

                if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(At(1))))
                {
                    return new Token(TokenKind.Number, "", ReadNumber(), line);
                }

                if (c == '"' || c == '\'')
                {
                    return new Token(TokenKind.String, ReadQuotedString(c), 0, line);
                }

                if (c == '[' && LongBracketLevel() is int level)
                {
                    return new Token(TokenKind.String, ReadLongString(level), 0, line);
                }

                string symbol = (c, At(1), At(2)) switch
                {
                    ('.', '.', '.') => "...",
                    ('.', '.', _) => "..",
                    ('=', '=', _) => "==",
                    ('~', '=', _) => "~=",
                    ('!', '=', _) => "!=",
                    ('<', '=', _) => "<=",
                    ('>', '=', _) => ">=",
                    _ when "+-*/%^#=<>(){}[];:,.".Contains(c) => c.ToString(),
                    _ => throw Error($"Unexpected character '{c}'"),
                };
                _position += symbol.Length;
                return new Token(TokenKind.Symbol, symbol, 0, line);
            }

            private void SkipWhitespaceAndComments()
            {
                while (_position < _source.Length)
                {
                    char c = _source[_position];
                    if (c == '\n')
                    {
                        _line++;
                        _position++;
                    }
                    else if (char.IsWhiteSpace(c))
                    {
                        _position++;
                    }
                    else if (c == '-' && At(1) == '-')
                    {
                        _position += 2;
                        if (At(0) == '[' && LongBracketLevel() is int level)
                        {
                            ReadLongString(level);
                        }
                        else
                        {
                            SkipLine();
                        }
                    }
                    else if (c == '#')
                    {
                        // FA's Lua also accepts '#' line comments
                        SkipLine();
                    }
                    else
                    {
                        return;
                    }
                }
            }

            private void SkipLine()
            {
                while (_position < _source.Length && _source[_position] != '\n')
                {
                    _position++;
                }
            }

            /// <summary>
            /// The level of a long bracket opening at the current position (<c>[[</c> is 0,
            /// <c>[==[</c> is 2), or null when there is none.
            /// </summary>
            private int? LongBracketLevel()
            {
                int level = 0;
                while (At(1 + level) == '=')
                {
                    level++;
                }
                return At(1 + level) == '[' ? level : null;
            }

            private string ReadLongString(int level)
            {
                int startLine = _line;
                _position += level + 2;

                // a newline directly after the opening bracket is not part of the string
                if (At(0) == '\r') _position++;
                if (At(0) == '\n') { _position++; _line++; }

                int start = _position;
                while (_position < _source.Length)
                {
                    char c = _source[_position];
                    if (c == ']')
                    {
                        int equals = 0;
                        while (At(1 + equals) == '=')
                        {
                            equals++;
                        }
                        if (equals == level && At(1 + equals) == ']')
                        {
                            string text = _source[start.._position];
                            _position += level + 2;
                            return text;
                        }
                    }
                    else if (c == '\n')
                    {
                        _line++;
                    }
                    _position++;
                }
                throw new LuaSyntaxException("Unfinished long string or comment", startLine);
            }

            private string ReadQuotedString(char quote)
            {
                _position++;
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    if (_position >= _source.Length || At(0) == '\n')
                    {
                        throw Error("Unfinished string");
                    }

                    char c = _source[_position++];
                    if (c == quote)
                    {
                        return builder.ToString();
                    }
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    char escape = At(0);
                    _position++;
                    switch (escape)
                    {
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        case 'r': builder.Append('\r'); break;
                        case 'a': builder.Append('\a'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'v': builder.Append('\v'); break;
                        case '\n': builder.Append('\n'); _line++; break;
                        case '\0': throw Error("Unfinished string");
                        default:
                            if (char.IsAsciiDigit(escape))
                            {
                                // \ddd: up to three decimal digits
                                int value = escape - '0';
                                for (int i = 0; i < 2 && char.IsAsciiDigit(At(0)); i++)
                                {
                                    value = value * 10 + (_source[_position++] - '0');
                                }
                                builder.Append((char)value);
                            }
                            else
                            {
                                // \\, \", \' and any other character stand for themselves
                                builder.Append(escape);
                            }
                            break;
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _position;
                if (At(0) == '0' && (At(1) == 'x' || At(1) == 'X'))
                {
                    _position += 2;
                    while (char.IsAsciiHexDigit(At(0)))
                    {
                        _position++;
                    }
                }
                else
                {
                    while (char.IsAsciiDigit(At(0)) || At(0) == '.')
                    {
                        _position++;
                    }
                    if (At(0) == 'e' || At(0) == 'E')
                    {
                        _position++;
                        if (At(0) == '+' || At(0) == '-')
                        {
                            _position++;
                        }
                        while (char.IsAsciiDigit(At(0)))
                        {
                            _position++;
                        }
                    }
                }

                string text = _source[start.._position];
                if (char.IsAsciiLetter(At(0)) || At(0) == '_' || !TryParseNumber(text, out double value))
                {
                    throw Error($"Malformed number '{text}{(char.IsAsciiLetterOrDigit(At(0)) ? At(0) : "")}'");
                }
                return value;
            }

            public static bool TryParseNumber(string text, out double value)
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    bool parsed = long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long hex);
                    value = hex;
                    return parsed;
                }
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }

        #endregion
    }
}
