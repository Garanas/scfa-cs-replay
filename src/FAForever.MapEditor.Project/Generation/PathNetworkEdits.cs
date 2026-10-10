using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// A generated network with the edits by hand applied (<see cref="PathNetworkEdits.Apply(PathNetwork, PathNodeSettings)"/>).
    /// </summary>
    /// <param name="Unmatched">The edits that found no generated node (a move whose node is gone was
    /// placed as an added node, and is listed too).</param>
    public sealed record EditedPathNetwork(PathNetwork Network, IReadOnlyList<PathEdit> Unmatched);

    /// <summary>
    /// Edits by hand on top of the generated path networks (user story R-3; the owner's decision:
    /// the generated network is the base, the edits go on top and are applied again after every
    /// generation, so a change to the terrain never throws them away).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Matching.</b> An edit refers to a generated node by the place it was generated at
    /// (<see cref="PathNodeRef"/>). After a generation every such place is matched to the nearest
    /// generated node of its layer within <see cref="Tolerance"/> (a third of the spacing, at least 2
    /// ogrids), nearest pairs first and each node once. The generation is deterministic, so where the
    /// terrain did not change the nodes come back exactly where they were; where it changed a little
    /// they move a little and are still found. A node added by hand is referred to by its number, and
    /// always found.
    /// </para>
    /// <para>
    /// <b>An edit whose node is gone</b> (the terrain or the spacing changed so much that no generated
    /// node lies near): a removal does nothing; a move places a node of its own where the node was
    /// moved to, linked as an added node is; a link or cut with an end that is gone does nothing.
    /// Such edits stay in the settings (they may match again after the next change) and are listed
    /// as unmatched, so the person can forget them (<see cref="ForgetUnmatched"/>).
    /// </para>
    /// <para>
    /// <b>Order.</b> Removals, then moves, then added nodes (each linked to up to three of the nearest
    /// nodes of its layer it sees within one and a half spacings, at least the nearest it sees), then
    /// cuts, then links.
    /// </para>
    /// </remarks>
    public static class PathNetworkEdits
    {
        /// <summary>
        /// How far a generated node may lie from the place an edit remembers and still be its node.
        /// </summary>
        public static float Tolerance(float spacing) => Math.Max(2, spacing / 3);

        /// <summary>
        /// The networks with the edits of the settings applied, layer by layer.
        /// </summary>
        public static EditedPathNetwork Apply(PathNetwork network, PathNodeSettings settings)
        {
            if (settings.Edits.IsEmpty)
            {
                return new EditedPathNetwork(network, []);
            }
            List<PathEdit> unmatched = [];
            ImmutableArray<PathLayerNetwork> layers = [.. network.Layers.Select(layer =>
            {
                NavGrid? grid = network.Navigation is MapNavigation navigation && PathNetworks.NavLayerOf(layer.Layer) is NavLayer nav ? navigation[nav] : null;
                (PathLayerNetwork edited, IReadOnlyList<PathEdit> missed) = Apply(layer, [.. settings.Edits.Where(edit => edit.Layer == layer.Layer)], grid);
                unmatched.AddRange(missed);
                return edited;
            })];
            // edits of layers not generated match nothing
            unmatched.AddRange(settings.Edits.Where(edit => network[edit.Layer] is null));
            return new EditedPathNetwork(network with { Layers = layers }, unmatched);
        }

        /// <summary>
        /// One layer's network with its edits applied.
        /// </summary>
        /// <param name="grid">The layer of the navigational mesh: added nodes link only to nodes they
        /// see on it; null for air, where every node sees every other.</param>
        public static (PathLayerNetwork Network, IReadOnlyList<PathEdit> Unmatched) Apply(PathLayerNetwork network, IReadOnlyList<PathEdit> edits, NavGrid? grid)
        {
            if (edits.Count == 0)
            {
                return (network, []);
            }
            List<GeneratedPathNode> nodes = [.. network.Nodes];
            HashSet<(int, int)> links = [.. network.Links.Select(link => (Math.Min(link.A, link.B), Math.Max(link.A, link.B)))];
            HashSet<int> removed = [];
            List<PathEdit> unmatched = [];
            Dictionary<PathNodeRef, int> found = Match(network, edits);
            foreach ((PathNodeRef reference, int index) in found)
            {
                // the node goes by the place the edits remember, so a later edit of it is the same node's
                nodes[index] = nodes[index] with { Ref = reference };
            }

            int? Resolve(PathNodeRef reference) => found.TryGetValue(reference, out int index) && !removed.Contains(index) ? index : null;

            foreach (PathEdit edit in edits.Where(edit => edit.Kind == PathEditKind.Remove))
            {
                if (Resolve(edit.Node) is int index)
                {
                    removed.Add(index);
                }
                else
                {
                    unmatched.Add(edit);
                }
            }
            List<int> added = [];
            foreach (PathEdit edit in edits.Where(edit => edit.Kind == PathEditKind.Move))
            {
                if (Resolve(edit.Node) is int index)
                {
                    nodes[index] = nodes[index] with { X = edit.ToX, Z = edit.ToZ };
                }
                else if (!edit.Node.IsAdded && !found.ContainsKey(edit.Node))
                {
                    // the node is gone: one of its own where it was moved to
                    found[edit.Node] = nodes.Count;
                    added.Add(nodes.Count);
                    nodes.Add(new GeneratedPathNode(edit.ToX, edit.ToZ, PathNodeRole.Added) { Ref = edit.Node });
                    unmatched.Add(edit);
                }
            }
            foreach (PathEdit edit in edits.Where(edit => edit.Kind == PathEditKind.Add && edit.Node.IsAdded))
            {
                found[edit.Node] = nodes.Count;
                added.Add(nodes.Count);
                nodes.Add(new GeneratedPathNode(edit.Node.X, edit.Node.Z, PathNodeRole.Added) { Ref = edit.Node });
            }
            foreach (int node in added)
            {
                foreach (int other in Nearest(nodes, removed, node, network.Spacing, grid))
                {
                    links.Add((Math.Min(node, other), Math.Max(node, other)));
                }
            }
            foreach (PathEdit edit in edits.Where(edit => edit.Kind == PathEditKind.Cut))
            {
                if (Resolve(edit.Node) is int a && Resolve(edit.Other) is int b)
                {
                    links.Remove((Math.Min(a, b), Math.Max(a, b)));
                }
                else
                {
                    unmatched.Add(edit);
                }
            }
            foreach (PathEdit edit in edits.Where(edit => edit.Kind == PathEditKind.Link))
            {
                if (Resolve(edit.Node) is int a && Resolve(edit.Other) is int b && a != b)
                {
                    links.Add((Math.Min(a, b), Math.Max(a, b)));
                }
                else
                {
                    unmatched.Add(edit);
                }
            }

            int[] numbers = new int[nodes.Count];
            int next = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                numbers[i] = removed.Contains(i) ? -1 : next++;
            }
            ImmutableArray<GeneratedPathNode> kept = [.. nodes.Where((_, i) => numbers[i] >= 0)];
            ImmutableArray<(int A, int B)> keptLinks = [.. links.Where(link => numbers[link.Item1] >= 0 && numbers[link.Item2] >= 0)
                .Select(link => (numbers[link.Item1], numbers[link.Item2])).Order()];
            return (network with { Nodes = kept, Links = keptLinks }, unmatched);
        }

        /// <summary>
        /// The generated node each place the edits remember stands for: nearest pairs within the
        /// tolerance first, each node once.
        /// </summary>
        private static Dictionary<PathNodeRef, int> Match(PathLayerNetwork network, IReadOnlyList<PathEdit> edits)
        {
            HashSet<PathNodeRef> references = [];
            foreach (PathEdit edit in edits)
            {
                if (edit.Kind != PathEditKind.Add && !edit.Node.IsAdded)
                {
                    references.Add(edit.Node);
                }
                if (edit.Kind is PathEditKind.Link or PathEditKind.Cut && !edit.Other.IsAdded)
                {
                    references.Add(edit.Other);
                }
            }
            float tolerance = Tolerance(network.Spacing);
            List<(float Distance, PathNodeRef Reference, int Node)> pairs = [];
            foreach (PathNodeRef reference in references)
            {
                for (int i = 0; i < network.Nodes.Length; i++)
                {
                    float distance = Vector2.Distance(new Vector2(reference.X, reference.Z), network.Nodes[i].Place);
                    if (distance <= tolerance)
                    {
                        pairs.Add((distance, reference, i));
                    }
                }
            }
            Dictionary<PathNodeRef, int> found = [];
            HashSet<int> taken = [];
            foreach ((_, PathNodeRef reference, int node) in pairs.OrderBy(pair => pair.Distance).ThenBy(pair => pair.Node))
            {
                if (!found.ContainsKey(reference) && taken.Add(node))
                {
                    found[reference] = node;
                }
            }
            return found;
        }

        /// <summary>
        /// The nodes an added node links to: up to three of the nearest it sees within one and a half
        /// spacings, and at least the nearest it sees.
        /// </summary>
        private static IEnumerable<int> Nearest(List<GeneratedPathNode> nodes, HashSet<int> removed, int node, float spacing, NavGrid? grid)
        {
            Vector2 from = nodes[node].Place;
            List<(float Distance, int Node)> seen = [];
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i == node || removed.Contains(i))
                {
                    continue;
                }
                seen.Add((Vector2.Distance(from, nodes[i].Place), i));
            }
            int linked = 0;
            float reach = spacing * 1.5f;
            foreach ((float distance, int other) in seen.OrderBy(pair => pair.Distance).ThenBy(pair => pair.Node))
            {
                if (linked >= 3 || (linked > 0 && distance > reach))
                {
                    yield break;
                }
                if (grid is null || (PathNetworks.CanWalk(grid, from, nodes[other].Place) && PathNetworks.CanWalk(grid, nodes[other].Place, from)))
                {
                    linked++;
                    yield return other;
                }
            }
        }

        #region Making edits

        /// <summary>
        /// Adds a node by hand at a place.
        /// </summary>
        public static PathNodeSettings AddNode(PathNodeSettings settings, PathLayer layer, float x, float z)
        {
            int number = settings.Edits.Where(edit => edit.Kind == PathEditKind.Add).Select(edit => edit.Node.Added).DefaultIfEmpty(0).Max() + 1;
            return settings with { Edits = settings.Edits.Add(new PathEdit(PathEditKind.Add, layer, new PathNodeRef(x, z, number))) };
        }

        /// <summary>
        /// Moves a node: an added one gets the new place in its edit; a generated one gets one move
        /// (an earlier move of it is replaced).
        /// </summary>
        public static PathNodeSettings MoveNode(PathNodeSettings settings, PathLayer layer, PathNodeRef node, float x, float z)
        {
            if (node.IsAdded)
            {
                return settings with
                {
                    Edits = [.. settings.Edits.Select(edit => edit.Kind == PathEditKind.Add && edit.Layer == layer && edit.Node.Added == node.Added
                        ? edit with { Node = edit.Node with { X = x, Z = z } }
                        : edit)],
                };
            }
            return settings with
            {
                Edits = [.. settings.Edits.Where(edit => !(edit.Kind == PathEditKind.Move && edit.Layer == layer && edit.Node == node)),
                    new PathEdit(PathEditKind.Move, layer, node) { ToX = x, ToZ = z }],
            };
        }

        /// <summary>
        /// Removes a node: an added one with every edit that refers to it; a generated one with a
        /// removal, after the edits that refer to it are dropped.
        /// </summary>
        public static PathNodeSettings RemoveNode(PathNodeSettings settings, PathLayer layer, PathNodeRef node)
        {
            ImmutableArray<PathEdit> kept = [.. settings.Edits.Where(edit => !(edit.Layer == layer && (edit.Refers(node) || (node.IsAdded && edit.Kind == PathEditKind.Add && edit.Node.Added == node.Added))))];
            return settings with { Edits = node.IsAdded ? kept : kept.Add(new PathEdit(PathEditKind.Remove, layer, node)) };
        }

        /// <summary>
        /// Links two nodes, or cuts their link: an edit of the other kind for the pair is taken away
        /// instead when there is one.
        /// </summary>
        public static PathNodeSettings Link(PathNodeSettings settings, PathLayer layer, PathNodeRef a, PathNodeRef b, bool cut)
        {
            bool Pair(PathEdit edit, PathEditKind kind) =>
                edit.Kind == kind && edit.Layer == layer && ((edit.Node == a && edit.Other == b) || (edit.Node == b && edit.Other == a));
            PathEditKind opposite = cut ? PathEditKind.Link : PathEditKind.Cut;
            if (settings.Edits.Any(edit => Pair(edit, opposite)))
            {
                return settings with { Edits = [.. settings.Edits.Where(edit => !Pair(edit, opposite))] };
            }
            PathEditKind kind = cut ? PathEditKind.Cut : PathEditKind.Link;
            return settings.Edits.Any(edit => Pair(edit, kind))
                ? settings
                : settings with { Edits = settings.Edits.Add(new PathEdit(kind, layer, a) { Other = b }) };
        }

        /// <summary>
        /// Forgets the edits of some layers (all when null).
        /// </summary>
        public static PathNodeSettings Clear(PathNodeSettings settings, PathLayer? layer = null) =>
            settings with { Edits = layer is null ? [] : [.. settings.Edits.Where(edit => edit.Layer != layer)] };

        /// <summary>
        /// Forgets the edits that found no generated node.
        /// </summary>
        public static PathNodeSettings ForgetUnmatched(PathNodeSettings settings, IReadOnlyList<PathEdit> unmatched) =>
            settings with { Edits = [.. settings.Edits.Where(edit => !unmatched.Contains(edit))] };

        #endregion
    }
}
