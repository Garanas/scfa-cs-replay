namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// Where a map-wide decal's texture comes from.
    /// </summary>
    /// <remarks>
    /// The names stay as they are: they are written in the project's JSON.
    /// </remarks>
    public enum MapWideSource
    {
        /// <summary>No such decal.</summary>
        None,

        /// <summary>Made from the final heightmap at export (the normal and shadow decals).</summary>
        Generated,

        /// <summary>A file of the map maker's own.</summary>
        File,
    }

    /// <summary>
    /// One slot of the map-wide decals: where its texture comes from, and the file when it is one.
    /// </summary>
    /// <param name="File">The file: relative to the project folder when it lies in it
    /// (<c>procedural/temp/albedo.png</c>), else absolute, or a path into the map's own folder
    /// (<c>/maps/kaali.v0004/env/decals/map_albedo.dds</c>, one of <see cref="MapDocument.Files"/>);
    /// null unless <paramref name="Source"/> is <see cref="MapWideSource.File"/>.</param>
    public sealed record MapWideSlot(MapWideSource Source, string? File = null)
    {
        public static MapWideSlot None { get; } = new MapWideSlot(MapWideSource.None);

        public static MapWideSlot Generated { get; } = new MapWideSlot(MapWideSource.Generated);

        public static MapWideSlot Of(string file) => new MapWideSlot(MapWideSource.File, file);
    }

    /// <summary>
    /// How the shadow decal is generated: ambient occlusion from the final heightmap (how much of
    /// the sky each place sees), and the sun's shadows as an option.
    /// </summary>
    public sealed record ShadowDecalSettings
    {
        public static ShadowDecalSettings Default { get; } = new ShadowDecalSettings();

        /// <summary>
        /// How far around each place the terrain is looked at, in ogrids.
        /// </summary>
        public float Radius { get; init; } = 16;

        /// <summary>
        /// How dark the occlusion makes a place: 1 makes the darkness the mean of the horizon's
        /// sines in eight directions.
        /// </summary>
        public float Strength { get; init; } = 1;

        /// <summary>
        /// The darkest the decal gets, 0 to 1 (Kaali's: 0.45).
        /// </summary>
        public float Darkest { get; init; } = 0.45f;

        /// <summary>
        /// Whether the places the terrain hides from the sun are darkened too (as dark as
        /// <see cref="Darkest"/>).
        /// </summary>
        public bool SunShadows { get; init; }
    }

    /// <summary>
    /// The decals over the whole map (the Decals mode's three slots): settings of the map, held apart
    /// from its list of decals, which the export turns into decals stretched over the whole map
    /// (<see cref="MapWideDecalsExport"/>), as Kaali's are: an albedo decal with its specular, a
    /// shadow decal (an albedo decal, black with the darkness in its alpha) and a normals decal.
    /// </summary>
    public sealed record MapWideDecals
    {
        /// <summary>
        /// No map-wide decals: what an imported map and an older project have.
        /// </summary>
        public static MapWideDecals None { get; } = new MapWideDecals();

        /// <summary>
        /// What a new map starts with: the normal and shadow decals generated, no albedo.
        /// </summary>
        public static MapWideDecals NewMap { get; } = new MapWideDecals { Normal = MapWideSlot.Generated, Shadow = MapWideSlot.Generated };

        /// <summary>
        /// The albedo decal: a file, or none (it cannot be generated).
        /// </summary>
        public MapWideSlot Albedo { get; init; } = MapWideSlot.None;

        /// <summary>
        /// The albedo decal's specular, its second texture; null for none.
        /// </summary>
        public string? Specular { get; init; }

        /// <summary>
        /// The normals decal: generated, a file, or none.
        /// </summary>
        public MapWideSlot Normal { get; init; } = MapWideSlot.None;

        /// <summary>
        /// The shadow decal: generated, a file, or none.
        /// </summary>
        public MapWideSlot Shadow { get; init; } = MapWideSlot.None;

        public ShadowDecalSettings ShadowSettings { get; init; } = ShadowDecalSettings.Default;

        /// <summary>
        /// Whether the map has none of them and the shadow's settings are the defaults (nothing to keep).
        /// </summary>
        public bool IsNone => this == None;

        /// <summary>
        /// The slot of a kind.
        /// </summary>
        public MapWideSlot SlotOf(MapWideKind kind) => kind switch
        {
            MapWideKind.Albedo => Albedo,
            MapWideKind.Normal => Normal,
            _ => Shadow,
        };

        /// <summary>
        /// These decals with the slot of a kind replaced.
        /// </summary>
        /// <exception cref="ArgumentException">The albedo cannot be generated.</exception>
        public MapWideDecals With(MapWideKind kind, MapWideSlot slot)
        {
            if (kind == MapWideKind.Albedo && slot.Source == MapWideSource.Generated)
            {
                throw new ArgumentException("The map-wide albedo cannot be generated: choose a file, or none");
            }
            if (slot.Source == MapWideSource.File && string.IsNullOrWhiteSpace(slot.File))
            {
                throw new ArgumentException($"The map-wide {kind.ToString().ToLowerInvariant()} decal from a file needs the file");
            }
            return kind switch
            {
                MapWideKind.Albedo => this with { Albedo = slot },
                MapWideKind.Normal => this with { Normal = slot },
                _ => this with { Shadow = slot },
            };
        }
    }

    /// <summary>
    /// The three map-wide decals.
    /// </summary>
    public enum MapWideKind
    {
        Albedo,
        Normal,
        Shadow,
    }
}
