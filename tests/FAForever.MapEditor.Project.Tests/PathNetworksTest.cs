using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class PathNetworksTest
    {
        /// <summary>
        /// On the test maps every generated node stands on ground its layer can walk, no link crosses
        /// ground it cannot (a cliff, the shore), and the nodes of each region of the mesh are one
        /// network: every node reaches every other of its region.
        /// </summary>
        [TestMethod]
        [DataRow(ProjectFolders.ThetaPassage, PathLayer.Land)]
        [DataRow(ProjectFolders.HardFfa, PathLayer.Land)]
        [DataRow(ProjectFolders.HardFfa, PathLayer.Water)]
        [DataRow(ProjectFolders.HardFfa, PathLayer.Amphibious)]
        [DataRow(ProjectFolders.CoopR01, PathLayer.Land)]
        public void ConnectsEveryRegionWithoutCrossingBlockedGround(string folder, PathLayer layer)
        {
            MapDocument document = MapImporter.ImportFolder(folder);
            MapNavigation navigation = Walkability.Generate(document);
            NavGrid grid = navigation[PathNetworks.NavLayerOf(layer)!.Value];

            PathLayerNetwork network = PathNetworks.Generate(document, navigation, new PathNetworkSettings { Layers = [layer], Replace = true })[layer]!;

            Assert.IsTrue(network.Nodes.Length > 0, "a network");
            foreach (GeneratedPathNode node in network.Nodes)
            {
                Assert.IsTrue(grid.GetLabel(new Vector3(node.X, 0, node.Z)) is not null, $"{layer} node at {node.X}, {node.Z} on blocked ground");
            }
            foreach ((int a, int b) in network.Links)
            {
                Vector2 from = new Vector2(network.Nodes[a].X, network.Nodes[a].Z);
                Vector2 to = new Vector2(network.Nodes[b].X, network.Nodes[b].Z);
                Assert.IsTrue(PathNetworks.CanWalk(grid, from, to), $"the link {from} to {to} crosses blocked ground");
                Assert.IsTrue(Vector2.Distance(from, to) <= network.Spacing * 3, $"the link {from} to {to} is long");
            }

            // one network per region, and every region of some size has nodes
            int[] piece = Pieces(network);
            foreach (IGrouping<int, int> region in Enumerable.Range(0, network.Nodes.Length).GroupBy(i => grid.GetLabel(new Vector3(network.Nodes[i].X, 0, network.Nodes[i].Z))!.Id))
            {
                Assert.AreEqual(1, region.Select(i => piece[i]).Distinct().Count(), $"region {region.Key} of {layer} is one network");
            }
            HashSet<int> withNodes = [.. network.Nodes.Select(node => grid.GetLabel(new Vector3(node.X, 0, node.Z))!.Id)];
            foreach (NavLabel label in grid.Labels.Where(label => label.Cells >= network.Spacing * network.Spacing))
            {
                Assert.IsTrue(withNodes.Contains(label.Id), $"region {label.Id} of {label.Cells} ogrids has nodes");
            }
        }

        [TestMethod]
        public void GoesThroughTheGapOfAWallAndNeverOverIt()
        {
            MapDocument document = Walled(MapImporter.ImportFolder(ProjectFolders.HardFfa), z => z >= 40 && z <= 44);
            MapNavigation navigation = Walkability.Generate(document);

            PathLayerNetwork network = PathNetworks.GenerateLayer(navigation.Land, PathLayer.Land, 16, [], replace: true);

            Assert.AreEqual(1, Pieces(network).Distinct().Count(), "one network over both halves");
            foreach ((int a, int b) in network.Links)
            {
                (GeneratedPathNode from, GeneratedPathNode to) = (network.Nodes[a], network.Nodes[b]);
                if ((from.X < 32) != (to.X < 32))
                {
                    float z = from.Z + (to.Z - from.Z) * (32 - from.X) / (to.X - from.X);
                    Assert.IsTrue(z >= 39 && z <= 46, $"a link crosses the wall at z = {z}, outside its gap");
                }
            }
        }

        [TestMethod]
        public void LeavesTwoHalvesApartWithoutAGap()
        {
            MapDocument document = Walled(MapImporter.ImportFolder(ProjectFolders.HardFfa), _ => false);
            MapNavigation navigation = Walkability.Generate(document);

            PathLayerNetwork network = PathNetworks.GenerateLayer(navigation.Land, PathLayer.Land, 16, [], replace: true);

            Assert.AreEqual(2, Pieces(network).Distinct().Count(), "a network per half");
            Assert.IsFalse(network.Links.Any(link => (network.Nodes[link.A].X < 32) != (network.Nodes[link.B].X < 32)));
        }

        [TestMethod]
        public void LaysTheAirNetworkOnAGridOverThePlayableArea()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            document = document with { Markers = document.Markers with { Areas = [new Area { Name = "AREA_1", X0 = 0, Z0 = 0, X1 = 128, Z1 = 64 }] } };

            PathLayerNetwork air = PathNetworks.GenerateAir(document, 32, [], replace: true);

            Assert.AreEqual(4 * 2, air.Nodes.Length);
            Assert.AreEqual(new GeneratedPathNode(16, 16), air.Nodes[0]);
            Assert.AreEqual(3 * 2 + 4, air.Links.Length, "each to the four beside it");
        }

        [TestMethod]
        public void PutsTheNetworkIntoTheMapWithTheMapEditorsNames()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Markers = new MarkerSet() };
            MapNavigation navigation = Walkability.Generate(document);
            PathNetwork network = PathNetworks.Generate(document, navigation, new PathNetworkSettings { Layers = [PathLayer.Land, PathLayer.Air], Spacing = 32 });

            MapDocument applied = PathNetworks.Apply(document, network);

            PathNode[] land = [.. applied.Markers.PathNodes.Where(node => node.Layer == PathLayer.Land)];
            Assert.AreEqual(network[PathLayer.Land]!.Nodes.Length, land.Length);
            Assert.AreEqual("LandPN0", land[0].Name);
            Assert.AreEqual($"LandPN{land.Length - 1}", land[^1].Name);
            Assert.AreEqual("AirPN0", applied.Markers.PathNodes.First(node => node.Layer == PathLayer.Air).Name);
            Assert.AreEqual(applied.Markers.PathNodes.Length, applied.Markers.PathNodes.Select(node => node.Id).Distinct().Count(), "ids unique");
            Assert.AreEqual(document.Terrain.Heightmap.HeightAt(land[0].Position.X, land[0].Position.Z), land[0].Position.Y, "on the terrain");
            foreach (PathNode node in applied.Markers.PathNodes)
            {
                foreach (int other in node.AdjacentTo)
                {
                    PathNode linked = applied.Markers.PathNodes.Single(n => n.Id == other);
                    Assert.AreEqual(node.Layer, linked.Layer);
                    CollectionAssert.Contains(linked.AdjacentTo.ToArray(), node.Id, "both ways");
                }
            }
            Assert.AreEqual(network.Links * 2, applied.Markers.PathNodes.Sum(node => node.AdjacentTo.Length));
        }

        [TestMethod]
        public void ReplacesTheLayersNodesOrAddsOnlyWhereNoneAre()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Markers = new MarkerSet() };
            MapNavigation navigation = Walkability.Generate(document);
            // a passable ogrid near the middle
            int cell = Enumerable.Range(0, navigation.Land.Cells.Length).Where(i => navigation.Land.Cells[i] > 0)
                .MinBy(i => Math.Abs(i % navigation.Land.Width - 100) + Math.Abs(i / navigation.Land.Width - 100));
            Vector2 at = new Vector2(cell % navigation.Land.Width + 0.5f, cell / navigation.Land.Width + 0.5f);
            PathNode mine = new PathNode { Id = 1, Name = "LandPN7", Layer = PathLayer.Land, Position = new Vector3(at.X, 0, at.Y) };
            PathNode water = new PathNode { Id = 2, Name = "WaterPN0", Layer = PathLayer.Water, Position = new Vector3(5, 0, 5) };
            document = document with { Markers = new MarkerSet { PathNodes = [mine, water], Chains = [new Chain { Name = "Chain_1", MarkerIds = [1, 2] }] } };

            MapDocument added = PathNetworks.Apply(document, PathNetworks.Generate(document, navigation, new PathNetworkSettings { Layers = [PathLayer.Land], Spacing = 32 }));
            PathNode kept = added.Markers.PathNodes.Single(node => node.Id == 1);
            Assert.AreEqual(mine.Position, kept.Position, "the map's node stays where it is");
            Assert.IsTrue(kept.AdjacentTo.Length > 0, "and is linked into the network");
            Assert.AreEqual("LandPN8", added.Markers.PathNodes.First(node => node.Id > 2).Name, "new names go on from the highest");
            Assert.IsFalse(added.Markers.PathNodes.Any(node => node.Id != 1 && node.Layer == PathLayer.Land
                && Vector2.Distance(new Vector2(node.Position.X, node.Position.Z), at) < 2), "no new node on the map's own");

            MapDocument replaced = PathNetworks.Apply(document, PathNetworks.Generate(document, navigation, new PathNetworkSettings { Layers = [PathLayer.Land], Spacing = 32, Replace = true }));
            Assert.IsFalse(replaced.Markers.PathNodes.Any(node => node.Id == 1), "the layer's own node went");
            Assert.IsTrue(replaced.Markers.PathNodes.Any(node => node.Id == 2), "other layers stay");
            CollectionAssert.AreEqual(new[] { 2 }, replaced.Markers.Chains[0].MarkerIds.ToArray(), "and out of its chain");
            Assert.AreEqual("LandPN0", replaced.Markers.PathNodes.First(node => node.Layer == PathLayer.Land).Name);
        }

        [TestMethod]
        public void IsOutOfDateWhenWalkabilityIsAndUsesTheCurrentMesh()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            GeneratedResults results = new GeneratedResults();
            results.Register(new WalkabilityGenerator());
            PathNetworkGenerator generator = new PathNetworkGenerator { Settings = new PathNetworkSettings { Layers = [PathLayer.Land], Replace = true } };
            results.Register(generator);
            generator.Walkability = results.Generate(WalkabilityGenerator.GeneratorId, document);

            GeneratedResult result = results.Generate(PathNetworkGenerator.GeneratorId, document);
            MapDocument applied = generator.Apply(document, result.Value);

            Assert.AreEqual(GeneratedStatus.Current, results.States(applied)[1].Status, "its own nodes do not make it out of date");
            MapDocument higher = document with { Water = document.Water with { Elevation = document.Water.Elevation + 1 } };
            Assert.AreEqual(GeneratedStatus.Stale, results.States(higher)[1].Status);
            Assert.AreEqual(((PathNetwork)result.Value).Nodes, applied.Markers.PathNodes.Count(node => node.Layer == PathLayer.Land));
        }

        [TestMethod]
        public void WalksALineOnlyOverItsRegion()
        {
            MapDocument document = Walled(MapImporter.ImportFolder(ProjectFolders.HardFfa), _ => false);
            NavGrid land = Walkability.Generate(document).Land;

            Assert.IsTrue(PathNetworks.CanWalk(land, new Vector2(5.5f, 5.5f), new Vector2(20.5f, 50.5f)));
            Assert.IsFalse(PathNetworks.CanWalk(land, new Vector2(5.5f, 5.5f), new Vector2(50.5f, 5.5f)), "over the wall");
        }

        private static bool Reaches(MapDocument document, PathNode from, PathNode to)
        {
            HashSet<int> seen = [from.Id];
            Queue<int> queue = new Queue<int>([from.Id]);
            while (queue.TryDequeue(out int id))
            {
                if (id == to.Id)
                {
                    return true;
                }
                foreach (int next in document.Markers.PathNodes.Single(node => node.Id == id).AdjacentTo)
                {
                    if (seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// The piece of the network each node is in.
        /// </summary>
        private static int[] Pieces(PathLayerNetwork network)
        {
            int[] piece = Enumerable.Range(0, network.Nodes.Length).ToArray();
            int Find(int i) => piece[i] == i ? i : piece[i] = Find(piece[i]);
            foreach ((int a, int b) in network.Links)
            {
                piece[Find(a)] = Find(b);
            }
            return [.. Enumerable.Range(0, network.Nodes.Length).Select(Find)];
        }

        /// <summary>
        /// A flat map of 64 by 64 ogrids with a wall across it at x = 32, open at rows <paramref name="gap"/>.
        /// </summary>
        private static MapDocument Walled(MapDocument template, Func<int, bool> gap)
        {
            const int size = 64;
            ushort[] samples = new ushort[(size + 1) * (size + 1)];
            for (int z = 0; z <= size; z++)
            {
                for (int x = 0; x <= size; x++)
                {
                    samples[z * (size + 1) + x] = (ushort)(x == 32 && !gap(z) ? 30 * 128 : 10 * 128);
                }
            }
            return template with
            {
                Terrain = template.Terrain with
                {
                    Heightmap = new Heightmap { Scale = 1f / 128, Samples = TiledBuffer<ushort>.FromArray(size + 1, size + 1, samples) },
                    Types = TiledBuffer<byte>.Create(size, size, 1),
                },
                Water = template.Water with { HasWater = false },
                Markers = new MarkerSet(),
            };
        }
    }
}
