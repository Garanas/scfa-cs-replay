using System.Text.RegularExpressions;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// What the editor reads and writes in a map's script (<c>_script.lua</c>): the area it makes the
    /// playable area at the start.
    /// </summary>
    /// <remarks>
    /// The game has no playable area of its own: the whole map is playable until the script calls
    /// <c>ScenarioFramework.SetPlayableArea('AREA_1', false)</c> (<c>lua/ScenarioFramework.lua</c>),
    /// which the map editors write into <c>OnPopulate</c>, after <c>ScenarioUtils.InitializeArmies()</c>
    /// (the FAF map editor's <c>DefaultScript.txt</c>). Of 99 vault maps, 35 call it there and 6 in
    /// <c>OnStart</c>; adaptive maps call it again later, in <c>Expand_MapExpand</c>, to grow the
    /// map during the game, and co-op missions at each mission's start. The start's playable area
    /// is therefore the first call, with a plain name, in <c>OnPopulate</c> or <c>OnStart</c>; a
    /// call in another function or behind a comment does not count.
    /// </remarks>
    public static partial class MapScript
    {
        /// <summary>
        /// The name of the area the script makes the playable area at the start; null when it makes
        /// none (the whole map is playable) or names it in a way other than a plain string.
        /// </summary>
        public static string? PlayableAreaOf(string script) => FindStartCall(script) is Match call ? call.Groups["name"].Value : null;

        /// <summary>
        /// The script with <paramref name="area"/> as the playable area at the start: the name in the
        /// start's call replaced, or a call added after <c>ScenarioUtils.InitializeArmies()</c> in
        /// <c>OnPopulate</c>. Null when the script has neither, so the editor cannot tell where it goes.
        /// </summary>
        public static string? WithPlayableArea(string script, string area)
        {
            string quoted = area.Replace("\\", "\\\\").Replace("'", "\\'");
            if (FindStartCall(script) is Match call)
            {
                Group name = call.Groups["name"];
                return name.Value == area ? script : script[..name.Index] + quoted + script[(name.Index + name.Length)..];
            }
            foreach ((string function, int start, int end) in Functions(script))
            {
                if (function != "OnPopulate")
                {
                    continue;
                }
                Match initialize = InitializeArmies().Match(script, start, end - start);
                if (initialize.Success)
                {
                    string newline = script.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                    string indent = initialize.Groups["indent"].Value;
                    int at = initialize.Index + initialize.Length;
                    return script[..at] + newline + indent + $"ScenarioFramework.SetPlayableArea('{quoted}', false)" + script[at..];
                }
            }
            return null;
        }

        /// <summary>
        /// The first call that sets the playable area by a plain name, outside comments, inside
        /// <c>OnPopulate</c> or <c>OnStart</c>.
        /// </summary>
        private static Match? FindStartCall(string script)
        {
            foreach ((string function, int start, int end) in Functions(script))
            {
                if (function is not ("OnPopulate" or "OnStart"))
                {
                    continue;
                }
                for (Match call = SetPlayableArea().Match(script, start, end - start); call.Success; call = call.NextMatch())
                {
                    if (!IsCommentedOut(script, call.Index))
                    {
                        return call;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// The functions of the script by name, each from its header to the next function's header
        /// (a function's end is not found without parsing the Lua; the next header is near enough).
        /// </summary>
        private static IEnumerable<(string Name, int Start, int End)> Functions(string script)
        {
            Match[] headers = [.. FunctionHeader().Matches(script).Where(header => !IsCommentedOut(script, header.Index))];
            for (int i = 0; i < headers.Length; i++)
            {
                yield return (headers[i].Groups["name"].Value, headers[i].Index, i + 1 < headers.Length ? headers[i + 1].Index : script.Length);
            }
        }

        /// <summary>
        /// Whether a <c>--</c> comes before the place on its line, or a block comment is open there.
        /// </summary>
        private static bool IsCommentedOut(string script, int index)
        {
            int lineStart = script.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            if (script.AsSpan(lineStart, index - lineStart).IndexOf("--", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            int open = script.LastIndexOf("--[[", index, StringComparison.Ordinal);
            return open >= 0 && script.IndexOf("]]", open, StringComparison.Ordinal) is int close && (close < 0 || close > index);
        }

        [GeneratedRegex(@"ScenarioFramework\.SetPlayableArea\(\s*(?<quote>['""])(?<name>(?:(?!\k<quote>)[^\\\r\n]|\\.)*)\k<quote>", RegexOptions.CultureInvariant)]
        private static partial Regex SetPlayableArea();

        [GeneratedRegex(@"^[ \t]*(?:local[ \t]+)?function[ \t]+(?<name>[\w.:]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
        private static partial Regex FunctionHeader();

        [GeneratedRegex(@"^(?<indent>[ \t]*)ScenarioUtils\.InitializeArmies\(\s*\)[ \t]*;?", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
        private static partial Regex InitializeArmies();
    }
}
