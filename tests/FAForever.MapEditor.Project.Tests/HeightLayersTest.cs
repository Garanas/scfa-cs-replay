using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Height layers (user story T-5): a stack that adds up to the heightmap, kept so after every
    /// change, tile by tile; the sculpt tool's work goes into the chosen layer; the project keeps
    /// them; the export takes their sum.
    /// </summary>
    [TestClass]
    public class HeightLayersTest
    {
        private const float Scale = 1f / 128;

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Flat(float height = 35) => NewMap.Create(new NewMapSettings { Size = 256, Height = height });

        private static float HeightAt(MapDocument document, int x, int z) => document.Terrain.Heightmap.GetHeight(x, z);

        /// <summary>
        /// A layer that holds <paramref name="value"/> in a square around (x, z), 0 elsewhere.
        /// </summary>
        private static Layer Square(MapDocument document, string name, LayerCombine combine, float value, int x, int z, int half, bool covered = false)
        {
            TiledBuffer<float>.Builder values = TiledBuffer<float>.Create(document.Width + 1, document.Height + 1).ToBuilder();
            TiledBuffer<byte>.Builder coverage = TiledBuffer<byte>.Create(document.Width + 1, document.Height + 1).ToBuilder();
            for (int j = z - half; j <= z + half; j++)
            {
                for (int i = x - half; i <= x + half; i++)
                {
                    values[i, j] = value;
                    coverage[i, j] = 255;
                }
            }
            return new Layer
            {
                Name = name,
                Source = LayerSource.Painted,
                Combine = combine,
                Values = values.ToBuffer(),
                Coverage = covered ? coverage.ToBuffer() : null,
            };
        }

        [TestMethod]
        public void TheBaseAloneIsTheHeightmapSampleForSample()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            MapDocument started = HeightLayers.Recomposite(HeightLayerEdits.Start(document));

            Assert.AreEqual(1, started.HeightStack.Count);
            Assert.IsTrue(started.HeightStack.Layers[0].IsBase);
            Assert.AreSame(document.Terrain.Heightmap.Samples, started.Terrain.Heightmap.Samples, "the sum is the heightmap: no tile changes");
            Assert.AreSame(document, HeightLayerEdits.Bake(document), "a map without layers stays as it is");
        }

        [TestMethod]
        public void LayersCombineByTheirWayStrengthAndMask()
        {
            MapDocument document = HeightLayerEdits.Start(Flat(35));
            document = HeightLayerEdits.Add(document, Square(document, "add", LayerCombine.Add, 10, 40, 40, 2));
            document = HeightLayerEdits.Add(document, Square(document, "max", LayerCombine.Maximum, 50, 80, 80, 2, covered: true));
            document = HeightLayerEdits.Add(document, Square(document, "min", LayerCombine.Minimum, 20, 120, 120, 2, covered: true));
            document = HeightLayerEdits.Add(document, Square(document, "replace", LayerCombine.Replace, 5, 160, 160, 2, covered: true));
            document = HeightLayers.Recomposite(document);

            Assert.AreEqual(45, HeightAt(document, 40, 40), 1e-3, "add");
            Assert.AreEqual(50, HeightAt(document, 80, 80), 1e-3, "maximum");
            Assert.AreEqual(20, HeightAt(document, 120, 120), 1e-3, "minimum");
            Assert.AreEqual(5, HeightAt(document, 160, 160), 1e-3, "replace");
            Assert.AreEqual(35, HeightAt(document, 10, 10), 1e-3, "nothing elsewhere");

            document = HeightLayers.Recomposite(HeightLayerEdits.SetStrength(document, "replace", 0.5f));
            Assert.AreEqual(20, HeightAt(document, 160, 160), 1e-2, "half way from 35 to 5");

            document = HeightLayers.Recomposite(HeightLayerEdits.SetVisible(document, "add", false));
            Assert.AreEqual(35, HeightAt(document, 40, 40), 1e-3, "a hidden layer counts for nothing");

            document = MaskLayers.Add(document, "half");
            TiledBuffer<byte>.Builder mask = document.Masks["half"].ToBuilder();
            for (int z = 0; z < mask.Height; z++)
            {
                for (int x = 0; x < mask.Width; x++)
                {
                    mask[x, z] = 51;
                }
            }
            document = document with { Masks = document.Masks.SetItem("half", mask.ToBuffer()) };
            document = HeightLayers.Recomposite(HeightLayerEdits.SetMask(document, "max", LayerStacks.MaskKey("half")));
            Assert.AreEqual(38, HeightAt(document, 80, 80), 1e-2, "a fifth of the way from 35 to 50");
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.SetMask(document, "max", LayerStacks.MaskKey("none")));
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.SetStrength(document, "Base", 0.5f));
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.Remove(document, "Base"));
        }

        [TestMethod]
        public void TheSessionKeepsTheHeightmapTheSumTileByTile()
        {
            EditSession session = new EditSession(Flat(35));
            session.Edit("Add a layer", d => HeightLayerEdits.Add(d, Square(d, "hill", LayerCombine.Add, 10, 40, 40, 2)));
            MapDocument added = session.Document;
            Assert.AreEqual(45, HeightAt(added, 40, 40), 1e-3, "the heightmap follows the step that adds the layer");

            session.Edit("Raise the hill", d => HeightLayerEdits.With(d, 1, Square(d, "hill", LayerCombine.Add, 20, 40, 40, 2)));
            MapDocument raised = session.Document;
            Assert.AreEqual(55, HeightAt(raised, 40, 40), 1e-3);
            Assert.AreEqual(1, raised.Terrain.Heightmap.Samples.ChangedTiles(added.Terrain.Heightmap.Samples).Count(), "only the tile the change touched");

            session.Undo();
            Assert.AreEqual(45, HeightAt(session.Document, 40, 40), 1e-3, "undo brings the sum back with the layer");

            session.Edit("Bake", HeightLayerEdits.Bake);
            Assert.IsTrue(session.Document.HeightStack.IsEmpty);
            Assert.AreEqual(45, HeightAt(session.Document, 40, 40), 1e-3, "baking keeps the sum");
        }

        [TestMethod]
        public void TheSculptToolWritesIntoTheChosenLayer()
        {
            EditSession session = new EditSession(Flat(35));
            session.Edit("Add a layer", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewSculpt(d, "Sculpt")));
            MapDocument before = session.Document;

            // without a chosen layer the heightmap stays the sum
            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 6, Strength = 2 }.Apply(stroke.Heightmap, Scale, 100, 100);
                Assert.AreEqual(35, HeightAt(stroke.Document, 100, 100), 1e-3, "no layer chosen: the change is gone");
                Assert.IsFalse(stroke.Commit());
            }

            session.SculptLayer = "Sculpt";
            Assert.IsNull(HeightLayers.SculptProblem(session.Document, "Sculpt"));
            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                HeightBrush brush = new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 6, Strength = 2 };
                brush.Apply(stroke.Heightmap, Scale, 100, 100);
                float first = HeightAt(stroke.Document, 100, 100);
                Assert.AreEqual(37, first, 0.05, "the brush's change shows");
                brush.Apply(stroke.Heightmap, Scale, 100, 100);
                Assert.AreEqual(39, HeightAt(stroke.Document, 100, 100), 0.05, "the next dab starts from the sum");
                Assert.IsTrue(stroke.Commit());
            }
            MapDocument sculpted = session.Document;
            Assert.AreSame(before.HeightStack.Layers[0], sculpted.HeightStack.Layers[0], "the base is untouched");
            Assert.AreEqual(4, sculpted.HeightStack.Layers[1].Values![100, 100], 0.05, "the layer holds the difference");

            session.Edit("Hide", d => HeightLayerEdits.SetVisible(d, "Sculpt", false));
            Assert.AreEqual(35, HeightAt(session.Document, 100, 100), 1e-3, "hiding the layer takes its work away");
            Assert.IsNotNull(HeightLayers.SculptProblem(session.Document, "Sculpt"), "a hidden layer is not sculpted");
            session.Undo();

            // a weaker layer gets more, so that the sum is what the brush made
            session.Edit("Half", d => HeightLayerEdits.SetStrength(d, "Sculpt", 0.5f));
            Assert.AreEqual(37, HeightAt(session.Document, 100, 100), 0.05);
            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                new HeightBrush { Mode = HeightBrushMode.Raise, Radius = 6, Strength = 1 }.Apply(stroke.Heightmap, Scale, 100, 100);
                stroke.Commit();
            }
            Assert.AreEqual(38, HeightAt(session.Document, 100, 100), 0.05);

            session.Edit("Lock", d => HeightLayerEdits.SetLocked(d, "Sculpt", true));
            Assert.IsNotNull(HeightLayers.SculptProblem(session.Document, "Sculpt"));
        }

        [TestMethod]
        public void TurningASculptLayerToReplaceKeepsItsLook()
        {
            EditSession session = new EditSession(Flat(35));
            session.Edit("Add", d => HeightLayerEdits.Add(d, Square(d, "hill", LayerCombine.Add, 10, 40, 40, 2)));
            session.Edit("Replace", d => HeightLayerEdits.SetCombine(d, "hill", LayerCombine.Replace));
            Layer layer = session.Document.HeightStack.Layers[1];
            Assert.AreEqual(LayerCombine.Replace, layer.Combine);
            Assert.IsNotNull(layer.Coverage);
            Assert.AreEqual(45, HeightAt(session.Document, 40, 40), 1e-3, "the hill stays");
            Assert.AreEqual(35, HeightAt(session.Document, 10, 10), 1e-3, "the rest does not become 0");

            session.Edit("Add again", d => HeightLayerEdits.SetCombine(d, "hill", LayerCombine.Add));
            Assert.AreEqual(45, HeightAt(session.Document, 40, 40), 1e-3);
            Assert.AreEqual(0, session.Document.HeightStack.Layers[1].Values![10, 10], 1e-3);
        }

        [TestMethod]
        public void MasksThatChangeAddTheirTilesUpAgain()
        {
            EditSession session = new EditSession(Flat(35));
            session.Edit("Mask", d => MaskLayers.Add(d, "rivers"));
            session.Edit("Add", d => HeightLayerEdits.Add(d, Square(d, "lift", LayerCombine.Add, 10, 128, 128, 100)));
            session.Edit("Masked", d => HeightLayerEdits.SetMask(d, "lift", LayerStacks.MaskKey("rivers")));
            MapDocument masked = session.Document;
            Assert.AreEqual(35, HeightAt(masked, 128, 128), 1e-3, "an empty mask: nothing");

            session.Edit("Paint", d =>
            {
                TiledBuffer<byte>.Builder mask = d.Masks["rivers"].ToBuilder();
                new MaskBrush { Radius = 4, Strength = 1, Falloff = BrushFalloff.Sharp }.Apply(mask, d.Width, d.Height, 128, 128);
                return d with { Masks = d.Masks.SetItem("rivers", mask.ToBuffer()) };
            });
            Assert.AreEqual(45, HeightAt(session.Document, 128, 128), 0.1, "the layer works where the mask is painted");
            Assert.IsTrue(session.Document.Terrain.Heightmap.Samples.ChangedTiles(masked.Terrain.Heightmap.Samples).Count() <= 4, "only around the dab");

            session.Edit("Rename", d => MaskLayers.Rename(d, "rivers", "water"));
            Assert.AreEqual(LayerStacks.MaskKey("water"), session.Document.HeightStack.Layers[1].Clip, "a renamed mask stays the layer's");
        }

        [TestMethod]
        public void TheProjectKeepsTheLayersAndTheExportTakesTheirSum()
        {
            MapDocument document = HeightLayerEdits.Start(Flat(35));
            document = HeightLayerEdits.Add(document, Square(document, "hill", LayerCombine.Add, 12.5f, 40, 40, 3));
            document = HeightLayerEdits.Add(document, Square(document, "plateau", LayerCombine.Replace, 60, 90, 90, 5, covered: true));
            document = HeightLayerEdits.SetStrength(document, "plateau", 0.75f);
            document = HeightLayerEdits.SetLocked(document, "hill", true);
            HeightCurve curve = new HeightCurve { Profile = CurveProfile.Ridge, Width = 4, Falloff = 6, Points = [new CurvePoint(150, 50, 8), new CurvePoint(200, 80, 8), new CurvePoint(220, 140, 4)] };
            document = HeightLayerEdits.Add(document, HeightLayerEdits.NewCurve(document, "ridge", curve));
            document = HeightLayers.Recomposite(HeightLayerEdits.SetVisible(document, "ridge", true));

            string project = ProjectFolders.New("layers");
            MapProjectStore.Save(document, project);
            Assert.IsTrue(File.Exists(Path.Combine(project, MapProjectLayout.HeightStackIndex)));
            Assert.IsTrue(File.Exists(Path.Combine(project, MapProjectLayout.StackLayer("heightmap", "hill"))));
            Assert.IsTrue(File.Exists(Path.Combine(project, MapProjectLayout.StackLayerCoverage("heightmap", "plateau"))));
            Assert.IsFalse(File.Exists(Path.Combine(project, MapProjectLayout.StackLayer("heightmap", "ridge"))), "a curve's values follow from its curve");

            MapDocument loaded = MapProjectStore.Load(project);
            CollectionAssert.AreEqual(document.HeightStack.Layers.Select(layer => layer.Name).ToArray(), loaded.HeightStack.Layers.Select(layer => layer.Name).ToArray());
            Assert.AreEqual(0.75f, loaded.HeightStack.Layers[2].Strength);
            Assert.IsTrue(loaded.HeightStack.Layers[1].Locked);
            Assert.AreEqual(curve, loaded.HeightStack.Layers[3].Curve);
            CollectionAssert.AreEqual(document.Terrain.Heightmap.Samples.ToArray(), loaded.Terrain.Heightmap.Samples.ToArray(), "the same sum");
            CollectionAssert.AreEqual(document.HeightStack.Layers[1].Values!.ToArray(), loaded.HeightStack.Layers[1].Values!.ToArray(), "differences survive the PNG");

            // a save after a change writes only that layer
            string hill = Path.Combine(project, MapProjectLayout.StackLayer("heightmap", "hill"));
            File.SetLastWriteTimeUtc(hill, new DateTime(2000, 1, 1));
            MapDocument changed = HeightLayers.Recomposite(HeightLayerEdits.SetStrength(loaded, "plateau", 0.5f));
            MapProjectStore.Save(changed, project, loaded);
            Assert.AreEqual(new DateTime(2000, 1, 1), File.GetLastWriteTimeUtc(hill));

            // the game gets the sum
            IReadOnlyDictionary<string, byte[]> exported = MapExporter.ToBytes(document);
            Scmap scmap = ScmapParser.Parse(exported[exported.Keys.Single(key => key.EndsWith(".scmap", StringComparison.Ordinal))]);
            CollectionAssert.AreEqual(document.Terrain.Heightmap.Samples.ToArray(), scmap.Heightmap.Samples);
            Assert.AreEqual(47.5f, scmap.Heightmap.Samples[40 * 257 + 40] * scmap.Heightmap.Scale, 1e-3);

            // without layers the project has no index
            MapProjectStore.Save(HeightLayerEdits.Bake(changed), project, changed);
            Assert.IsFalse(File.Exists(Path.Combine(project, MapProjectLayout.HeightStackIndex)));
            Assert.IsFalse(File.Exists(hill));
        }

        [TestMethod]
        public void OtherToolsChangeLayersAndImportedSources()
        {
            MapDocument document = HeightLayerEdits.Start(Flat(35));
            string project = ProjectFolders.New("layers-reload");
            MapProjectStore.Save(document, project);
            string source = Path.Combine(project, MapProjectLayout.ProceduralTemp, "gaea.png");
            ushort[] high = Enumerable.Repeat((ushort)(50 * 128), 129 * 129).ToArray();
            ProjectImages.WriteGray16(source, 129, 129, high);
            ushort[] samples = HeightmapFiles.ReadResampled(document, source, out _);
            document = HeightLayers.Recomposite(HeightLayerEdits.Add(document, HeightLayerEdits.NewImported(document, "gaea", samples, "procedural/temp/gaea.png")));
            Assert.AreEqual(50, HeightAt(document, 10, 10), 1e-3, "an imported layer replaces");
            MapProjectStore.Save(document, project);

            foreach (string file in new[] { MapProjectLayout.HeightStackIndex, MapProjectLayout.StackLayer("heightmap", "gaea"), "procedural/temp/gaea.png" })
            {
                Assert.AreSame(document, MapProjectStore.Reload(document, project, [file]), $"the editor's own files change nothing: {file}");
            }

            ProjectImages.WriteGray16(source, 129, 129, Enumerable.Repeat((ushort)(40 * 128), 129 * 129).ToArray());
            MapDocument reloaded = HeightLayers.Follow(document, MapProjectStore.Reload(document, project, ["procedural/temp/gaea.png"]), null);
            Assert.AreEqual(40, HeightAt(reloaded, 10, 10), 1e-3, "the source read again");

            // with layers another tool's heightmap becomes the base
            MapDocument hidden = HeightLayers.Recomposite(HeightLayerEdits.SetVisible(reloaded, "gaea", false));
            ProjectImages.WriteGray16(Path.Combine(project, MapProjectLayout.Heightmap), 257, 257, Enumerable.Repeat((ushort)(20 * 128), 257 * 257).ToArray());
            MapDocument based = HeightLayers.Follow(hidden, MapProjectStore.Reload(hidden, project, [MapProjectLayout.Heightmap]), null);
            Assert.AreEqual(20, HeightAt(based, 10, 10), 1e-3);
            Assert.AreEqual(20, based.HeightStack.Layers[0].Values![10, 10], 1e-3);
        }

        [TestMethod]
        public void EditingACurveTileByTileGivesWhatAddingUpEverythingGives()
        {
            EditSession session = new EditSession(MapImporter.ImportFolder(ProjectFolders.HardFfa));
            HeightCurve curve = new HeightCurve { Profile = CurveProfile.Ridge, Points = [new CurvePoint(40, 100, 15)] };
            session.Edit("Curve", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewCurve(d, "ridge", curve)));
            foreach (CurvePoint point in new[] { new CurvePoint(80, 100, 15), new CurvePoint(120, 110, 15), new CurvePoint(140, 150, 15) })
            {
                curve = curve with { Points = curve.Points.Add(point) };
                session.Edit("Point", d => HeightLayerEdits.SetCurve(d, "ridge", curve));
                CollectionAssert.AreEqual(HeightLayers.Recomposite(session.Document with { HeightStack = session.Document.HeightStack with { Layers = [.. session.Document.HeightStack.Layers] } }).Terrain.Heightmap.Samples.ToArray(),
                    session.Document.Terrain.Heightmap.Samples.ToArray(), $"after {curve.Points.Length} points");
            }
        }

        [TestMethod]
        public void CurvesShapeTheTerrainAlongThem()
        {
            EditSession session = new EditSession(Flat(35));
            HeightCurve ridge = new HeightCurve { Profile = CurveProfile.Ridge, Width = 4, Falloff = 6, Points = [new CurvePoint(50, 128, 10), new CurvePoint(200, 128, 10)] };
            session.Edit("Curve", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewCurve(d, "ridge", ridge)));
            Assert.AreEqual(45, HeightAt(session.Document, 120, 128), 1e-3, "on the ridge");
            Assert.AreEqual(35, HeightAt(session.Document, 120, 140), 1e-3, "beyond width and falloff");
            float slope = HeightAt(session.Document, 120, 133);
            Assert.IsTrue(slope > 35 && slope < 45, "fading over the falloff");

            MapDocument before = session.Document;
            session.Edit("Move a point", d => HeightLayerEdits.SetCurve(d, "ridge", ridge with { Points = [new CurvePoint(50, 128, 10), new CurvePoint(200, 160, 10)] }));
            Assert.AreEqual(45, HeightAt(session.Document, 200, 160), 1e-3, "the terrain follows the curve");
            Assert.IsTrue(session.Document.Terrain.Heightmap.Samples.ChangedTiles(before.Terrain.Heightmap.Samples).Count() < 16, "only near the curve");

            session.Edit("Road", d => HeightLayerEdits.SetCurve(d, "ridge", new HeightCurve { Profile = CurveProfile.Road, Width = 6, Falloff = 4, Points = [new CurvePoint(20, 20, 30), new CurvePoint(20, 120, 40)] }));
            Layer road = session.Document.HeightStack.Layers[1];
            Assert.AreEqual(LayerCombine.Replace, road.Combine, "a road replaces");
            Assert.AreEqual(30, HeightAt(session.Document, 20, 20), 1e-2);
            Assert.AreEqual(35, HeightAt(session.Document, 20, 70), 0.1, "half way between the points' heights");
            Assert.AreEqual(35, HeightAt(session.Document, 60, 70), 1e-3);

            session.Edit("Cliff", d => HeightLayerEdits.SetCurve(d, "ridge", new HeightCurve { Profile = CurveProfile.Cliff, Width = 8, Falloff = 10, Points = [new CurvePoint(100, 50, 20), new CurvePoint(100, 200, 20)] }));
            session.Edit("Add", d => HeightLayerEdits.SetCombine(d, "ridge", LayerCombine.Add));
            // walking down the map (z grows), the left in the top view is +x
            Assert.AreEqual(55, HeightAt(session.Document, 104, 120), 0.1, "high on the left");
            Assert.AreEqual(35, HeightAt(session.Document, 96, 120), 0.1, "low on the right");

            IReadOnlyList<CurvePoint> spline = HeightCurves.Spline(new HeightCurve { Points = [new CurvePoint(0, 0, 0), new CurvePoint(10, 5, 0), new CurvePoint(20, 0, 0)] });
            Assert.IsTrue(spline.Any(point => MathF.Abs(point.X - 10) < 1e-3 && MathF.Abs(point.Z - 5) < 1e-3), "the spline passes through its points");
        }

        [TestMethod]
        public void ACurveMirroredByTheSymmetryShapesItsImagesAndIsKept()
        {
            EditSession session = new EditSession(Flat(35));
            HeightCurve ridge = new HeightCurve
            {
                Profile = CurveProfile.Ridge, Width = 4, Falloff = 6,
                Points = [new CurvePoint(40, 60, 10), new CurvePoint(90, 70, 10)],
                Symmetry = new SymmetrySetting(MapSymmetryKind.Rotational, 4),
            };
            session.Edit("Curve", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewCurve(d, "ridge", ridge)));
            foreach (MapSymmetryTransform transform in MapSymmetry.Transforms(MapSymmetryKind.Rotational, 4, 256, 256))
            {
                System.Numerics.Vector2 image = transform.Apply(new System.Numerics.Vector2(60, 64));
                Assert.AreEqual(HeightAt(session.Document, 60, 64), HeightAt(session.Document, (int)MathF.Round(image.X), (int)MathF.Round(image.Y)), 0.05, $"the image at {image}");
            }
            Assert.AreEqual(4, HeightCurves.Parts(ridge, 256, 256).Count);

            // a cliff mirrored: its high side is the image of the curve's
            HeightCurve cliff = new HeightCurve
            {
                Profile = CurveProfile.Cliff, Width = 8, Falloff = 10,
                Points = [new CurvePoint(60, 50, 20), new CurvePoint(60, 200, 20)],
                Symmetry = new SymmetrySetting(MapSymmetryKind.MirroredLeftRight, 2),
            };
            session.Edit("Cliff", d => HeightLayerEdits.SetCurve(d, "ridge", cliff));
            Assert.AreEqual(55, HeightAt(session.Document, 64, 120), 0.1, "high on the curve's left");
            Assert.AreEqual(55, HeightAt(session.Document, 192, 120), 0.1, "and on the image of that side");
            Assert.AreEqual(35, HeightAt(session.Document, 200, 120), 0.1);

            EditedMap map = EditedMap.New(session.Document);
            map.SaveAs(ProjectFolders.New("mirrored-curve"));
            HeightCurve kept = EditedMap.OpenProject(map.ProjectFolder!).Document.HeightStack.Layers[1].Curve!;
            Assert.AreEqual(cliff.Symmetry, kept.Symmetry, "the project keeps the curve's symmetry");
            Assert.AreEqual(55, HeightAt(EditedMap.OpenProject(map.ProjectFolder!).Document, 192, 120), 0.1);
        }
    }
}
