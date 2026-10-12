namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A picture projected onto the map (user story N-2), as <c>sketch/images.json</c> lists it. The
    /// picture itself is <c>sketch/images/&lt;name&gt;.png</c>.
    /// </summary>
    public sealed record ProjectedImageDto
    {
        public required string Name { get; init; }

        public required double CentreX { get; init; }

        public required double CentreZ { get; init; }

        public required double Width { get; init; }

        public required double Height { get; init; }

        public double Turn { get; init; }

        public double Opacity { get; init; } = 1;

        public bool Greyscale { get; init; }

        public bool Locked { get; init; }

        public bool Shown { get; init; } = true;

        public bool AboveSketch { get; init; }

        public bool KeepProportions { get; init; } = true;
    }
}
