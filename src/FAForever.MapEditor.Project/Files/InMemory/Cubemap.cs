namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A cubemap that units reflect.
    /// </summary>
    public sealed record Cubemap
    {
        public required string Name { get; init; }

        public required string Texture { get; init; }
    }
}
