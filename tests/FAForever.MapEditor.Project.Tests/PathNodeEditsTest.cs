using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The skeleton of test grids, and the edits by hand on top of generated path networks.
    /// </summary>
    [TestClass]
    public class PathNodeEditsTest
    {
        /// <summary>
        /// A grid from rows of text: '#' blocked, a digit a label, '.' label 1.
        /// </summary>
        private static (int[] Labels, int Width, int Height) Grid(params string[] rows)
        {
            int width = rows[0].Length;
            int[] labels = new int[width * rows.Length];
            for (int z = 0; z < rows.Length; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    char c = rows[z][x];
                    labels[z * width + x] = c == '#' ? 0 : c == '.' ? 1 : c - '0';
                }
            }
            return (labels, width, rows.Length);
        }

        private static (int[] Labels, int Width, int Height) Corridor(int length, int thickness, int pad = 2)
        {
            int width = length + pad * 2;
            int height = thickness + pad * 2;
            int[] labels = new int[width * height];
            for (int z = pad; z < pad + thickness; z++)
            {
                for (int x = pad; x < pad + length; x++)
                {
                    labels[z * width + x] = 1;
                }
            }
            return (labels, width, height);
        }

        [TestMethod]
        public void ThinsACorridorToOneLineAlongItsMiddle()
        {
            (int[] labels, int width, int height) = Corridor(40, 7);

            bool[] skeleton = PathSkeleton.Thin(labels, width, height);

            int[] cells = [.. Enumerable.Range(0, skeleton.Length).Where(i => skeleton[i])];
            Assert.IsTrue(cells.Length >= 20, $"a line, {cells.Length} cells");
            // one cell wide, mostly on the middle row (2 + 3)
            Assert.IsTrue(cells.Count(i => i / width == 5) >= cells.Length * 0.8, "along the middle");
            Assert.AreEqual(cells.Length, cells.Select(i => i % width).Distinct().Count(), "one cell per column");
            SkeletonGraph graph = PathSkeleton.Trace(skeleton, labels, width, height);
            Assert.AreEqual(2, graph.Nodes.Count(node => node.End), "two dead ends");
            Assert.AreEqual(1, graph.Chains.Count);
        }

        [TestMethod]
        public void KeepsTheHoleOfARingAndJoinsTheArmsOfACross()
        {
            (int[] ring, int rw, int rh) = Grid(
                "############",
                "#..........#",
                "#..........#",
                "#...####...#",
                "#...####...#",
                "#...####...#",
                "#..........#",
                "#..........#",
                "############");
            bool[] ringSkeleton = PathSkeleton.Thin(ring, rw, rh);
            PathSkeleton.Prune(ringSkeleton, ring, rw, rh, 3);
            SkeletonGraph ringGraph = PathSkeleton.Trace(ringSkeleton, ring, rw, rh);
            Assert.AreEqual(0, ringGraph.Nodes.Count(node => node.End), "a ring has no dead end|" + string.Join("|", Enumerable.Range(0, rh).Select(z => new string(Enumerable.Range(0, rw).Select(x => ringSkeleton[z * rw + x] ? (char)(48 + Degree(ringSkeleton, x, z, rw, rh)) : ring[z * rw + x] > 0 ? (char)46 : (char)35).ToArray()))));
            Assert.IsTrue(ringGraph.Chains.Count >= 1);

            (int[] cross, int cw, int ch) = Grid(
                "#########...#########",
                "#########...#########",
                "#########...#########",
                "#########...#########",
                "#########...#########",
                "#####################".Replace('#', '.'),
                ".....................",
                ".....................",
                "#########...#########",
                "#########...#########",
                "#########...#########",
                "#########...#########",
                "#########...#########");
            bool[] crossSkeleton = PathSkeleton.Thin(cross, cw, ch);
            PathSkeleton.Prune(crossSkeleton, cross, cw, ch, 3);
            SkeletonGraph crossGraph = PathSkeleton.Trace(crossSkeleton, cross, cw, ch);
            Assert.AreEqual(4, crossGraph.Nodes.Count(node => node.End), "four arms");
            Assert.IsTrue(crossGraph.Nodes.Any(node => !node.End), "a junction");
        }

        private static int Degree(bool[] k, int x, int z, int w, int h) { int n = 0; for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dz != 0) && x + dx >= 0 && x + dx < w && z + dz >= 0 && z + dz < h && k[(z + dz) * w + x + dx]) n++; return n; }

        [TestMethod]
        public void KeepsTwoRegionsThatTouchApart()
        {
            (int[] labels, int width, int height) = Grid(
                "1111122222",
                "1111122222",
                "1111122222",
                "1111122222");

            bool[] skeleton = PathSkeleton.Thin(labels, width, height);

            Assert.IsTrue(Enumerable.Range(0, skeleton.Length).Any(i => skeleton[i] && labels[i] == 1));
            Assert.IsTrue(Enumerable.Range(0, skeleton.Length).Any(i => skeleton[i] && labels[i] == 2));
        }

        [TestMethod]
        public void PrunesShortSpursAndKeepsALine()
        {
            (int[] labels, int width, int height) = Corridor(30, 1);
            bool[] skeleton = PathSkeleton.Thin(labels, width, height);
            int before = skeleton.Count(cell => cell);

            PathSkeleton.Prune(skeleton, labels, width, height, 50);

            Assert.AreEqual(before, skeleton.Count(cell => cell), "a line without a junction stays whole");
        }

        private static (PathLayerNetwork Network, NavGrid Grid) Land(MapDocument document, float spacing = 16)
        {
            NavGrid grid = Walkability.Generate(document).Land;
            return (PathNetworks.GenerateLayer(grid, PathLayer.Land, spacing), grid);
        }

        [TestMethod]
        public void AddsMovesAndRemovesNodesAndLinks()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            (PathLayerNetwork network, NavGrid grid) = Land(document);
            GeneratedPathNode first = network.Nodes[0];
            (int a, int b) = network.Links[^1];
            GeneratedPathNode linkedA = network.Nodes[a];
            GeneratedPathNode linkedB = network.Nodes[b];
            Vector2 open = network.Nodes[network.Nodes.Length / 2].Place + new Vector2(0.5f, 0.5f);

            PathNodeSettings settings = PathNodeSettings.Default;
            settings = PathNetworkEdits.AddNode(settings, PathLayer.Land, open.X, open.Y);
            settings = PathNetworkEdits.MoveNode(settings, PathLayer.Land, first.Ref, first.X + 1, first.Z + 1);
            settings = PathNetworkEdits.Link(settings, PathLayer.Land, linkedA.Ref, linkedB.Ref, cut: true);
            (PathLayerNetwork edited, IReadOnlyList<PathEdit> unmatched) = PathNetworkEdits.Apply(network, [.. settings.Edits], grid);

            Assert.AreEqual(0, unmatched.Count);
            Assert.AreEqual(network.Nodes.Length + 1, edited.Nodes.Length);
            GeneratedPathNode added = edited.Nodes.Single(node => node.Role == PathNodeRole.Added);
            Assert.AreEqual(new PathNodeRef(open.X, open.Y, 1), added.Ref);
            Assert.IsTrue(edited.Neighbours()[edited.Nodes.IndexOf(added)].Count is >= 1 and <= 3, "linked to its nearest neighbours");
            Assert.AreEqual((first.X + 1, first.Z + 1), (edited.Nodes[0].X, edited.Nodes[0].Z), "moved, keeping its reference");
            Assert.AreEqual(first.Ref, edited.Nodes[0].Ref);
            Assert.IsFalse(edited.Links.Contains((a, b)), "the link is cut");

            // a link cut and added again: no edit is left; a node removed goes with its links
            PathNodeSettings again = PathNetworkEdits.Link(settings, PathLayer.Land, linkedB.Ref, linkedA.Ref, cut: false);
            Assert.AreEqual(2, again.Edits.Length);
            PathNodeSettings removed = PathNetworkEdits.RemoveNode(again, PathLayer.Land, first.Ref);
            Assert.AreEqual(PathEditKind.Remove, removed.Edits[^1].Kind);
            Assert.IsFalse(removed.Edits.Any(edit => edit.Kind == PathEditKind.Move), "the move of a removed node goes");
            (PathLayerNetwork without, _) = PathNetworkEdits.Apply(network, [.. removed.Edits], grid);
            Assert.AreEqual(network.Nodes.Length, without.Nodes.Length, "one added, one removed");
            Assert.IsFalse(without.Nodes.Any(node => node.Ref == first.Ref));
            PathNodeSettings noAdded = PathNetworkEdits.RemoveNode(removed, PathLayer.Land, added.Ref);
            Assert.IsFalse(noAdded.Edits.Any(edit => edit.Kind == PathEditKind.Add), "an added node removed leaves no edit");
        }

        [TestMethod]
        public void EditsSurviveRegeneratingAfterTheTerrainChanged()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            (PathLayerNetwork network, NavGrid grid) = Land(document);
            // a node far from where the terrain changes, and one right there
            GeneratedPathNode far = network.Nodes.MaxBy(node => node.X + node.Z)!;
            GeneratedPathNode near = network.Nodes.MinBy(node => Vector2.Distance(node.Place, new Vector2(40, 40)))!;
            PathNodeSettings settings = PathNetworkEdits.MoveNode(PathNodeSettings.Default, PathLayer.Land, far.Ref, far.X - 2, far.Z);
            settings = PathNetworkEdits.RemoveNode(settings, PathLayer.Land, near.Ref);

            // a cliff dug around the place of the near node
            Heightmap heights = document.Terrain.Heightmap;
            ushort[] samples = heights.Samples.ToArray();
            int size = heights.Samples.Width;
            for (int z = 0; z < heights.Samples.Height; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (Math.Abs(x - near.X) < 12 && Math.Abs(z - near.Z) < 12)
                    {
                        samples[z * size + x] = (ushort)Math.Min(ushort.MaxValue, samples[z * size + x] + ((x + z) % 2 == 0 ? 6000 : 0));
                    }
                }
            }
            MapDocument dug = document with { Terrain = document.Terrain with { Heightmap = heights with { Samples = TiledBuffer<ushort>.FromArray(size, heights.Samples.Height, samples) } } };
            (PathLayerNetwork regenerated, NavGrid dugGrid) = Land(dug);

            (PathLayerNetwork edited, IReadOnlyList<PathEdit> unmatched) = PathNetworkEdits.Apply(regenerated, [.. settings.Edits], dugGrid);

            Assert.IsTrue(edited.Nodes.Any(node => node.Ref == far.Ref && node.X == far.X - 2), "the move found its node again");
            Assert.IsTrue(unmatched.All(edit => edit.Kind == PathEditKind.Remove), "only the removal of a node in the dug ground may miss");
        }

        [TestMethod]
        public void AMoveWhoseNodeIsGoneBecomesANodeOfItsOwn()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            (PathLayerNetwork network, NavGrid grid) = Land(document);
            GeneratedPathNode node = network.Nodes[3];
            PathNodeRef gone = PathNodeRef.Generated(-500, -500);
            PathNodeSettings settings = PathNodeSettings.Default with
            {
                Edits = [new PathEdit(PathEditKind.Move, PathLayer.Land, gone) { ToX = node.X + 1, ToZ = node.Z }, new PathEdit(PathEditKind.Remove, PathLayer.Land, gone)],
            };

            (PathLayerNetwork edited, IReadOnlyList<PathEdit> unmatched) = PathNetworkEdits.Apply(network, [.. settings.Edits], grid);

            Assert.AreEqual(2, unmatched.Count);
            Assert.AreEqual(network.Nodes.Length + 1, edited.Nodes.Length);
            Assert.AreEqual(PathNodeRole.Added, edited.Nodes[^1].Role);
            Assert.AreEqual(0, PathNetworkEdits.ForgetUnmatched(settings, unmatched).Edits.Length);
        }

        [TestMethod]
        public void TheSettingsAndEditsAreKeptInTheProject()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            PathNodeSettings settings = new PathNodeSettings { Layers = [PathLayer.Land, PathLayer.Air], Spacing = 24, Smoothing = 5, MinimumDistance = 6, Fill = false, Replace = true, OnExport = true };
            settings = PathNetworkEdits.AddNode(settings, PathLayer.Land, 10.5f, 12.5f);
            settings = PathNetworkEdits.MoveNode(settings, PathLayer.Land, PathNodeRef.Generated(30, 30), 31, 32);
            settings = PathNetworkEdits.Link(settings, PathLayer.Land, new PathNodeRef(10.5f, 12.5f, 1), PathNodeRef.Generated(30, 30), cut: false);
            settings = PathNetworkEdits.RemoveNode(settings, PathLayer.Air, PathNodeRef.Generated(4, 4));
            string project = ProjectFolders.New("path-nodes");

            MapProjectStore.Save(document with { Procedural = document.Procedural with { PathNodes = settings } }, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreEqual(settings, loaded.Procedural.PathNodes);
            string plain = ProjectFolders.New("path-nodes-default");
            MapProjectStore.Save(document, plain);
            Assert.IsTrue(MapProjectStore.Load(plain).Procedural.PathNodes.IsDefault);
        }
    }
}
