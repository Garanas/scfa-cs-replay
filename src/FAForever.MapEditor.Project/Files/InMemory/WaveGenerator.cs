using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A generator of the waves that roll onto a shore.
    /// </summary>
    public sealed record WaveGenerator
    {
        public required string Texture { get; init; }

        public required string Ramp { get; init; }

        public Vector3 Position { get; init; }

        public float Rotation { get; init; }

        public Vector3 Velocity { get; init; }

        public float LifetimeFirst { get; init; }

        public float LifetimeSecond { get; init; }

        public float PeriodFirst { get; init; }

        public float PeriodSecond { get; init; }

        public float ScaleFirst { get; init; }

        public float ScaleSecond { get; init; }

        public float FrameCount { get; init; }

        public float FrameRateFirst { get; init; }

        public float FrameRateSecond { get; init; }

        public float StripCount { get; init; }
    }
}
