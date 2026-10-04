namespace FAForever.Replay
{
    /// <summary>
    /// Reads blueprint files (<c>.bp</c>), mirroring <c>lua/system/Blueprints.lua</c> of the game:
    /// a file calls <c>UnitBlueprint { ... }</c>, <c>ProjectileBlueprint { ... }</c>, etc. one or
    /// more times. Pure managed code without file access, so it runs in WebAssembly; the caller
    /// supplies the file's text.
    /// </summary>
    public static class BlueprintParser
    {
        private delegate Blueprint Reader(BlueprintTableReader table, string blueprintId, string source);

        /// <summary>
        /// Parses the text of a blueprint file into <see cref="BlueprintUnit"/>,
        /// <see cref="BlueprintProjectile"/>, <see cref="BlueprintProp"/>, <see cref="BlueprintMesh"/>,
        /// <see cref="BlueprintEmitter"/>, <see cref="BlueprintTrailEmitter"/> and <see cref="BlueprintBeam"/>
        /// records, in file order.
        /// </summary>
        /// <param name="text">The content of the file.</param>
        /// <param name="source">The game path of the file (e.g. <c>/units/uel0101/uel0101_unit.bp</c>),
        /// which determines the blueprint ids just as in the game.</param>
        /// <exception cref="LuaSyntaxException">The file is not valid (blueprint) Lua.</exception>
        public static IReadOnlyList<Blueprint> Parse(string text, string source)
        {
            List<Blueprint> blueprints = new List<Blueprint>();

            LuaFunction Define(string function, Func<LuaData.Table, string, string> getId, Reader read) => arguments =>
            {
                if (arguments is not [LuaData.Table data])
                {
                    throw new FormatException($"{function} expects a single table");
                }
                string blueprintSource = data.TryGetStringValue("Source", out string? explicitSource) ? explicitSource! : source;
                blueprints.Add(read(new BlueprintTableReader(data), getId(data, blueprintSource), blueprintSource));
                return new LuaData.Nil();
            };

            Dictionary<string, LuaFunction> functions = new Dictionary<string, LuaFunction>
            {
                ["UnitBlueprint"] = Define("UnitBlueprint", GetShortId, BlueprintUnit.Read),
                ["ProjectileBlueprint"] = Define("ProjectileBlueprint", GetPathId, BlueprintProjectile.Read),
                ["PropBlueprint"] = Define("PropBlueprint", GetPathId, BlueprintProp.Read),
                ["MeshBlueprint"] = Define("MeshBlueprint", GetLongId, BlueprintMesh.Read),
                ["EmitterBlueprint"] = Define("EmitterBlueprint", GetPathId, BlueprintEmitter.Read),
                ["TrailEmitterBlueprint"] = Define("TrailEmitterBlueprint", GetPathId, BlueprintTrailEmitter.Read),
                ["BeamBlueprint"] = Define("BeamBlueprint", GetPathId, BlueprintBeam.Read),

                // In the game Sound() turns its table into a sound handle; we keep the table
                // ({ Bank = ..., Cue = ..., LodCutoff = ... }).
                ["Sound"] = arguments => arguments is [LuaData.Table sound] ? sound : new LuaData.Nil(),
            };

            LuaSourceParser.Execute(text, functions);
            return blueprints;
        }

        // The ids the game assigns: SetShortId, SetLongId and SetBackwardsCompatId in lua/system/Blueprints.lua.

        /// <summary>
        /// Units: the explicit <c>BlueprintId</c>, otherwise <c>gsub(lower(source), "^.*/([^/]+)_[a-z]+%.bp$", "%1")</c>:
        /// <c>/units/uel0101/uel0101_unit.bp</c> → <c>uel0101</c>, and the path unchanged when it does not match.
        /// </summary>
        private static string GetShortId(LuaData.Table data, string source)
        {
            if (data.TryGetStringValue("BlueprintId", out string? explicitId))
            {
                return explicitId!;
            }

            string path = source.ToLowerInvariant();
            int slash = path.LastIndexOf('/');
            if (slash < 0 || !path.EndsWith(".bp", StringComparison.Ordinal))
            {
                return path;
            }

            string name = path[(slash + 1)..^3];
            int underscore = name.LastIndexOf('_');
            ReadOnlySpan<char> suffix = underscore > 0 ? name.AsSpan(underscore + 1) : default;
            if (suffix.IsEmpty || suffix.ContainsAnyExceptInRange('a', 'z'))
            {
                return path;
            }
            return name[..underscore];
        }

        /// <summary>
        /// Meshes: the explicit <c>BlueprintId</c>, otherwise the lower case path without <c>.bp</c>.
        /// </summary>
        private static string GetLongId(LuaData.Table data, string source)
        {
            if (data.TryGetStringValue("BlueprintId", out string? explicitId))
            {
                return explicitId!;
            }

            string path = source.ToLowerInvariant();
            return path.EndsWith(".bp", StringComparison.Ordinal) ? path[..^3] : path;
        }

        /// <summary>
        /// Everything else: always the lower case path, even when the file sets a <c>BlueprintId</c>.
        /// </summary>
        private static string GetPathId(LuaData.Table data, string source) => source.ToLowerInvariant();
    }
}
