using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// How a prop stands: its heading, its up (the axis its model stands along) and its tilt, as
    /// parts of its rotation matrix. The rows of the matrix (<see cref="Prop.RotationX"/>,
    /// <see cref="Prop.RotationY"/>, <see cref="Prop.RotationZ"/>) are where the model's own axes
    /// point on the map, so <see cref="Prop.RotationY"/> is its up; a matrix of zeros counts as
    /// standing upright, facing +x.
    /// </summary>
    /// <remarks>
    /// The heading is <see cref="MapObjects.PropHeading"/>, which a turn about the prop's own up
    /// changes by exactly the angle turned (<see cref="ObjectEdits.RotateObjects"/>); everything
    /// here keeps it, so tilting a prop never turns it. The tilt is given as pitch and roll in the
    /// prop's own frame: its up, seen in the frame of the upright prop with the same heading, is
    /// <c>(-sin roll cos pitch, cos roll cos pitch, sin pitch)</c>. Pitch leans the prop towards its
    /// own +z, roll towards its own -x.
    /// </remarks>
    public static class PropOrientation
    {
        /// <summary>
        /// The axis the prop's model stands along, of length 1.
        /// </summary>
        public static Vector3 Up(Prop prop) => IsUnset(prop) ? Vector3.UnitY : Normalized(prop.RotationY, Vector3.UnitY);

        /// <summary>
        /// The prop's heading in radians (<see cref="MapObjects.PropHeading"/>).
        /// </summary>
        public static float Heading(Prop prop) => IsUnset(prop) ? 0 : MapObjects.PropHeading(prop);

        /// <summary>
        /// The prop standing straight up, with its heading.
        /// </summary>
        public static Prop Upright(Prop prop) => Oriented(prop, Heading(prop), Vector3.UnitY);

        /// <summary>
        /// The prop turned so that its up is <paramref name="up"/> (such as the terrain's normal
        /// under it), keeping its heading.
        /// </summary>
        public static Prop AlignedTo(Prop prop, Vector3 up) => Oriented(prop, Heading(prop), up);

        /// <summary>
        /// The prop with a heading and an up: the rows of its matrix made from them. An up that
        /// points down or along the ground is taken as up.
        /// </summary>
        public static Prop Oriented(Prop prop, float heading, Vector3 up)
        {
            (Vector3 x, Vector3 y, Vector3 z) = Frame(heading, up);
            return prop with { RotationX = x, RotationY = y, RotationZ = z };
        }

        /// <summary>
        /// The rows of a rotation whose up is <paramref name="up"/> and whose heading
        /// (<see cref="MapObjects.PropHeading"/>) is <paramref name="heading"/>.
        /// </summary>
        public static (Vector3 X, Vector3 Y, Vector3 Z) Frame(float heading, Vector3 up)
        {
            Vector3 y = Normalized(up, Vector3.UnitY);
            if (y.Y < 1e-3f)
            {
                // a prop does not stand on its side or upside down
                y = Vector3.UnitY;
            }
            // any frame with this up: the map's x laid onto the plane across the up
            Vector3 x = Vector3.Normalize(Vector3.UnitX - Vector3.Dot(Vector3.UnitX, y) * y);
            Vector3 z = Vector3.Cross(x, y);
            // then turned about the up until it has the heading; a turn about the up adds to the heading
            float turn = heading - MathF.Atan2(z.X, x.X);
            (float sin, float cos) = MathF.SinCos(turn);
            return (x * cos - z * sin, y, x * sin + z * cos);
        }

        /// <summary>
        /// How far the prop leans, as pitch and roll in radians (see the remarks of the class); both
        /// 0 for an upright prop.
        /// </summary>
        public static (float Pitch, float Roll) Tilt(Prop prop)
        {
            Vector3 local = LocalUp(Heading(prop), Up(prop));
            return (MathF.Asin(Math.Clamp(local.Z, -1, 1)), MathF.Atan2(-local.X, local.Y));
        }

        /// <summary>
        /// The prop leaning by a pitch and roll in radians (see the remarks of the class), keeping its heading.
        /// </summary>
        public static Prop WithTilt(Prop prop, float pitch, float roll)
        {
            float heading = Heading(prop);
            return Oriented(prop, heading, UpOf(heading, pitch, roll));
        }

        /// <summary>
        /// The up of a prop with a heading, pitch and roll, on the map.
        /// </summary>
        public static Vector3 UpOf(float heading, float pitch, float roll)
        {
            (Vector3 x, Vector3 y, Vector3 z) = Frame(heading, Vector3.UnitY);
            (float sinPitch, float cosPitch) = MathF.SinCos(pitch);
            (float sinRoll, float cosRoll) = MathF.SinCos(roll);
            return -sinRoll * cosPitch * x + cosRoll * cosPitch * y + sinPitch * z;
        }

        /// <summary>
        /// The angle between the prop's up and straight up, in radians.
        /// </summary>
        public static float Lean(Prop prop) => MathF.Acos(Math.Clamp(Up(prop).Y, -1, 1));

        /// <summary>
        /// The normal of the terrain at a place in ogrids: from the heights an ogrid to each side.
        /// </summary>
        public static Vector3 TerrainNormal(Heightmap heights, float x, float z)
        {
            float dx = (heights.HeightAt(x + 1, z) - heights.HeightAt(x - 1, z)) / 2;
            float dz = (heights.HeightAt(x, z + 1) - heights.HeightAt(x, z - 1)) / 2;
            return Vector3.Normalize(new Vector3(-dx, 1, -dz));
        }

        /// <summary>
        /// The size of a prop when it is scaled the same along every axis, which is how props are
        /// scaled: its x scale, a scale of 0 counting as 1.
        /// </summary>
        public static float UniformScale(Prop prop) => prop.Scale.X == 0 ? 1 : prop.Scale.X;

        /// <summary>
        /// The folder of a prop's blueprint, which says what it is: <c>/env/evergreen/props/rocks/rock01_prop.bp</c>
        /// is of <c>rocks</c>; one directly in <c>props</c> is of <c>other</c>. The asset catalogue's
        /// category of a prop is the same.
        /// </summary>
        public static string CategoryOf(string blueprint)
        {
            string path = blueprint.Replace('\\', '/');
            int slash = path.LastIndexOf('/');
            if (slash <= 0)
            {
                return "other";
            }
            string folder = path[(path.LastIndexOf('/', slash - 1) + 1)..slash].ToLowerInvariant();
            return folder == "props" ? "other" : folder;
        }

        /// <summary>
        /// Whether props of a category tilt with the ground they stand on: rocks, crystals, logs and
        /// clutter do; trees, bushes, buildings and the rest stand upright.
        /// </summary>
        public static bool TiltsWithTerrain(string category) =>
            category.StartsWith("rock", StringComparison.Ordinal)
            || category.StartsWith("crystal", StringComparison.Ordinal)
            || category.StartsWith("boulder", StringComparison.Ordinal)
            || category is "logs" or "clutter" or "debris" or "wreckage" or "stones";

        private static Vector3 LocalUp(float heading, Vector3 up)
        {
            (Vector3 x, Vector3 y, Vector3 z) = Frame(heading, Vector3.UnitY);
            return new Vector3(Vector3.Dot(up, x), Vector3.Dot(up, y), Vector3.Dot(up, z));
        }

        private static bool IsUnset(Prop prop) => prop.RotationX == Vector3.Zero && prop.RotationY == Vector3.Zero && prop.RotationZ == Vector3.Zero;

        private static Vector3 Normalized(Vector3 vector, Vector3 otherwise)
        {
            float length = vector.Length();
            return length < 1e-6f || !float.IsFinite(length) ? otherwise : vector / length;
        }
    }
}
