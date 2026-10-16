namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The mass and energy in the map's props.
    /// </summary>
    public sealed record Reclaim
    {
        public double Mass { get; init; }

        public double Energy { get; init; }
    }
}
