using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Which way <see cref="DecalEdits.OrderDecals"/> moves decals in the map's list, whose order
    /// decides which lies on top: the last is drawn last.
    /// </summary>
    public enum DecalOrder
    {
        /// <summary>
        /// One place towards the top: each decal changes places with the decal above it.
        /// </summary>
        Forward,

        /// <summary>
        /// One place towards the bottom.
        /// </summary>
        Backward,

        /// <summary>
        /// On top of every other decal, keeping their order among each other.
        /// </summary>
        ToFront,

        /// <summary>
        /// Under every other decal.
        /// </summary>
        ToBack,
    }

    /// <summary>
    /// Edits of decals (user stories D-1, D-2): a new decal from a texture, its size, the distances
    /// at which it shows, its place in the map's order, and the decal groups. Sizes and places are in
    /// ogrids; a decal's <see cref="Decal.Position"/> is a corner of its rectangle, which
    /// <see cref="MapObjects.DecalCentre"/> turns into its middle.
    /// </summary>
    /// <remarks>
    /// A decal's <see cref="Decal.Scale"/> holds its width (x, along its own x axis) and its length
    /// (z); maps keep y the same as x, as the FAF map editor writes it, and these edits do too.
    /// </remarks>
    public static class DecalEdits
    {
        /// <summary>
        /// The distance beyond which a new decal is not drawn: the one maps use most (Adaptive
        /// Archsimkats Valley: 1306 of its 3634 decals; the others 200 to 1250).
        /// </summary>
        public const float DefaultCutOffLod = 500;

        /// <summary>
        /// The smallest side a decal is given, in ogrids.
        /// </summary>
        public const float SmallestSide = 0.1f;

        /// <summary>
        /// How a map names a decal's texture: the game's path with a slash before it
        /// (<c>/env/common/decals/erosion018_normals.dds</c>), from an asset's key (<c>env/common/decals/erosion018_normals.dds</c>).
        /// </summary>
        public static string TexturePath(string key) => key.StartsWith('/') ? key : "/" + key;

        /// <summary>
        /// What a decal with this texture draws: a normal map (<c>..._normals.dds</c>, the asset's
        /// category <c>normals</c>) changes the light, anything else is an albedo.
        /// </summary>
        public static DecalType TypeOf(string texture) =>
            Path.GetFileNameWithoutExtension(texture).EndsWith("_normals", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(texture).EndsWith("_normal", StringComparison.OrdinalIgnoreCase)
                ? DecalType.Normals
                : DecalType.Albedo;

        /// <summary>
        /// A new decal centred on a place, lying on the terrain: its texture in the first slot (the
        /// second empty, as maps write it), its type from the texture (<see cref="TypeOf"/>).
        /// </summary>
        /// <param name="texture">The game's path, or an asset's key (<see cref="TexturePath"/>).</param>
        /// <param name="width">Along the decal's own x axis, in ogrids.</param>
        /// <param name="length">Along its z axis.</param>
        /// <param name="heading">Its turn about the up axis in radians, from +x towards +z.</param>
        public static Decal NewDecal(Heightmap heights, string texture, float x, float z, float width, float length, float heading,
            float cutOffLod = DefaultCutOffLod, float nearCutOffLod = 0)
        {
            width = MathF.Max(width, SmallestSide);
            length = MathF.Max(length, SmallestSide);
            Vector3 scale = new Vector3(width, width, length);
            (Vector2 u, Vector2 v) = MapObjects.DecalEdges(scale, heading);
            Vector2 corner = new Vector2(x, z) - (u + v) / 2;
            string path = TexturePath(texture);
            return new Decal
            {
                Type = TypeOf(path),
                Textures = [path, ""],
                Scale = scale,
                Position = new Vector3(corner.X, heights.HeightAt(corner.X, corner.Y), corner.Y),
                Rotation = new Vector3(0, heading, 0),
                CutOffLod = cutOffLod,
                NearCutOffLod = nearCutOffLod,
                OwnerArmy = -1,
            };
        }

        /// <summary>
        /// Adds a decal on top of the others (<see cref="DocumentEdits.AddDecal"/>).
        /// </summary>
        /// <param name="added">The new decal, with its id.</param>
        public static MapDocument PlaceDecal(this MapDocument document, Decal decal, out ObjectRef added)
        {
            document = document.AddDecal(decal);
            added = ObjectRef.Decal(document.Decals[^1].Id);
            return document;
        }

        /// <summary>
        /// Scales each decal about its own centre: its width by <paramref name="x"/>, its length by
        /// <paramref name="z"/> (1 keeps a side). Things other than decals are left out.
        /// </summary>
        public static MapDocument ScaleDecals(this MapDocument document, IEnumerable<ObjectRef> things, float x, float z)
        {
            Heightmap heights = document.Terrain.Heightmap;
            return document.ChangeDecals(things, decal => Resized(heights, decal, decal.Scale.X * x, decal.Scale.Z * z));
        }

        /// <summary>
        /// Gives each decal a width or length (what is left out stays), about its own centre.
        /// </summary>
        public static MapDocument SizeDecals(this MapDocument document, IEnumerable<ObjectRef> things, float? width, float? length)
        {
            Heightmap heights = document.Terrain.Heightmap;
            return document.ChangeDecals(things, decal => Resized(heights, decal, width ?? decal.Scale.X, length ?? decal.Scale.Z));
        }

        /// <summary>
        /// Sets the distances at which the decals show (what is left out stays): beyond
        /// <paramref name="cutOffLod"/> and closer than <paramref name="nearCutOffLod"/> the game does not draw them.
        /// </summary>
        public static MapDocument SetDecalLods(this MapDocument document, IEnumerable<ObjectRef> things, float? cutOffLod, float? nearCutOffLod) =>
            document.ChangeDecals(things, decal => decal with
            {
                CutOffLod = cutOffLod ?? decal.CutOffLod,
                NearCutOffLod = nearCutOffLod ?? decal.NearCutOffLod,
            });

        /// <summary>
        /// Moves the decals in the map's list, which decides what lies on top (the last). The ones
        /// moved keep their order among each other; a decal already at the end it moves to stays.
        /// </summary>
        public static MapDocument OrderDecals(this MapDocument document, IEnumerable<ObjectRef> things, DecalOrder order)
        {
            HashSet<int> ids = [.. things.Where(thing => thing.Kind == ObjectKind.Decal).Select(thing => thing.Id)];
            ImmutableArray<Decal> decals = document.Decals;
            if (ids.Count == 0 || decals.IsEmpty)
            {
                return document;
            }
            Decal[] list = [.. decals];
            bool Moved(int i) => ids.Contains(list[i].Id);
            switch (order)
            {
                case DecalOrder.ToFront:
                    list = [.. list.Where(decal => !ids.Contains(decal.Id)), .. list.Where(decal => ids.Contains(decal.Id))];
                    break;
                case DecalOrder.ToBack:
                    list = [.. list.Where(decal => ids.Contains(decal.Id)), .. list.Where(decal => !ids.Contains(decal.Id))];
                    break;
                case DecalOrder.Forward:
                    // from the top down, so a block of moved decals goes up together
                    for (int i = list.Length - 2; i >= 0; i--)
                    {
                        if (Moved(i) && !Moved(i + 1))
                        {
                            (list[i], list[i + 1]) = (list[i + 1], list[i]);
                        }
                    }
                    break;
                default:
                    for (int i = 1; i < list.Length; i++)
                    {
                        if (Moved(i) && !Moved(i - 1))
                        {
                            (list[i], list[i - 1]) = (list[i - 1], list[i]);
                        }
                    }
                    break;
            }
            return list.SequenceEqual(decals) ? document : document with { Decals = [.. list] };
        }

        #region Groups

        /// <summary>
        /// An id for a new decal group: one more than the highest in use.
        /// </summary>
        public static int NextDecalGroupId(this MapDocument document) =>
            document.DecalGroups.IsEmpty ? 1 : document.DecalGroups.Max(group => group.Id) + 1;

        /// <summary>
        /// Adds a group of decals under a new id and a name no other group has
        /// (<see cref="DocumentEdits.FreeName"/>); things other than decals, and decals the map
        /// does not have, are left out.
        /// </summary>
        /// <param name="added">The new group.</param>
        public static MapDocument AddDecalGroup(this MapDocument document, string name, IEnumerable<ObjectRef> things, out DecalGroup added)
        {
            string wanted = string.IsNullOrWhiteSpace(name) ? "Group 1" : name.Trim();
            added = new DecalGroup
            {
                Id = document.NextDecalGroupId(),
                Name = DocumentEdits.FreeName(document.DecalGroups.Select(group => group.Name), wanted),
                DecalIds = DecalIdsOf(document, things),
            };
            return document with { DecalGroups = document.DecalGroups.Add(added) };
        }

        /// <exception cref="KeyNotFoundException">No group has the id.</exception>
        public static MapDocument RenameDecalGroup(this MapDocument document, int id, string name)
        {
            string trimmed = name.Trim();
            if (trimmed.Length == 0)
            {
                throw new ArgumentException("A decal group needs a name");
            }
            return document.UpdateDecalGroup(id, group => group with { Name = trimmed });
        }

        /// <summary>
        /// Removes a group; its decals stay on the map.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No group has the id.</exception>
        public static MapDocument RemoveDecalGroup(this MapDocument document, int id)
        {
            _ = document.FindDecalGroup(id) ?? throw new KeyNotFoundException($"The map has no decal group {id}");
            return document with { DecalGroups = [.. document.DecalGroups.Where(group => group.Id != id)] };
        }

        /// <summary>
        /// Adds decals to a group, after the ones it holds; those it holds already stay where they are.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No group has the id.</exception>
        public static MapDocument AddToDecalGroup(this MapDocument document, int id, IEnumerable<ObjectRef> things)
        {
            ImmutableArray<int> added = DecalIdsOf(document, things);
            return document.UpdateDecalGroup(id, group => group with { DecalIds = [.. group.DecalIds, .. added.Where(decal => !group.DecalIds.Contains(decal))] });
        }

        /// <summary>
        /// Takes decals out of a group; they stay on the map.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No group has the id.</exception>
        public static MapDocument RemoveFromDecalGroup(this MapDocument document, int id, IEnumerable<ObjectRef> things)
        {
            HashSet<int> removed = [.. things.Where(thing => thing.Kind == ObjectKind.Decal).Select(thing => thing.Id)];
            return document.UpdateDecalGroup(id, group => group with { DecalIds = [.. group.DecalIds.Where(decal => !removed.Contains(decal))] });
        }

        public static DecalGroup? FindDecalGroup(this MapDocument document, int id) => document.DecalGroups.FirstOrDefault(group => group.Id == id);

        /// <summary>
        /// The decals of a group that the map has, in the group's order.
        /// </summary>
        public static IEnumerable<ObjectRef> DecalsOf(this MapDocument document, DecalGroup group) =>
            group.DecalIds.Select(ObjectRef.Decal).Where(document.Exists);

        private static MapDocument UpdateDecalGroup(this MapDocument document, int id, Func<DecalGroup, DecalGroup> update) =>
            document with { DecalGroups = document.DecalGroups.Update(group => group.Id == id, update, $"decal group {id}") };

        private static ImmutableArray<int> DecalIdsOf(MapDocument document, IEnumerable<ObjectRef> things) =>
            [.. things.Where(thing => thing.Kind == ObjectKind.Decal && document.Exists(thing)).Select(thing => thing.Id).Distinct()];

        #endregion

        /// <summary>
        /// The document with each named decal changed; other things are left out.
        /// </summary>
        private static MapDocument ChangeDecals(this MapDocument document, IEnumerable<ObjectRef> things, Func<Decal, Decal> change)
        {
            HashSet<int> ids = [.. things.Where(thing => thing.Kind == ObjectKind.Decal).Select(thing => thing.Id)];
            if (ids.Count == 0)
            {
                return document;
            }
            ImmutableArray<Decal>.Builder? changed = null;
            ImmutableArray<Decal> decals = document.Decals;
            for (int i = 0; i < decals.Length; i++)
            {
                if (ids.Contains(decals[i].Id) && change(decals[i]) is Decal result && result != decals[i])
                {
                    changed ??= decals.ToBuilder();
                    changed[i] = result;
                }
            }
            return changed is null ? document : document with { Decals = changed.MoveToImmutable() };
        }

        /// <summary>
        /// A decal with another width and length, about its centre, as high above the terrain at its
        /// new corner as it was at the old one.
        /// </summary>
        private static Decal Resized(Heightmap heights, Decal decal, float width, float length)
        {
            width = MathF.Max(width, SmallestSide);
            length = MathF.Max(length, SmallestSide);
            Vector3 centre = MapObjects.DecalCentre(decal);
            Vector3 scale = new Vector3(width, width, length);
            (Vector2 u, Vector2 v) = MapObjects.DecalEdges(scale, decal.Rotation.Y);
            Vector2 corner = new Vector2(centre.X, centre.Z) - (u + v) / 2;
            float above = decal.Position.Y - heights.HeightAt(decal.Position.X, decal.Position.Z);
            return decal with
            {
                Scale = scale,
                Position = new Vector3(corner.X, heights.HeightAt(corner.X, corner.Y) + above, corner.Y),
            };
        }
    }
}
