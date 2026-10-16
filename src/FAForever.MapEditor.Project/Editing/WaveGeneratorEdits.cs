using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Placing, aiming and moving the water's wave generators (user story W-1). A generator's
    /// direction is the way its waves roll, on the map (x east, z south); the game keeps it twice:
    /// as the velocity's x and z, and as the rotation, which in the vault's maps is
    /// π minus the angle of that direction from +z (abhor.v0004's 211 generators).
    /// </summary>
    public static class WaveGeneratorEdits
    {
        /// <summary>
        /// How fast the waves of a new generator roll, in ogrids a tick: about what the vault's maps use (0.01 to 0.045).
        /// </summary>
        public const float DefaultSpeed = 0.02f;

        /// <summary>
        /// The rotation the game gives a generator whose waves roll in <paramref name="direction"/>.
        /// </summary>
        public static float RotationOf(Vector2 direction) => MathF.PI - MathF.Atan2(direction.X, direction.Y);

        /// <summary>
        /// The way a generator's waves roll, of length 1: its velocity's x and z, or without a
        /// velocity the way its rotation says.
        /// </summary>
        public static Vector2 DirectionOf(WaveGenerator generator)
        {
            Vector2 velocity = new Vector2(generator.Velocity.X, generator.Velocity.Z);
            if (velocity.LengthSquared() > 1e-12f)
            {
                return Vector2.Normalize(velocity);
            }
            float heading = MathF.PI - generator.Rotation;
            return new Vector2(MathF.Sin(heading), MathF.Cos(heading));
        }

        /// <summary>
        /// The direction as a compass heading in degrees: 0 north (towards -z), 90 east.
        /// </summary>
        public static double HeadingOf(Vector2 direction)
        {
            double heading = Math.Atan2(direction.X, -direction.Y) * 180 / Math.PI;
            return heading < 0 ? heading + 360 : heading;
        }

        /// <summary>
        /// The direction of a compass heading in degrees (<see cref="HeadingOf"/>).
        /// </summary>
        public static Vector2 DirectionOfHeading(double degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new Vector2((float)Math.Sin(radians), (float)-Math.Cos(radians));
        }

        /// <summary>
        /// The generator with its waves rolling in <paramref name="direction"/> at <paramref name="speed"/>:
        /// its rotation and velocity both. A direction of no length keeps it as it is.
        /// </summary>
        public static WaveGenerator Aimed(WaveGenerator generator, Vector2 direction, float speed)
        {
            if (direction.LengthSquared() < 1e-12f)
            {
                return generator;
            }
            Vector2 unit = Vector2.Normalize(direction);
            return generator with
            {
                Rotation = RotationOf(unit),
                Velocity = new Vector3(unit.X * speed, generator.Velocity.Y, unit.Y * speed),
            };
        }

        /// <summary>
        /// Adds <paramref name="generator"/> and its images in the other parts of a symmetric map:
        /// one per transform (the identity first), each at its place and with its direction mirrored
        /// or turned as the transform says.
        /// </summary>
        /// <param name="added">The indices of the added generators, the one given first.</param>
        public static MapDocument AddWaveGenerators(this MapDocument document, WaveGenerator generator, IReadOnlyList<MapSymmetryTransform> transforms, out int[] added)
        {
            IReadOnlyList<MapSymmetryTransform> parts = transforms.Count == 0 ? [MapSymmetryTransform.Identity] : transforms;
            Vector2 place = new Vector2(generator.Position.X, generator.Position.Z);
            Vector2 direction = DirectionOf(generator);
            float speed = new Vector2(generator.Velocity.X, generator.Velocity.Z).Length();
            var generators = document.Water.WaveGenerators.ToBuilder();
            added = new int[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                Vector2 at = parts[i].Apply(place);
                WaveGenerator image = generator with { Position = new Vector3(at.X, generator.Position.Y, at.Y) };
                if (i > 0)
                {
                    Vector2 turned = parts[i].ApplyDirection(direction);
                    image = speed > 0
                        ? Aimed(image, turned, speed)
                        : image with { Rotation = RotationOf(turned) };
                }
                added[i] = generators.Count;
                generators.Add(image);
            }
            return document with { Water = document.Water with { WaveGenerators = generators.ToImmutable() } };
        }

        /// <summary>
        /// Moves a generator to a place in ogrids; its height stays.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">There is no generator <paramref name="index"/>.</exception>
        public static MapDocument MoveWaveGenerator(this MapDocument document, int index, float x, float z)
        {
            WaveGenerator generator = document.Water.WaveGenerators[index];
            return document with
            {
                Water = document.Water with
                {
                    WaveGenerators = document.Water.WaveGenerators.SetItem(index, generator with { Position = new Vector3(x, generator.Position.Y, z) }),
                },
            };
        }

        /// <summary>
        /// The generator nearest a place within <paramref name="reach"/> ogrids, or null.
        /// </summary>
        public static int? WaveGeneratorNear(this MapDocument document, float x, float z, float reach)
        {
            int? nearest = null;
            float best = reach * reach;
            for (int i = 0; i < document.Water.WaveGenerators.Length; i++)
            {
                Vector3 at = document.Water.WaveGenerators[i].Position;
                float distance = (at.X - x) * (at.X - x) + (at.Z - z) * (at.Z - z);
                if (distance <= best)
                {
                    best = distance;
                    nearest = i;
                }
            }
            return nearest;
        }
    }
}
