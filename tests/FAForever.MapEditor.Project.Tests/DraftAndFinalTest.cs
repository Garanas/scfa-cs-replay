using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The draft and final heightmaps (wave 9A, <see cref="HeightStacks"/>): two stacks of layers,
    /// shared layers that are one layer with a place and a visibility of their own in each, the
    /// final's base read from a procedural tool's output, brushes on either, the project keeping
    /// both, the export taking the final, the draft written for World Machine and Gaea.
    /// </summary>
    [TestClass]
    public class DraftAndFinalTest
    {
        private const float Scale = 1f / 128;

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Flat(float height = 35) => NewMap.Create(new NewMapSettings { Size = 256, Height = height });

        private static float FinalAt(MapDocument document, int x, int z) => document.Terrain.Heightmap.GetHeight(x, z);

        private static float DraftAt(MapDocument document, int x, int z) => document.DraftHeightmap.GetHeight(x, z);

        private static Layer Square(MapDocument document, string name, float value, int x, int z, int half)
        {
            TiledBuffer<float>.Builder values = TiledBuffer<float>.Create(document.Width + 1, document.Height + 1).ToBuilder();
            for (int j = z - half; j <= z + half; j++)
            {
                for (int i = x - half; i <= x + half; i++)
                {
                    values[i, j] = value;
                }
            }
            return new Layer { Name = name, Source = LayerSource.Painted, Combine = LayerCombine.Add, Values = values.ToBuffer() };
        }

        /// <summary>
        /// A map whose final's base is a flat output of a procedural tool at <paramref name="baseHeight"/>
        /// and whose draft keeps the old base, with a draft-only blockout hill.
        /// </summary>
        private static MapDocument Parted(float baseHeight = 20)
        {
            MapDocument document = Flat(35);
            document = HeightLayerEdits.Add(document, Square(document, "Blockout", 10, 60, 60, 4));
            ushort[] output = Enumerable.Repeat((ushort)(baseHeight / Scale), 257 * 257).ToArray();
            document = HeightStacks.UseAsFinalBase(document, output, "procedural/temp/gaea.png");
            document = HeightStacks.SetUse(document, "Blockout", LayerUse.Draft);
            return HeightLayers.Recomposite(document);
        }

        [TestMethod]
        public void ANewStackSharesEverythingAndTheDraftIsTheFinal()
        {
            MapDocument document = HeightLayerEdits.Start(Flat(35));
            Assert.AreEqual(1, document.DraftStack.Count);
            Assert.AreSame(document.HeightStack.Layers[0], document.DraftStack.Layers[0], "the base is shared");
            Assert.IsTrue(HeightStacks.SharesBase(document));
            Assert.AreEqual(LayerUse.Shared, HeightStacks.DefaultUse(document, HeightStackKind.Draft), "while the final has no base of its own, new layers go into both");

            document = HeightLayers.Recomposite(HeightLayerEdits.Add(document, Square(document, "Hill", 10, 40, 40, 2), shown: HeightStackKind.Draft));
            Assert.AreEqual(LayerUse.Shared, HeightStacks.UseOf(document, "Hill"));
            Assert.IsNull(document.DraftSamples, "the same layers in the same order make the same heights: no samples of its own");
            Assert.AreSame(document.Terrain.Heightmap, document.DraftHeightmap);
            Assert.AreEqual(45, FinalAt(document, 40, 40), 1e-3);
        }

        [TestMethod]
        public void TheFinalsBaseComesFromTheOutputAndTheDraftKeepsItsOwn()
        {
            MapDocument document = Parted(20);
            Assert.IsFalse(HeightStacks.SharesBase(document));
            Assert.AreEqual(HeightStacks.FinalBaseName, document.HeightStack.Layers[0].Name);
            Assert.IsInstanceOfType<ImageSource>(document.HeightStack.Layers[0].Source);
            Assert.IsTrue(document.HeightStack.Layers[0].IsBase);
            Assert.AreEqual(LayerUse.Draft, HeightStacks.UseOf(document, HeightLayerEdits.BaseName), "the old base stays the draft's");
            Assert.AreEqual(LayerUse.Draft, HeightStacks.UseOf(document, "Blockout"));

            Assert.AreEqual(20, FinalAt(document, 10, 10), 1e-3, "the final is the output");
            Assert.AreEqual(20, FinalAt(document, 60, 60), 1e-3, "without the blockout");
            Assert.AreEqual(35, DraftAt(document, 10, 10), 1e-3, "the draft keeps its base");
            Assert.AreEqual(45, DraftAt(document, 60, 60), 1e-3, "and its blockout");
            Assert.AreEqual(LayerUse.Final, HeightStacks.DefaultUse(document, HeightStackKind.Final), "a new layer goes into the stack shown");

            // a shared layer (a plateau's flat) is applied on both
            document = HeightLayers.Recomposite(HeightLayerEdits.Add(document, Square(document, "Plateau", 5, 120, 120, 3), use: LayerUse.Shared));
            Assert.AreEqual(25, FinalAt(document, 120, 120), 1e-3);
            Assert.AreEqual(40, DraftAt(document, 120, 120), 1e-3);

            // the output read again; then the final shares the draft's base again
            ushort[] lower = Enumerable.Repeat((ushort)(12 / Scale), 257 * 257).ToArray();
            document = HeightLayers.Recomposite(HeightStacks.UseAsFinalBase(document, lower, "procedural/temp/gaea2.png"));
            Assert.AreEqual(12, FinalAt(document, 10, 10), 1e-3);
            Assert.AreEqual("procedural/temp/gaea2.png", document.HeightStack.Layers[0].Path);
            document = HeightLayers.Recomposite(HeightStacks.ShareDraftBase(document));
            Assert.IsTrue(HeightStacks.SharesBase(document));
            Assert.AreEqual(35, FinalAt(document, 10, 10), 1e-3);
        }

        [TestMethod]
        public void ASharedLayerIsOneLayerWithAPlaceAndAVisibilityInEach()
        {
            MapDocument document = Parted();
            document = HeightLayerEdits.Add(document, Square(document, "Flats", 5, 120, 120, 3), use: LayerUse.Shared);
            document = HeightLayerEdits.Add(document, Square(document, "Ridge", 8, 120, 120, 3), use: LayerUse.Final);
            document = HeightLayers.Recomposite(document);

            // its own place in each
            document = HeightLayerEdits.Move(document, "Flats", 2, HeightStackKind.Final);
            Assert.AreEqual(2, document.HeightStack.IndexOf("Flats"));
            Assert.AreEqual(2, document.DraftStack.IndexOf("Flats"), "the draft's order stays");

            // its own on/off in each
            document = HeightLayers.Recomposite(HeightLayerEdits.SetVisible(document, "Flats", false, HeightStackKind.Draft));
            Assert.IsFalse(document.DraftStack.Find("Flats")!.Visible);
            Assert.IsTrue(document.HeightStack.Find("Flats")!.Visible);
            Assert.AreEqual(33, FinalAt(document, 120, 120), 1e-3, "the final still has it");
            Assert.AreEqual(35, DraftAt(document, 120, 120), 1e-3, "the draft does not");

            // its content is one: strength, name and removal reach both
            document = HeightLayerEdits.SetStrength(document, "Flats", 0.5f);
            Assert.AreEqual(0.5f, document.DraftStack.Find("Flats")!.Strength);
            Assert.IsFalse(document.DraftStack.Find("Flats")!.Visible, "the draft's visibility stays its own");
            document = HeightLayerEdits.Rename(document, "Flats", "Plateau flats");
            Assert.AreEqual(LayerUse.Shared, HeightStacks.UseOf(document, "Plateau flats"));
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.Rename(document, "Ridge", HeightLayerEdits.BaseName), "a name is unique over both stacks");
            document = HeightLayerEdits.Remove(document, "Plateau flats");
            Assert.IsNull(HeightStacks.UseOf(document, "Plateau flats"));

            // a use changes membership; a base keeps its own
            document = HeightStacks.SetUse(document, "Ridge", LayerUse.Shared);
            Assert.AreEqual(document.DraftStack.Count - 1, document.DraftStack.IndexOf("Ridge"), "shared into the draft on top");
            document = HeightStacks.SetUse(document, "Ridge", LayerUse.Draft);
            Assert.IsNull(document.HeightStack.Find("Ridge"));
            Assert.ThrowsException<ArgumentException>(() => HeightStacks.SetUse(document, HeightStacks.FinalBaseName, LayerUse.Shared));
        }

        [TestMethod]
        public void BrushesWriteTheChosenStackAndASharedLayerReachesBoth()
        {
            MapDocument start = Parted();
            start = HeightLayerEdits.Add(start, HeightLayerEdits.NewSculpt(start, "Shared sculpt"), use: LayerUse.Shared);
            start = HeightLayerEdits.Add(start, HeightLayerEdits.NewSculpt(start, "Draft sculpt"), use: LayerUse.Draft);
            EditSession session = new EditSession(HeightLayers.Recomposite(start));
            HeightBrush brush = new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 6, Strength = 2 };

            // the draft's own layer: the final stays
            session.Heights = HeightStackKind.Draft;
            session.SculptLayer = "Draft sculpt";
            Assert.IsNull(HeightLayers.SculptProblem(session.Document, "Draft sculpt", HeightStackKind.Draft));
            Assert.IsNotNull(HeightLayers.SculptProblem(session.Document, "Draft sculpt", HeightStackKind.Final), "not a layer of the final");
            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                brush.Apply(stroke.Heightmap, Scale, 200, 200);
                Assert.AreEqual(37, DraftAt(stroke.Document, 200, 200), 0.05, "the brush's change shows in the draft");
                Assert.IsTrue(stroke.Commit());
            }
            Assert.AreEqual(20, FinalAt(session.Document, 200, 200), 1e-3, "the final is untouched");
            Assert.AreEqual(2, session.Document.DraftStack.Find("Draft sculpt")!.Values![200, 200], 0.05);

            // a shared layer sculpted in the draft: the final has it too
            session.SculptLayer = "Shared sculpt";
            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                brush.Apply(stroke.Heightmap, Scale, 100, 100);
                stroke.Commit();
            }
            Assert.AreEqual(37, DraftAt(session.Document, 100, 100), 0.05);
            Assert.AreEqual(22, FinalAt(session.Document, 100, 100), 0.05, "the shared layer's work is in the final");
            Assert.AreSame(session.Document.DraftStack.Find("Shared sculpt")!.Values, session.Document.HeightStack.Find("Shared sculpt")!.Values, "one grid");

            // and sculpted in the final: the draft has it too
            session.Heights = HeightStackKind.Final;
            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                brush.Apply(stroke.Heightmap, Scale, 150, 150);
                stroke.Commit();
            }
            Assert.AreEqual(22, FinalAt(session.Document, 150, 150), 0.05);
            Assert.AreEqual(37, DraftAt(session.Document, 150, 150), 0.05);

            // the draft composited tile by tile is what compositing everything makes
            CollectionAssert.AreEqual(LayerStacks.Composite(session.Document, session.Document.DraftStack).ToArray().Select(h => (ushort)Math.Round(h / Scale)).ToArray(),
                session.Document.DraftHeightmap.Samples.ToArray());

            session.Undo();
            session.Undo();
            Assert.AreEqual(35, DraftAt(session.Document, 100, 100), 0.05, "undo takes both back");
            Assert.AreEqual(20, FinalAt(session.Document, 100, 100), 0.05);
        }

        [TestMethod]
        public void ABrushOnAMapWhoseStacksAreTheSameKeepsOneComposite()
        {
            EditSession session = new EditSession(Flat(35));
            session.Edit("Add", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewSculpt(d, "Sculpt")));
            session.SculptLayer = "Sculpt";
            session.Heights = HeightStackKind.Draft;
            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 6, Strength = 2 }.Apply(stroke.Heightmap, Scale, 100, 100);
                stroke.Commit();
            }
            Assert.IsNull(session.Document.DraftSamples, "the stacks still hold the same layers");
            Assert.AreEqual(37, FinalAt(session.Document, 100, 100), 0.05);
            Assert.AreSame(session.Document.DraftStack.Layers[1], session.Document.HeightStack.Layers[1]);
        }

        [TestMethod]
        public void TheProjectKeepsBothStacksAndTheExportTakesTheFinal()
        {
            MapDocument document = Parted();
            document = HeightLayerEdits.Add(document, Square(document, "Flats", 5, 120, 120, 3), use: LayerUse.Shared);
            document = HeightLayers.Recomposite(HeightLayerEdits.SetVisible(document, "Flats", false, HeightStackKind.Draft));
            string project = ProjectFolders.New("draft-final");
            MapProjectStore.Save(document, project);
            ProjectImages.WriteGray16(Path.Combine(project, "procedural/temp/gaea.png"), 257, 257, Enumerable.Repeat((ushort)(20 / Scale), 257 * 257).ToArray());

            MapDocument loaded = MapProjectStore.Load(project);
            CollectionAssert.AreEqual(document.HeightStack.Layers.Select(layer => layer.Name).ToArray(), loaded.HeightStack.Layers.Select(layer => layer.Name).ToArray());
            CollectionAssert.AreEqual(document.DraftStack.Layers.Select(layer => layer.Name).ToArray(), loaded.DraftStack.Layers.Select(layer => layer.Name).ToArray());
            Assert.IsFalse(loaded.DraftStack.Find("Flats")!.Visible);
            Assert.IsTrue(loaded.HeightStack.Find("Flats")!.Visible);
            Assert.AreSame(loaded.DraftStack.Find(HeightLayerEdits.BaseName)!.Values, loaded.DraftStack.Layers[0].Values);
            Assert.AreSame(loaded.HeightStack.Find("Flats")!.Values, loaded.DraftStack.Find("Flats")!.Values, "a shared layer is one grid after loading");
            CollectionAssert.AreEqual(document.DraftHeightmap.Samples.ToArray(), loaded.DraftHeightmap.Samples.ToArray());
            CollectionAssert.AreEqual(document.Terrain.Heightmap.Samples.ToArray(), loaded.Terrain.Heightmap.Samples.ToArray());

            // the game gets the final
            IReadOnlyDictionary<string, byte[]> exported = MapExporter.ToBytes(loaded);
            Scmap scmap = ScmapParser.Parse(exported[exported.Keys.Single(key => key.EndsWith(".scmap", StringComparison.Ordinal))]);
            CollectionAssert.AreEqual(loaded.Terrain.Heightmap.Samples.ToArray(), scmap.Heightmap.Samples);
            Assert.AreEqual(25, scmap.Heightmap.Samples[120 * 257 + 120] * scmap.Heightmap.Scale, 1e-3);
            CollectionAssert.AreNotEqual(loaded.DraftHeightmap.Samples.ToArray(), scmap.Heightmap.Samples);
        }

        [TestMethod]
        public void AnOlderStackOpensWithEveryLayerShared()
        {
            MapDocument document = Flat(35);
            document = HeightLayers.Recomposite(HeightLayerEdits.Add(document, Square(document, "Hill", 10, 40, 40, 2)));
            string project = ProjectFolders.New("draft-older");
            MapProjectStore.Save(document, project);
            string index = Path.Combine(project, MapProjectLayout.HeightStackIndex);
            string json = File.ReadAllText(index);
            Assert.IsFalse(json.Contains("\"draft\"", StringComparison.Ordinal), "stacks that are the same write no draft list");
            Assert.IsFalse(json.Contains("\"use\"", StringComparison.Ordinal));

            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(2, loaded.DraftStack.Count);
            Assert.AreSame(loaded.HeightStack.Layers[1], loaded.DraftStack.Layers[1]);
            Assert.IsNull(loaded.DraftSamples, "the same terrain");
        }

        [TestMethod]
        public void TheOutputsFileIsReadAgainAlsoBeforeTheFirstSaveOfItsLayer()
        {
            EditedMap map = EditedMap.New(Flat(35));
            string project = ProjectFolders.New("draft-reload");
            map.SaveAs(project);
            string output = Path.Combine(project, "procedural/temp/gaea.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            ProjectImages.WriteGray16(output, 129, 129, Enumerable.Repeat((ushort)(20 / Scale), 129 * 129).ToArray());
            ushort[] samples = HeightmapFiles.ReadResampled(map.Document, output, out _);
            map.Session.Edit("Use as the final's base", d => HeightStacks.UseAsFinalBase(d, samples, "procedural/temp/gaea.png"));
            Assert.AreEqual(20, FinalAt(map.Document, 10, 10), 1e-3);

            // not saved since: the file is another tool's all the same
            ProjectImages.WriteGray16(output, 129, 129, Enumerable.Repeat((ushort)(28 / Scale), 129 * 129).ToArray());
            Assert.IsTrue(map.Reload(["procedural/temp/gaea.png"]));
            Assert.AreEqual(28, FinalAt(map.Document, 10, 10), 1e-3, "the final's base follows its file");
            Assert.AreEqual(35, DraftAt(map.Document, 10, 10), 1e-3, "the draft keeps its base");
        }

        [TestMethod]
        public void ResizingKeepsSharedLayersOne()
        {
            MapDocument document = Parted();
            document = HeightLayers.Recomposite(HeightLayerEdits.Add(document, Square(document, "Flats", 5, 120, 120, 3), use: LayerUse.Shared));
            MapDocument resized = MapResize.Resize(document, new MapResizeSettings { Width = 512, Height = 512 }).Document;
            Assert.AreSame(resized.HeightStack.Find("Flats"), resized.DraftStack.Find("Flats"));
            Assert.AreEqual(513, resized.DraftHeightmap.Samples.Width);
            Assert.AreEqual(LayerUse.Draft, HeightStacks.UseOf(resized, "Blockout"));
        }

        [TestMethod]
        public void TheDraftIsWrittenForProceduralToolsAndTheirOutputsListed()
        {
            MapDocument document = Parted();
            document = document with { Masks = document.Masks.SetItem("rivers", TiledBuffer<byte>.Create(128, 128, 200)) };
            string project = ProjectFolders.New("draft-out");
            Directory.CreateDirectory(project);
            IReadOnlyList<string> written = ProceduralFiles.WriteDraft(document, project, ["rivers", "missing"]);
            CollectionAssert.AreEquivalent(new[] { ProceduralFiles.DraftHeightmap, ProceduralFiles.DraftHeightmapRaw, ProceduralFiles.DraftMask("rivers") }, written.ToArray());

            (int width, int height, ushort[] png) = HeightmapFiles.Read(Path.Combine(project, ProceduralFiles.DraftHeightmap));
            Assert.AreEqual(257, width);
            CollectionAssert.AreEqual(document.DraftHeightmap.Samples.ToArray(), png, "the draft, not the final");
            (_, _, ushort[] raw) = HeightmapFiles.Read(Path.Combine(project, ProceduralFiles.DraftHeightmapRaw));
            CollectionAssert.AreEqual(png, raw);
            Assert.AreEqual(200, ProjectImages.ReadGray8(Path.Combine(project, ProceduralFiles.DraftMask("rivers")), 128, 128)[0]);

            // the tool's output beside them is listed; the inputs are not
            ProjectImages.WriteGray16(Path.Combine(project, "procedural/temp/out/gaea.png"), 65, 65, new ushort[65 * 65]);
            CollectionAssert.AreEqual(new[] { "procedural/temp/out/gaea.png" }, ProceduralFiles.Outputs(project).ToArray());

            // a mask no longer ticked loses its file
            ProceduralFiles.WriteDraft(document, project, []);
            Assert.IsFalse(File.Exists(Path.Combine(project, ProceduralFiles.DraftMask("rivers"))));
        }
    }
}
