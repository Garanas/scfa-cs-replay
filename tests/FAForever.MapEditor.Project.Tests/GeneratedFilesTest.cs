using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Who writes a project's file shows in its name (format 7): <c>.gen</c> for what the editor makes
    /// from other things (a stack's grid, a rules layer's grid, the draft), the map's own name for
    /// what is painted or another tool writes; an image layer reads a tool's file and never writes it.
    /// </summary>
    [TestClass]
    public class GeneratedFilesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static readonly LayerTarget Forest = LayerTarget.Mask("forest");

        private static MapDocument Flat() => MaskLayers.Add(NewMap.Create(new NewMapSettings { Size = 256, Height = 35 }), "forest");

        private static MapDocument WithRules(MapDocument document) =>
            CoverageLayers.Recomposite(CoverageLayerEdits.Add(document, Forest, CoverageLayerEdits.NewRules(document, Forest, "Slopes")));

        [TestMethod]
        public void NamesFollowWhoMakesTheFile()
        {
            Assert.AreEqual("stratum-masks/stratum-2.gen.png", MapProjectLayout.Generated("stratum-masks/stratum-2.png"));
            Assert.IsTrue(MapProjectLayout.IsGenerated("stacks\\mask-forest\\Slopes.gen.png"));
            Assert.IsFalse(MapProjectLayout.IsGenerated("masks/generated.png"));
            Assert.AreEqual("masks/forest.png", MapProjectLayout.WithoutGenerated("masks/forest.gen.png"));
            Assert.AreEqual("procedural/temp/draft-heightmap.gen.png", ProceduralFiles.DraftHeightmap);
            Assert.AreEqual("procedural/temp/draft-mask-rivers.gen.png", ProceduralFiles.DraftMask("rivers"));
            Assert.IsTrue(ProceduralFiles.IsInput("procedural/temp/draft-heightmap.png"), "as format 6 named it");
            Assert.IsFalse(ProceduralFiles.IsInput("procedural/temp/forest.png"), "a tool's output");

            MapDocument document = WithRules(Flat());
            Assert.AreEqual("masks/forest.gen.png", MapProjectLayout.GridFile(document, Forest));
            Assert.AreEqual("water/foam.png", MapProjectLayout.GridFile(document, LayerTarget.Water(WaterMask.Foam)));
            Assert.AreEqual("stacks/mask-forest/Slopes.gen.png", MapProjectLayout.StackLayerFile(Forest.Key, document.CoverageStacks[Forest.Key].Find("Slopes")!));
        }

        [TestMethod]
        public void AMaskMadeByAStackIsTheEditorsAndOpensUnderItsName()
        {
            MapDocument document = WithRules(Flat());
            string project = ProjectFolders.New("generated-mask");
            MapProjectStore.Save(document, project);
            Assert.IsTrue(File.Exists(Path.Combine(project, "masks", "forest.gen.png")));
            Assert.IsFalse(File.Exists(Path.Combine(project, "masks", "forest.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "mask-forest", "Slopes.gen.png")), "a rules layer's grid is made from its rules");
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "mask-forest", CoverageLayers.PaintedName + ".png")), "a painted layer is the map's own");

            MapDocument loaded = MapProjectStore.Load(project);
            CollectionAssert.AreEqual(new[] { "forest" }, loaded.Masks.Keys.ToArray(), "the mask's name, without .gen");
            Assert.IsTrue(loaded.CoverageStacks.ContainsKey(Forest.Key));

            // what another tool writes into the editor's file is not read back
            byte[] white = Enumerable.Repeat((byte)255, document.Masks["forest"].Width * document.Masks["forest"].Height).ToArray();
            ProjectImages.WriteGray8(Path.Combine(project, "masks", "forest.gen.png"), document.Masks["forest"].Width, document.Masks["forest"].Height, white);
            Assert.AreSame(loaded, MapProjectStore.Reload(loaded, project, ["masks/forest.gen.png"]));
        }

        [TestMethod]
        public void AProjectOfFormatSixOpensUnchangedAndItsSaveRenamesTheEditorsFiles()
        {
            MapDocument document = WithRules(Flat());
            string project = ProjectFolders.New("format6-names");
            MapProjectStore.Save(document, project);
            // as format 6 named them
            File.Move(Path.Combine(project, "masks", "forest.gen.png"), Path.Combine(project, "masks", "forest.png"));
            File.Move(Path.Combine(project, "stacks", "mask-forest", "Slopes.gen.png"), Path.Combine(project, "stacks", "mask-forest", "Slopes.png"));

            MapDocument loaded = MapProjectStore.Load(project);
            CollectionAssert.AreEqual(document.Masks["forest"].ToArray(), loaded.Masks["forest"].ToArray());
            Assert.IsNotNull(loaded.CoverageStacks[Forest.Key].Find("Slopes")!.Coverage);

            MapProjectStore.Save(loaded, project);
            Assert.IsTrue(File.Exists(Path.Combine(project, "masks", "forest.gen.png")));
            Assert.IsFalse(File.Exists(Path.Combine(project, "masks", "forest.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "mask-forest", "Slopes.gen.png")));
            Assert.IsFalse(File.Exists(Path.Combine(project, "stacks", "mask-forest", "Slopes.png")));
            Assert.AreSame(loaded, MapProjectStore.Reload(loaded, project, ["masks/forest.png", "stacks/mask-forest/Slopes.png"]), "the files renamed by the save are no edit");
        }

        [TestMethod]
        public void AnImageLayerReadsTheToolsFileAndNeverWritesIt()
        {
            MapDocument document = Flat();
            TiledBuffer<byte> mask = document.Masks["forest"];
            string project = ProjectFolders.New("image-layer");
            MapProjectStore.Save(document, project);
            string image = Path.Combine(project, "procedural", "temp", "forest.png");
            ProjectImages.WriteGray8(image, mask.Width, mask.Height, Enumerable.Repeat((byte)100, mask.Width * mask.Height).ToArray());
            document = CoverageLayerEdits.Add(document, Forest, CoverageLayerEdits.NewImage(document, Forest, "Gaea",
                CoverageLayerEdits.ReadImage(document, Forest, image), "procedural/temp/forest.png"));
            document = CoverageLayers.Recomposite(document);
            Assert.AreEqual(100, document.Masks["forest"][10, 10]);

            MapProjectStore.Save(document, project);
            Assert.IsFalse(File.Exists(Path.Combine(project, "stacks", "mask-forest", "Gaea.png")), "no copy of a file in the project");

            // the tool writes its output again: the layer follows
            ProjectImages.WriteGray8(image, mask.Width, mask.Height, Enumerable.Repeat((byte)200, mask.Width * mask.Height).ToArray());
            Assert.IsTrue(MapProjectStore.IsImageLayerFile(document, project, "procedural/temp/forest.png"));
            MapDocument reloaded = CoverageLayers.Recomposite(MapProjectStore.Reload(document, project, ["procedural/temp/forest.png"]));
            Assert.AreEqual(200, reloaded.Masks["forest"][10, 10]);
            Assert.AreEqual(200, CoverageLayers.Recomposite(MapProjectStore.Load(project)).Masks["forest"][10, 10], "and opening reads it");

            DateTime written = File.GetLastWriteTimeUtc(image);
            MapProjectStore.Save(reloaded, project, document);
            Assert.AreEqual(written, File.GetLastWriteTimeUtc(image), "the editor never writes it");
        }
    }
}
