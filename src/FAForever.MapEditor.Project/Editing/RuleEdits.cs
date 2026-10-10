using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The edits of the rules of masks (user story K-2) and of props (P-4): adding, changing,
    /// moving and removing rules, the props the rules placed, and what follows when a mask is
    /// renamed or removed. Rules are named by their mask and place in its list, prop rules by name.
    /// </summary>
    public static class RuleEdits
    {
        #region Masks

        /// <summary>
        /// The rules of a mask, empty when it has none.
        /// </summary>
        public static ImmutableArray<MaskRule> MaskRulesOf(this MapDocument document, string mask) =>
            document.MaskRules.TryGetValue(mask, out MaskRuleSet? rules) ? rules.Rules : [];

        /// <summary>
        /// The document with a mask's rules; none leaves it a mask painted by hand.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask.</exception>
        public static MapDocument SetMaskRules(this MapDocument document, string mask, ImmutableArray<MaskRule> rules)
        {
            RequireMask(document, mask);
            return document with
            {
                MaskRules = rules.IsDefaultOrEmpty
                    ? document.MaskRules.Remove(mask)
                    : document.MaskRules.SetItem(mask, new MaskRuleSet { Rules = rules }),
            };
        }

        /// <summary>
        /// The document with a rule added to a mask's, at the end or at <paramref name="index"/>.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask, or the place is outside its list.</exception>
        public static MapDocument AddMaskRule(this MapDocument document, string mask, MaskRule rule, int? index = null)
        {
            ImmutableArray<MaskRule> rules = MaskRulesOf(document, mask);
            int at = index ?? rules.Length;
            if (at < 0 || at > rules.Length)
            {
                throw new ArgumentException($"The mask '{mask}' has {rules.Length} rules: a rule goes at 0 to {rules.Length}", nameof(index));
            }
            return SetMaskRules(document, mask, rules.Insert(at, rule));
        }

        /// <summary>
        /// The document with a rule of a mask changed.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask or rule.</exception>
        public static MapDocument UpdateMaskRule(this MapDocument document, string mask, int index, Func<MaskRule, MaskRule> change)
        {
            ImmutableArray<MaskRule> rules = MaskRulesOf(document, mask);
            RequireRule(mask, rules, index);
            MaskRule changed = change(rules[index]);
            return changed == rules[index] ? document : SetMaskRules(document, mask, rules.SetItem(index, changed));
        }

        /// <summary>
        /// The document without a rule of a mask; without its last rule the mask stays as it is, painted by hand.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask or rule.</exception>
        public static MapDocument RemoveMaskRule(this MapDocument document, string mask, int index)
        {
            ImmutableArray<MaskRule> rules = MaskRulesOf(document, mask);
            RequireRule(mask, rules, index);
            return SetMaskRules(document, mask, rules.RemoveAt(index));
        }

        /// <summary>
        /// The document with a rule of a mask moved to another place in its list.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask or rule, or the place is outside the list.</exception>
        public static MapDocument MoveMaskRule(this MapDocument document, string mask, int index, int to)
        {
            ImmutableArray<MaskRule> rules = MaskRulesOf(document, mask);
            RequireRule(mask, rules, index);
            RequireRule(mask, rules, to);
            if (index == to)
            {
                return document;
            }
            MaskRule rule = rules[index];
            return SetMaskRules(document, mask, rules.RemoveAt(index).Insert(to, rule));
        }

        /// <summary>
        /// The document after a mask was renamed: its rules under the new name, and every rule that
        /// read it reading the new name.
        /// </summary>
        public static MapDocument MaskRenamed(MapDocument document, string name, string newName)
        {
            ImmutableSortedDictionary<string, MaskRuleSet> maskRules = document.MaskRules;
            if (maskRules.TryGetValue(name, out MaskRuleSet? own))
            {
                maskRules = maskRules.Remove(name).Add(newName, own);
            }
            maskRules = maskRules.ToImmutableSortedDictionary(entry => entry.Key, entry =>
                entry.Value.Rules.Any(rule => rule.Kind == MaskRuleKind.Layer && rule.Source == name)
                    ? entry.Value with { Rules = [.. entry.Value.Rules.Select(rule => rule.Kind == MaskRuleKind.Layer && rule.Source == name ? rule with { Source = newName } : rule)] }
                    : entry.Value, StringComparer.Ordinal);
            ImmutableArray<PropRule> propRules = [.. document.PropRules.Select(rule => rule.Conditions.Any(condition => condition.Layer == name)
                ? rule with { Conditions = [.. rule.Conditions.Select(condition => condition.Layer == name ? condition with { Layer = newName } : condition)] }
                : rule)];
            return document with { MaskRules = maskRules, PropRules = propRules };
        }

        /// <summary>
        /// The document after a mask was removed: without its rules. Rules that read it stay, and say
        /// so when generated.
        /// </summary>
        public static MapDocument MaskRemoved(MapDocument document, string name) =>
            document.MaskRules.ContainsKey(name) ? document with { MaskRules = document.MaskRules.Remove(name) } : document;

        private static void RequireMask(MapDocument document, string mask)
        {
            if (!document.Masks.ContainsKey(mask))
            {
                throw new ArgumentException($"The map has no mask '{mask}'", nameof(mask));
            }
        }

        private static void RequireRule(string mask, ImmutableArray<MaskRule> rules, int index)
        {
            if (index < 0 || index >= rules.Length)
            {
                throw new ArgumentException(rules.IsEmpty
                    ? $"The mask '{mask}' has no rules"
                    : $"The mask '{mask}' has rules 0 to {rules.Length - 1}, not {index}", nameof(index));
            }
        }

        #endregion

        #region Props

        /// <summary>
        /// The longest name a prop rule may have.
        /// </summary>
        public const int MaxRuleNameLength = 64;

        /// <summary>
        /// A prop rule by name (any case), or null.
        /// </summary>
        public static PropRule? FindPropRule(this MapDocument document, string name) =>
            document.PropRules.FirstOrDefault(rule => rule.Name == name)
            ?? document.PropRules.FirstOrDefault(rule => rule.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Why a name cannot be a new prop rule's, or null when it can.
        /// </summary>
        /// <param name="except">A rule the name may equal (one being renamed).</param>
        public static string? PropRuleNameProblem(MapDocument document, string name, string? except = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A rule needs a name";
            }
            if (name.Length > MaxRuleNameLength)
            {
                return $"A rule's name has at most {MaxRuleNameLength} characters";
            }
            if (name != name.Trim())
            {
                return "A rule's name does not start or end with a space";
            }
            if (document.PropRules.FirstOrDefault(rule => rule.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && rule.Name != except) is PropRule taken)
            {
                return $"The map has a rule '{taken.Name}' already";
            }
            return null;
        }

        /// <summary>
        /// A name no prop rule has: <paramref name="stem"/>, else with the first free number after it.
        /// </summary>
        public static string FreePropRuleName(MapDocument document, string stem = "Rule")
        {
            if (PropRuleNameProblem(document, stem) is null)
            {
                return stem;
            }
            for (int number = 2; ; number++)
            {
                string name = $"{stem} {number}";
                if (PropRuleNameProblem(document, name) is null)
                {
                    return name;
                }
            }
        }

        /// <summary>
        /// The document with a new prop rule after the others.
        /// </summary>
        /// <exception cref="ArgumentException">Its name cannot be a rule's (<see cref="PropRuleNameProblem"/>).</exception>
        public static MapDocument AddPropRule(this MapDocument document, PropRule rule)
        {
            if (PropRuleNameProblem(document, rule.Name) is string problem)
            {
                throw new ArgumentException(problem, nameof(rule));
            }
            return document with { PropRules = document.PropRules.Add(rule) };
        }

        /// <summary>
        /// The document with a prop rule changed; a new name goes to the props it placed too.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such rule, or the new name cannot be a rule's.</exception>
        public static MapDocument UpdatePropRule(this MapDocument document, string name, Func<PropRule, PropRule> change)
        {
            PropRule rule = RequirePropRule(document, name);
            PropRule changed = change(rule);
            if (changed == rule)
            {
                return document;
            }
            if (changed.Name != rule.Name && PropRuleNameProblem(document, changed.Name, except: rule.Name) is string problem)
            {
                throw new ArgumentException(problem, nameof(change));
            }
            document = document with { PropRules = document.PropRules.Replace(rule, changed) };
            if (changed.Name != rule.Name)
            {
                document = document with { Props = [.. document.Props.Select(prop => prop.Rule == rule.Name ? prop with { Rule = changed.Name } : prop)] };
            }
            return document;
        }

        /// <summary>
        /// The document without a prop rule and (unless <paramref name="keepProps"/>) the props it placed.
        /// </summary>
        /// <param name="keepProps">Keep its props as props placed by hand.</param>
        /// <exception cref="ArgumentException">There is no such rule.</exception>
        public static MapDocument RemovePropRule(this MapDocument document, string name, bool keepProps = false)
        {
            PropRule rule = RequirePropRule(document, name);
            ImmutableArray<Prop> props = keepProps
                ? [.. document.Props.Select(prop => prop.Rule == rule.Name ? prop with { Rule = null } : prop)]
                : [.. document.Props.Where(prop => prop.Rule != rule.Name)];
            return document with { PropRules = document.PropRules.Remove(rule), Props = props };
        }

        /// <summary>
        /// The document with a prop rule moved to another place in the order the rules place.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such rule, or the place is outside the list.</exception>
        public static MapDocument MovePropRule(this MapDocument document, string name, int to)
        {
            PropRule rule = RequirePropRule(document, name);
            if (to < 0 || to >= document.PropRules.Length)
            {
                throw new ArgumentException($"The map has rules 0 to {document.PropRules.Length - 1}, not {to}", nameof(to));
            }
            return document with { PropRules = document.PropRules.Remove(rule).Insert(to, rule) };
        }

        /// <summary>
        /// How many props a rule placed (any rule's when <paramref name="name"/> is null).
        /// </summary>
        public static int CountRuleProps(this MapDocument document, string? name = null) =>
            document.Props.Count(prop => name is null ? prop.Rule is not null : prop.Rule == name);

        /// <summary>
        /// The document with the props any rule placed replaced by <paramref name="props"/>, which get
        /// ids after the highest of the props that stay. Props placed by hand stay as they are.
        /// </summary>
        public static MapDocument ReplaceRuleProps(MapDocument document, ImmutableArray<Prop> props)
        {
            ImmutableArray<Prop> kept = [.. document.Props.Where(prop => prop.Rule is null)];
            int next = kept.IsEmpty ? 1 : kept.Max(prop => prop.Id) + 1;
            ImmutableArray<Prop>.Builder all = ImmutableArray.CreateBuilder<Prop>(kept.Length + props.Length);
            all.AddRange(kept);
            foreach (Prop prop in props)
            {
                all.Add(prop with { Id = next++ });
            }
            return document with { Props = all.MoveToImmutable() };
        }

        /// <summary>
        /// The document with props of the rules made props placed by hand: generating again leaves them.
        /// </summary>
        public static MapDocument KeepRuleProps(this MapDocument document, IReadOnlySet<int> ids) =>
            document with { Props = [.. document.Props.Select(prop => prop.Rule is not null && ids.Contains(prop.Id) ? prop with { Rule = null } : prop)] };

        private static PropRule RequirePropRule(MapDocument document, string name) =>
            FindPropRule(document, name) ?? throw new ArgumentException(document.PropRules.IsEmpty
                ? $"The map has no rule '{name}': it has no rules"
                : $"The map has no rule '{name}': {string.Join(", ", document.PropRules.Select(rule => rule.Name))}", nameof(name));

        #endregion
    }
}
