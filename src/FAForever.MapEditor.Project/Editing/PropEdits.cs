using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Edits of many props at once, named by id: tilting them with the terrain, standing them up,
    /// setting them on the ground, scaling, replacing their blueprints; adding and removing many in
    /// one pass; and setting the props back on the ground after the terrain under them changed
    /// (user story P-1).
    /// </summary>
    /// <remarks>
    /// Every edit keeps a prop's heading (<see cref="PropOrientation"/>). An id that names no prop
    /// is left out. Each changes the props in one pass over the map's props, so a selection of
    /// thousands costs no more than one.
    /// </remarks>
    public static class PropEdits
    {
        /// <summary>
        /// Tilts each prop so that its up follows the terrain's normal under it.
        /// </summary>
        public static MapDocument AlignPropsToTerrain(this MapDocument document, IEnumerable<int> ids)
        {
            Heightmap heights = document.Terrain.Heightmap;
            return document.ChangeProps(ids, prop => PropOrientation.AlignedTo(prop, PropOrientation.TerrainNormal(heights, prop.Position.X, prop.Position.Z)));
        }

        /// <summary>
        /// Stands each prop straight up.
        /// </summary>
        public static MapDocument StandPropsUpright(this MapDocument document, IEnumerable<int> ids) =>
            document.ChangeProps(ids, PropOrientation.Upright);

        /// <summary>
        /// Puts each prop on the terrain, <paramref name="sink"/> game units into it (a rock that
        /// sits partly in the soil); a negative sink lifts it.
        /// </summary>
        public static MapDocument DropPropsToGround(this MapDocument document, IEnumerable<int> ids, float sink = 0)
        {
            Heightmap heights = document.Terrain.Heightmap;
            return document.ChangeProps(ids, prop => prop with { Position = prop.Position with { Y = heights.HeightAt(prop.Position.X, prop.Position.Z) - sink } });
        }

        /// <summary>
        /// Gives each prop a pitch, a roll or both, in radians (what is left out stays as each has it).
        /// </summary>
        public static MapDocument SetPropTilt(this MapDocument document, IEnumerable<int> ids, float? pitch, float? roll) =>
            document.ChangeProps(ids, prop =>
            {
                (float p, float r) = PropOrientation.Tilt(prop);
                return PropOrientation.WithTilt(prop, pitch ?? p, roll ?? r);
            });

        /// <summary>
        /// Scales each prop the same along every axis.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The scale is not above 0.</exception>
        public static MapDocument SetPropScale(this MapDocument document, IEnumerable<int> ids, float scale)
        {
            if (!(scale > 0) || !float.IsFinite(scale))
            {
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "A scale is above 0");
            }
            return document.ChangeProps(ids, prop => prop with { Scale = new Vector3(scale) });
        }

        /// <summary>
        /// Gives each prop another blueprint: the one <paramref name="blueprint"/> answers for it
        /// (such as another of its family's variants); the place, turn and scale stay.
        /// </summary>
        public static MapDocument ReplacePropBlueprints(this MapDocument document, IEnumerable<int> ids, Func<Prop, string> blueprint) =>
            document.ChangeProps(ids, prop => blueprint(prop) is string replaced && replaced != prop.Blueprint ? prop with { Blueprint = replaced } : prop);

        /// <summary>
        /// Changes each prop by a function of its own, such as a random turn and scale.
        /// </summary>
        public static MapDocument ChangeProps(this MapDocument document, IEnumerable<int> ids, Func<Prop, Prop> change)
        {
            HashSet<int> chosen = [.. ids];
            if (chosen.Count == 0)
            {
                return document;
            }
            ImmutableArray<Prop> props = document.Props;
            ImmutableArray<Prop>.Builder? changed = null;
            for (int i = 0; i < props.Length; i++)
            {
                if (chosen.Contains(props[i].Id) && change(props[i]) is Prop result && result != props[i])
                {
                    changed ??= props.ToBuilder();
                    changed[i] = result;
                }
            }
            return changed is null ? document : document with { Props = changed.MoveToImmutable() };
        }

        /// <summary>
        /// Adds many props in one pass, each under a new id (the ids they carry are not used).
        /// </summary>
        /// <param name="ids">The ids they were given, in their order.</param>
        public static MapDocument AddProps(this MapDocument document, IReadOnlyList<Prop> props, out ImmutableArray<int> ids)
        {
            if (props.Count == 0)
            {
                ids = [];
                return document;
            }
            int next = document.NextPropId();
            ImmutableArray<int>.Builder given = ImmutableArray.CreateBuilder<int>(props.Count);
            ImmutableArray<Prop>.Builder all = ImmutableArray.CreateBuilder<Prop>(document.Props.Length + props.Count);
            all.AddRange(document.Props);
            foreach (Prop prop in props)
            {
                given.Add(next);
                all.Add(prop with { Id = next++ });
            }
            ids = given.MoveToImmutable();
            return document with { Props = all.MoveToImmutable() };
        }

        /// <summary>
        /// Removes the props with these ids, in one pass.
        /// </summary>
        public static MapDocument RemoveProps(this MapDocument document, IReadOnlyCollection<int> ids)
        {
            if (ids.Count == 0)
            {
                return document;
            }
            HashSet<int> removed = ids as HashSet<int> ?? [.. ids];
            ImmutableArray<Prop> kept = [.. document.Props.Where(prop => !removed.Contains(prop.Id))];
            return kept.Length == document.Props.Length ? document : document with { Props = kept };
        }

        /// <summary>
        /// Sets the props back on the terrain where its heights changed since <paramref name="before"/>
        /// (such as after a stroke of the sculpt brush): each prop on a changed part lies as high
        /// above or below the terrain as it did. Only the props on the changed tiles of the heightmap
        /// are looked at closely, so a stroke on a map of thousands of props stays cheap.
        /// </summary>
        /// <param name="moved">How many props moved.</param>
        public static MapDocument PropsFollowTerrain(this MapDocument document, Heightmap before, out int moved)
        {
            moved = 0;
            Heightmap after = document.Terrain.Heightmap;
            if (ReferenceEquals(after.Samples, before.Samples) || document.Props.IsEmpty
                || after.Samples.Width != before.Samples.Width || after.Samples.Height != before.Samples.Height)
            {
                return document;
            }
            HashSet<(int X, int Y)> tiles = [.. after.Samples.ChangedTiles(before.Samples)];
            if (tiles.Count == 0)
            {
                return document;
            }
            const int size = TiledBuffer<ushort>.TileSize;
            bool Touched(float x, float z)
            {
                // a place reads the four corners around it: a corner on the next tile counts as well
                int x0 = Math.Clamp((int)x, 0, after.Samples.Width - 1);
                int z0 = Math.Clamp((int)z, 0, after.Samples.Height - 1);
                int x1 = Math.Min(x0 + 1, after.Samples.Width - 1);
                int z1 = Math.Min(z0 + 1, after.Samples.Height - 1);
                return tiles.Contains((x0 / size, z0 / size)) || tiles.Contains((x1 / size, z0 / size))
                    || tiles.Contains((x0 / size, z1 / size)) || tiles.Contains((x1 / size, z1 / size));
            }
            ImmutableArray<Prop> props = document.Props;
            ImmutableArray<Prop>.Builder? changed = null;
            for (int i = 0; i < props.Length; i++)
            {
                Vector3 position = props[i].Position;
                if (!Touched(position.X, position.Z))
                {
                    continue;
                }
                float rise = after.HeightAt(position.X, position.Z) - before.HeightAt(position.X, position.Z);
                if (rise == 0)
                {
                    continue;
                }
                changed ??= props.ToBuilder();
                changed[i] = props[i] with { Position = position with { Y = position.Y + rise } };
                moved++;
            }
            return changed is null ? document : document with { Props = changed.MoveToImmutable() };
        }
    }
}
