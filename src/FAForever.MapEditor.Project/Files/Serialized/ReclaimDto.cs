namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The mass and energy in the map's props.
    /// </summary>
    public sealed record ReclaimDto
    {
        public required double Mass { get; init; }

        public required double Energy { get; init; }
    }
}
