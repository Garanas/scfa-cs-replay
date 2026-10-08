namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The resources an army starts with.
    /// </summary>
    public sealed record Economy
    {
        public double Mass { get; init; }

        public double Energy { get; init; }
    }
}
