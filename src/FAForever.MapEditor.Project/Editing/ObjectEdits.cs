using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Edits of many things on the map at once, whatever their kind, named by
    /// <see cref="ObjectRef"/>: move, place, turn, remove, copy and paste. Places are in ogrids and
    /// angles in radians, as <see cref="MapObjects"/> defines them.
    /// </summary>
    /// <remarks>
    /// Things keep to the terrain: after a move a thing lies as high above (or below) the terrain as
    /// it did before, so what lay on the ground stays on it and a sunken rock stays sunken. A
    /// reference to a thing that does not exist is left out.
    /// </remarks>
    public static class ObjectEdits
    {
        /// <summary>
        /// Moves the things across the map by an offset.
        /// </summary>
        public static MapDocument MoveObjects(this MapDocument document, IEnumerable<ObjectRef> things, float dx, float dz)
        {
            Heightmap heights = document.Terrain.Heightmap;
            Vector3 Move(Vector3 position) => Ground(heights, position, position.X + dx, position.Z + dz);
            return document.Change(things,
                prop => prop with { Position = Move(prop.Position) },
                decal => decal with { Position = Move(decal.Position) },
                marker => marker with { Position = Deposited(document, marker, Move(marker.Position)) },
                unit => unit with { Position = Move(unit.Position) },
                area => area with { X0 = area.X0 + dx, X1 = area.X1 + dx, Z0 = area.Z0 + dz, Z1 = area.Z1 + dz });
        }

        /// <summary>
        /// Puts each thing's centre at a place: what is left out stays, so <c>x</c> alone lines them up.
        /// </summary>
        public static MapDocument PlaceObjects(this MapDocument document, IEnumerable<ObjectRef> things, float? x, float? z)
        {
            Heightmap heights = document.Terrain.Heightmap;
            Vector3 Place(Vector3 position) => Ground(heights, position, x ?? position.X, z ?? position.Z);
            return document.Change(things,
                prop => prop with { Position = Place(prop.Position) },
                decal =>
                {
                    Vector3 centre = MapObjects.DecalCentre(decal);
                    float cx = x ?? centre.X;
                    float cz = z ?? centre.Z;
                    return decal with { Position = Ground(heights, decal.Position, decal.Position.X + cx - centre.X, decal.Position.Z + cz - centre.Z) };
                },
                marker => marker with { Position = Deposited(document, marker, Place(marker.Position)) },
                unit => unit with { Position = Place(unit.Position) },
                area =>
                {
                    double ox = x is float cx ? cx - (area.X0 + area.X1) / 2 : 0;
                    double oz = z is float cz ? cz - (area.Z0 + area.Z1) / 2 : 0;
                    return area with { X0 = area.X0 + ox, X1 = area.X1 + ox, Z0 = area.Z0 + oz, Z1 = area.Z1 + oz };
                });
        }

        /// <summary>
        /// Turns each thing about the up axis through its own centre, from +x towards +z (clockwise in
        /// the top view). A prop's whole rotation turns, so a tilted rock stays tilted; areas do not turn.
        /// </summary>
        public static MapDocument RotateObjects(this MapDocument document, IEnumerable<ObjectRef> things, float radians)
        {
            Heightmap heights = document.Terrain.Heightmap;
            return document.Change(things,
                prop => TurnProp(prop, radians),
                decal => TurnDecal(heights, decal, Wrap(decal.Rotation.Y + radians)),
                marker => marker with { Orientation = marker.Orientation with { Y = Wrap(marker.Orientation.Y + radians) } },
                unit => unit with { Orientation = unit.Orientation with { Y = Wrap(unit.Orientation.Y + radians) } },
                area => area);
        }

        /// <summary>
        /// Turns the things about the up axis through one place, from +x towards +z (clockwise in
        /// the top view): each turns about its own centre as <see cref="RotateObjects"/> does, and
        /// its centre goes round the place. Areas do not turn; their centres go round.
        /// </summary>
        public static MapDocument RotateObjectsAbout(this MapDocument document, IEnumerable<ObjectRef> things, float radians, Vector2 pivot)
        {
            Heightmap heights = document.Terrain.Heightmap;
            (float sin, float cos) = MathF.SinCos(radians);
            Vector2 Orbit(Vector2 place)
            {
                Vector2 d = place - pivot;
                return pivot + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
            }
            Vector3 Around(Vector3 position)
            {
                Vector2 to = Orbit(new Vector2(position.X, position.Z));
                return Ground(heights, position, to.X, to.Y);
            }
            return document.Change(things,
                prop => TurnProp(prop, radians) with { Position = Around(prop.Position) },
                decal =>
                {
                    // turned about its centre, which then goes round
                    Decal turned = TurnDecal(heights, decal, Wrap(decal.Rotation.Y + radians));
                    Vector3 centre = MapObjects.DecalCentre(turned);
                    Vector2 to = Orbit(new Vector2(centre.X, centre.Z));
                    return turned with { Position = Ground(heights, turned.Position, turned.Position.X + to.X - centre.X, turned.Position.Z + to.Y - centre.Z) };
                },
                marker => marker with { Position = Deposited(document, marker, Around(marker.Position)), Orientation = marker.Orientation with { Y = Wrap(marker.Orientation.Y + radians) } },
                unit => unit with { Position = Around(unit.Position), Orientation = unit.Orientation with { Y = Wrap(unit.Orientation.Y + radians) } },
                area =>
                {
                    Vector2 centre = new Vector2((float)(area.X0 + area.X1) / 2, (float)(area.Z0 + area.Z1) / 2);
                    Vector2 d = Orbit(centre) - centre;
                    return area with { X0 = area.X0 + d.X, X1 = area.X1 + d.X, Z0 = area.Z0 + d.Y, Z1 = area.Z1 + d.Y };
                });
        }

        /// <summary>
        /// Scales the things along two axes turned by <paramref name="heading"/> (radians, from +x
        /// towards +z): a decal's width by <paramref name="x"/> and length by <paramref name="z"/>
        /// (in its own axes), a prop alike along every axis by the factor that changes most, an area
        /// along the map's axes. With a <paramref name="pivot"/> the centres spread from (or gather
        /// to) it too, markers and units included; without, each thing scales about its own centre
        /// and markers and units stay as they are.
        /// </summary>
        public static MapDocument ScaleObjects(this MapDocument document, IEnumerable<ObjectRef> things, float x, float z, float heading = 0, Vector2? pivot = null)
        {
            Heightmap heights = document.Terrain.Heightmap;
            (float sin, float cos) = MathF.SinCos(heading);
            float uniform = MathF.Abs(x - 1) >= MathF.Abs(z - 1) ? x : z;
            // a place relative to the pivot, scaled in the turned axes
            Vector2 Spread(Vector2 place)
            {
                if (pivot is not Vector2 p)
                {
                    return place;
                }
                Vector2 d = place - p;
                Vector2 local = new Vector2(d.X * cos + d.Y * sin, -d.X * sin + d.Y * cos) * new Vector2(x, z);
                return p + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);
            }
            Vector3 Moved(Vector3 position)
            {
                Vector2 to = Spread(new Vector2(position.X, position.Z));
                return Ground(heights, position, to.X, to.Y);
            }
            return document.Change(things,
                prop =>
                {
                    Vector3 scale = prop.Scale == Vector3.Zero ? Vector3.One : prop.Scale;
                    return prop with { Scale = scale * uniform, Position = Moved(prop.Position) };
                },
                decal =>
                {
                    Decal resized = DecalEdits.Resized(heights, decal, decal.Scale.X * x, decal.Scale.Z * z);
                    Vector3 centre = MapObjects.DecalCentre(resized);
                    Vector2 to = Spread(new Vector2(centre.X, centre.Z));
                    return resized with { Position = Ground(heights, resized.Position, resized.Position.X + to.X - centre.X, resized.Position.Z + to.Y - centre.Z) };
                },
                marker => pivot is null ? marker : marker with { Position = Deposited(document, marker, Moved(marker.Position)) },
                unit => pivot is null ? unit : unit with { Position = Moved(unit.Position) },
                area =>
                {
                    double cx = (area.X0 + area.X1) / 2;
                    double cz = (area.Z0 + area.Z1) / 2;
                    Vector2 to = Spread(new Vector2((float)cx, (float)cz));
                    double hx = Math.Abs(area.X1 - area.X0) / 2 * x;
                    double hz = Math.Abs(area.Z1 - area.Z0) / 2 * z;
                    return area with { X0 = to.X - hx, X1 = to.X + hx, Z0 = to.Y - hz, Z1 = to.Y + hz };
                });
        }

        /// <summary>
        /// Turns each thing about the up axis through its own centre until it faces one way.
        /// </summary>
        public static MapDocument SetHeading(this MapDocument document, IEnumerable<ObjectRef> things, float radians)
        {
            Heightmap heights = document.Terrain.Heightmap;
            float heading = Wrap(radians);
            return document.Change(things,
                prop => TurnProp(prop, heading - MapObjects.PropHeading(prop)),
                decal => TurnDecal(heights, decal, heading),
                marker => marker with { Orientation = marker.Orientation with { Y = heading } },
                unit => unit with { Orientation = unit.Orientation with { Y = heading } },
                area => area);
        }

        /// <summary>
        /// Removes the things, with every link to a removed marker (<see cref="DocumentEdits.RemoveMarker"/>)
        /// and every decal group's mention of a removed decal.
        /// </summary>
        public static MapDocument RemoveObjects(this MapDocument document, IEnumerable<ObjectRef> things)
        {
            ObjectRef[] list = [.. things.Where(document.Exists)];
            HashSet<int> props = [.. list.Where(t => t.Kind == ObjectKind.Prop).Select(t => t.Id)];
            HashSet<int> decals = [.. list.Where(t => t.Kind == ObjectKind.Decal).Select(t => t.Id)];
            if (props.Count > 0)
            {
                document = document with { Props = [.. document.Props.Where(prop => !props.Contains(prop.Id))] };
            }
            if (decals.Count > 0)
            {
                document = document with
                {
                    Decals = [.. document.Decals.Where(decal => !decals.Contains(decal.Id))],
                    DecalGroups = [.. document.DecalGroups.Select(group => group.DecalIds.Any(decals.Contains) ? group with { DecalIds = [.. group.DecalIds.Where(id => !decals.Contains(id))] } : group)],
                };
            }
            foreach (ObjectRef thing in list)
            {
                document = thing.Kind switch
                {
                    ObjectKind.Marker => document.RemoveMarker(thing.Id),
                    ObjectKind.Unit => document.RemoveUnit(thing.Name!),
                    ObjectKind.Area => document.RemoveArea(thing.Name!),
                    _ => document,
                };
            }
            return document;
        }

        /// <summary>
        /// Copies of the things, to paste into this or another map.
        /// </summary>
        public static ObjectClipboard CopyObjects(this MapDocument document, IEnumerable<ObjectRef> things)
        {
            ImmutableArray<object> items = [.. things.Select(document.Find).OfType<object>()];
            Vector3[] places = [.. things.Select(document.PlaceOf).OfType<Vector3>()];
            Vector2 centre = places.Length == 0
                ? Vector2.Zero
                : new Vector2(places.Average(p => p.X), places.Average(p => p.Z));
            return new ObjectClipboard(items, centre);
        }

        /// <summary>
        /// Adds copies of what was copied, moved by an offset: props, decals and markers under new ids,
        /// markers, units and areas under names not taken (<see cref="DocumentEdits.FreeName"/>). Path
        /// nodes keep their links among each other; links to nodes that were not copied are left out.
        /// A unit whose army the map does not have is left out.
        /// </summary>
        /// <param name="added">What was added, in the clipboard's order.</param>
        public static MapDocument Paste(this MapDocument document, ObjectClipboard clipboard, float dx, float dz, out ImmutableArray<ObjectRef> added)
        {
            Heightmap heights = document.Terrain.Heightmap;
            Vector3 Move(Vector3 position) => Ground(heights, position, position.X + dx, position.Z + dz);
            ImmutableArray<ObjectRef>.Builder refs = ImmutableArray.CreateBuilder<ObjectRef>();
            Dictionary<int, int> nodeIds = [];
            List<PathNode> nodes = [];
            foreach (object item in clipboard.Items)
            {
                switch (item)
                {
                    case Prop prop:
                        document = document.AddProp(prop with { Position = Move(prop.Position), Rule = null });
                        refs.Add(ObjectRef.Prop(document.Props[^1].Id));
                        break;
                    case Decal decal:
                        document = document.AddDecal(decal with { Position = Move(decal.Position) });
                        refs.Add(ObjectRef.Decal(document.Decals[^1].Id));
                        break;
                    case PathNode node:
                        document = document.AddMarker(node with { Position = Move(node.Position), AdjacentTo = [] }, out Marker addedNode);
                        nodeIds[node.Id] = addedNode.Id;
                        nodes.Add(node);
                        refs.Add(ObjectRef.Marker(addedNode.Id));
                        break;
                    case Marker marker:
                        document = document.AddMarker(marker with { Position = Move(marker.Position) }, out Marker addedMarker);
                        refs.Add(ObjectRef.Marker(addedMarker.Id));
                        break;
                    case PlacedUnit unit when document.Armies.Any(army => army.Name == unit.Army):
                        string name = document.FindUnit(unit.Unit.Name) is null ? unit.Unit.Name : document.NextUnitName();
                        document = document.AddUnit(unit.Army, unit.Group, unit.Unit with { Name = name, Position = Move(unit.Unit.Position) });
                        refs.Add(ObjectRef.Unit(name));
                        break;
                    case Area area:
                        document = document.AddArea(area with { X0 = area.X0 + dx, X1 = area.X1 + dx, Z0 = area.Z0 + dz, Z1 = area.Z1 + dz }, out Area addedArea);
                        refs.Add(ObjectRef.Area(addedArea.Name));
                        break;
                }
            }
            foreach (PathNode node in nodes)
            {
                ImmutableArray<int> links = [.. node.AdjacentTo.Where(nodeIds.ContainsKey).Select(id => nodeIds[id])];
                if (!links.IsEmpty)
                {
                    document = document.UpdateMarker(nodeIds[node.Id], marker => ((PathNode)marker) with { AdjacentTo = links });
                }
            }
            added = refs.ToImmutable();
            return document;
        }

        /// <summary>
        /// The document with each named thing changed by its kind's function, in one pass per kind.
        /// </summary>
        private static MapDocument Change(this MapDocument document, IEnumerable<ObjectRef> things,
            Func<Prop, Prop> prop, Func<Decal, Decal> decal, Func<Marker, Marker> marker, Func<Unit, Unit> unit, Func<Area, Area> area)
        {
            HashSet<int> props = [];
            HashSet<int> decals = [];
            HashSet<int> markers = [];
            HashSet<string> units = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> areas = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectRef thing in things)
            {
                _ = thing.Kind switch
                {
                    ObjectKind.Prop => props.Add(thing.Id),
                    ObjectKind.Decal => decals.Add(thing.Id),
                    ObjectKind.Marker => markers.Add(thing.Id),
                    ObjectKind.Unit => units.Add(thing.Name!),
                    _ => areas.Add(thing.Name!),
                };
            }

            ImmutableArray<T> Each<T>(ImmutableArray<T> items, Func<T, bool> chosen, Func<T, T> change)
            {
                ImmutableArray<T>.Builder? changed = null;
                for (int i = 0; i < items.Length; i++)
                {
                    if (chosen(items[i]) && change(items[i]) is T result && !ReferenceEquals(result, items[i]))
                    {
                        changed ??= items.ToBuilder();
                        changed[i] = result;
                    }
                }
                return changed?.MoveToImmutable() ?? items;
            }
            ImmutableArray<T> EachMarker<T>(ImmutableArray<T> items) where T : Marker =>
                Each(items, m => markers.Contains(m.Id), m => (T)marker(m));
            UnitGroup EachUnit(UnitGroup group)
            {
                ImmutableArray<Unit> changedUnits = Each(group.Units, u => units.Contains(u.Name), unit);
                ImmutableArray<UnitGroup> changedGroups = Each(group.Groups, _ => true, EachUnit);
                return changedUnits == group.Units && changedGroups == group.Groups ? group : group with { Units = changedUnits, Groups = changedGroups };
            }

            if (props.Count > 0)
            {
                document = document with { Props = Each(document.Props, p => props.Contains(p.Id), prop) };
            }
            if (decals.Count > 0)
            {
                document = document with { Decals = Each(document.Decals, d => decals.Contains(d.Id), decal) };
            }
            if (markers.Count > 0 || areas.Count > 0)
            {
                MarkerSet set = document.Markers;
                document = document with
                {
                    Markers = set with
                    {
                        Spawns = EachMarker(set.Spawns),
                        Resources = EachMarker(set.Resources),
                        PathNodes = EachMarker(set.PathNodes),
                        AiMarkers = EachMarker(set.AiMarkers),
                        WeatherDefinitions = EachMarker(set.WeatherDefinitions),
                        WeatherGenerators = EachMarker(set.WeatherGenerators),
                        Areas = Each(set.Areas, a => areas.Contains(a.Name), area),
                    },
                };
            }
            if (units.Count > 0)
            {
                document = document with { Armies = Each(document.Armies, _ => true, army => EachUnit(army.Units) is UnitGroup changed && !ReferenceEquals(changed, army.Units) ? army with { Units = changed } : army) };            }
            return document;
        }

        /// <summary>
        /// A place at (x, z) as high above the terrain as <paramref name="from"/> was above it at its own place.
        /// </summary>
        /// <summary>
        /// Where a marker goes: a deposit (mass or hydrocarbon) on the centre of the ogrid nearest to
        /// the place, where the game puts deposits and where placing one puts it, as high above the
        /// terrain; any other marker at the place.
        /// </summary>
        private static Vector3 Deposited(MapDocument document, Marker marker, Vector3 place)
        {
            if (marker is not ResourceMarker)
            {
                return place;
            }
            // halfway between two centres it goes to the one nearer the map's middle, so that a deposit
            // and its mirror partner moved by images of one offset stay each other's images
            float x = OgridCentre(place.X, document.Width / 2f);
            float z = OgridCentre(place.Z, document.Height / 2f);
            return Ground(document.Terrain.Heightmap, place, x, z);
        }

        private static float OgridCentre(float value, float middle)
        {
            float below = MathF.Floor(value - 0.5f) + 0.5f;
            float above = below + 1;
            float toBelow = value - below;
            return toBelow < 0.5f - 1e-4f ? below
                : toBelow > 0.5f + 1e-4f ? above
                : MathF.Abs(below - middle) < MathF.Abs(above - middle) ? below : above;
        }

        private static Vector3 Ground(Heightmap heights, Vector3 from, float x, float z) =>
            new Vector3(x, heights.HeightAt(x, z) + from.Y - heights.HeightAt(from.X, from.Z), z);

        /// <summary>
        /// A prop turned about the up axis: its rotation matrix (rows <see cref="Prop.RotationX"/>,
        /// <see cref="Prop.RotationY"/>, <see cref="Prop.RotationZ"/>) multiplied from the left by the
        /// turn, which keeps any tilt it has.
        /// </summary>
        private static Prop TurnProp(Prop prop, float radians)
        {
            (float sin, float cos) = MathF.SinCos(radians);
            return prop with
            {
                RotationX = prop.RotationX * cos - prop.RotationZ * sin,
                RotationZ = prop.RotationX * sin + prop.RotationZ * cos,
            };
        }

        /// <summary>
        /// A decal turned about its centre until it faces <paramref name="heading"/>: its corner moves
        /// around the centre, which stays.
        /// </summary>
        private static Decal TurnDecal(Heightmap heights, Decal decal, float heading)
        {
            Vector3 centre = MapObjects.DecalCentre(decal);
            (Vector2 u, Vector2 v) = MapObjects.DecalEdges(decal.Scale, heading);
            Vector2 corner = new Vector2(centre.X, centre.Z) - (u + v) / 2;
            return decal with
            {
                Position = Ground(heights, decal.Position, corner.X, corner.Y),
                Rotation = decal.Rotation with { Y = heading },
            };
        }

        /// <summary>
        /// An angle between -pi and pi.
        /// </summary>
        private static float Wrap(float radians)
        {
            float wrapped = MathF.IEEERemainder(radians, 2 * MathF.PI);
            return wrapped <= -MathF.PI ? wrapped + 2 * MathF.PI : wrapped;
        }
    }

    /// <summary>
    /// Copies of things on a map (<see cref="Prop"/>, <see cref="Decal"/>, <see cref="Marker"/>,
    /// <see cref="PlacedUnit"/>, <see cref="Area"/>), to paste with <see cref="ObjectEdits.Paste"/>.
    /// </summary>
    /// <param name="Centre">The middle of their places (x, z), to paste them around another place.</param>
    public sealed record ObjectClipboard(ImmutableArray<object> Items, Vector2 Centre)
    {
        public bool IsEmpty => Items.IsEmpty;
    }
}
