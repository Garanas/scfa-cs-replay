using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// A box around a model, in its own space and in ogrids (the blueprint's scale applied).
/// </summary>
public readonly record struct ObjectBounds(Vector3 Min, Vector3 Max)
{
    /// <summary>
    /// What stands in for a model that is not there (or not read yet): a box of one ogrid on the
    /// ground, as the 3D view draws it.
    /// </summary>
    public static ObjectBounds Box { get; } = new ObjectBounds(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 1, 0.5f));
}

/// <summary>
/// Where the objects of a map stand and how they are turned, as the 3D view draws them and picking
/// finds them: one place for both, so that what one points at is what one sees.
/// </summary>
public static class ObjectPlacement
{
    /// <summary>
    /// From this distance of the camera on, a marker keeps its size on screen; closer by it keeps its
    /// size on the map.
    /// </summary>
    public const float MarkerNear = 80;

    /// <summary>
    /// A prop's model to the map: its three axes (a full rotation, so a prop may lean), scaled by
    /// its scale (none counts as 1), at its position.
    /// </summary>
    public static Matrix4x4 Prop(Prop prop)
    {
        Vector3 scale = new Vector3(
            prop.Scale.X == 0 ? 1 : prop.Scale.X,
            prop.Scale.Y == 0 ? 1 : prop.Scale.Y,
            prop.Scale.Z == 0 ? 1 : prop.Scale.Z);
        bool turned = prop.RotationX != Vector3.Zero || prop.RotationY != Vector3.Zero || prop.RotationZ != Vector3.Zero;
        Vector3 x = turned ? prop.RotationX : Vector3.UnitX;
        Vector3 y = turned ? prop.RotationY : Vector3.UnitY;
        Vector3 z = turned ? prop.RotationZ : Vector3.UnitZ;
        return new Matrix4x4(
            x.X * scale.X, x.Y * scale.X, x.Z * scale.X, 0,
            y.X * scale.Y, y.Y * scale.Y, y.Z * scale.Y, 0,
            z.X * scale.Z, z.Y * scale.Z, z.Z * scale.Z, 0,
            prop.Position.X, prop.Position.Y, prop.Position.Z, 1);
    }

    /// <summary>
    /// A unit's model to the map: turned by its orientation (radians about each axis), at its position.
    /// </summary>
    public static Matrix4x4 Unit(Unit unit) =>
        Matrix4x4.CreateFromYawPitchRoll(unit.Orientation.Y, unit.Orientation.X, unit.Orientation.Z)
        * Matrix4x4.CreateTranslation(unit.Position);

    /// <summary>
    /// Every unit of every army, with the army's index.
    /// </summary>
    public static IEnumerable<(Unit Unit, int Army)> Units(MapDocument document)
    {
        for (int army = 0; army < document.Armies.Length; army++)
        {
            foreach (Unit unit in UnitsOf(document.Armies[army].Units))
            {
                yield return (unit, army);
            }
        }
    }

    private static IEnumerable<Unit> UnitsOf(UnitGroup group) =>
        group.Units.Concat(group.Groups.SelectMany(UnitsOf));

    /// <summary>
    /// The markers the 3D view shows: the spawns, deposits, AI markers and weather when
    /// <paramref name="markers"/>, the path nodes when <paramref name="pathNodes"/>.
    /// Of those, only the kinds of <paramref name="kinds"/> (user story R-6).
    /// </summary>
    public static IEnumerable<Marker> Markers(MapDocument document, bool markers, bool pathNodes, MarkerKinds kinds = MarkerKinds.All) =>
        document.Markers.All.Where(marker => (marker is PathNode ? pathNodes : markers) && (kinds & MarkerEdits.KindOf(marker)) != 0);

    /// <summary>
    /// How high a marker's symbol stands on the map, in ogrids: spawns two and a half times as
    /// high as the others, path nodes smaller; from <see cref="MarkerNear"/> on it grows with the
    /// distance to the camera, so that it keeps its size on screen.
    /// </summary>
    public static float MarkerHeight(Marker marker, Vector3 place, Vector3 eye) =>
        MarkerBase(marker) * Math.Max(1, Vector3.Distance(place, eye) / MarkerNear);

    /// <summary>
    /// A marker's height close by: its kind's size.
    /// </summary>
    public static float MarkerBase(Marker marker) => marker switch
    {
        Spawn => 5,
        PathNode => 1.2f,
        _ => 2,
    };

    /// <summary>
    /// Where a marker's symbol stands: its place on the terrain.
    /// </summary>
    public static Vector3 MarkerPlace(Marker marker, Heightmap heights) =>
        new Vector3(marker.Position.X, GroundAt(heights, marker.Position.X, marker.Position.Z), marker.Position.Z);

    /// <summary>
    /// The height of the terrain at a place, between its samples; outside the map that of its edge.
    /// </summary>
    public static float GroundAt(Heightmap heights, float x, float z)
    {
        int last = heights.Samples.Width - 1;
        int lastRow = heights.Samples.Height - 1;
        float cx = Math.Clamp(x, 0, last);
        float cz = Math.Clamp(z, 0, lastRow);
        int x0 = Math.Min((int)cx, last);
        int z0 = Math.Min((int)cz, lastRow);
        int x1 = Math.Min(x0 + 1, last);
        int z1 = Math.Min(z0 + 1, lastRow);
        float fx = cx - x0;
        float fz = cz - z0;
        float top = heights.GetHeight(x0, z0) * (1 - fx) + heights.GetHeight(x1, z0) * fx;
        float bottom = heights.GetHeight(x0, z1) * (1 - fx) + heights.GetHeight(x1, z1) * fx;
        return top * (1 - fz) + bottom * fz;
    }
}

/// <summary>
/// What picking looks at: the kinds the 3D view shows, of the markers those of <see cref="MarkerKinds"/>.
/// </summary>
/// <param name="HiddenDecals">The ids of the decals not drawn (those of hidden groups), which are
/// not picked; null for none.</param>
/// <param name="Include">Whether a thing may be picked besides its kind (only the isolated
/// selection); null for every thing.</param>
public sealed record PickOptions(bool Props = true, bool Units = true, bool Markers = true, bool PathNodes = false, bool Decals = true, IReadOnlySet<int>? HiddenDecals = null, MarkerKinds MarkerKinds = MarkerKinds.All,
    Func<ObjectRef, bool>? Include = null);

/// <summary>
/// Finds the object under a point of the 3D view: the ray from the camera through it against each
/// object's bounds (a model's box, turned and placed as drawn; a marker's symbol as a sphere),
/// the nearest hit in front of the terrain. A decal is what lies under the point when no object
/// does: the last in the map's order, which lies on top.
/// </summary>
public static class ObjectPicking
{
    /// <param name="origin">Where the ray starts: the camera's eye.</param>
    /// <param name="direction">Its direction (any length).</param>
    /// <param name="boundsOf">The bounds of a blueprint's model (<see cref="ObjectBounds.Box"/> when
    /// it has none).</param>
    /// <param name="terrain">How far along the ray it meets the terrain, in units of
    /// <paramref name="direction"/>; null when it misses the map. What lies behind the terrain is
    /// hidden.</param>
    public static ObjectRef? Pick(MapDocument document, Vector3 origin, Vector3 direction, PickOptions shown, Func<string, ObjectBounds> boundsOf, float? terrain)
    {
        float limit = terrain is float t ? t + Math.Max(1, t * 0.01f) : float.PositiveInfinity;
        float nearest = limit;
        ObjectRef? found = null;

        void Consider(float? hit, ObjectRef what)
        {
            if (hit is float at && at >= 0 && at < nearest && shown.Include?.Invoke(what) != false)
            {
                nearest = at;
                found = what;
            }
        }

        if (shown.Props)
        {
            foreach (Prop prop in document.Props)
            {
                Consider(HitBox(ObjectPlacement.Prop(prop), boundsOf(prop.Blueprint), origin, direction), ObjectRef.Prop(prop.Id));
            }
        }
        if (shown.Units)
        {
            foreach ((Unit unit, _) in ObjectPlacement.Units(document))
            {
                Consider(HitBox(ObjectPlacement.Unit(unit), boundsOf(unit.Blueprint), origin, direction), ObjectRef.Unit(unit.Name));
            }
        }
        if (shown.Markers || shown.PathNodes)
        {
            Heightmap heights = document.Terrain.Heightmap;
            foreach (Marker marker in ObjectPlacement.Markers(document, shown.Markers, shown.PathNodes, shown.MarkerKinds))
            {
                Vector3 place = ObjectPlacement.MarkerPlace(marker, heights);
                float height = ObjectPlacement.MarkerHeight(marker, place, origin);
                Consider(HitSphere(place + new Vector3(0, height / 2, 0), height / 2, origin, direction), ObjectRef.Marker(marker.Id));
            }
        }

        if (found is null && shown.Decals && terrain is float ground)
        {
            Vector3 point = origin + direction * ground;
            for (int i = document.Decals.Length - 1; i >= 0; i--)
            {
                if (Covers(document.Decals[i], new Vector2(point.X, point.Z)) && shown.HiddenDecals?.Contains(document.Decals[i].Id) != true
                    && shown.Include?.Invoke(ObjectRef.Decal(document.Decals[i].Id)) != false)
                {
                    return ObjectRef.Decal(document.Decals[i].Id);
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Where along the ray it enters a box placed by <paramref name="placement"/>, or null.
    /// </summary>
    public static float? HitBox(Matrix4x4 placement, ObjectBounds bounds, Vector3 origin, Vector3 direction)
    {
        if (!Matrix4x4.Invert(placement, out Matrix4x4 inverse))
        {
            return null;
        }
        // an affine map keeps the ray's parameter: a hit in the box's space is one along the ray
        Vector3 o = Vector3.Transform(origin, inverse);
        Vector3 d = Vector3.TransformNormal(direction, inverse);
        float enter = float.NegativeInfinity;
        float leave = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float oa = o[axis];
            float da = d[axis];
            float low = bounds.Min[axis];
            float high = bounds.Max[axis];
            if (MathF.Abs(da) < 1e-12f)
            {
                if (oa < low || oa > high)
                {
                    return null;
                }
                continue;
            }
            float t0 = (low - oa) / da;
            float t1 = (high - oa) / da;
            enter = Math.Max(enter, Math.Min(t0, t1));
            leave = Math.Min(leave, Math.Max(t0, t1));
        }
        return enter <= leave && leave >= 0 ? Math.Max(enter, 0) : null;
    }

    /// <summary>
    /// Where along the ray it enters a sphere, or null.
    /// </summary>
    public static float? HitSphere(Vector3 center, float radius, Vector3 origin, Vector3 direction)
    {
        Vector3 m = origin - center;
        float a = Vector3.Dot(direction, direction);
        float b = Vector3.Dot(m, direction);
        float c = Vector3.Dot(m, m) - radius * radius;
        float discriminant = b * b - a * c;
        if (a <= 0 || discriminant < 0)
        {
            return null;
        }
        float root = MathF.Sqrt(discriminant);
        float enter = (-b - root) / a;
        float leave = (-b + root) / a;
        return leave < 0 ? null : Math.Max(enter, 0);
    }

    /// <summary>
    /// Whether a place of the map lies on a decal: its rectangle from its corner (its position)
    /// along its two edges, turned about that corner, as the 3D view draws it.
    /// </summary>
    public static bool Covers(Decal decal, Vector2 place)
    {
        (float sin, float cos) = MathF.SinCos(decal.Rotation.Y);
        Vector2 corner = new Vector2(decal.Position.X, decal.Position.Z);
        Vector2 edgeU = new Vector2(decal.Scale.X * cos, decal.Scale.X * sin);
        Vector2 edgeV = new Vector2(-decal.Scale.Z * sin, decal.Scale.Z * cos);
        float determinant = edgeU.X * edgeV.Y - edgeU.Y * edgeV.X;
        if (MathF.Abs(determinant) < 1e-9f)
        {
            return false;
        }
        Vector2 p = place - corner;
        float u = (p.X * edgeV.Y - p.Y * edgeV.X) / determinant;
        float v = (edgeU.X * p.Y - edgeU.Y * p.X) / determinant;
        return u is >= 0 and <= 1 && v is >= 0 and <= 1;
    }
}
