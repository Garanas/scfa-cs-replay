using System.Numerics;
using System.Runtime.CompilerServices;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What lies on a map (markers, props, decals, units, areas) as one kind of thing to select:
    /// where each is, which way it faces, and which lies under a place (<see cref="Nearest"/>) or in a
    /// rectangle (<see cref="Inside"/>). Places are in ogrids, x and z across the map.
    /// </summary>
    /// <remarks>
    /// A thing's place is its centre: a marker's, prop's or unit's position, the middle of a decal's
    /// rectangle (whose position is a corner), the middle of an area. Its heading is its turn about
    /// the up axis in radians, from +x towards +z (clockwise in the top view, where z grows
    /// downwards): a decal's <see cref="Decal.Rotation"/> y, a marker's or unit's orientation y, and
    /// for a prop the direction its own x axis points in (<see cref="Prop.RotationX"/> and the others
    /// are the rows of its rotation matrix). Areas do not turn.
    /// </remarks>
    public static class MapObjects
    {
        private static readonly ConditionalWeakTable<MapDocument, Index> Indexes = [];

        /// <summary>
        /// Every thing of the chosen kinds, markers first, then areas, props, units and decals.
        /// Of the markers, only the kinds of <paramref name="markers"/>.
        /// </summary>
        public static IEnumerable<ObjectRef> All(this MapDocument document, ObjectKinds kinds, MarkerKinds markers = MarkerKinds.All)
        {
            if (kinds.HasFlag(ObjectKinds.Markers))
            {
                foreach (Marker marker in document.Markers.All.Where(marker => marker is not PathNode && (markers & MarkerEdits.KindOf(marker)) != 0))
                {
                    yield return ObjectRef.Marker(marker.Id);
                }
            }
            if (kinds.HasFlag(ObjectKinds.PathNodes))
            {
                foreach (PathNode node in document.Markers.PathNodes.Where(node => (markers & MarkerEdits.KindOf(node.Layer)) != 0))
                {
                    yield return ObjectRef.Marker(node.Id);
                }
            }
            if (kinds.HasFlag(ObjectKinds.Areas))
            {
                foreach (Area area in document.Markers.Areas)
                {
                    yield return ObjectRef.Area(area.Name);
                }
            }
            if (kinds.HasFlag(ObjectKinds.Props))
            {
                foreach (Prop prop in document.Props)
                {
                    yield return ObjectRef.Prop(prop.Id);
                }
            }
            if (kinds.HasFlag(ObjectKinds.Units))
            {
                foreach (PlacedUnit unit in document.AllUnits())
                {
                    yield return ObjectRef.Unit(unit.Unit.Name);
                }
            }
            if (kinds.HasFlag(ObjectKinds.Decals))
            {
                foreach (Decal decal in document.Decals)
                {
                    yield return ObjectRef.Decal(decal.Id);
                }
            }
        }

        public static bool Exists(this MapDocument document, ObjectRef thing) => Indexes.GetValue(document, d => new Index(d)).Find(thing) is not null;

        /// <summary>
        /// The marker, prop, decal, unit (<see cref="PlacedUnit"/>) or area a reference names, or null.
        /// </summary>
        public static object? Find(this MapDocument document, ObjectRef thing) => Indexes.GetValue(document, d => new Index(d)).Find(thing);

        /// <summary>
        /// Whether the thing is a path node (they are chosen apart from the other markers).
        /// </summary>
        public static bool IsPathNode(this MapDocument document, ObjectRef thing) => thing.Kind == ObjectKind.Marker && document.Find(thing) is PathNode;

        /// <summary>
        /// The thing's centre, with its height; null when it does not exist.
        /// </summary>
        public static Vector3? PlaceOf(this MapDocument document, ObjectRef thing) => document.Find(thing) switch
        {
            Marker marker => marker.Position,
            Prop prop => prop.Position,
            PlacedUnit unit => unit.Unit.Position,
            Decal decal => DecalCentre(decal),
            Area area => AreaCentre(document, area),
            _ => null,
        };

        /// <summary>
        /// The thing's turn about the up axis in radians; null for an area or a thing that does not exist.
        /// </summary>
        public static float? HeadingOf(this MapDocument document, ObjectRef thing) => document.Find(thing) switch
        {
            Marker marker => marker.Orientation.Y,
            Prop prop => PropHeading(prop),
            PlacedUnit unit => unit.Unit.Orientation.Y,
            Decal decal => decal.Rotation.Y,
            _ => null,
        };

        /// <summary>
        /// The corners of a decal's or an area's rectangle (x, z), in order around it; empty for the
        /// other kinds and for a thing that does not exist.
        /// </summary>
        public static Vector2[] OutlineOf(this MapDocument document, ObjectRef thing) => document.Find(thing) switch
        {
            Decal decal => DecalCorners(decal),
            Area area => [new Vector2((float)area.X0, (float)area.Z0), new Vector2((float)area.X1, (float)area.Z0), new Vector2((float)area.X1, (float)area.Z1), new Vector2((float)area.X0, (float)area.Z1)],
            _ => [],
        };

        /// <summary>
        /// The thing of the chosen kinds nearest to a place, as a click picks it: a marker, prop or
        /// unit within a few pixels of its centre (more for a spawn, which is drawn larger), an area
        /// within a few pixels of its edge, a decal when the place lies in its rectangle. Things picked
        /// by their centre win over decals; among decals the one on top (the last) wins.
        /// </summary>
        /// <param name="markers">The kinds of markers among them (those the views show, user story R-6).</param>
        /// <param name="ogridsPerPixel">How large a pixel of the view is on the map: the sizes on
        /// screen become distances on the map with it.</param>
        /// <param name="include">Whether a thing may be picked at all (the editor leaves out what it
        /// hides, such as the decals of a hidden group); null for every thing.</param>
        /// <param name="footprints">What a structure takes up (<see cref="UnitFootprints"/>): a click inside
        /// its rectangle, turned by its heading, picks it; null picks every unit by its centre.</param>
        public static ObjectRef? Nearest(this MapDocument document, float x, float z, float ogridsPerPixel, ObjectKinds kinds, Func<ObjectRef, bool>? include = null, MarkerKinds markers = MarkerKinds.All, UnitFootprints? footprints = null)
        {
            ObjectRef? best = null;
            float bestScore = float.MaxValue;
            void Consider(ObjectRef thing, float distance, float pixels)
            {
                float reach = pixels * ogridsPerPixel;
                if (distance <= reach && distance / reach < bestScore && (include is null || include(thing)))
                {
                    best = thing;
                    bestScore = distance / reach;
                }
            }
            float Distance(Vector3 place) => MathF.Sqrt((place.X - x) * (place.X - x) + (place.Z - z) * (place.Z - z));

            if (kinds.HasFlag(ObjectKinds.Markers))
            {
                foreach (Marker marker in document.Markers.All.Where(marker => marker is not PathNode && (markers & MarkerEdits.KindOf(marker)) != 0))
                {
                    Consider(ObjectRef.Marker(marker.Id), Distance(marker.Position), marker is Spawn ? 12 : 8);
                }
            }
            if (kinds.HasFlag(ObjectKinds.PathNodes))
            {
                foreach (PathNode node in document.Markers.PathNodes.Where(node => (markers & MarkerEdits.KindOf(node.Layer)) != 0))
                {
                    Consider(ObjectRef.Marker(node.Id), Distance(node.Position), 6);
                }
            }
            if (kinds.HasFlag(ObjectKinds.Props))
            {
                foreach (Prop prop in document.Props)
                {
                    Consider(ObjectRef.Prop(prop.Id), Distance(prop.Position), 6);
                }
            }
            if (kinds.HasFlag(ObjectKinds.Units))
            {
                foreach (PlacedUnit unit in document.AllUnits())
                {
                    float centre = Distance(unit.Unit.Position);
                    if (footprints?.Invoke(unit.Unit.Blueprint) is (double sizeX, double sizeZ) && InsideFootprint(unit.Unit, sizeX, sizeZ, x, z))
                    {
                        // inside a structure's footprint: it wins over what the pixels reach, the nearer centre first
                        (float halfX, float halfZ) = FootprintHalves(sizeX, sizeZ);
                        ObjectRef thing = ObjectRef.Unit(unit.Unit.Name);
                        float score = 0.25f * centre / MathF.Max(0.001f, MathF.Sqrt(halfX * halfX + halfZ * halfZ));
                        if (score < bestScore && (include is null || include(thing)))
                        {
                            best = thing;
                            bestScore = score;
                        }
                        continue;
                    }
                    Consider(ObjectRef.Unit(unit.Unit.Name), centre, 7);
                }
            }
            if (kinds.HasFlag(ObjectKinds.Areas))
            {
                foreach (Area area in document.Markers.Areas)
                {
                    float dx = MathF.Max(MathF.Max((float)area.X0 - x, x - (float)area.X1), 0);
                    float dz = MathF.Max(MathF.Max((float)area.Z0 - z, z - (float)area.Z1), 0);
                    float outside = MathF.Sqrt(dx * dx + dz * dz);
                    float inside = MathF.Min(MathF.Min(x - (float)area.X0, (float)area.X1 - x), MathF.Min(z - (float)area.Z0, (float)area.Z1 - z));
                    Consider(ObjectRef.Area(area.Name), outside > 0 ? outside : MathF.Max(inside, 0), 5);
                }
            }
            if (best is null && kinds.HasFlag(ObjectKinds.Decals))
            {
                for (int i = document.Decals.Length - 1; i >= 0; i--)
                {
                    if (Contains(DecalCorners(document.Decals[i]), new Vector2(x, z)) && (include is null || include(ObjectRef.Decal(document.Decals[i].Id))))
                    {
                        return ObjectRef.Decal(document.Decals[i].Id);
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The things of the chosen kinds whose centre lies in a rectangle, as a box drawn with the
        /// mouse selects them; the corners may come in any order.
        /// </summary>
        /// <param name="markers">The kinds of markers among them (those the views show, user story R-6).</param>
        /// <param name="footprints">What a structure takes up (<see cref="UnitFootprints"/>): a box that
        /// touches its rectangle takes it in; null takes a unit by its centre.</param>
        public static IEnumerable<ObjectRef> Inside(this MapDocument document, float x0, float z0, float x1, float z1, ObjectKinds kinds, MarkerKinds markers = MarkerKinds.All, UnitFootprints? footprints = null)
        {
            (float left, float right) = (MathF.Min(x0, x1), MathF.Max(x0, x1));
            (float top, float bottom) = (MathF.Min(z0, z1), MathF.Max(z0, z1));
            foreach (ObjectRef thing in document.All(kinds, markers))
            {
                if (document.PlaceOf(thing) is not Vector3 place)
                {
                    continue;
                }
                if (place.X >= left && place.X <= right && place.Z >= top && place.Z <= bottom)
                {
                    yield return thing;
                }
                else if (footprints is not null && thing.Kind == ObjectKind.Unit && document.Find(thing) is PlacedUnit unit
                    && footprints(unit.Unit.Blueprint) is (double sizeX, double sizeZ) && FootprintReaches(unit.Unit, sizeX, sizeZ, left, top, right, bottom))
                {
                    yield return thing;
                }
            }
        }

        /// <summary>
        /// What a structure takes up in ogrids, along x and z when it is not turned; null for a unit
        /// that is picked by its centre (the mobile ones, and any whose blueprint is unknown).
        /// </summary>
        public delegate (double SizeX, double SizeZ)? UnitFootprints(string blueprint);

        /// <summary>
        /// The corners of a unit's footprint (x, z), in order around it, turned by its heading
        /// (clockwise in the top view) about its centre; sides under 1 ogrid count as 1.
        /// </summary>
        public static Vector2[] FootprintCorners(Unit unit, double sizeX, double sizeZ)
        {
            (float hx, float hz) = FootprintHalves(sizeX, sizeZ);
            (float sin, float cos) = MathF.SinCos(unit.Orientation.Y);
            Vector2 centre = new Vector2(unit.Position.X, unit.Position.Z);
            Vector2 u = new Vector2(hx * cos, hx * sin);
            Vector2 v = new Vector2(-hz * sin, hz * cos);
            return [centre - u - v, centre + u - v, centre + u + v, centre - u + v];
        }

        private static (float X, float Z) FootprintHalves(double sizeX, double sizeZ) => (MathF.Max(1, (float)sizeX) / 2, MathF.Max(1, (float)sizeZ) / 2);

        private static bool InsideFootprint(Unit unit, double sizeX, double sizeZ, float x, float z) =>
            Contains(FootprintCorners(unit, sizeX, sizeZ), new Vector2(x, z));

        // whether a footprint and a box (axes parallel to the map's) share a point: the corners' span against the box, then the box's corners against the footprint
        private static bool FootprintReaches(Unit unit, double sizeX, double sizeZ, float left, float top, float right, float bottom)
        {
            Vector2[] corners = FootprintCorners(unit, sizeX, sizeZ);
            float minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X), minZ = corners.Min(c => c.Y), maxZ = corners.Max(c => c.Y);
            if (maxX < left || minX > right || maxZ < top || minZ > bottom)
            {
                return false;
            }
            // a turned footprint's span can overlap the box where the footprint itself does not: separate on the footprint's own axes
            Vector2 u = Vector2.Normalize(corners[1] - corners[0]);
            Vector2 v = Vector2.Normalize(corners[3] - corners[0]);
            Vector2[] box = [new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom), new Vector2(left, bottom)];
            foreach (Vector2 axis in (ReadOnlySpan<Vector2>)[u, v])
            {
                float a0 = corners.Min(c => Vector2.Dot(c, axis)), a1 = corners.Max(c => Vector2.Dot(c, axis));
                float b0 = box.Min(c => Vector2.Dot(c, axis)), b1 = box.Max(c => Vector2.Dot(c, axis));
                if (a1 < b0 || b1 < a0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The middle of a decal's rectangle; its position is a corner, which it turns about.
        /// </summary>
        public static Vector3 DecalCentre(Decal decal)
        {
            (Vector2 u, Vector2 v) = DecalEdges(decal.Scale, decal.Rotation.Y);
            Vector2 centre = new Vector2(decal.Position.X, decal.Position.Z) + (u + v) / 2;
            return new Vector3(centre.X, decal.Position.Y, centre.Y);
        }

        /// <summary>
        /// The two edges of a decal's rectangle from its corner (x, z): across its width and its length.
        /// </summary>
        public static (Vector2 U, Vector2 V) DecalEdges(Vector3 scale, float rotation)
        {
            (float sin, float cos) = MathF.SinCos(rotation);
            return (new Vector2(scale.X * cos, scale.X * sin), new Vector2(-scale.Z * sin, scale.Z * cos));
        }

        /// <summary>
        /// The direction a prop's own x axis points in, about the up axis.
        /// </summary>
        public static float PropHeading(Prop prop) => MathF.Atan2(prop.RotationZ.X, prop.RotationX.X);

        private static Vector3 AreaCentre(MapDocument document, Area area)
        {
            float x = (float)(area.X0 + area.X1) / 2;
            float z = (float)(area.Z0 + area.Z1) / 2;
            return new Vector3(x, document.Terrain.Heightmap.HeightAt(x, z), z);
        }

        private static Vector2[] DecalCorners(Decal decal)
        {
            (Vector2 u, Vector2 v) = DecalEdges(decal.Scale, decal.Rotation.Y);
            Vector2 corner = new Vector2(decal.Position.X, decal.Position.Z);
            return [corner, corner + u, corner + u + v, corner + v];
        }

        /// <summary>
        /// Whether a point lies in a convex quadrilateral, whichever way round its corners go.
        /// </summary>
        private static bool Contains(Vector2[] corners, Vector2 point)
        {
            int sign = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 a = corners[i];
                Vector2 b = corners[(i + 1) % corners.Length];
                float cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
                int side = Math.Sign(cross);
                if (side != 0)
                {
                    if (sign != 0 && side != sign)
                    {
                        return false;
                    }
                    sign = side;
                }
            }
            return sign != 0;
        }

        /// <summary>
        /// The things of one version of a document by id or name, made when first asked for.
        /// </summary>
        private sealed class Index(MapDocument document)
        {
            private Dictionary<int, Marker>? _markers;
            private Dictionary<int, Prop>? _props;
            private Dictionary<int, Decal>? _decals;
            private Dictionary<string, PlacedUnit>? _units;
            private Dictionary<string, Area>? _areas;

            public object? Find(ObjectRef thing)
            {
                lock (this)
                {
                    return thing.Kind switch
                    {
                        ObjectKind.Marker => (_markers ??= Dictionary(document.Markers.All, marker => marker.Id)).GetValueOrDefault(thing.Id),
                        ObjectKind.Prop => (_props ??= Dictionary(document.Props, prop => prop.Id)).GetValueOrDefault(thing.Id),
                        ObjectKind.Decal => (_decals ??= Dictionary(document.Decals, decal => decal.Id)).GetValueOrDefault(thing.Id),
                        ObjectKind.Unit => (_units ??= Dictionary(document.AllUnits(), unit => unit.Unit.Name)).GetValueOrDefault(thing.Name ?? ""),
                        _ => (_areas ??= Dictionary(document.Markers.Areas, area => area.Name)).GetValueOrDefault(thing.Name ?? ""),
                    };
                }
            }

            // the first of two with the same key wins, as a search through the list would find it
            private static Dictionary<TKey, T> Dictionary<TKey, T>(IEnumerable<T> items, Func<T, TKey> key) where TKey : notnull
            {
                Dictionary<TKey, T> dictionary = [];
                foreach (T item in items)
                {
                    dictionary.TryAdd(key(item), item);
                }
                return dictionary;
            }
        }
    }
}
