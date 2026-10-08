namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A mass or hydrocarbon deposit.
    /// </summary>
    public sealed record ResourceMarker : Marker
    {
        public ResourceKind Kind { get; init; }

        /// <summary>
        /// The army without which an adaptive map leaves the deposit out.
        /// </summary>
        public string? SpawnWithArmy { get; init; }

        public override Color32 Color => Color32.FromArgb(Kind == ResourceKind.Mass ? 0xff808080 : 0xff008000);
    }
}
