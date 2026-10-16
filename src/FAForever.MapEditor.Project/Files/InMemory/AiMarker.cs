namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A marker the AI or a script reads, e.g. a rally point or an expansion area, or a marker of a
    /// type the editor does not know.
    /// </summary>
    public sealed record AiMarker : Marker
    {
        /// <summary>
        /// The colours of the types the map editors know, as most maps of the vault write them.
        /// </summary>
        private static readonly Dictionary<string, uint> Colors = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            [BlankType] = 0xff800080,
            ["Combat Zone"] = 0xff800000,
            ["Defensive Point"] = 0xff008000,
            ["Expansion Area"] = 0xff008080,
            ["Large Expansion Area"] = 0xff008080,
            ["Island"] = 0xff0000aa,
            ["Naval Area"] = 0xff0000ff,
            ["Naval Defensive Point"] = 0xff0080ff,
            ["Naval Link"] = 0xffff0000,
            ["Naval Rally Point"] = 0xff00ffff,
            ["Protected Experimental Construction"] = 0xff0000aa,
            ["Rally Point"] = 0xff808000,
            ["Transport Marker"] = 0xff80a088,
        };

        /// <summary>
        /// The type of a blank marker: a place in the world a script points at, or a point of a chain.
        /// The editor shows it as a kind of its own (<c>MarkerKinds.Blank</c>); a blank marker named
        /// like an army is a spawn (<c>MarkerTranslation</c>).
        /// </summary>
        public const string BlankType = "Blank Marker";

        /// <summary>
        /// The types of AI markers the map editors know, in alphabetical order: what a map maker
        /// chooses from. The blank marker is a kind of its own, not among them.
        /// </summary>
        public static IReadOnlyList<string> KnownTypes { get; } = [.. Colors.Keys.Where(type => type != BlankType).Order(StringComparer.Ordinal)];

        /// <summary>
        /// Whether it is a blank marker (<see cref="BlankType"/>).
        /// </summary>
        public bool IsBlank => Type == BlankType;

        /// <summary>
        /// The type as the game names it, e.g. <c>Rally Point</c> or <c>Expansion Area</c>.
        /// </summary>
        public required string Type { get; init; }

        public override Color32 Color => Color32.FromArgb(Colors.GetValueOrDefault(Type, 0xff800080));
    }
}
