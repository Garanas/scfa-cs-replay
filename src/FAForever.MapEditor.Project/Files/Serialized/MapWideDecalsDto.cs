namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// One slot of the map-wide decals: <c>None</c>, <c>Generated</c> or <c>File</c>, with its file.
    /// </summary>
    public sealed record MapWideSlotDto
    {
        public required string Source { get; init; }

        /// <summary>
        /// The file (relative to the project when inside it); absent unless the source is a file.
        /// </summary>
        public string? File { get; init; }
    }

    /// <summary>
    /// The decals over the whole map (<c>decals.json</c>'s <c>mapWide</c>); absent for none.
    /// </summary>
    public sealed record MapWideDecalsDto
    {
        public MapWideSlotDto? Albedo { get; init; }

        /// <summary>
        /// The albedo decal's specular file; absent for none.
        /// </summary>
        public string? Specular { get; init; }

        public MapWideSlotDto? Normal { get; init; }

        public MapWideSlotDto? Shadow { get; init; }

        /// <summary>
        /// How the shadow decal is generated; absent for the defaults.
        /// </summary>
        public ShadowDecalSettingsDto? ShadowSettings { get; init; }
    }

    /// <summary>
    /// How the shadow decal is generated: radius in ogrids, strength, the darkest it gets, whether
    /// the sun's shadows darken too.
    /// </summary>
    public sealed record ShadowDecalSettingsDto
    {
        public float? Radius { get; init; }

        public float? Strength { get; init; }

        public float? Darkest { get; init; }

        public bool? SunShadows { get; init; }
    }
}
