namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// Who builds what, as the game decides it. A builder (engineer, factory, commander, quantum
    /// gate, mobile factory, ...) can build every unit that matches one of the category expressions
    /// in its <c>Economy.BuildableCategory</c>, e.g. <c>"BUILTBYTIER2FACTORY AEON MOBILE AIR"</c>: a unit
    /// matches when it has all the categories of the expression. A unit's own lower case id counts as
    /// one of its categories (the game adds it when it loads the blueprint, <c>lua/system/Blueprints.lua</c>),
    /// so an expression may also name a unit, e.g. <c>"uab3101"</c>. Commanders and support commanders
    /// can build more after an enhancement (<c>BuildableCategoryAdds</c>, e.g. "Tech 2 Engineering Suite").
    /// </summary>
    /// <remarks>
    /// The expressions in the FA repository only use spaces (all of these categories); the game also
    /// knows <c>+</c>, <c>-</c> and parentheses, which no blueprint uses. Units the game creates while
    /// loading (support commander presets) are not in the blueprint files and so not in the tree.
    /// </remarks>
    public static class UnitBuildTree
    {
        /// <summary>
        /// What each unit can build, by blueprint id (lower case), sorted by id. Units that build
        /// nothing are left out.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Builds(IReadOnlyList<BlueprintUnit> units)
        {
            // every unit's categories plus its own id, for matching
            List<(string Id, HashSet<string> Categories)> candidates = units
                .Select(unit => (Id(unit), new HashSet<string>(unit.Categories.Append(Id(unit)), StringComparer.OrdinalIgnoreCase)))
                .ToList();

            Dictionary<string, IReadOnlyList<string>> builds = new Dictionary<string, IReadOnlyList<string>>();
            foreach (BlueprintUnit builder in units)
            {
                string[][] expressions = Expressions(builder).ToArray();
                if (expressions.Length == 0)
                {
                    continue;
                }

                List<string> buildable = candidates
                    .Where(candidate => expressions.Any(expression => expression.All(candidate.Categories.Contains)))
                    .Select(candidate => candidate.Id)
                    .Distinct()
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (buildable.Count > 0)
                {
                    builds[Id(builder)] = buildable;
                }
            }

            return builds;
        }

        /// <summary>
        /// The units a player can get in a game: the commanders (category <c>COMMAND</c>) and
        /// everything their builds and upgrades (<c>General.UpgradesTo</c>) lead to.
        /// </summary>
        public static IReadOnlySet<string> Buildable(IReadOnlyList<BlueprintUnit> units, IReadOnlyDictionary<string, IReadOnlyList<string>> builds)
        {
            Dictionary<string, BlueprintUnit> byId = units
                .GroupBy(Id)
                .ToDictionary(group => group.Key, group => group.First());

            HashSet<string> reached = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> queue = new Queue<string>();
            foreach (BlueprintUnit unit in units.Where(unit => unit.HasCategory("COMMAND")))
            {
                if (reached.Add(Id(unit)))
                {
                    queue.Enqueue(Id(unit));
                }
            }

            while (queue.TryDequeue(out string? id))
            {
                IEnumerable<string> next = builds.GetValueOrDefault(id) ?? [];
                if (byId.TryGetValue(id, out BlueprintUnit? unit) && unit.General.UpgradesTo is { Length: > 0 } upgrade)
                {
                    next = next.Append(upgrade.ToLowerInvariant());
                }

                foreach (string target in next)
                {
                    if (byId.ContainsKey(target) && reached.Add(target))
                    {
                        queue.Enqueue(target);
                    }
                }
            }

            return reached;
        }

        /// <summary>
        /// The category expressions of a builder, each split into its categories.
        /// </summary>
        private static IEnumerable<string[]> Expressions(BlueprintUnit builder) =>
            builder.Economy.BuildableCategory
                .Concat(builder.Enhancements.Values.Select(enhancement => enhancement.BuildableCategoryAdds).OfType<string>())
                .Select(expression => expression.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(categories => categories.Length > 0);

        private static string Id(BlueprintUnit unit) => unit.BlueprintId.ToLowerInvariant();
    }
}
