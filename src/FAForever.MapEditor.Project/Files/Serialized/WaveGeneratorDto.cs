namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A generator of the waves that roll onto a shore.
    /// </summary>
    public sealed record WaveGeneratorDto
    {
        public required string Texture { get; init; }

        public required string Ramp { get; init; }

        public required Vector3Dto Position { get; init; }

        public required float Rotation { get; init; }

        public required Vector3Dto Velocity { get; init; }

        public required float LifetimeFirst { get; init; }

        public required float LifetimeSecond { get; init; }

        public required float PeriodFirst { get; init; }

        public required float PeriodSecond { get; init; }

        public required float ScaleFirst { get; init; }

        public required float ScaleSecond { get; init; }

        public required float FrameCount { get; init; }

        public required float FrameRateFirst { get; init; }

        public required float FrameRateSecond { get; init; }

        public required float StripCount { get; init; }
    }
}
