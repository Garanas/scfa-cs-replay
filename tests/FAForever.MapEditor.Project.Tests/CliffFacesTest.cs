using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Cliff faces that block walkability (user story T-4, D3): the ogrids under a cliff curve's face
    /// count as marked blocked for walkability and the export, unless marked open by hand.
    /// </summary>
    [TestClass]
    public class CliffFacesTest
    {
        private const int Size = 256;

        /// <summary>
        /// A gentle cliff across the whole map at x = 100, raising its left (the west, walking south
        /// to north, from z = -10 to beyond the map) by 0.5 over a face of 4 ogrids:
        /// far too gentle to block by its slope.
        /// </summary>
        private static HeightCurve Cliff(bool blocks = true) => new HeightCurve
        {
            Profile = CurveProfile.Cliff, Width = 20, Falloff = 10, CliffFace = 4, BlocksWalkability = blocks,
            Points = [new CurvePoint(100, -10, 0.5f), new CurvePoint(100, Size + 10, 0.5f)],
        };

        private static MapDocument MapWith(HeightCurve curve)
        {
            MapDocument document = HeightLayerEdits.Start(NewMap.Create(new NewMapSettings { Size = Size, Height = 40, Water = false }));
            return HeightLayers.Recomposite(HeightLayerEdits.Add(document, HeightLayerEdits.NewCurve(document, "cliff", curve)));
        }

        /// <summary>
        /// Whether land units can walk straight across the face along row <paramref name="z"/>. (The
        /// regions on both sides stay one: the map's last row holds no terrain type the game reads,
        /// so no face blocks it.)
        /// </summary>
        private static bool Joined(MapNavigation navigation, int z = 128)
        {
            Assert.IsTrue(Walkability.IsPassable(navigation.Land, 80, z) && Walkability.IsPassable(navigation.Land, 120, z), "both sides are walkable ground");
            return Enumerable.Range(80, 41).All(x => Walkability.IsPassable(navigation.Land, x, z));
        }

        [TestMethod]
        public void AGentleCliffsFaceBlocksTheNavigationalMeshOnlyWhenItSaysSo()
        {
            MapDocument blocking = MapWith(Cliff());
            int[] face = CliffFaces.Of(Cliff(), Size, Size);
            Assert.IsTrue(face.Length >= Size * 2, $"a wall at least two ogrids thick, got {face.Length}");
            Assert.IsTrue(face.All(i => Math.Abs(i % Size + 0.5f - 100) <= 2.01f), "within half the face of the drop");
            Assert.AreEqual(face.Length, CliffFaces.Count(blocking));
            Assert.IsFalse(Joined(Walkability.Generate(blocking)), "the face blocks the way across");

            MapDocument open = MapWith(Cliff(blocks: false));
            Assert.AreEqual(0, CliffFaces.Count(open));
            Assert.IsTrue(Joined(Walkability.Generate(open)), "the slope alone lets units through");

            // another profile, a hidden layer: no face
            Assert.AreEqual(0, CliffFaces.Of(Cliff() with { Profile = CurveProfile.Ridge }, Size, Size).Length);
            Assert.AreEqual(0, CliffFaces.Count(HeightLayerEdits.SetVisible(blocking, "cliff", false)));
        }

        [TestMethod]
        public void AHandMarkOpenWins()
        {
            MapDocument document = MapWith(Cliff());
            TiledBuffer<byte>.Builder marks = PaintTarget.WalkabilityMarks.Read(document).ToBuilder();
            for (int z = 96; z < 160; z++)
            {
                for (int x = 90; x < 110; x++)
                {
                    marks[x, z] = WalkabilityMarks.Open;
                }
            }
            MapDocument marked = PaintTarget.WalkabilityMarks.Write(document, marks.ToBuffer());
            Assert.IsTrue(Joined(Walkability.Generate(marked)), "a passage marked open through the face");
            Assert.AreEqual(WalkabilityMarks.None, marked.WalkabilityMarks![100, 20], "the face is not written into the marks");

            TiledBuffer<byte> shown = WalkabilityMarks.WithCliffFaces(marked)!;
            Assert.AreEqual(WalkabilityMarks.Blocked, shown[100, 20], "shown blocked where no mark is");
            Assert.AreEqual(WalkabilityMarks.Open, shown[100, 128], "the open mark shows");

            Checks.Problem[] problems = [.. Checks.MapChecks.OpenMarksTheGameBlocks(new Checks.ProblemContext(marked))];
            Assert.IsTrue(problems.Any(problem => problem.Severity == Checks.ProblemSeverity.Info && problem.Message.Contains("cliff faces")));
        }

        [TestMethod]
        [DataRow(MapSymmetryKind.MirroredLeftRight, 2)]
        [DataRow(MapSymmetryKind.Rotational, 2)]
        public void TheImagesFacesBlockToo(MapSymmetryKind kind, int count)
        {
            HeightCurve curve = Cliff() with
            {
                Points = [new CurvePoint(60, 40, 3), new CurvePoint(70, 120, 3)],
                NoiseAmplitude = 1.5f, NoiseScale = 8, NoiseSeed = 4,
                Symmetry = new SymmetrySetting(kind, count),
            };
            HashSet<int> cells = [.. CliffFaces.Of(curve, Size, Size)];
            int alone = CliffFaces.Of(curve with { Symmetry = null }, Size, Size).Length;
            Assert.IsTrue(cells.Count >= alone * 2 - 4, "the curve's and its image's");
            MapSymmetryTransform transform = MapSymmetry.Transforms(kind, count, Size, Size).Skip(1).Single();
            int matched = 0;
            foreach (int i in CliffFaces.Of(curve with { Symmetry = null }, Size, Size))
            {
                System.Numerics.Vector2 image = transform.Apply(new System.Numerics.Vector2(i % Size + 0.5f, i / Size + 0.5f));
                matched += cells.Contains((int)MathF.Floor(image.Y) * Size + (int)MathF.Floor(image.X)) ? 1 : 0;
            }
            Assert.IsTrue(matched >= alone * 0.95, $"the image's face is the image of the curve's, noise included: {matched} of {alone}");
        }

        [TestMethod]
        public void AClosedCliffBlocksItsRim()
        {
            HeightCurve mesa = Cliff() with
            {
                Closed = true,
                Points = [new CurvePoint(100, 100, 2), new CurvePoint(160, 100, 2), new CurvePoint(160, 160, 2), new CurvePoint(100, 160, 2)],
            };
            Assert.IsTrue(mesa.Fills);
            int[] face = CliffFaces.Of(mesa, Size, Size);
            Assert.IsTrue(face.Length > 200);
            Assert.IsFalse(face.Contains(130 * Size + 130), "not its inside");
            Assert.IsFalse(face.Contains(20 * Size + 20), "not far outside");
            MapNavigation navigation = Walkability.Generate(MapWith(mesa));
            Assert.AreNotEqual(navigation.Land.Cells[130 * Size + 130], navigation.Land.Cells[20 * Size + 20], "the mesa's top is cut off");
        }

        [TestMethod]
        public void TheExportWritesTheBlockingTypeUnderTheFace()
        {
            MapDocument document = MapWith(Cliff());
            Scmap scmap = MapExporter.Export(document).Scmap;
            int x = CliffFaces.Of(Cliff(), Size, Size).First(i => i / Size == 50) % Size;
            Assert.AreEqual(WalkabilityMarks.BlockingType, scmap.TerrainTypes[51 * Size + x + 1], "at the ogrid's far corner");
            Assert.AreNotEqual(WalkabilityMarks.BlockingType, scmap.TerrainTypes[51 * Size + 21]);
            Assert.AreNotEqual(WalkabilityMarks.BlockingType, document.Terrain.Types[x + 1, 51], "the map's own types stay");
            MapNavigation game = NavGenerator.Generate(scmap.Heightmap, null, scmap.TerrainTypes, []);
            Assert.IsFalse(Joined(game), "the game blocks it too");
        }

        [TestMethod]
        public void TheSettingIsKeptInTheProjectAndOlderCurvesDoNotBlock()
        {
            string folder = ProjectFolders.New("cliff-faces");
            MapDocument document = MapWith(Cliff());
            MapProjectStore.Save(document, folder);
            Assert.IsTrue(MapProjectStore.Load(folder).HeightStack.Layers.Single(layer => layer.Name == "cliff").Curve!.BlocksWalkability);
            string json = Path.Combine(folder, "stacks", "heightmap.json");
            string text = File.ReadAllText(json);
            StringAssert.Contains(text, "blocksWalkability");
            // a project written before the setting: the property is absent
            string older = System.Text.RegularExpressions.Regex.Replace(text, @",\s*""blocksWalkability""\s*:\s*true|""blocksWalkability""\s*:\s*true\s*,", "");
            Assert.AreNotEqual(text, older);
            File.WriteAllText(json, older);
            Assert.IsFalse(MapProjectStore.Load(folder).HeightStack.Layers.Single(layer => layer.Name == "cliff").Curve!.BlocksWalkability);
        }

        [TestMethod]
        public void ACliffEditOrTheSettingMakesWalkabilityOutOfDate()
        {
            MapDocument document = MapWith(Cliff());
            GeneratedResults results = new GeneratedResults();
            WalkabilityGenerator generator = new WalkabilityGenerator();
            results.Register(generator);
            results.Generate(generator.Id, document);
            Assert.IsFalse(results.ResultOf(generator.Id)!.IsStale(document));
            MapDocument off = HeightLayerEdits.SetCurve(document, "cliff", Cliff(blocks: false));
            CollectionAssert.Contains(results.ResultOf(generator.Id)!.ChangedInputs(off).ToList(), GeneratedInput.CliffFaces.Name);
        }
    }
}
