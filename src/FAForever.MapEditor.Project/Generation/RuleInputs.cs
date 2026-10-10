using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The inputs of the generators of rules (user stories K-2, P-4): the rules themselves, and the
    /// layers they read. A list of layers is the same in two versions when each layer is the same
    /// object, so an input compares in a few reference comparisons.
    /// </summary>
    public static class RuleInputs
    {
        /// <summary>
        /// The rules of the masks.
        /// </summary>
        public static GeneratedInput MaskRules { get; } = new GeneratedInput("mask rules", document => document.MaskRules);

        /// <summary>
        /// The masks for tools the rules of masks read that are not made from rules themselves (those
        /// follow from the other inputs).
        /// </summary>
        public static GeneratedInput MaskRuleSources { get; } = new GeneratedInput("masks the rules read", document =>
            new References([.. document.MaskRules.Values
                .SelectMany(rules => rules.Sources)
                .Where(source => !document.MaskRules.ContainsKey(source))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(source => (object?)document.Masks.GetValueOrDefault(source))]));

        /// <summary>
        /// The rules of props.
        /// </summary>
        public static GeneratedInput PropRules { get; } = new GeneratedInput("prop rules", document => document.PropRules);

        /// <summary>
        /// The masks for tools the rules of props read (strata are in <see cref="GeneratedInput.StratumMasks"/>).
        /// </summary>
        public static GeneratedInput PropRuleMasks { get; } = new GeneratedInput("masks the prop rules read", document =>
            new References([.. document.PropRules
                .SelectMany(rule => rule.Layers)
                .Where(layer => !Files.InMemory.MaskRuleSources.IsStratum(layer, out _))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(layer => (object?)document.Masks.GetValueOrDefault(layer))]));

        /// <summary>
        /// Parts of a version of the document that are the same as another's when each is the same object.
        /// </summary>
        private sealed class References(ImmutableArray<object?> parts) : IEquatable<References>
        {
            public bool Equals(References? other) =>
                other is not null && other.Count == parts.Length && parts.Select((part, index) => ReferenceEquals(part, other.Part(index))).All(same => same);

            public override bool Equals(object? obj) => Equals(obj as References);

            public override int GetHashCode() => parts.Length;

            private int Count => parts.Length;

            private object? Part(int index) => parts[index];
        }
    }
}
