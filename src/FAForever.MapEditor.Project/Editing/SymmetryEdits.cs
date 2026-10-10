using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A thing's counterpart in another part of a symmetric map: <paramref name="Partner"/> lies where
    /// <paramref name="Transform"/> takes <paramref name="Of"/>.
    /// </summary>
    public readonly record struct SymmetryPartner(ObjectRef Of, ObjectRef Partner, MapSymmetryTransform Transform);

    /// <summary>
    /// Edits that keep a map symmetric (user story F-12): the images of things in the other parts of
    /// the map, their counterparts that are there already (mirror partners), and moving, turning and
    /// removing things together with their partners. Places are in ogrids, angles in radians as
    /// <see cref="MapObjects"/> defines them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A thing's mirror partner under a transform is the thing of the same kind and likeness nearest
    /// to the image of its place, within a tolerance: a prop or unit of the same blueprint, a decal of
    /// the same texture, a marker of the same kind (a deposit of the same resource, an AI marker of the
    /// same type, a path node of the same layer). Areas have none: the playable area stays as it is.
    /// </para>
    /// <para>
    /// An image keeps the thing's height above the terrain. A prop's whole rotation is mirrored with
    /// <see cref="MapSymmetryTransform.Apply(Matrix4x4)"/> (it stays a rotation); a unit faces where the
    /// image of its facing points (the yaw of <see cref="MapSymmetryTransform.ApplyYaw"/>, as the views
    /// draw units); a decal's rectangle lies over the image of its rectangle, its texture turned (a
    /// decal cannot be drawn mirrored); a marker keeps its orientation, which the game does not use.
    /// </para>
    /// </remarks>
    public static class SymmetryEdits
    {
        /// <summary>
        /// How far, in ogrids, a partner may lie from the image of a thing's place.
        /// </summary>
        public const float DefaultTolerance = 1;

        private static readonly ConditionalWeakTable<MapDocument, PartnerIndex> Indexes = [];

        #region Partners

        /// <summary>
        /// The thing's mirror partner under a transform, or null when there is none (or the image of
        /// its place is its own place: a thing on the axis is its own image).
        /// </summary>
        public static ObjectRef? PartnerOf(this MapDocument document, ObjectRef thing, MapSymmetryTransform transform, float tolerance = DefaultTolerance)
        {
            if (document.Find(thing) is not object found || LikenessOf(found) is not string likeness || document.PlaceOf(thing) is not Vector3 place)
            {
                return null;
            }
            Vector2 image = transform.Apply(new Vector2(place.X, place.Z));
            if (Vector2.Distance(image, new Vector2(place.X, place.Z)) <= tolerance)
            {
                return null;
            }
            return Indexes.GetValue(document, d => new PartnerIndex(d)).Nearest(likeness, image, tolerance, thing);
        }

        /// <summary>
        /// The mirror partners of the things under every transform but the identity, each partner once:
        /// the first thing that finds it keeps it, and a partner that is one of the things is left out
        /// (it moves as itself).
        /// </summary>
        /// <param name="transforms">The parts of the map (<see cref="MapSymmetry.Transforms"/>); the identity is skipped.</param>
        public static ImmutableArray<SymmetryPartner> Partners(this MapDocument document, IEnumerable<ObjectRef> things, IReadOnlyList<MapSymmetryTransform> transforms, float tolerance = DefaultTolerance)
        {
            ObjectRef[] list = [.. things];
            HashSet<ObjectRef> taken = [.. list];
            ImmutableArray<SymmetryPartner>.Builder partners = ImmutableArray.CreateBuilder<SymmetryPartner>();
            foreach (ObjectRef thing in list)
            {
                foreach (MapSymmetryTransform transform in transforms)
                {
                    if (transform == MapSymmetryTransform.Identity)
                    {
                        continue;
                    }
                    if (document.PartnerOf(thing, transform, tolerance) is ObjectRef partner && taken.Add(partner))
                    {
                        partners.Add(new SymmetryPartner(thing, partner, transform));
                    }
                }
            }
            return partners.ToImmutable();
        }

        /// <summary>
        /// The things without those that are the mirror partner of a thing kept before them: of a
        /// selection that holds things and their images, the things alone, so that a copy mirrored
        /// again (<see cref="MirrorObjects"/>) is made once per part.
        /// </summary>
        public static ImmutableArray<ObjectRef> WithoutPartners(this MapDocument document, IEnumerable<ObjectRef> things, IReadOnlyList<MapSymmetryTransform> transforms, float tolerance = DefaultTolerance)
        {
            ImmutableArray<ObjectRef>.Builder kept = ImmutableArray.CreateBuilder<ObjectRef>();
            HashSet<ObjectRef> images = [];
            foreach (ObjectRef thing in things)
            {
                if (images.Contains(thing))
                {
                    continue;
                }
                kept.Add(thing);
                foreach (MapSymmetryTransform transform in transforms)
                {
                    if (transform != MapSymmetryTransform.Identity && document.PartnerOf(thing, transform, tolerance) is ObjectRef partner)
                    {
                        images.Add(partner);
                    }
                }
            }
            return kept.ToImmutable();
        }

        /// <summary>
        /// What makes two things counterparts besides their place: the kind and the blueprint, texture,
        /// resource, type or layer; null for what has no partner (areas, weather).
        /// </summary>
        public static string? LikenessOf(object thing) => thing switch
        {
            Prop prop => "prop:" + prop.Blueprint.ToLowerInvariant(),
            PlacedUnit unit => "unit:" + unit.Unit.Blueprint.ToLowerInvariant(),
            Decal decal => "decal:" + (decal.Textures.IsEmpty ? "" : decal.Textures[0].ToLowerInvariant()),
            AiMarker ai => "ai:" + ai.Type,
            Spawn or ResourceMarker or PathNode => "marker:" + MarkerEdits.KindOf((Marker)thing),
            _ => null,
        };

        #endregion

        #region Images

        /// <summary>
        /// The image of a prop: its place and its whole rotation mirrored, as high above the terrain.
        /// </summary>
        public static Prop ImageOf(Prop prop, MapSymmetryTransform transform, Heightmap heights)
        {
            Matrix4x4 rotation = new Matrix4x4(
                prop.RotationX.X, prop.RotationX.Y, prop.RotationX.Z, 0,
                prop.RotationY.X, prop.RotationY.Y, prop.RotationY.Z, 0,
                prop.RotationZ.X, prop.RotationZ.Y, prop.RotationZ.Z, 0,
                0, 0, 0, 1);
            bool unset = prop.RotationX == Vector3.Zero && prop.RotationY == Vector3.Zero && prop.RotationZ == Vector3.Zero;
            Matrix4x4 mirrored = transform.Apply(unset ? Matrix4x4.Identity : rotation);
            return prop with
            {
                Position = Grounded(heights, prop.Position, transform),
                RotationX = new Vector3(mirrored.M11, mirrored.M12, mirrored.M13),
                RotationY = new Vector3(mirrored.M21, mirrored.M22, mirrored.M23),
                RotationZ = new Vector3(mirrored.M31, mirrored.M32, mirrored.M33),
            };
        }

        /// <summary>
        /// The image of a decal: its rectangle over the image of its rectangle, turned so that its
        /// width runs along the image of its width.
        /// </summary>
        public static Decal ImageOf(Decal decal, MapSymmetryTransform transform, Heightmap heights)
        {
            Vector3 centre = MapObjects.DecalCentre(decal);
            Vector2 image = transform.Apply(new Vector2(centre.X, centre.Z));
            (float sin, float cos) = MathF.SinCos(decal.Rotation.Y);
            Vector2 width = transform.ApplyDirection(new Vector2(cos, sin));
            float heading = MathF.Atan2(width.Y, width.X);
            (Vector2 u, Vector2 v) = MapObjects.DecalEdges(decal.Scale, heading);
            Vector2 corner = image - (u + v) / 2;
            float above = decal.Position.Y - heights.HeightAt(decal.Position.X, decal.Position.Z);
            return decal with
            {
                Position = new Vector3(corner.X, heights.HeightAt(corner.X, corner.Y) + above, corner.Y),
                Rotation = decal.Rotation with { Y = heading },
            };
        }

        /// <summary>
        /// The image of a unit: its place, facing where the image of its facing points; a mirror turns
        /// its roll the other way.
        /// </summary>
        public static Unit ImageOf(Unit unit, MapSymmetryTransform transform, Heightmap heights) => unit with
        {
            Position = Grounded(heights, unit.Position, transform),
            Orientation = new Vector3(unit.Orientation.X, transform.ApplyYaw(unit.Orientation.Y), transform.IsMirror ? -unit.Orientation.Z : unit.Orientation.Z),
        };

        /// <summary>
        /// The image of a marker: its place, as high above the terrain; its orientation stays. A path
        /// node's links are left to the caller.
        /// </summary>
        public static Marker ImageOf(Marker marker, MapSymmetryTransform transform, Heightmap heights) =>
            marker with { Position = Grounded(heights, marker.Position, transform) };

        /// <summary>
        /// Adds the images of the things in every part of the map but the first (the identity), in one
        /// edit: copies of props, decals, markers and units at their mirrored places. An image is left
        /// out where the thing has a mirror partner already, or where it would lie on the thing itself
        /// (on an axis, at the middle of a turn) or outside the map. Areas are left out.
        /// </summary>
        /// <remarks>
        /// Markers are named as new ones are: a spawn is the next playable army (<c>ARMY_n</c>, its army
        /// added), a deposit the next free name; a deposit waiting for an army waits for the army of that
        /// army's spawn's partner. A path node's image links to the images of its neighbours, or to their
        /// partners where those were not mirrored. A unit's image goes into the unit's army and group.
        /// </remarks>
        /// <param name="adjustUnit">Changes a unit's image before it is added (the editor snaps a
        /// structure to the build grid); null to add it as it is.</param>
        /// <param name="added">The images, in the order of the things and then of the transforms.</param>
        public static MapDocument MirrorObjects(this MapDocument document, IEnumerable<ObjectRef> things, IReadOnlyList<MapSymmetryTransform> transforms,
            out ImmutableArray<ObjectRef> added, float tolerance = DefaultTolerance, Func<Unit, Unit>? adjustUnit = null)
        {
            MapDocument before = document;
            Heightmap heights = document.Terrain.Heightmap;
            ObjectRef[] list = [.. things.Where(document.Exists)];
            HashSet<ObjectRef> mirrored = [.. list];
            ImmutableArray<ObjectRef>.Builder images = ImmutableArray.CreateBuilder<ObjectRef>();
            // per transform, the image of each path node mirrored: their links are made when all are added
            Dictionary<(MapSymmetryTransform, int), int> nodeImages = [];
            List<(PathNode Node, MapSymmetryTransform Transform, int Image)> nodes = [];
            List<Prop> props = [];

            bool Inside(Vector3 place) => place.X >= 0 && place.Z >= 0 && place.X <= document.Width && place.Z <= document.Height;

            foreach (ObjectRef thing in list)
            {
                object found = before.Find(thing)!;
                if (LikenessOf(found) is null || before.PlaceOf(thing) is not Vector3 place)
                {
                    continue;
                }
                foreach (MapSymmetryTransform transform in transforms)
                {
                    if (transform == MapSymmetryTransform.Identity)
                    {
                        continue;
                    }
                    Vector3 image = transform.Apply(place);
                    if (!Inside(image)
                        || Vector2.Distance(new Vector2(image.X, image.Z), new Vector2(place.X, place.Z)) <= tolerance
                        || before.PartnerOf(thing, transform, tolerance) is not null)
                    {
                        continue;
                    }
                    switch (found)
                    {
                        case Prop prop:
                            props.Add(ImageOf(prop, transform, heights));
                            break;
                        case Decal decal:
                            document = document.PlaceDecal(ImageOf(decal, transform, heights), out ObjectRef decalImage);
                            images.Add(decalImage);
                            break;
                        case PlacedUnit unit:
                            Unit unitImage = ImageOf(unit.Unit, transform, heights) with { Name = document.NextUnitName() };
                            if (adjustUnit is not null)
                            {
                                // snapped elsewhere, it keeps its height above the terrain
                                unitImage = adjustUnit(unitImage);
                                float above = unit.Unit.Position.Y - heights.HeightAt(unit.Unit.Position.X, unit.Unit.Position.Z);
                                unitImage = unitImage with { Position = unitImage.Position with { Y = heights.HeightAt(unitImage.Position.X, unitImage.Position.Z) + above } };
                            }
                            document = document.AddUnit(unit.Army, unit.Group, unitImage);
                            images.Add(ObjectRef.Unit(unitImage.Name));
                            break;
                        case Marker marker:
                            Marker markerImage = ImageOf(marker, transform, heights);
                            markerImage = markerImage switch
                            {
                                Spawn spawn => spawn with { Name = "" },
                                ResourceMarker resource => resource with
                                {
                                    Position = Snapped(heights, resource.Position, resource, marker),
                                    SpawnWithArmy = resource.SpawnWithArmy is string army ? PartnerArmy(before, army, transform, tolerance) : null,
                                },
                                PathNode node => node with { AdjacentTo = [] },
                                // a name that is taken becomes the next free one of its kind
                                _ => markerImage,
                            };
                            document = document.AddMarker(markerImage, out Marker addedMarker);
                            images.Add(ObjectRef.Marker(addedMarker.Id));
                            if (marker is PathNode original)
                            {
                                nodeImages[(transform, original.Id)] = addedMarker.Id;
                                nodes.Add((original, transform, addedMarker.Id));
                            }
                            break;
                    }
                }
            }

            if (props.Count > 0)
            {
                document = document.AddProps(props, out ImmutableArray<int> ids);
                images.AddRange(ids.Select(ObjectRef.Prop));
            }

            foreach ((PathNode node, MapSymmetryTransform transform, int image) in nodes)
            {
                foreach (int neighbour in node.AdjacentTo)
                {
                    int? other = nodeImages.TryGetValue((transform, neighbour), out int imaged) ? imaged
                        : before.PartnerOf(ObjectRef.Marker(neighbour), transform, tolerance) is ObjectRef partner ? partner.Id
                        : null;
                    if (other is int linked && document.FindMarker(linked) is PathNode && !document.AreLinked(image, linked))
                    {
                        document = document.LinkPathNodes(image, linked);
                    }
                }
            }
            added = images.ToImmutable();
            return document;
        }

        #endregion

        #region Moving with partners

        /// <summary>
        /// Moves the things and their partners: each partner by the image of the offset, so that it
        /// stays the image of its thing.
        /// </summary>
        public static MapDocument MoveObjects(this MapDocument document, IReadOnlyCollection<ObjectRef> things, IReadOnlyList<SymmetryPartner> partners, float dx, float dz)
        {
            document = document.MoveObjects(things, dx, dz);
            foreach (IGrouping<MapSymmetryTransform, SymmetryPartner> group in partners.GroupBy(partner => partner.Transform))
            {
                Vector2 offset = group.Key.ApplyDirection(new Vector2(dx, dz));
                document = document.MoveObjects(group.Select(partner => partner.Partner), offset.X, offset.Y);
            }
            return document;
        }

        /// <summary>
        /// Turns the things and their partners, each about its own centre: a partner in a mirrored part
        /// the other way.
        /// </summary>
        public static MapDocument RotateObjects(this MapDocument document, IReadOnlyCollection<ObjectRef> things, IReadOnlyList<SymmetryPartner> partners, float radians)
        {
            document = document.RotateObjects(things, radians);
            document = document.RotateObjects(partners.Where(partner => !partner.Transform.IsMirror).Select(partner => partner.Partner), radians);
            return document.RotateObjects(partners.Where(partner => partner.Transform.IsMirror).Select(partner => partner.Partner), -radians);
        }

        /// <summary>
        /// Turns the things about one place, as <see cref="ObjectEdits.RotateObjectsAbout"/> does, and
        /// each partner about the image of that place in its part: the other way in a mirrored part,
        /// so that the partners stay the images of their things.
        /// </summary>
        public static MapDocument RotateObjectsAbout(this MapDocument document, IReadOnlyCollection<ObjectRef> things, IReadOnlyList<SymmetryPartner> partners, float radians, Vector2 pivot)
        {
            document = document.RotateObjectsAbout(things, radians, pivot);
            foreach (IGrouping<MapSymmetryTransform, SymmetryPartner> group in partners.GroupBy(partner => partner.Transform))
            {
                document = document.RotateObjectsAbout(group.Select(partner => partner.Partner), group.Key.IsMirror ? -radians : radians, group.Key.Apply(pivot));
            }
            return document;
        }

        /// <summary>
        /// Scales the things as <see cref="ObjectEdits.ScaleObjects"/> does, and each partner by the
        /// same factors along the images of the axes, about the image of the pivot (or each about its
        /// own centre without one): a decal's partner, whose width runs along the image of its width,
        /// gets the same width and length.
        /// </summary>
        public static MapDocument ScaleObjects(this MapDocument document, IReadOnlyCollection<ObjectRef> things, IReadOnlyList<SymmetryPartner> partners, float x, float z, float heading = 0, Vector2? pivot = null)
        {
            document = document.ScaleObjects(things, x, z, heading, pivot);
            (float sin, float cos) = MathF.SinCos(heading);
            foreach (IGrouping<MapSymmetryTransform, SymmetryPartner> group in partners.GroupBy(partner => partner.Transform))
            {
                Vector2 axis = group.Key.ApplyDirection(new Vector2(cos, sin));
                Vector2? image = pivot is Vector2 p ? group.Key.Apply(p) : null;
                document = document.ScaleObjects(group.Select(partner => partner.Partner), x, z, MathF.Atan2(axis.Y, axis.X), image);
            }
            return document;
        }

        /// <summary>
        /// Puts each thing's centre at a place and turns it to face a way, as
        /// <see cref="ObjectEdits.PlaceObjects"/> and <see cref="ObjectEdits.SetHeading"/> do, and each
        /// partner at the image of its thing's new place, turned by as much as its thing (the other
        /// way in a mirrored part).
        /// </summary>
        public static MapDocument PlaceObjects(this MapDocument document, IReadOnlyCollection<ObjectRef> things, IReadOnlyList<SymmetryPartner> partners, float? x, float? z, float? heading)
        {
            MapDocument before = document;
            if (x is not null || z is not null)
            {
                document = document.PlaceObjects(things, x, z);
            }
            if (heading is float radians)
            {
                document = document.SetHeading(things, radians);
            }
            foreach (SymmetryPartner partner in partners)
            {
                if (document.PlaceOf(partner.Of) is Vector3 place && (x is not null || z is not null))
                {
                    Vector3 image = partner.Transform.Apply(place);
                    document = document.PlaceObjects([partner.Partner], image.X, image.Z);
                }
                if (heading is not null && before.HeadingOf(partner.Of) is float old && document.HeadingOf(partner.Of) is float turned)
                {
                    float turn = turned - old;
                    document = document.RotateObjects([partner.Partner], partner.Transform.IsMirror ? -turn : turn);
                }
            }
            return document;
        }

        #endregion

        /// <summary>
        /// A place moved by a transform, as high above the terrain as it was.
        /// </summary>
        private static Vector3 Grounded(Heightmap heights, Vector3 position, MapSymmetryTransform transform)
        {
            Vector3 image = transform.Apply(position);
            return new Vector3(image.X, heights.HeightAt(image.X, image.Z) + position.Y - heights.HeightAt(position.X, position.Z), image.Z);
        }

        /// <summary>
        /// A deposit's image at the centre of its ogrid, where the game puts deposits.
        /// </summary>
        private static Vector3 Snapped(Heightmap heights, Vector3 image, ResourceMarker resource, Marker original)
        {
            float x = MarkerEdits.Snap(image.X, MarkerSnap.Centre);
            float z = MarkerEdits.Snap(image.Z, MarkerSnap.Centre);
            return new Vector3(x, heights.HeightAt(x, z) + original.Position.Y - heights.HeightAt(original.Position.X, original.Position.Z), z);
        }

        /// <summary>
        /// The army whose spawn is the partner of an army's spawn; the army itself when there is none.
        /// </summary>
        private static string PartnerArmy(MapDocument document, string army, MapSymmetryTransform transform, float tolerance) =>
            document.Markers.Spawns.FirstOrDefault(spawn => spawn.Name == army) is Spawn spawn
            && document.PartnerOf(ObjectRef.Marker(spawn.Id), transform, tolerance) is ObjectRef partner
            && document.Find(partner) is Spawn other
                ? other.Name
                : army;

        /// <summary>
        /// The things of one version of a document by likeness, in cells of the map, made when first asked for.
        /// </summary>
        private sealed class PartnerIndex
        {
            private const float CellSize = 8;
            private readonly Dictionary<(string Likeness, int X, int Z), List<(ObjectRef Thing, Vector2 Place)>> _cells = [];

            public PartnerIndex(MapDocument document)
            {
                foreach (ObjectRef thing in document.All(ObjectKinds.All & ~ObjectKinds.Areas))
                {
                    if (document.Find(thing) is object found && LikenessOf(found) is string likeness && document.PlaceOf(thing) is Vector3 place)
                    {
                        (int x, int z) = Cell(place.X, place.Z);
                        if (!_cells.TryGetValue((likeness, x, z), out List<(ObjectRef, Vector2)>? list))
                        {
                            list = [];
                            _cells[(likeness, x, z)] = list;
                        }
                        list.Add((thing, new Vector2(place.X, place.Z)));
                    }
                }
            }

            public ObjectRef? Nearest(string likeness, Vector2 place, float tolerance, ObjectRef except)
            {
                ObjectRef? best = null;
                float bestDistance = tolerance;
                (int x0, int z0) = Cell(place.X - tolerance, place.Y - tolerance);
                (int x1, int z1) = Cell(place.X + tolerance, place.Y + tolerance);
                lock (this)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            if (!_cells.TryGetValue((likeness, x, z), out List<(ObjectRef Thing, Vector2 Place)>? list))
                            {
                                continue;
                            }
                            foreach ((ObjectRef thing, Vector2 at) in list)
                            {
                                float distance = Vector2.Distance(at, place);
                                if (distance <= bestDistance && thing != except)
                                {
                                    best = thing;
                                    bestDistance = distance;
                                }
                            }
                        }
                    }
                }
                return best;
            }

            private static (int, int) Cell(float x, float z) => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(z / CellSize));
        }
    }
}
