using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class WalkabilityTest
    {
        /// <summary>
        /// The editor's walkability of a map is the navigational mesh FileFormats.Map makes from the
        /// game's own files, layer by layer and ogrid by ogrid.
        /// </summary>
        [TestMethod]
        [DataRow(ProjectFolders.ThetaPassage, "theta_passage_-_faf_version")]
        [DataRow(ProjectFolders.HardFfa, "HardFFA")]
        [DataRow(ProjectFolders.CoopR01, "SCCA_Coop_R01")]
        public void IsTheMeshOfTheGamesFiles(string folder, string name)
        {
            string files = Path.Combine(folder, name);
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(files + ".scmap"));
            MapSave save = MapSaveParser.Parse(File.ReadAllText(files + "_save.lua"));
            MapScenario scenario = MapScenarioParser.Parse(File.ReadAllText(files + "_scenario.lua"));
            MapNavigation expected = NavGenerator.Generate(scmap, save, NavGenerator.FindPlayableArea(scenario, save));

            MapNavigation actual = Walkability.Generate(MapImporter.ImportFolder(folder));

            Assert.AreEqual(expected.HasWater, actual.HasWater);
            foreach (NavLayer layer in Enum.GetValues<NavLayer>())
            {
                CollectionAssert.AreEqual(expected[layer].Cells, actual[layer].Cells, $"{layer}");
                CollectionAssert.AreEqual(
                    expected[layer].Labels.Select(label => (label.Cells, label.Extractors.Count, label.Hydrocarbons.Count)).ToList(),
                    actual[layer].Labels.Select(label => (label.Cells, label.Extractors.Count, label.Hydrocarbons.Count)).ToList(),
                    $"{layer}");
            }
        }

        [TestMethod]
        public void TakesTheFirstAreaOfASkirmishMapAsThePlayableArea()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            MapDocument withArea = document with { Markers = document.Markers with { Areas = [new Area { Name = "AREA_1", X0 = 10, Z0 = 20, X1 = 200, Z1 = 210 }] } };

            Assert.AreEqual(new MapArea("AREA_1", 10, 20, 200, 210), Walkability.PlayableArea(withArea));
            Assert.IsNull(Walkability.PlayableArea(document with { Markers = document.Markers with { Areas = [new Area { Name = "AREA_1", X0 = 10, Z0 = 20, X1 = 30, Z1 = 210 }] } }), "32 ogrids or less is no playable area");
            Assert.IsFalse(Walkability.IsPassable(Walkability.Generate(withArea).Land, 5, 100), "outside the playable area");
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

        [TestMethod]
        public void FindsANewPassageAndWhereItIs()
        {
            MapDocument template = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            MapNavigation closed = Walkability.Generate(Walled(template, _ => false));
            MapNavigation open = Walkability.Generate(Walled(template, z => z >= 40 && z <= 44));

            WalkabilityChanges changes = WalkabilityChanges.Compare(closed, open, NavLayer.Land)!;

            Assert.AreEqual(2, closed.Land.Labels.Count, "two halves");
            Assert.AreEqual(1, open.Land.Labels.Count, "joined");
            Assert.IsTrue(changes.Opened > 0);
            Assert.AreEqual(0, changes.Blocked);
            WalkabilityLink passage = changes.Passages.Single();
            Assert.AreEqual(2, passage.Regions);
            Assert.IsTrue(passage.Place.X >= 31 && passage.Place.X <= 33 && passage.Place.Y >= 39 && passage.Place.Y <= 45, $"at {passage.Place}");
            Assert.AreEqual(0, changes.Closures.Count);
            Assert.AreEqual(WalkabilityChange.Opened, changes.Cells[42 * 64 + 31]);

            // and back: the path closes
            WalkabilityChanges back = WalkabilityChanges.Compare(open, closed, NavLayer.Land)!;
            Assert.AreEqual(changes.Opened, back.Blocked);
            Assert.AreEqual(0, back.Passages.Count);
            Assert.AreEqual(2, back.Closures.Single().Regions);
        }

        [TestMethod]
        public void FindsNothingWhenNothingChanged()
        {
            MapDocument template = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            MapNavigation navigation = Walkability.Generate(template);

            WalkabilityChanges changes = WalkabilityChanges.Compare(navigation, Walkability.Generate(template), NavLayer.Land)!;

            Assert.IsTrue(changes.IsEmpty);
            Assert.AreEqual(0, changes.Passages.Count + changes.Closures.Count);
        }

        [TestMethod]
        public void GeneratesWhenAskedAndIsOutOfDateWhenTheTerrainChanges()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            GeneratedResults results = new GeneratedResults();
            results.Register(new WalkabilityGenerator());
            results.Register(new NormalMapGenerator());

            results.Generate(WalkabilityGenerator.GeneratorId, document);
            Assert.IsInstanceOfType<MapNavigation>(results.ResultOf("walkability")!.Value);

            MapDocument recoloured = document with { Water = document.Water with { SurfaceColor = new Vector3(1, 0, 0) } };
            Assert.AreEqual(GeneratedStatus.Current, results.States(recoloured)[0].Status, "the water's colour does not matter");
            MapDocument pathNode = document with { Markers = document.Markers with { PathNodes = [] } };
            Assert.AreEqual(GeneratedStatus.Current, results.States(pathNode)[0].Status, "path nodes do not matter");
            MapDocument higher = document with { Water = document.Water with { Elevation = document.Water.Elevation + 1 } };
            CollectionAssert.AreEqual(new[] { "water level" }, results.States(higher)[0].ChangedInputs.ToArray());
        }

        [TestMethod]
        public void PutsTheGeneratedNormalMapIntoTheDocument()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            NormalMapGenerator generator = new NormalMapGenerator();

            object value = generator.Generate(document);
            MapDocument applied = generator.Apply(document, value);

            Assert.AreSame(value, applied.Terrain.NormalMap);
            Assert.AreEqual(document.Terrain.NormalMapFormat, applied.Terrain.NormalMapFormat);
            Assert.AreSame(document.Terrain.Heightmap, applied.Terrain.Heightmap);
            Assert.IsFalse(GeneratedResult.Of(generator, document, value).IsStale(applied), "applying it leaves it current");
        }
    }
}
