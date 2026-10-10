using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The layer stack model (wave 8): one compositor for heights and coverage, tile by tile, every
    /// way of combining, clip masks and mask references, the tiles a change touches, a brush's work
    /// going into the chosen painted layer of a coverage stack, the stack edits, and project format
    /// 6 with the conversion of older projects.
    /// </summary>
    [TestClass]
    public class LayerStacksTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Flat(float height = 35) => NewMap.Create(new NewMapSettings { Size = 256, Height = height });

        /// <summary>
        /// A document with a mask for tools of the map's size and an empty stack for it.
        /// </summary>
        private static (MapDocument Document, LayerStack Stack) Coverage()
        {
            MapDocument document = MaskLayers.Add(Flat(), "forest");
            return (document, LayerStack.Empty(LayerTarget.Mask("forest")));
        }

        /// <summary>
        /// A painted coverage layer holding <paramref name="value"/> in a square around (x, z).
        /// </summary>
        private static Layer Square(string name, LayerCombine combine, byte value, int x, int z, int half, int size = 256, float strength = 1)
        {
            TiledBuffer<byte>.Builder grid = TiledBuffer<byte>.Create(size, size).ToBuilder();
            for (int j = z - half; j <= z + half; j++)
            {
                for (int i = x - half; i <= x + half; i++)
                {
                    grid[i, j] = value;
                }
            }
            return new Layer { Name = name, Source = LayerSource.Painted, Combine = combine, Strength = strength, Coverage = grid.ToBuffer() };
        }

        private static Layer Full(string name, LayerCombine combine, byte value, int size = 256) =>
            new Layer { Name = name, Source = LayerSource.Painted, Combine = combine, Coverage = TiledBuffer<byte>.Create(size, size, value) };

        private static TiledBuffer<byte> Composite(MapDocument document, LayerStack stack) =>
            LayerStacks.CompositeCoverage(document, stack, LayerStack.Empty(LayerTarget.Mask("forest")).Target.Grid!.Read(document));

        [TestMethod]
        public void CoverageCombinesByEveryWayAndStaysWithinItsRange()
        {
            (MapDocument document, LayerStack empty) = Coverage();
            byte Over(LayerCombine combine, byte value, float strength = 1)
            {
                LayerStack stack = LayerStackEdits.Add(empty, Full("half", LayerCombine.Add, 128));
                stack = LayerStackEdits.Add(stack, Full("layer", combine, value) with { Strength = strength });
                return Composite(document, stack)[20, 20];
            }
            Assert.AreEqual(255, Over(LayerCombine.Add, 255), "128 + 255 stays full");
            Assert.AreEqual(0, Over(LayerCombine.Subtract, 255), "128 - 255 stays empty");
            Assert.AreEqual(64, Over(LayerCombine.Subtract, 64));
            Assert.AreEqual(200, Over(LayerCombine.Maximum, 200), "the higher");
            Assert.AreEqual(128, Over(LayerCombine.Maximum, 20));
            Assert.AreEqual(32, Over(LayerCombine.Minimum, 32), "the lower");
            Assert.AreEqual(69, Over(LayerCombine.Replace, 10, 0.5f), 1, "half way from 128 to 10");
            Assert.AreEqual(64, Over(LayerCombine.Multiply, 128), 1, "128 times a half");

            // a painted layer of a coverage stack works where its values are, by its way: adding a square
            LayerStack squares = LayerStackEdits.Add(LayerStackEdits.Add(empty, Full("half", LayerCombine.Add, 128)), Square("max", LayerCombine.Maximum, 200, 100, 100, 2));
            Assert.AreEqual(200, Composite(document, squares)[100, 100]);
            Assert.AreEqual(128, Composite(document, squares)[5, 5], "the rest as the lower layer made it");

            // a clip mask weakens a layer by its value; a hidden one counts for nothing
            document = MaskLayers.Add(document, "clip");
            document = document with { Masks = document.Masks.SetItem("clip", TiledBuffer<byte>.Create(256, 256, 51)) };
            LayerStack clipped = LayerStackEdits.SetClip(squares, "max", LayerStacks.MaskKey("clip"));
            Assert.AreEqual(128 + (200 - 128) / 5f, Composite(document, clipped)[100, 100], 1, "a fifth of the way to 200");
            Assert.AreEqual(128, Composite(document, LayerStackEdits.SetVisible(squares, "max", false))[100, 100]);
        }

        [TestMethod]
        public void MaskReferencesAreReadWhileCompositingWithThresholdAndInvert()
        {
            (MapDocument document, LayerStack stack) = Coverage();
            document = MaskLayers.Add(document, "rivers", half: true);
            TiledBuffer<byte>.Builder rivers = document.Masks["rivers"].ToBuilder();
            for (int z = 0; z < 128; z++)
            {
                for (int x = 0; x < 64; x++)
                {
                    rivers[x, z] = (byte)(x < 32 ? 255 : 60);
                }
            }
            document = document with { Masks = document.Masks.SetItem("rivers", rivers.ToBuffer()) };
            stack = LayerStackEdits.Add(stack, new Layer { Name = "rivers", Source = new MaskRefSource(LayerStacks.MaskKey("rivers"), Threshold: 0.5f) });
            TiledBuffer<byte> made = Composite(document, stack);
            Assert.AreEqual(255, made[10, 100], "a half-size mask covers the whole map");
            Assert.AreEqual(0, made[100, 100], "below the threshold counts as nothing");
            Assert.AreEqual(0, made[200, 100]);

            LayerStack inverted = stack.With(0, stack.Layers[0] with { Source = new MaskRefSource(LayerStacks.MaskKey("rivers"), Invert: true) });
            Assert.AreEqual(0, Composite(document, inverted)[10, 100]);
            Assert.AreEqual(255, Composite(document, inverted)[200, 100]);

            // in a stack of heights the mask raises the terrain by its height
            document = HeightLayerEdits.Add(document, new Layer { Name = "Rivers", Source = new MaskRefSource(LayerStacks.MaskKey("rivers"), Height: -10) });
            document = HeightLayers.Recomposite(document);
            Assert.AreEqual(25, document.Terrain.Heightmap.GetHeight(10, 100), 1e-2, "35 lowered by 10 where the mask is full");
            Assert.AreEqual(35, document.Terrain.Heightmap.GetHeight(200, 100), 1e-2);

            // renaming the mask keeps the reference
            EditSession session = new EditSession(document);
            session.Edit("Rename", d => MaskLayers.Rename(d, "rivers", "water"));
            Assert.AreEqual(LayerStacks.MaskKey("water"), ((MaskRefSource)session.Document.HeightStack.Layers[1].Source).Key);
            Assert.AreEqual(25, session.Document.Terrain.Heightmap.GetHeight(10, 100), 1e-2);
        }

        [TestMethod]
        public void AChangeCompositesOnlyTheTilesItTouches()
        {
            (MapDocument document, LayerStack stack) = Coverage();
            stack = LayerStackEdits.Add(stack, Full("ground", LayerCombine.Add, 100));
            stack = LayerStackEdits.Add(stack, Square("paint", LayerCombine.Maximum, 0, 10, 10, 0));
            TiledBuffer<byte> before = Composite(document, stack);

            TiledBuffer<byte>.Builder painted = stack.Layers[1].Coverage!.ToBuilder();
            painted[150, 150] = 250;
            LayerStack after = stack.With(1, stack.Layers[1] with { Coverage = painted.ToBuffer() });
            bool[] dirty = LayerStacks.ChangedTiles(document, stack, document, after);
            Assert.AreEqual(1, dirty.Count(tile => tile), "the one tile the dab touched");
            TiledBuffer<byte> incremental = LayerStacks.CompositeCoverage(document, after, before, dirty);
            CollectionAssert.AreEqual(Composite(document, after).ToArray(), incremental.ToArray(), "the same as compositing everything");
            Assert.AreEqual(1, incremental.ChangedTiles(before).Count());
            Assert.AreSame(before, LayerStacks.CompositeCoverage(document, stack, before), "nothing changed: the same grid");

            Assert.IsTrue(LayerStacks.ChangedTiles(document, stack, document, LayerStackEdits.SetStrength(stack, "paint", 0.5f)).All(tile => tile), "a setting: every tile");
        }

        [TestMethod]
        public void ABrushOnACoverageTargetPaintsIntoTheChosenLayer()
        {
            (MapDocument document, LayerStack stack) = Coverage();
            stack = LayerStackEdits.Add(stack, Full("ground", LayerCombine.Add, 50));
            stack = LayerStackEdits.Add(stack, new Layer { Name = "paint", Source = LayerSource.Painted, Strength = 0.5f, Coverage = TiledBuffer<byte>.Create(256, 256) });
            TiledBuffer<byte> composite = Composite(document, stack);

            // the brush paints the composite up to 150 around (100, 100)
            TiledBuffer<byte>.Builder brushed = composite.ToBuilder();
            new MaskBrush { Radius = 4, Strength = 1, Falloff = BrushFalloff.Sharp }.Apply(brushed, 256, 256, 100, 100);
            for (int z = 90; z < 110; z++)
            {
                for (int x = 90; x < 110; x++)
                {
                    brushed[x, z] = Math.Min(brushed[x, z], (byte)150);
                }
            }
            TiledBuffer<byte> now = brushed.ToBuffer();
            bool[] dirty = new bool[16];
            LayerStack taken = LayerStacks.TakeChanges(document, stack, composite, now, 1, "paint", dirty);
            Assert.AreNotSame(stack, taken);
            Assert.AreSame(stack.Layers[0], taken.Layers[0], "the lower layer is untouched");
            Assert.AreEqual(200, taken.Layers[1].Coverage![100, 100], 1, "at half strength the layer holds twice the change");
            Assert.AreEqual(150, Composite(document, taken)[100, 100], 1, "the composite is what the brush made");
            Assert.IsTrue(dirty.Any(tile => tile));

            Assert.AreSame(stack, LayerStacks.TakeChanges(document, stack, composite, now, 1, "ground2", new bool[16]), "no such layer: nothing changes");
            LayerStack locked = LayerStackEdits.SetLocked(stack, "paint", true);
            Assert.AreSame(locked, LayerStacks.TakeChanges(document, locked, composite, now, 1, "paint", new bool[16]), "a locked layer is not painted");
        }

        [TestMethod]
        public void StackEditsKeepTheBaseAndNames()
        {
            LayerStack stack = LayerStack.Empty(LayerTarget.Mask("forest"));
            stack = LayerStackEdits.Add(stack, Full("a", LayerCombine.Add, 1));
            stack = LayerStackEdits.Add(stack, Full("b", LayerCombine.Add, 2));
            stack = LayerStackEdits.Add(stack, Full("c", LayerCombine.Add, 3), above: "a");
            CollectionAssert.AreEqual(new[] { "a", "c", "b" }, stack.Layers.Select(layer => layer.Name).ToArray());
            stack = LayerStackEdits.Move(stack, "b", 0);
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, stack.Layers.Select(layer => layer.Name).ToArray(), "without a base any place");
            Assert.ThrowsException<ArgumentException>(() => LayerStackEdits.Add(stack, Full("A", LayerCombine.Add, 1)), "names are unique without case");
            Assert.ThrowsException<ArgumentException>(() => LayerStackEdits.Add(stack, new Layer { Name = "curve", Source = new CurveSource(new HeightCurve()) }), "a curve fits heights only");
            Assert.ThrowsException<ArgumentException>(() => LayerStackEdits.Add(stack, Full("base", LayerCombine.Replace, 0) with { IsBase = true }), "a base is the lowest");
            stack = LayerStackEdits.Rename(stack, "c", "cc");
            stack = LayerStackEdits.Remove(stack, "a");
            CollectionAssert.AreEqual(new[] { "b", "cc" }, stack.Layers.Select(layer => layer.Name).ToArray());
            Assert.AreEqual("d", LayerStackEdits.FreeName(stack, "d"));
            Assert.AreEqual("b 2", LayerStackEdits.FreeName(stack, "b"));

            MapDocument heights = HeightLayerEdits.Start(Flat());
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.Move(heights, "Base", 1));
            Assert.ThrowsException<ArgumentException>(() => LayerStackEdits.SetCombine(heights.HeightStack, "Base", LayerCombine.Add));
            Assert.IsNotNull(LayerStacks.PaintProblem(heights.HeightStack, "nothing"));
            Assert.IsNull(LayerStacks.PaintProblem(heights.HeightStack, "Base"), "the base is painted");
        }

        [TestMethod]
        public void HeightsSubtractAndMultiplyAndASubtractingSculptLayerKeepsItsLook()
        {
            EditSession session = new EditSession(Flat(35));
            session.Edit("Add", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewSculpt(d, "dig") with
            {
                Values = TiledBuffer<float>.Create(257, 257).ToBuilder() is var values && Set(values, 40, 40, 10) ? values.ToBuffer() : null,
            }));
            Assert.AreEqual(45, session.Document.Terrain.Heightmap.GetHeight(40, 40), 1e-3);
            session.Edit("Subtract", d => HeightLayerEdits.SetCombine(d, "dig", LayerCombine.Subtract));
            Assert.AreEqual(45, session.Document.Terrain.Heightmap.GetHeight(40, 40), 1e-3, "the look stays: the differences turn round");
            Assert.AreEqual(-10, session.Document.HeightStack.Layers[1].Values![40, 40], 1e-3);
            session.Edit("Replace", d => HeightLayerEdits.SetCombine(d, "dig", LayerCombine.Replace));
            Assert.AreEqual(45, session.Document.Terrain.Heightmap.GetHeight(40, 40), 1e-3);
            Assert.AreEqual(35, session.Document.Terrain.Heightmap.GetHeight(10, 10), 1e-3);

            session.Edit("Factor", d => HeightLayerEdits.Add(d, new Layer
            {
                Name = "factor", Source = LayerSource.Painted, Combine = LayerCombine.Multiply, Values = TiledBuffer<float>.Create(257, 257, 0.5f),
            }));
            Assert.AreEqual(17.5f, session.Document.Terrain.Heightmap.GetHeight(10, 10), 1e-2, "35 times a half");
        }

        private static bool Set(TiledBuffer<float>.Builder values, int x, int z, float value)
        {
            values[x, z] = value;
            return true;
        }

        [TestMethod]
        public void FormatSixKeepsTheStackBesideItsLayersAndTheComposite()
        {
            MapDocument document = HeightLayerEdits.Start(Flat(35));
            document = HeightLayerEdits.Add(document, HeightLayerEdits.NewSculpt(document, "Sculpt"));
            document = HeightLayerEdits.Add(document, new Layer { Name = "Lift", Source = new MaskRefSource(LayerStacks.StratumKey(2), 0.25f, true, 4) });
            document = HeightLayers.Recomposite(document);
            string project = ProjectFolders.New("stacks");
            MapProjectStore.Save(document, project);

            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "heightmap.json")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "heightmap", "Base.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "heightmap", "Sculpt.png")));
            Assert.IsFalse(File.Exists(Path.Combine(project, "stacks", "heightmap", "Lift.png")), "a mask reference has no file");
            Assert.IsTrue(File.Exists(Path.Combine(project, MapProjectLayout.Heightmap)), "the composite is written");
            Assert.IsFalse(Directory.Exists(Path.Combine(project, "layers")));
            JsonObject map = JsonNode.Parse(File.ReadAllText(Path.Combine(project, MapProjectLayout.Map)))!.AsObject();
            Assert.AreEqual(6, map["formatVersion"]!.GetValue<int>());
            JsonObject index = JsonNode.Parse(File.ReadAllText(Path.Combine(project, "stacks", "heightmap.json")))!.AsObject();
            Assert.AreEqual("heightmap", index["target"]!.GetValue<string>());
            Assert.AreEqual("painted", index["layers"]![0]!["source"]!.GetValue<string>());
            Assert.IsTrue(index["layers"]![0]!["base"]!.GetValue<bool>());

            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(new MaskRefSource(LayerStacks.StratumKey(2), 0.25f, true, 4), loaded.HeightStack.Layers[2].Source);
            CollectionAssert.AreEqual(document.Terrain.Heightmap.Samples.ToArray(), loaded.Terrain.Heightmap.Samples.ToArray());

            // without layers the stack's files go
            MapProjectStore.Save(HeightLayerEdits.Bake(loaded), project, loaded);
            Assert.IsFalse(Directory.Exists(Path.Combine(project, "stacks")));
        }

        [TestMethod]
        public void AProjectOfFormatFiveOpensUnchangedAndItsSaveWritesFormatSix()
        {
            // a format 5 project: the layers in layers/, as editors before format 6 wrote them
            MapDocument document = HeightLayerEdits.Start(Flat(35));
            TiledBuffer<float>.Builder hill = TiledBuffer<float>.Create(257, 257).ToBuilder();
            hill[40, 40] = 12.5f;
            document = HeightLayerEdits.Add(document, HeightLayerEdits.NewSculpt(document, "hill") with { Values = hill.ToBuffer() });
            HeightCurve curve = new HeightCurve { Profile = CurveProfile.Road, Width = 4, Falloff = 4, Points = [new CurvePoint(100, 100, 30), new CurvePoint(150, 120, 30)] };
            document = HeightLayerEdits.Add(document, HeightLayerEdits.NewCurve(document, "road", curve));
            document = HeightLayerEdits.SetStrength(HeightLayerEdits.SetLocked(document, "hill", true), "road", 0.75f);
            document = HeightLayers.Recomposite(document);
            string project = ProjectFolders.New("format5");
            MapProjectStore.Save(document, project);
            Directory.CreateDirectory(Path.Combine(project, "layers"));
            File.Move(Path.Combine(project, "stacks", "heightmap", "Base.png"), Path.Combine(project, "layers", "Base.png"));
            File.Move(Path.Combine(project, "stacks", "heightmap", "hill.png"), Path.Combine(project, "layers", "hill.png"));
            Directory.Delete(Path.Combine(project, "stacks"), recursive: true);
            File.WriteAllText(Path.Combine(project, "layers", "layers.json"), """
                {
                  "layers": [
                    { "name": "Base", "kind": "Base", "combine": "Replace", "offset": 0 },
                    { "name": "hill", "kind": "Sculpt", "combine": "Add", "locked": true, "offset": -256 },
                    { "name": "road", "kind": "Curve", "combine": "Replace", "strength": 0.75,
                      "curve": { "profile": "Road", "width": 4, "falloff": 4, "points": [[100, 100, 30], [150, 120, 30]] } }
                  ]
                }
                """);
            string mapFile = Path.Combine(project, MapProjectLayout.Map);
            File.WriteAllText(mapFile, File.ReadAllText(mapFile).Replace("\"formatVersion\": 6", "\"formatVersion\": 5", StringComparison.Ordinal));

            MapDocument opened = MapProjectStore.Load(project);
            CollectionAssert.AreEqual(new[] { "Base", "hill", "road" }, opened.HeightStack.Layers.Select(layer => layer.Name).ToArray());
            Assert.IsTrue(opened.HeightStack.Layers[0].IsBase);
            Assert.IsInstanceOfType<PaintedSource>(opened.HeightStack.Layers[1].Source);
            Assert.IsTrue(opened.HeightStack.Layers[1].Locked);
            Assert.AreEqual(curve, opened.HeightStack.Layers[2].Curve);
            Assert.AreEqual(0.75f, opened.HeightStack.Layers[2].Strength);
            CollectionAssert.AreEqual(document.Terrain.Heightmap.Samples.ToArray(), opened.Terrain.Heightmap.Samples.ToArray(), "the terrain looks the same");

            // the save writes the stack and takes the old files away
            EditedMap map = EditedMap.OpenProject(project);
            map.Session.Edit("Strength", d => HeightLayerEdits.SetStrength(d, "road", 0.5f));
            map.Save();
            Assert.IsTrue(File.Exists(Path.Combine(project, MapProjectLayout.HeightStackIndex)));
            Assert.IsTrue(File.Exists(Path.Combine(project, MapProjectLayout.StackLayer("heightmap", "hill"))));
            Assert.IsFalse(Directory.Exists(Path.Combine(project, "layers")), "layers/ is gone");
            MapDocument again = MapProjectStore.Load(project);
            Assert.AreEqual(0.5f, again.HeightStack.Layers[2].Strength);
            CollectionAssert.AreEqual(map.Session.Document.Terrain.Heightmap.Samples.ToArray(), again.Terrain.Heightmap.Samples.ToArray());
        }

        [TestMethod]
        public void AnotherToolChangingALayerFileOfTheStackIsReadAgain()
        {
            MapDocument document = HeightLayers.Recomposite(HeightLayerEdits.Add(HeightLayerEdits.Start(Flat(35)), HeightLayerEdits.NewSculpt(Flat(35), "Sculpt")));
            string project = ProjectFolders.New("stack-reload");
            MapProjectStore.Save(document, project);
            Assert.AreSame(document, MapProjectStore.Reload(document, project, [MapProjectLayout.HeightStackIndex, MapProjectLayout.StackLayer("heightmap", "Sculpt")]),
                "the editor's own files change nothing");

            // a tool writes 5 units into the sculpt layer (sample 0 is -256 for an adding layer)
            ushort[] samples = Enumerable.Repeat((ushort)((256 + 5) * 128), 257 * 257).ToArray();
            ProjectImages.WriteGray16(Path.Combine(project, MapProjectLayout.StackLayer("heightmap", "Sculpt")), 257, 257, samples);
            MapDocument reloaded = HeightLayers.Follow(document, MapProjectStore.Reload(document, project, [MapProjectLayout.StackLayer("heightmap", "Sculpt")]), null);
            Assert.AreEqual(40, reloaded.Terrain.Heightmap.GetHeight(10, 10), 1e-2);
        }
    }
}
