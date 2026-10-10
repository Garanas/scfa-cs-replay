using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What a selection preset holds (user story F-10): things of several kinds at once, such as a
    /// civilian base with its units, props, decals and markers, as a project writes them, each
    /// placed relative to the preset's middle (x and z in ogrids) with its height above the terrain
    /// under it (y). Kinds the selection had none of are null.
    /// </summary>
    /// <param name="Units">The units in the groups they were in below the deepest group holding them
    /// all (<see cref="UnitGroupEdits.CaptureUnits"/>); placed as a new group named after the preset.</param>
    /// <param name="Count">How many things it holds.</param>
    public sealed record SelectionPresetSettings(
        IReadOnlyList<PropDto>? Props,
        IReadOnlyList<DecalDto>? Decals,
        MarkerSetDto? Markers,
        UnitGroupDto? Units,
        int Count)
    {
        /// <summary>
        /// How many things of each kind, in words: <c>3 units, 12 props, 2 decals</c>.
        /// </summary>
        public string Summary()
        {
            List<string> parts = [];
            void Add(int count, string one, string many)
            {
                if (count > 0)
                {
                    parts.Add(count == 1 ? $"1 {one}" : $"{count} {many}");
                }
            }
            Add(Units is null ? 0 : CountUnits(Units), "unit", "units");
            Add(Props?.Count ?? 0, "prop", "props");
            Add(Decals?.Count ?? 0, "decal", "decals");
            Add(Markers is null ? 0 : MapProjectMapping.ToModel(Markers).All.Count(), "marker", "markers");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        private static int CountUnits(UnitGroupDto group) => group.Units.Count + group.Groups.Sum(CountUnits);
    }

    /// <summary>
    /// Selection presets (kind <c>selection</c>): the selected things of every kind but areas, saved
    /// around their middle and placed again elsewhere, on this map or another, turned about the
    /// place. Markers keep their links and chains (<see cref="MarkerPresets"/>), units go into a new
    /// group of an army (<see cref="UnitGroupEdits.PlaceUnits"/>). Areas are part of a map's layout,
    /// not of a piece placed again, and are left out.
    /// </summary>
    public static class SelectionPresets
    {
        public const string Kind = "selection";

        /// <summary>
        /// The things as a preset, around their middle (rounded to a whole ogrid, so structures stay
        /// on the build grid wherever the preset is placed).
        /// </summary>
        /// <param name="name">The name of the units' group when placed (the preset's).</param>
        /// <exception cref="ArgumentException">None of the things is on the map, or only areas.</exception>
        public static SelectionPresetSettings Capture(MapDocument document, IEnumerable<ObjectRef> things, string name)
        {
            ObjectRef[] chosen = [.. things.Where(thing => thing.Kind != ObjectKind.Area && document.Find(thing) is not null).Distinct()];
            if (chosen.Length == 0)
            {
                throw new ArgumentException("Nothing to save: select units, props, decals or markers first (areas are left out)");
            }
            Vector3[] places = [.. chosen.Select(document.PlaceOf).OfType<Vector3>()];
            Vector2 middle = new Vector2(MathF.Round(places.Average(p => p.X)), MathF.Round(places.Average(p => p.Z)));
            Heightmap heights = document.Terrain.Heightmap;
            Vector3 Relative(Vector3 p) => new Vector3(p.X - middle.X, p.Y - heights.HeightAt(p.X, p.Z), p.Z - middle.Y);

            HashSet<int> propIds = [.. chosen.Where(t => t.Kind == ObjectKind.Prop).Select(t => t.Id)];
            HashSet<int> decalIds = [.. chosen.Where(t => t.Kind == ObjectKind.Decal).Select(t => t.Id)];
            int[] markerIds = [.. chosen.Where(t => t.Kind == ObjectKind.Marker).Select(t => t.Id)];
            string[] unitNames = [.. chosen.Where(t => t.Kind == ObjectKind.Unit).Select(t => t.Name!)];

            PropDto[] props = [.. document.Props.Where(p => propIds.Contains(p.Id)).Select(p => MapProjectMapping.ToDto(p with { Position = Relative(p.Position), Rule = null }))];
            DecalDto[] decals = [.. document.Decals.Where(d => decalIds.Contains(d.Id)).Select(d => MapProjectMapping.ToDto(d with { Position = Relative(d.Position) }))];
            MarkerSetDto? markers = markerIds.Length == 0 ? null : MarkerPresets.Capture(document, markerIds, middle).Markers;
            UnitGroupDto? units = unitNames.Length == 0 ? null : MapProjectMapping.ToDto(document.CaptureUnits(unitNames, name, middle));
            return new SelectionPresetSettings(
                props.Length == 0 ? null : props,
                decals.Length == 0 ? null : decals,
                markers,
                units,
                chosen.Length);
        }

        /// <summary>
        /// Places a preset with its middle at a place, turned about it: props and decals as high
        /// above the terrain as they were saved (those that would lie outside the map are left out),
        /// markers under free names with their links and chains (kept inside the map), units as a new
        /// group of <paramref name="army"/> in the group <paramref name="parent"/> (made when missing),
        /// named after <paramref name="name"/>. Deposits go to ogrid centres as placing them does.
        /// </summary>
        /// <param name="radians">The turn, clockwise in the top view.</param>
        /// <param name="added">What was placed: props, decals, markers, then units.</param>
        /// <exception cref="ArgumentException">The preset holds units and the army is null or not the map's.</exception>
        public static MapDocument Place(MapDocument document, SelectionPresetSettings settings, string name, string? army, IReadOnlyList<string> parent,
            float x, float z, float radians, out ImmutableArray<ObjectRef> added)
        {
            Heightmap heights = document.Terrain.Heightmap;
            ImmutableArray<ObjectRef>.Builder refs = ImmutableArray.CreateBuilder<ObjectRef>();
            bool Inside(float px, float pz) => px >= 0 && pz >= 0 && px <= document.Width && pz <= document.Height;
            Vector3 Absolute(Vector3Dto p) => new Vector3(x + p.X, heights.HeightAt(x + p.X, z + p.Z) + p.Y, z + p.Z);

            foreach (PropDto dto in settings.Props ?? [])
            {
                if (!Inside(x + dto.Position.X, z + dto.Position.Z))
                {
                    continue;
                }
                document = document.AddProp(MapProjectMapping.ToModel(dto, 0) with { Position = Absolute(dto.Position), Rule = null });
                refs.Add(ObjectRef.Prop(document.Props[^1].Id));
            }
            foreach (DecalDto dto in settings.Decals ?? [])
            {
                Decal decal = MapProjectMapping.ToModel(dto) with { Position = Absolute(dto.Position) };
                Vector3 centre = MapObjects.DecalCentre(decal);
                if (!Inside(centre.X, centre.Z))
                {
                    continue;
                }
                document = document.AddDecal(decal);
                refs.Add(ObjectRef.Decal(document.Decals[^1].Id));
            }
            if (settings.Markers is MarkerSetDto markers)
            {
                document = MarkerPresets.Place(document, new MarkerPresetSettings(markers, 0), x, z, out ImmutableArray<ObjectRef> placedMarkers);
                refs.AddRange(placedMarkers);
            }
            if (settings.Units is UnitGroupDto units && (units.Units.Count > 0 || units.Groups.Count > 0))
            {
                if (army is null || !document.Armies.Any(a => a.Name == army))
                {
                    throw new ArgumentException(army is null ? "The preset has units: the map needs an army to place them for" : $"The map has no army {army}");
                }
                for (int depth = 1; depth <= parent.Count; depth++)
                {
                    if (document.FindGroup(army, [.. parent.Take(depth)]) is null)
                    {
                        document = document.AddGroup(army, [.. parent.Take(depth - 1)], parent[depth - 1], out _);
                    }
                }
                UnitGroup template = MapProjectMapping.ToModel(units) with { Name = name.Replace('/', '-') };
                document = document.PlaceUnits(template, army, parent, x, z, 0, out ImmutableArray<string> names, out _);
                refs.AddRange(names.Select(ObjectRef.Unit));
            }
            added = refs.ToImmutable();
            if (radians != 0 && !added.IsEmpty)
            {
                document = document.RotateObjectsAbout(added, radians, new Vector2(x, z));
            }
            return document;
        }
    }
}
