using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapResizeTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// A new map with hills, painted grids and one of every kind of thing at known places.
        /// </summary>
        private static MapDocument Map(int size = 256)
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = size, Players = 2 });
            TiledBuffer<ushort>.Builder heights = document.Terrain.Heightmap.Samples.ToBuilder();
            for (int z = 0; z <= size; z++)
            {
                for (int x = 0; x <= size; x++)
                {
                    heights[x, z] = (ushort)(4000 + x * 7 + z * 3);
                }
            }
            TiledBuffer<byte>.Builder types = document.Terrain.Types.ToBuilder();
            types[3, 4] = 9;
            TiledBuffer<byte>.Builder stratum = document.Terrain.StratumMasks.Layers[0].ToBuilder();
            stratum[5, 6] = 200;
            TiledBuffer<byte>.Builder foam = document.Water.FoamMask.ToBuilder();
            foam[7, 8] = 99;
            document = document with
            {
                Terrain = document.Terrain with
                {
                    Heightmap = document.Terrain.Heightmap with { Samples = heights.ToBuffer() },
                    Types = types.ToBuffer(),
                    StratumMasks = document.Terrain.StratumMasks with { Layers = document.Terrain.StratumMasks.Layers.SetItem(0, stratum.ToBuffer()) },
                },
                Water = document.Water with
                {
                    FoamMask = foam.ToBuffer(),
                    WaveGenerators = [new WaveGenerator { Texture = "/textures/wave.dds", Ramp = "/textures/ramp.dds", Position = new Vector3(40, 30, 40) }],
                },
            };
            document = MaskLayers.Add(document, "Forest");
            document = MaskLayers.Add(document, "Rivers", half: true);
            TiledBuffer<byte>.Builder forest = document.Masks["Forest"].ToBuilder();
            forest[10, 11] = 255;
            document = document with { Masks = document.Masks.SetItem("Forest", forest.ToBuffer()) };

            document = document
                .AddProp(new Prop { Id = 1, Blueprint = "/env/rock.bp", Position = new Vector3(20, 40, 30), RotationX = Vector3.UnitX, RotationY = Vector3.UnitY, RotationZ = Vector3.UnitZ, Scale = Vector3.One })
                .AddProp(new Prop { Id = 2, Blueprint = "/env/rock.bp", Position = new Vector3(size - 10, 40, size - 10), RotationX = Vector3.UnitX, RotationY = Vector3.UnitY, RotationZ = Vector3.UnitZ, Scale = Vector3.One })
                .AddDecal(new Decal { Id = 1, Textures = ["/env/decal.dds"], Scale = new Vector3(8, 8, 8), Position = new Vector3(50, 40, 50) })
                .AddDecal(new Decal { Id = 2, Textures = ["/env/decal.dds"], Scale = new Vector3(8, 8, 8), Position = new Vector3(size - 20, 40, size - 20) })
                .AddUnit("NEUTRAL_CIVILIAN", ["INITIAL"], new Unit { Name = "UNIT_1", Blueprint = "uec1101", Position = new Vector3(60, 40, 70) })
                .AddUnit("NEUTRAL_CIVILIAN", ["INITIAL"], new Unit { Name = "UNIT_2", Blueprint = "uec1101", Position = new Vector3(size - 30, 40, size - 30) })
                .AddMarker(new PathNode { Id = 0, Name = "LandPN1", Layer = PathLayer.Land, Position = new Vector3(80, 40, 80) }, out Marker near)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN2", Layer = PathLayer.Land, Position = new Vector3(size - 40, 40, size - 40) }, out Marker far)
                .AddArea(new Area { Name = "CORNER", X0 = size - 64, Z0 = size - 64, X1 = size, Z1 = size });
            return document.LinkPathNodes(near.Id, far.Id);
        }

        [TestMethod]
        public void TheHeightLayersGrowWithTheMapAndStillAddUp()
        {
            MapDocument document = EditedMap.ImportMap(ProjectFolders.HardFfa).Document;
            HeightLayer raised = HeightLayerEdits.NewSculpt(document, "Raised");
            int side = raised.Values.Width;
            TiledBuffer<float>.Builder values = raised.Values.ToBuilder();
            values[10, 10] = 5;
            document = HeightLayers.Recomposite(HeightLayerEdits.Add(document, raised with { Values = values.ToBuffer() }));

            MapDocument grown = MapResize.Resize(document, new MapResizeSettings { Width = 512, Height = 512, Anchor = MapAnchor.TopLeft }).Document;

            Assert.IsTrue(grown.HeightLayers.All(layer => layer.Values.Width == 513 && layer.Values.Height == 513), "every layer at the new corners");
            Assert.AreEqual(5f, HeightLayers.Find(grown, "Raised")!.Values[10, 10], "the old values at their place");
            Assert.AreEqual(0f, HeightLayers.Find(grown, "Raised")!.Values[400, 400], "an adding layer adds nothing where the map gained");
            CollectionAssert.AreEqual(HeightLayers.Recomposite(grown).Terrain.Heightmap.Samples.ToArray(), grown.Terrain.Heightmap.Samples.ToArray(), "the heightmap is the layers' sum");
            Assert.AreEqual(257, side);
        }

        [TestMethod]
        public void GrowsEveryPartAroundTheOldMap()
        {
            MapDocument original = Map();

            MapResizeResult result = MapResize.Resize(original, new MapResizeSettings { Width = 512, Height = 512 });
            MapDocument grown = result.Document;

            Assert.AreEqual((128, 128), (result.OffsetX, result.OffsetZ), "the centre");
            Assert.AreEqual(0, result.Removed.Total);
            Assert.AreEqual((512, 512), (grown.Width, grown.Height));
            Assert.AreEqual(original.Terrain.Heightmap.Samples[10, 20], grown.Terrain.Heightmap.Samples[138, 148]);
            Assert.AreEqual(original.Terrain.Heightmap.Samples[0, 0], grown.Terrain.Heightmap.Samples[0, 0], "the edge's height continues outwards");
            Assert.AreEqual(original.Terrain.Heightmap.Samples[256, 0], grown.Terrain.Heightmap.Samples[512, 50]);
            Assert.AreEqual((512, 512), (grown.Terrain.Types.Width, grown.Terrain.Types.Height));
            Assert.AreEqual(9, grown.Terrain.Types[131, 132]);
            Assert.AreEqual((256, 256), (grown.Terrain.StratumMasks.Width, grown.Terrain.StratumMasks.Height), "half the map's size stays half");
            Assert.AreEqual(200, grown.Terrain.StratumMasks.Layers[0][64 + 5, 64 + 6]);
            Assert.AreEqual(99, grown.Water.FoamMask[64 + 7, 64 + 8]);
            Assert.AreEqual((256, 256), (grown.Water.Map.Width, grown.Water.Map.Height));
            Assert.AreEqual((512, 512), (grown.Terrain.NormalMap.Width, grown.Terrain.NormalMap.Height));
            Assert.AreEqual(256, grown.Terrain.Preview.Width, "the preview keeps the game's size");
            Assert.AreEqual(512, grown.Masks["Forest"].Width);
            Assert.AreEqual(256, grown.Masks["Rivers"].Width);
            Assert.AreEqual(255, grown.Masks["Forest"][138, 139]);
            Assert.AreEqual(0, grown.Masks["Forest"][0, 0], "masks for tools are empty where the map grew");

            Assert.AreEqual(new Vector3(148, 40, 158), grown.Props[0].Position);
            Assert.AreEqual(new Vector3(178, 40, 178), grown.Decals[0].Position);
            Assert.AreEqual(new Vector3(188, 40, 198), grown.FindUnit("UNIT_1")!.Unit.Position);
            Assert.AreEqual(new Vector3(208, 40, 208), grown.Markers.PathNodes.Single(node => node.Name == "LandPN1").Position);
            Assert.AreEqual(new Vector3(168, 30, 168), grown.Water.WaveGenerators[0].Position);
            Assert.AreEqual(new Area { Name = "AREA_1", X0 = 128, Z0 = 128, X1 = 384, Z1 = 384 }, grown.Markers.Areas[0], "areas move with the map");
            Assert.AreEqual(original.Markers.Spawns[0].Position + new Vector3(128, 0, 128), grown.Markers.Spawns[0].Position);
        }

        [TestMethod]
        public void GrowsAndCropsBackToTheSameMap()
        {
            MapDocument original = Map();

            MapDocument grown = MapResize.Resize(original, new MapResizeSettings { Width = 1024, Height = 512, Anchor = MapAnchor.Right }).Document;
            MapResizeResult back = MapResize.Resize(grown, new MapResizeSettings { Width = 256, Height = 256, Offset = (-768, -128) });

            Assert.AreEqual(0, back.Removed.Total);
            CollectionAssert.AreEqual(original.Terrain.Heightmap.Samples.ToArray(), back.Document.Terrain.Heightmap.Samples.ToArray());
            CollectionAssert.AreEqual(original.Terrain.Types.ToArray(), back.Document.Terrain.Types.ToArray());
            CollectionAssert.AreEqual(original.Terrain.StratumMasks.Layers[0].ToArray(), back.Document.Terrain.StratumMasks.Layers[0].ToArray());
            CollectionAssert.AreEqual(original.Masks["Rivers"].ToArray(), back.Document.Masks["Rivers"].ToArray());
            CollectionAssert.AreEqual(original.Props.ToArray(), back.Document.Props.ToArray());
            CollectionAssert.AreEqual(original.Decals.ToArray(), back.Document.Decals.ToArray());
            CollectionAssert.AreEqual(original.Markers.PathNodes.ToArray(), back.Document.Markers.PathNodes.ToArray());
            CollectionAssert.AreEqual(original.Markers.Areas.ToArray(), back.Document.Markers.Areas.ToArray());
            CollectionAssert.AreEqual(original.AllUnits().Select(placed => placed.Unit).ToArray(), back.Document.AllUnits().Select(placed => placed.Unit).ToArray());
        }

        [TestMethod]
        public void CropsAwayWhatLiesOutsideTheNewMap()
        {
            MapDocument original = Map(512).SetPlayableArea("CORNER");

            MapResizeResult result = MapResize.Resize(original, new MapResizeSettings { Width = 256, Height = 256, Anchor = MapAnchor.TopLeft });
            MapDocument cropped = result.Document;

            Assert.AreEqual((0, 0), (result.OffsetX, result.OffsetZ));
            Assert.AreEqual(new MapResizeRemoved(Props: 1, Decals: 1, Units: 1, Markers: 1, Areas: 1, WaveGenerators: 0), result.Removed);
            Assert.AreEqual("1 prop, 1 decal, 1 unit, 1 marker, 1 area", result.Removed.ToString());
            Assert.AreEqual(1, cropped.Props.Length);
            Assert.IsNull(cropped.FindUnit("UNIT_2"));
            PathNode near = cropped.Markers.PathNodes.Single();
            Assert.AreEqual(0, near.AdjacentTo.Length, "the link to the removed node goes with it");
            Assert.AreEqual(2, cropped.Markers.Spawns.Length, "every army keeps its spawn");
            Assert.AreEqual(2, result.SpawnsMoved, "both lie just right of the middle, 256.5");
            Assert.IsTrue(cropped.Markers.Spawns.All(spawn => spawn.Position.X <= 256 && spawn.Position.Z <= 256));
            Assert.AreEqual(new Area { Name = "AREA_1", X0 = 0, Z0 = 0, X1 = 256, Z1 = 256 }, cropped.Markers.Areas.Single(), "cut to the map");
            Assert.AreEqual(1, result.AreasClipped);
            Assert.IsNull(cropped.Scenario.PlayableArea, "the playable area was removed: the first area is it again");
            Assert.AreEqual("AREA_1", cropped.PlayableArea!.Name);
        }

        [TestMethod]
        public void FillsWhatTheMapGainsWithAHeightAndCanFitThePlayableArea()
        {
            MapDocument original = Map();

            MapDocument grown = MapResize.Resize(original, new MapResizeSettings { Width = 512, Height = 256, Anchor = MapAnchor.Left, FillHeight = 12.5f, FitPlayableArea = true }).Document;

            Assert.AreEqual(12.5f, grown.Terrain.Heightmap.GetHeight(400, 100));
            Assert.AreEqual(original.Terrain.Heightmap.Samples[256, 100], grown.Terrain.Heightmap.Samples[256, 100]);
            Assert.AreEqual(new Area { Name = "AREA_1", X0 = 0, Z0 = 0, X1 = 512, Z1 = 256 }, grown.Markers.Areas[0]);
            Assert.AreEqual(new Area { Name = "CORNER", X0 = 192, Z0 = 192, X1 = 256, Z1 = 256 }, grown.Markers.Areas[1]);
        }

        [TestMethod]
        [DataRow(MapAnchor.TopLeft, 0, 0)]
        [DataRow(MapAnchor.Top, 128, 0)]
        [DataRow(MapAnchor.TopRight, 256, 0)]
        [DataRow(MapAnchor.Left, 0, 384)]
        [DataRow(MapAnchor.Centre, 128, 384)]
        [DataRow(MapAnchor.Right, 256, 384)]
        [DataRow(MapAnchor.BottomLeft, 0, 768)]
        [DataRow(MapAnchor.Bottom, 128, 768)]
        [DataRow(MapAnchor.BottomRight, 256, 768)]
        public void PutsTheOldMapAtItsAnchor(MapAnchor anchor, int x, int z)
        {
            Assert.AreEqual((x, z), MapResize.OffsetOf(256, 256, new MapResizeSettings { Width = 512, Height = 1024, Anchor = anchor }));
            Assert.AreEqual((-x / 2, -z / 4), MapResize.OffsetOf(512, 1024, new MapResizeSettings { Width = 256, Height = 256, Anchor = anchor }) is (int cx, int cz) ? (cx / 2, cz / 4) : (0, 0));
            Assert.AreEqual((7, -3), MapResize.OffsetOf(256, 256, new MapResizeSettings { Width = 512, Height = 512, Anchor = anchor, Offset = (7, -3) }), "an offset wins");
        }

        [TestMethod]
        public void RefusesSizesTheGameDoesNotHave()
        {
            MapDocument original = Map();
            Assert.ThrowsException<ArgumentException>(() => MapResize.Resize(original, new MapResizeSettings { Width = 300, Height = 256 }));
            Assert.ThrowsException<ArgumentException>(() => MapResize.Resize(original, new MapResizeSettings { Width = 256, Height = 8192 }));
            Assert.ThrowsException<ArgumentException>(() => MapResize.Resize(original, new MapResizeSettings { Width = 512, Height = 512, FillHeight = 900 }));
        }

        [TestMethod]
        public void AGrownMapExportsAndImportsAgain()
        {
            MapDocument grown = MapResize.Resize(Map(), new MapResizeSettings { Width = 512, Height = 1024, Anchor = MapAnchor.Bottom }).Document;

            string folder = MapExporter.WriteFolder(grown, ProjectFolders.New("resized"));
            MapDocument imported = MapImporter.ImportFolder(folder);

            Assert.AreEqual((512, 1024), (imported.Width, imported.Height));
            MapScenario scenario = MapScenarioParser.Parse(File.ReadAllText(Directory.GetFiles(folder, "*_scenario.lua").Single()));
            Assert.AreEqual((512, 1024), scenario.Size);
            CollectionAssert.AreEqual(grown.Terrain.Heightmap.Samples.ToArray(), imported.Terrain.Heightmap.Samples.ToArray());
            CollectionAssert.AreEqual(grown.Terrain.Types.ToArray(), imported.Terrain.Types.ToArray());
            CollectionAssert.AreEqual(grown.Terrain.StratumMasks.Layers[0].ToArray(), imported.Terrain.StratumMasks.Layers[0].ToArray());
            CollectionAssert.AreEqual(grown.Water.FoamMask.ToArray(), imported.Water.FoamMask.ToArray());
            Assert.AreEqual(grown.Props.Length, imported.Props.Length);
            Assert.AreEqual(grown.Props[0].Position, imported.Props[0].Position);
            CollectionAssert.AreEqual(grown.Markers.Areas.ToArray(), imported.Markers.Areas.ToArray());

            // and as a project
            string project = ProjectFolders.New("resized-project");
            MapProjectStore.Save(grown, project);
            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual((512, 1024), (loaded.Width, loaded.Height));
            Assert.AreEqual(512, loaded.Masks["Forest"].Width);
        }
    }
}
