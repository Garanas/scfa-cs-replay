namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The resources an army starts with.
    /// </summary>
    public sealed record EconomyDto
    {
        public required double Mass { get; init; }

        public required double Energy { get; init; }
    }
}
