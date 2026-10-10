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
        /// The rules of a mask (of its lowest rules layer, <see cref="RulesSource"/>), empty when it has none.
        /// </summary>
        public static ImmutableArray<MaskRule> MaskRulesOf(this MapDocument document, string mask) =>
            document.CoverageStacks.TryGetValue(LayerTarget.Mask(mask).Key, out LayerStack? stack) && RulesLayerOf(stack) is { Source: RulesSource rules }
                ? rules.Rules.Rules
                : [];

        /// <summary>
        /// The rules of a rules layer of any stack of coverage; empty for a layer that is no rules layer.
        /// </summary>
        public static ImmutableArray<MaskRule> LayerRulesOf(this MapDocument document, LayerTarget target, string layer) =>
            CoverageLayers.Exists(document, target) && CoverageLayers.StackOf(document, target).Find(layer) is { Source: RulesSource rules } ? rules.Rules.Rules : [];

        /// <summary>
        /// The lowest rules layer of a stack, or null.
        /// </summary>
        public static Layer? RulesLayerOf(LayerStack stack) => stack.Layers.FirstOrDefault(layer => layer.Source is RulesSource);

        /// <summary>
        /// The document with a mask's rules: the rules of its lowest rules layer, or of a new one
        /// (<c>Rules</c>, replacing what is below, as a mask made from rules is); none turns that layer
        /// into a painted one holding what the rules made last, so the mask is painted by hand again.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask.</exception>
        public static MapDocument SetMaskRules(this MapDocument document, string mask, ImmutableArray<MaskRule> rules)
        {
            RequireMask(document, mask);
            LayerTarget target = LayerTarget.Mask(mask);
            LayerStack stack = CoverageLayers.StackOf(document, target);
            if (RulesLayerOf(stack) is Layer layer)
            {
                return SetLayerRules(document, target, layer.Name, rules);
            }
            if (rules.IsDefaultOrEmpty)
            {
                return document;
            }
            Layer added = CoverageLayerEdits.NewRules(document, target, LayerStackEdits.FreeName(stack, "Rules"), new MaskRuleSet { Rules = rules }) with
            {
                Combine = LayerCombine.Replace,
                // a mask made from rules shows what it held until they are generated
                Coverage = target.Grid!.Read(document),
            };
            return CoverageLayerEdits.Add(document, target, added);
        }

        /// <summary>
        /// The document with a rules layer's rules; none turns it into a painted layer holding what
        /// the rules made last.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such grid or layer, or it is no rules layer.</exception>
        public static MapDocument SetLayerRules(this MapDocument document, LayerTarget target, string layer, ImmutableArray<MaskRule> rules)
        {
            if (!rules.IsDefaultOrEmpty)
            {
                return CoverageLayerEdits.SetRules(document, target, layer, new MaskRuleSet { Rules = rules });
            }
            LayerStack stack = CoverageLayers.StackOf(document, target);
            int index = LayerStackEdits.IndexOf(stack, layer, CoverageLayers.What(target));
            Layer held = stack.Layers[index];
            if (held.Source is not RulesSource)
            {
                throw new ArgumentException($"The {CoverageLayers.What(target)} {held.Name} is not made by rules", nameof(layer));
            }
            return CoverageLayers.WithStack(document, stack.With(index, held with { Source = LayerSource.Painted }));
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
        /// The document with a rule changed.
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
        /// The document after a mask was renamed: its stack under the new name, and every rule, clip,
        /// mask reference and prop rule that read it reading the new name.
        /// </summary>
        public static MapDocument MaskRenamed(MapDocument document, string name, string newName)
        {
            string oldKey = LayerTarget.Mask(name).Key;
            ImmutableSortedDictionary<string, LayerStack> stacks = document.CoverageStacks;
            if (stacks.TryGetValue(oldKey, out LayerStack? own))
            {
                stacks = stacks.Remove(oldKey).SetItem(LayerTarget.Mask(newName).Key, own with { Target = LayerTarget.Mask(newName) });
            }
            bool Reads(string source) => source == name || source == LayerStacks.MaskKey(name);
            string Renamed(string source) => source == name ? newName : LayerStacks.MaskKey(newName);
            stacks = stacks.ToImmutableSortedDictionary(entry => entry.Key, entry =>
            {
                LayerStack stack = entry.Value;
                ImmutableArray<Layer> layers = [.. stack.Layers.Select(layer =>
                {
                    Layer changed = layer;
                    if (changed.Clip is string clip && Reads(clip))
                    {
                        changed = changed with { Clip = Renamed(clip) };
                    }
                    if (changed.Source is MaskRefSource reference && Reads(reference.Key))
                    {
                        changed = changed with { Source = reference with { Key = Renamed(reference.Key) } };
                    }
                    if (changed.Source is RulesSource rules && rules.Rules.Rules.Any(rule => rule.Kind == MaskRuleKind.Layer && Reads(rule.Source)))
                    {
                        changed = changed with
                        {
                            Source = new RulesSource(rules.Rules with { Rules = [.. rules.Rules.Rules.Select(rule => rule.Kind == MaskRuleKind.Layer && Reads(rule.Source) ? rule with { Source = Renamed(rule.Source) } : rule)] }),
                        };
                    }
                    return changed;
                })];
                return layers.AsSpan().SequenceEqual(stack.Layers.AsSpan()) ? stack : stack with { Layers = layers };
            }, StringComparer.Ordinal);
            ImmutableArray<PropRule> propRules = [.. document.PropRules.Select(rule => rule.Conditions.Any(condition => Reads(condition.Layer))
                ? rule with { Conditions = [.. rule.Conditions.Select(condition => Reads(condition.Layer) ? condition with { Layer = Renamed(condition.Layer) } : condition)] }
                : rule)];
            return document with { CoverageStacks = stacks, PropRules = propRules };
        }

        /// <summary>
        /// The document after a mask was removed: without its stack. Rules and layers that read it
        /// stay, and say so when generated (a clip or mask reference of a missing mask counts as none).
        /// </summary>
        public static MapDocument MaskRemoved(MapDocument document, string name) =>
            document.CoverageStacks.ContainsKey(LayerTarget.Mask(name).Key)
                ? document with { CoverageStacks = document.CoverageStacks.Remove(LayerTarget.Mask(name).Key) }
                : document;

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
