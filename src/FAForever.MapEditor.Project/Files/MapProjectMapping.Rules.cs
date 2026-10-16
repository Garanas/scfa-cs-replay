using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Files
{
    // The rules of masks (K-2) and props (P-4).
    internal static partial class MapProjectMapping
    {
        public static MaskRulesDto ToDto(MaskRuleSet source) => new MaskRulesDto
        {
            Rules = [.. source.Rules.Select(ToDto)],
        };

        public static MaskRuleSet ToModel(MaskRulesDto source) => new MaskRuleSet
        {
            Rules = [.. source.Rules.Select(ToModel)],
        };

        /// <summary>
        /// A rule with the parameters its kind reads.
        /// </summary>
        public static MaskRuleDto ToDto(MaskRule source)
        {
            bool range = source.Kind is MaskRuleKind.Slope or MaskRuleKind.Height or MaskRuleKind.WaterDistance;
            bool radius = source.Kind is MaskRuleKind.Blur or MaskRuleKind.Grow or MaskRuleKind.Shrink;
            return new MaskRuleDto
            {
                Kind = source.Kind,
                Combine = source.MakesValue ? source.Combine : null,
                Invert = source.MakesValue && source.Invert ? true : null,
                Min = range || source.Kind is MaskRuleKind.Layer or MaskRuleKind.Threshold ? source.Min : null,
                Max = range ? source.Max : null,
                Soft = range || source.Kind == MaskRuleKind.Threshold ? source.Soft : null,
                Source = source.Kind == MaskRuleKind.Layer ? source.Source : null,
                Scale = source.Kind == MaskRuleKind.Noise ? source.Scale : null,
                Seed = source.Kind == MaskRuleKind.Noise ? source.Seed : null,
                Radius = radius ? source.Radius : null,
            };
        }

        /// <summary>
        /// A rule; what the file leaves out has the value of <see cref="MaskRule"/>'s defaults.
        /// </summary>
        public static MaskRule ToModel(MaskRuleDto source)
        {
            MaskRule start = new MaskRule { Kind = source.Kind };
            return start with
            {
                Combine = source.Combine ?? start.Combine,
                Invert = source.Invert ?? start.Invert,
                Min = source.Min ?? start.Min,
                Max = source.Max ?? start.Max,
                Soft = source.Soft ?? start.Soft,
                Source = source.Source ?? start.Source,
                Scale = source.Scale ?? start.Scale,
                Seed = source.Seed ?? start.Seed,
                Radius = source.Radius ?? start.Radius,
            };
        }

        public static PropRuleDto ToDto(PropRule source) => new PropRuleDto
        {
            Name = source.Name,
            Palette = [.. source.Palette.Select(entry => new PaletteEntryDto { Blueprint = entry.Blueprint, Weight = entry.Weight })],
            Conditions = [.. source.Conditions.Select(condition => new PropRuleConditionDto { Layer = condition.Layer, Threshold = condition.Threshold, Forbid = condition.Forbid ? true : null })],
            Fade = source.Fade,
            Density = source.Density,
            Spacing = source.Spacing,
            Clustering = source.Clustering,
            ClusterSize = source.ClusterSize,
            ClusterCover = source.ClusterCover,
            MinSlope = source.MinSlope,
            MaxSlope = source.MaxSlope,
            AboveWater = source.AboveWater,
            Seed = source.Seed,
            Alignment = source.Placement.Alignment,
            RandomTurn = source.Placement.RandomTurn,
            RandomTilt = source.Placement.RandomTilt,
            ScaleMin = source.Placement.ScaleMin,
            ScaleMax = source.Placement.ScaleMax,
            Sink = source.Placement.Sink,
            Mirror = source.Mirror ? null : false,
        };

        public static PropRule ToModel(PropRuleDto source) => new PropRule
        {
            Name = source.Name,
            Palette = [.. source.Palette.Select(entry => new PaletteEntry(entry.Blueprint, entry.Weight))],
            Conditions = [.. source.Conditions.Select(condition => new PropRuleCondition(condition.Layer, condition.Threshold, condition.Forbid ?? false))],
            Fade = source.Fade,
            Density = source.Density,
            Spacing = source.Spacing,
            Clustering = source.Clustering,
            ClusterSize = source.ClusterSize,
            ClusterCover = source.ClusterCover,
            MinSlope = source.MinSlope,
            MaxSlope = source.MaxSlope,
            AboveWater = source.AboveWater,
            Seed = source.Seed,
            Placement = new PropPlacement
            {
                Alignment = source.Alignment,
                RandomTurn = source.RandomTurn,
                RandomTilt = source.RandomTilt,
                ScaleMin = source.ScaleMin,
                ScaleMax = source.ScaleMax,
                Sink = source.Sink,
            },
            Mirror = source.Mirror ?? true,
        };
    }
}
