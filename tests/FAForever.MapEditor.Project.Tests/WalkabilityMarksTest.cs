using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Walkability marked by hand (user story T-7): blocked and open over what is generated, kept
    /// in the project, written by the export as the game takes it.
    /// </summary>
    [TestClass]
    public class WalkabilityMarksTest
    {
        /// <summary>
        /// A flat map of 512 ogrids without water, with a cliff (one ogrid wide, too steep) across
        /// x = 300.
        /// </summary>
        private static MapDocument Map()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 512, Height = 40, Water = false });
            TiledBuffer<ushort>.Builder samples = document.Terrain.Heightmap.Samples.ToBuilder();
            for (int z = 0; z <= 512; z++)
            {
                for (int x = 301; x <= 512; x++)
                {
                    samples[x, z] = (ushort)(samples[x, z] + 128 * 10);
                }
            }
            return document with { Terrain = document.Terrain with { Heightmap = document.Terrain.Heightmap with { Samples = samples.ToBuffer() } } };
        }

        private static MapDocument Marked(MapDocument document, params (int X, int Z, byte Mark)[] marks)
        {
            TiledBuffer<byte>.Builder grid = PaintTarget.WalkabilityMarks.Read(document).ToBuilder();
            foreach ((int x, int z, byte mark) in marks)
            {
                grid[x, z] = mark;
            }
            return PaintTarget.WalkabilityMarks.Write(document, grid.ToBuffer());
        }

        [TestMethod]
        public void MarksOverrideWhatIsGenerated()
        {
            MapDocument document = Map();
            MapNavigation plain = Walkability.Generate(document);
            Assert.IsTrue(Walkability.IsPassable(plain.Land, 100, 100));
            Assert.IsFalse(Walkability.IsPassable(plain.Land, 300, 100), "the cliff");

            MapDocument marked = Marked(document, (100, 100, WalkabilityMarks.Blocked), (300, 100, WalkabilityMarks.Open), (300, 101, WalkabilityMarks.Open));
            MapNavigation navigation = Walkability.Generate(marked);
            Assert.IsFalse(Walkability.IsPassable(navigation.Land, 100, 100), "marked blocked");
            Assert.IsTrue(Walkability.IsPassable(navigation.Land, 300, 100), "marked open");
            Assert.AreEqual((1, 2), WalkabilityMarks.Count(marked.WalkabilityMarks));
            Assert.IsNull(WalkabilityMarks.ForcedOpen(Marked(document, (5, 5, WalkabilityMarks.Blocked))), "none open");
        }

        [TestMethod]
        public void TheExportBlocksWithATerrainTypeSoTheGameAgrees()
        {
            MapDocument marked = Marked(Map(), (100, 100, WalkabilityMarks.Blocked), (300, 100, WalkabilityMarks.Open));
            Scmap scmap = MapExporter.Export(marked).Scmap;

            Assert.AreEqual(WalkabilityMarks.BlockingType, scmap.TerrainTypes[101 * 512 + 101], "at the ogrid's far corner, where the game reads it");
            Assert.AreEqual(1, marked.Terrain.Types[101, 101], "the map's own types stay");
            MapNavigation game = NavGenerator.Generate(scmap.Heightmap, null, scmap.TerrainTypes, []);
            Assert.IsFalse(Walkability.IsPassable(game.Land, 100, 100), "the game blocks it too");
            Assert.IsFalse(Walkability.IsPassable(game.Land, 300, 100), "no file makes the cliff walkable for the game");

            // an open mark takes a blocking type away
            MapDocument blockingType = marked with { Terrain = marked.Terrain with { Types = TerrainOf(marked, 51, 51, WalkabilityMarks.BlockingType) } };
            MapDocument opened = Marked(blockingType, (50, 50, WalkabilityMarks.Open));
            Assert.AreEqual(WalkabilityMarks.OpenType, MapExporter.Export(opened).Scmap.TerrainTypes[51 * 512 + 51]);
        }

        private static TiledBuffer<byte> TerrainOf(MapDocument document, int x, int z, byte type)
        {
            TiledBuffer<byte>.Builder types = document.Terrain.Types.ToBuilder();
            types[x, z] = type;
            return types.ToBuffer();
        }

        [TestMethod]
        public void MarksAreKeptInTheProjectAndReadAgain()
        {
            string folder = ProjectFolders.New("marks");
            MapDocument document = Map();
            MapProjectStore.Save(document, folder);
            Assert.IsFalse(File.Exists(Path.Combine(folder, MapProjectLayout.WalkabilityMarks)), "no marks, no file");

            MapDocument marked = Marked(document, (10, 20, WalkabilityMarks.Blocked), (30, 40, WalkabilityMarks.Open));
            MapProjectStore.Save(marked, folder, document);
            MapDocument loaded = MapProjectStore.Load(folder);
            Assert.AreEqual(WalkabilityMarks.Blocked, loaded.WalkabilityMarks![10, 20]);
            Assert.AreEqual(WalkabilityMarks.Open, loaded.WalkabilityMarks[30, 40]);
            Assert.AreEqual(WalkabilityMarks.None, loaded.WalkabilityMarks[0, 0]);

            // another tool paints the file
            byte[] pixels = new byte[512 * 512];
            pixels[5 * 512 + 6] = 255;
            ProjectImages.WriteGray8(Path.Combine(folder, MapProjectLayout.WalkabilityMarks), 512, 512, pixels);
            MapDocument reloaded = MapProjectStore.Reload(loaded, folder, [MapProjectLayout.WalkabilityMarks]);
            Assert.AreEqual(WalkabilityMarks.Open, reloaded.WalkabilityMarks![6, 5]);
            Assert.AreEqual(WalkabilityMarks.None, reloaded.WalkabilityMarks[10, 20]);

            File.Delete(Path.Combine(folder, MapProjectLayout.WalkabilityMarks));
            Assert.IsNull(MapProjectStore.Reload(reloaded, folder, [MapProjectLayout.WalkabilityMarks]).WalkabilityMarks);
        }

        [TestMethod]
        public void TheCheckWarnsOfGroundMarkedOpenThatTheGameBlocks()
        {
            MapDocument document = Map();
            Assert.AreEqual(0, MapChecks.OpenMarksTheGameBlocks(new ProblemContext(Marked(document, (100, 100, WalkabilityMarks.Open)))).Count(), "flat ground: the game lets units pass");
            Problem problem = MapChecks.OpenMarksTheGameBlocks(new ProblemContext(Marked(document, (300, 100, WalkabilityMarks.Open), (300, 7, WalkabilityMarks.Open)))).Single();
            Assert.AreEqual("walkability-marks", problem.Check);
            StringAssert.StartsWith(problem.Message, "2 ogrids");
            Assert.AreEqual(new System.Numerics.Vector2(300.5f, 7.5f), problem.Place);
        }

        [TestMethod]
        public void MarksGrowAndShrinkWithTheMap()
        {
            MapDocument marked = Marked(Map(), (10, 10, WalkabilityMarks.Blocked));
            MapDocument resized = MapResize.Resize(marked, new MapResizeSettings { Width = 1024, Height = 1024, Anchor = MapAnchor.TopLeft }).Document;
            Assert.AreEqual(1024, resized.WalkabilityMarks!.Width);
            Assert.AreEqual(WalkabilityMarks.Blocked, resized.WalkabilityMarks[10, 10]);
        }
    }
}
