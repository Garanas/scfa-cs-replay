using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The stacks of coverage (wave 9B): a stratum's mask, a mask for tools and the water's maps as
    /// stacks of layers. A grid without a stack is its own painted layer (older projects open so,
    /// losslessly); a painted layer's alpha keeps it to where it was painted; a brush paints the
    /// chosen painted layer; mask references and clips follow the masks they read; renaming,
    /// duplicating, removing masks and moving strata take their stacks along; the rules of masks are
    /// rules layers; the project keeps the stacks.
    /// </summary>
    [TestClass]
    public class CoverageLayersTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Flat() => NewMap.Create(new NewMapSettings { Size = 256, Height = 35 });

        private static readonly LayerTarget Forest = LayerTarget.Mask("forest");

        /// <summary>
        /// A brush's square of <paramref name="value"/> around (x, z) into a grid, through a transaction.
        /// </summary>
        private static void Paint(EditSession session, PaintTarget target, int x, int z, int half, byte value)
        {
            using EditTransaction stroke = session.Begin("Paint");
            TiledBuffer<byte>.Builder grid = stroke.Grid(target);
            for (int j = z - half; j <= z + half; j++)
            {
                for (int i = x - half; i <= x + half; i++)
                {
                    grid[i, j] = value;
                }
            }
            stroke.Commit();
        }

        [TestMethod]
        public void AGridWithoutAStackIsItsOwnPaintedLayer()
        {
            MapDocument document = Flat();
            LayerStack stack = CoverageLayers.StackOf(document, LayerTarget.Stratum(2));
            Assert.AreEqual(1, stack.Count);
            Assert.AreEqual(CoverageLayers.PaintedName, stack.Layers[0].Name);
            Assert.AreSame(document.Terrain.StratumMasks.Layers[1], stack.Layers[0].Coverage, "the layer is the grid itself");
            Assert.IsTrue(document.CoverageStacks.IsEmpty, "nothing is kept until the stack is edited");
            Assert.AreEqual(8 + 3, CoverageLayers.Targets(document).Count(), "eight strata and the water's three maps");
        }

        [TestMethod]
        public void ANewPaintedLayerWorksOnlyWherePainted()
        {
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            Paint(session, PaintTarget.Mask("forest"), 40, 40, 4, 200);
            session.Edit("Add a layer", d => CoverageLayerEdits.Add(d, Forest, CoverageLayerEdits.NewPainted(d, Forest, "Clearing")));
            Assert.AreEqual(2, session.Document.CoverageStacks["mask-forest"].Count, "the first layer starts the stack from the grid's own painted layer");
            Assert.AreEqual(200, session.Document.Masks["forest"][40, 40], "an untouched painted layer changes nothing");

            // the brush paints the chosen layer: a clearing in the forest, replacing only where it paints
            session.PaintLayers["mask-forest"] = "Clearing";
            Paint(session, PaintTarget.Mask("forest"), 41, 41, 1, 0);
            Assert.AreEqual(0, session.Document.Masks["forest"][41, 41]);
            Assert.AreEqual(200, session.Document.Masks["forest"][38, 38], "the forest around stays");
            Layer clearing = session.Document.CoverageStacks["mask-forest"].Find("Clearing")!;
            Assert.AreEqual(255, clearing.Alpha![41, 41], "painted there");
            Assert.AreEqual(0, clearing.Alpha[38, 38], "untouched there");
            Assert.AreEqual(200, session.Document.CoverageStacks["mask-forest"].Find(CoverageLayers.PaintedName)!.Coverage![41, 41], "the layer below keeps its forest");

            // hiding the clearing brings the forest back
            session.Edit("Hide", d => CoverageLayerEdits.SetVisible(d, Forest, "Clearing", false));
            Assert.AreEqual(200, session.Document.Masks["forest"][41, 41]);
        }

        [TestMethod]
        public void AStackBackToItsOwnPaintedLayerLeavesTheDocument()
        {
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            session.Edit("Add a layer", d => CoverageLayerEdits.Add(d, Forest, CoverageLayerEdits.NewPainted(d, Forest, "Extra")));
            Assert.IsTrue(session.Document.CoverageStacks.ContainsKey("mask-forest"));
            session.Edit("Remove", d => CoverageLayerEdits.Remove(d, Forest, "Extra"));
            Assert.IsFalse(session.Document.CoverageStacks.ContainsKey("mask-forest"), "one painted layer as the grid holds it is no stack");
        }

        [TestMethod]
        public void AMaskReferenceFollowsTheMaskItReads()
        {
            LayerTarget stratum = LayerTarget.Stratum(3);
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            session.Edit("Read the forest", d => CoverageLayerEdits.Add(d, stratum, CoverageLayerEdits.NewMaskRef(d, "Forest", "forest")));
            Assert.AreEqual("mask:forest", ((MaskRefSource)session.Document.CoverageStacks["stratum-3"].Find("Forest")!.Source).Key, "one way to name a mask");
            Assert.AreEqual(0, session.Document.Terrain.StratumMasks.Layers[2][20, 20]);

            Paint(session, PaintTarget.Mask("forest"), 40, 40, 6, 255);
            // the stratum mask is half the map's size: (40, 40) in ogrids is (20, 20)
            Assert.AreEqual(255, session.Document.Terrain.StratumMasks.Layers[2][20, 20], "painting the mask paints the stratum");

            session.Edit("Invert", d => CoverageLayerEdits.SetMaskRef(d, stratum, "Forest", invert: true));
            Assert.AreEqual(0, session.Document.Terrain.StratumMasks.Layers[2][20, 20]);
            Assert.AreEqual(255, session.Document.Terrain.StratumMasks.Layers[2][100, 100]);

            Assert.ThrowsException<ArgumentException>(() => CoverageLayerEdits.NewMaskRef(session.Document, "Nothing", "no such mask"));
            Assert.ThrowsException<ArgumentException>(() => CoverageLayerEdits.Add(session.Document, stratum, CoverageLayerEdits.NewMaskRef(session.Document, "Self", "stratum:3")),
                "a stack cannot read what it makes");
        }

        [TestMethod]
        public void RenamingDuplicatingAndRemovingAMaskTakeItsStack()
        {
            LayerTarget stratum = LayerTarget.Stratum(1);
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            session.Edit("Layer", d => CoverageLayerEdits.Add(d, Forest, CoverageLayerEdits.NewPainted(d, Forest, "Clearing")));
            session.Edit("Read", d => CoverageLayerEdits.Add(d, stratum, CoverageLayerEdits.NewMaskRef(d, "Forest", "forest")));
            session.Edit("Rename", d => MaskLayers.Rename(d, "forest", "woods"));
            Assert.IsTrue(session.Document.CoverageStacks.ContainsKey("mask-woods"));
            Assert.IsFalse(session.Document.CoverageStacks.ContainsKey("mask-forest"));
            Assert.AreEqual(LayerTarget.Mask("woods"), session.Document.CoverageStacks["mask-woods"].Target);
            Assert.AreEqual("mask:woods", ((MaskRefSource)session.Document.CoverageStacks["stratum-1"].Find("Forest")!.Source).Key, "a reference follows the new name");

            session.Edit("Duplicate", d => MaskLayers.Duplicate(d, "woods", "woods copy"));
            Assert.AreEqual(2, session.Document.CoverageStacks["mask-woods copy"].Count, "the copy has the same layers");
            Assert.AreSame(session.Document.Masks["woods"], session.Document.Masks["woods copy"]);

            session.Edit("Remove", d => MaskLayers.Remove(d, "woods"));
            Assert.IsFalse(session.Document.CoverageStacks.ContainsKey("mask-woods"));
        }

        [TestMethod]
        public void MovingAndRemovingStrataTakeTheirStacks()
        {
            LayerTarget second = LayerTarget.Stratum(2);
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            session.Edit("Layer", d => CoverageLayerEdits.Add(d, second, CoverageLayerEdits.NewPainted(d, second, "Paths")));
            session.Edit("Move", d => d.MoveStratum(2, 4));
            Assert.IsFalse(session.Document.CoverageStacks.ContainsKey("stratum-2"));
            Assert.AreEqual(LayerTarget.Stratum(4), session.Document.CoverageStacks["stratum-4"].Target);
            session.Edit("Remove", d => d.RemoveStratum(4));
            Assert.IsFalse(session.Document.CoverageStacks.Keys.Any(key => key.StartsWith("stratum-", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void RulesLayersOfStrataAreGenerated()
        {
            LayerTarget stratum = LayerTarget.Stratum(2);
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            Paint(session, PaintTarget.Mask("forest"), 100, 100, 10, 255);
            MaskRuleSet rules = new MaskRuleSet { Rules = [new MaskRule { Kind = MaskRuleKind.Layer, Source = "mask:forest" }] };
            session.Edit("Rules", d => CoverageLayerEdits.Add(d, stratum, CoverageLayerEdits.NewRules(d, stratum, "Under the forest", rules)));
            Assert.AreEqual(0, session.Document.Terrain.StratumMasks.Layers[1][50, 50], "rules make nothing until generated");

            MaskRulesGenerator generator = new MaskRulesGenerator();
            object made = generator.Generate(session.Document);
            session.Edit("Generate", d => generator.Apply(d, made));
            Assert.AreEqual(255, session.Document.Terrain.StratumMasks.Layers[1][50, 50]);
            Assert.AreEqual(0, session.Document.Terrain.StratumMasks.Layers[1][10, 10]);
            Assert.IsFalse(session.Document.MaskRules.ContainsKey("stratum-2"), "the rules of masks are the masks' only");
        }

        [TestMethod]
        public void TheWaterMapsAreStacksToo()
        {
            LayerTarget foam = LayerTarget.Water(WaterMask.Foam);
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            Paint(session, PaintTarget.Mask("forest"), 100, 100, 10, 255);
            session.Edit("Foam where the forest is", d => CoverageLayerEdits.Add(d, foam, CoverageLayerEdits.NewMaskRef(d, "Forest", "forest")));
            Assert.AreEqual(255, session.Document.Water.FoamMask[50, 50]);
            session.Edit("Half", d => CoverageLayerEdits.SetStrength(d, foam, "Forest", 0.5f));
            Assert.AreEqual(128, session.Document.Water.FoamMask[50, 50]);
        }

        [TestMethod]
        public void TheProjectKeepsTheStacksAndAnOlderProjectOpensUnchanged()
        {
            MapDocument document = MaskLayers.Add(Flat(), "forest");
            EditSession session = new EditSession(document);
            Paint(session, PaintTarget.Stratum(2), 40, 40, 4, 180);
            string old = ProjectFolders.New("coverage-old");
            MapProjectStore.Save(session.Document, old);
            Assert.IsFalse(Directory.Exists(Path.Combine(old, "stacks")), "grids without stacks of their own write none");
            MapDocument reopened = MapProjectStore.Load(old);
            Assert.IsTrue(reopened.CoverageStacks.IsEmpty);
            LayerStack own = CoverageLayers.StackOf(reopened, LayerTarget.Stratum(2));
            Assert.AreEqual(180, own.Layers.Single().Coverage![40, 40], "one painted layer holding the mask");

            LayerTarget second = LayerTarget.Stratum(2);
            session.Edit("Layer", d => CoverageLayerEdits.Add(d, second, CoverageLayerEdits.NewPainted(d, second, "Paths")));
            session.PaintLayers["stratum-2"] = "Paths";
            Paint(session, PaintTarget.Stratum(2), 60, 60, 2, 90);
            session.Edit("Read", d => CoverageLayerEdits.Add(d, second, CoverageLayerEdits.NewMaskRef(d, "Forest", "forest", 0.25f) with { Clip = "mask:forest" }));
            string project = ProjectFolders.New("coverage-stacks");
            MapProjectStore.Save(session.Document, project);
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "stratum-2.json")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "stratum-2", "Paths.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "stratum-2", "Paths.coverage.png")), "a painted layer's alpha");
            Assert.IsTrue(File.Exists(Path.Combine(project, "stratum-masks", "stratum-2.gen.png")), "the composite, named as the editor's");
            Assert.IsFalse(File.Exists(Path.Combine(project, "stratum-masks", "stratum-2.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stratum-masks", "stratum-1.png")), "a grid without a stack keeps the map's own name");

            MapDocument loaded = MapProjectStore.Load(project);
            LayerStack stack = loaded.CoverageStacks["stratum-2"];
            CollectionAssert.AreEqual(new[] { CoverageLayers.PaintedName, "Paths", "Forest" }, stack.Layers.Select(layer => layer.Name).ToArray());
            Assert.AreEqual(255, stack.Find("Paths")!.Alpha![60, 60]);
            Assert.AreEqual(0.25f, ((MaskRefSource)stack.Find("Forest")!.Source).Threshold);
            Assert.AreEqual("mask:forest", stack.Find("Forest")!.Clip);
            CollectionAssert.AreEqual(session.Document.Terrain.StratumMasks.Layers[1].ToArray(), loaded.Terrain.StratumMasks.Layers[1].ToArray());

            // the stack ends: its files go, the grid stays
            session.Edit("Remove", d => CoverageLayerEdits.Remove(CoverageLayerEdits.Remove(d, second, "Paths"), second, "Forest"));
            Assert.IsFalse(session.Document.CoverageStacks.ContainsKey("stratum-2"));
            MapProjectStore.Save(session.Document, project, previous: loaded);
            Assert.IsFalse(File.Exists(Path.Combine(project, "stacks", "stratum-2.json")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stratum-masks", "stratum-2.png")), "the map's own again");
            Assert.IsFalse(File.Exists(Path.Combine(project, "stratum-masks", "stratum-2.gen.png")));
        }

        [TestMethod]
        public void TheExportWritesTheComposites()
        {
            LayerTarget foam = LayerTarget.Water(WaterMask.Foam);
            EditSession session = new EditSession(MaskLayers.Add(Flat(), "forest"));
            Paint(session, PaintTarget.Mask("forest"), 100, 100, 10, 255);
            session.Edit("Foam", d => CoverageLayerEdits.Add(d, foam, CoverageLayerEdits.NewMaskRef(d, "Forest", "forest")));
            string folder = MapExporter.WriteFolder(session.Document, ProjectFolders.New("coverage-export"));
            MapDocument imported = MapImporter.ImportFolder(folder);
            Assert.AreEqual(255, imported.Water.FoamMask[50, 50]);
            Assert.IsTrue(imported.Masks.IsEmpty, "masks are never exported");
        }
    }
}
