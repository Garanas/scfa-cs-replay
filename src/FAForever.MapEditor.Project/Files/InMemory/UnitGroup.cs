using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A named group of units and further groups.
    /// </summary>
    public sealed record UnitGroup
    {
        public required string Name { get; init; }

        public ImmutableArray<UnitGroup> Groups { get; init; } = [];

        public ImmutableArray<Unit> Units { get; init; } = [];

        /// <summary>
        /// The unit preset the group was placed from (user story U-9), or null for a group made
        /// otherwise. The link is the project's own: the export leaves it out, and the editor never
        /// changes the map from the preset.
        /// </summary>
        public PresetInstance? Instance { get; init; }
    }

    /// <summary>
    /// How a group came from a unit preset (user story U-9): the preset's name, a fingerprint of the
    /// preset's content when it was placed (<c>UnitPresets.Fingerprint</c>; compared with the file's
    /// to tell whether the preset changed since), where and how it was placed, and when.
    /// </summary>
    /// <param name="X">The place the preset's middle was put at, in ogrids.</param>
    /// <param name="Heading">The turn it was placed with, in radians, clockwise in the top view.</param>
    public sealed record PresetInstance(string Preset, string Fingerprint, float X, float Z, float Heading, DateTimeOffset PlacedAt);
}
