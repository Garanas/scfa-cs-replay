using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The map maker's sketch (user story N-1): sheets and strokes as edits of the document, mirrored
    /// by the symmetry, rubbed out by an eraser, kept in <c>sketch/sketch.json</c> and never exported.
    /// </summary>
    [TestClass]
    public class SketchTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Map() => NewMap.Create(new NewMapSettings { Size = 256, Height = 20, Water = false });

        private static SketchStroke Line(SketchColour colour, params (float X, float Z)[] points) =>
            new SketchStroke(colour, SketchWidth.Medium, [.. points.Select(point => new Vector2(point.X, point.Z))]);

        private static MapDocument Drawn(MapDocument document, string? sheet, params SketchStroke[] strokes) => document.AddStrokes(sheet, strokes, out _);

        private static IReadOnlyList<MapSymmetryTransform> Rotational(MapDocument document) =>
            MapSymmetry.Transforms(MapSymmetryKind.Rotational, 2, document.Width, document.Height);

        [TestMethod]
        public void AMapStartsWithoutASketchAndTheFirstStrokeMakesTheFirstSheet()
        {
            MapDocument document = Map();
            Assert.IsTrue(document.Sketch.IsEmpty);

            MapDocument drawn = document.AddStrokes(null, [Line(SketchColour.Red, (10, 10), (50, 10))], out string sheet);

            Assert.AreEqual("Sheet 1", sheet);
            Assert.AreEqual(1, drawn.Sketch.Sheets.Length);
            Assert.AreEqual(1, drawn.Sketch.Sheets[0].Strokes.Length);
            Assert.IsTrue(drawn.Sketch.Sheets[0].Shown);
            Assert.AreEqual(SketchSheet.DefaultOpacity, drawn.Sketch.Sheets[0].Opacity);
        }

        [TestMethod]
        public void SheetsAreNamedUniquelyWithoutRegardToCase()
        {
            MapDocument document = Map().AddSheet(null, out string first).AddSheet("Layout A", out string second).AddSheet(null, out string third);
            Assert.AreEqual("Sheet 1", first);
            Assert.AreEqual("Layout A", second);
            Assert.AreEqual("Sheet 2", third);

            Assert.ThrowsException<ArgumentException>(() => document.AddSheet("layout a", out _), "a name is taken without regard to case");
            Assert.ThrowsException<ArgumentException>(() => document.AddSheet("  ", out _));
            Assert.ThrowsException<ArgumentException>(() => document.AddSheet(new string('x', SketchEdits.NameLength + 1), out _));
            Assert.ThrowsException<ArgumentException>(() => document.RenameSheet("Sheet 1", "Layout A"));
            Assert.AreSame(document, document.RenameSheet("Layout A", "Layout A"), "its own name changes nothing");
            Assert.AreSame(document.Sketch.Find("layout a"), document.Sketch.Sheets[1], "a name matches without regard to case");
            ArgumentException missing = Assert.ThrowsException<ArgumentException>(() => document.SheetNamed("Nope"));
            StringAssert.Contains(missing.Message, "Sheet 1, Layout A, Sheet 2");

            MapDocument renamed = document.RenameSheet("Layout A", "Plan B");
            CollectionAssert.AreEqual(new[] { "Sheet 1", "Plan B", "Sheet 2" }, renamed.Sketch.Sheets.Select(sheet => sheet.Name).ToArray());
        }

        [TestMethod]
        public void ASheetIsDuplicatedShownHiddenClearedAndDeleted()
        {
            MapDocument document = Drawn(Map().AddSheet("Layout A", out _), "Layout A", Line(SketchColour.Blue, (5, 5), (9, 9)));
            document = document.SetLegend("Layout A", SketchColour.Blue, "water").SetSheetOpacity("Layout A", 0.3f);

            MapDocument copied = document.DuplicateSheet("Layout A", null, out string copy);
            Assert.AreEqual("Layout A copy", copy);
            Assert.AreEqual(2, copied.Sketch.Sheets.Length);
            Assert.AreEqual("Layout A copy", copied.Sketch.Sheets[1].Name, "right after the original");
            Assert.AreEqual(1, copied.Sketch.Sheets[1].Strokes.Length);
            Assert.AreEqual("water", copied.Sketch.Sheets[1].LegendOf(SketchColour.Blue));
            Assert.AreEqual(0.3f, copied.Sketch.Sheets[1].Opacity);
            copied.DuplicateSheet("Layout A", null, out string again);
            Assert.AreEqual("Layout A copy 2", again, "a free name");

            MapDocument hidden = copied.ShowSheet("Layout A copy", false);
            Assert.IsFalse(hidden.Sketch.Sheets[1].Shown);
            Assert.IsTrue(hidden.Sketch.Sheets[0].Shown);
            Assert.AreSame(hidden, hidden.ShowSheet("Layout A copy", false), "nothing changed");

            MapDocument cleared = hidden.ClearSheet("Layout A copy");
            Assert.IsTrue(cleared.Sketch.Sheets[1].Strokes.IsEmpty);
            Assert.AreEqual("water", cleared.Sketch.Sheets[1].LegendOf(SketchColour.Blue), "the legend stays");
            Assert.AreSame(cleared, cleared.ClearSheet("Layout A copy"));

            MapDocument deleted = cleared.DeleteSheet("Layout A");
            Assert.AreEqual(1, deleted.Sketch.Sheets.Length);
            Assert.AreEqual("Layout A copy", deleted.Sketch.Sheets[0].Name);
            Assert.IsTrue(deleted.DeleteSheet("Layout A copy").Sketch.IsEmpty);
        }

        [TestMethod]
        public void TheLegendHoldsATextPerColour()
        {
            MapDocument document = Map().AddSheet("A", out _);
            Assert.IsFalse(document.Sketch.Sheets[0].HasLegend);

            MapDocument legend = document.SetLegend("A", SketchColour.Red, "  spawns ").SetLegend("A", SketchColour.Black, "cliffs");
            SketchSheet sheet = legend.Sketch.Sheets[0];
            Assert.AreEqual("spawns", sheet.LegendOf(SketchColour.Red), "trimmed");
            Assert.AreEqual("cliffs", sheet.LegendOf(SketchColour.Black));
            Assert.AreEqual("", sheet.LegendOf(SketchColour.Green));
            Assert.IsTrue(sheet.HasLegend);
            Assert.AreEqual(SketchLook.ColourCount, sheet.Legend.Length);
            Assert.AreSame(legend, legend.SetLegend("A", SketchColour.Red, "spawns"));
            Assert.IsFalse(legend.SetLegend("A", SketchColour.Red, "").SetLegend("A", SketchColour.Black, "").Sketch.Sheets[0].HasLegend);
            Assert.ThrowsException<ArgumentException>(() => legend.SetLegend("A", SketchColour.Red, new string('x', SketchEdits.LegendLength + 1)));
        }

        [TestMethod]
        public void TheColoursAndWidthsAreFixedAndParsedByNameOrNumber()
        {
            Assert.AreEqual(SketchColour.Red, SketchLook.ParseColour("1"));
            Assert.AreEqual(SketchColour.Black, SketchLook.ParseColour("6"));
            Assert.AreEqual(SketchColour.Green, SketchLook.ParseColour("Green"));
            Assert.AreEqual(SketchWidth.Thick, SketchLook.ParseWidth("thick"));
            Assert.ThrowsException<ArgumentException>(() => SketchLook.ParseColour("7"));
            Assert.ThrowsException<ArgumentException>(() => SketchLook.ParseColour("purple"));
            Assert.AreEqual(SketchLook.ColourCount, Enum.GetValues<SketchColour>().Select(SketchLook.Argb).Distinct().Count(), "six different colours");
            Assert.IsTrue(SketchLook.Pixels(SketchWidth.Thin) < SketchLook.Pixels(SketchWidth.Medium) && SketchLook.Pixels(SketchWidth.Medium) < SketchLook.Pixels(SketchWidth.Thick));
        }

        [TestMethod]
        public void AStrokeIsRepeatedByTheSymmetry()
        {
            MapDocument document = Map();
            SketchStroke stroke = Line(SketchColour.Yellow, (10, 20), (40, 20));

            IReadOnlyList<SketchStroke> two = SketchEdits.WithImages(stroke, Rotational(document));
            Assert.AreEqual(2, two.Count);
            Assert.AreEqual(new Vector2(246, 236), two[1].Points[0], "turned half a circle around the middle (128, 128)");
            Assert.AreEqual(new Vector2(216, 236), two[1].Points[1]);
            Assert.AreEqual(SketchColour.Yellow, two[1].Colour);

            IReadOnlyList<SketchStroke> mirrored = SketchEdits.WithImages(Line(SketchColour.Red, (10, 20), (40, 20)),
                MapSymmetry.Transforms(MapSymmetryKind.MirroredLeftRight, 2, 256, 256));
            Assert.AreEqual(new Vector2(246, 20), mirrored[1].Points[0]);

            Assert.AreEqual(1, SketchEdits.WithImages(stroke, []).Count, "no symmetry: the stroke alone");
            Assert.AreEqual(1, SketchEdits.WithImages(stroke, [MapSymmetryTransform.Identity]).Count);
            IReadOnlyList<SketchStroke> onTheAxis = SketchEdits.WithImages(Line(SketchColour.Red, (128, 10), (128, 90)),
                MapSymmetry.Transforms(MapSymmetryKind.MirroredLeftRight, 2, 256, 256));
            Assert.AreEqual(1, onTheAxis.Count, "a line along the mirror's axis is its own image");
        }

        [TestMethod]
        public void TheEraserCutsAStrokeInTwoWhereItCrossesItsMiddle()
        {
            MapDocument document = Drawn(Map(), null, Line(SketchColour.Red, (0, 50), (100, 50)));

            MapDocument erased = document.Erase("Sheet 1", [new Vector2(50, 40), new Vector2(50, 60)], 5, whole: false, [], out int count);

            Assert.AreEqual(1, count);
            ImmutableArray<SketchStroke> strokes = erased.Sketch.Sheets[0].Strokes;
            Assert.AreEqual(2, strokes.Length, "cut in two");
            Assert.AreEqual(new Vector2(0, 50), strokes[0].Points[0]);
            Assert.AreEqual(45f, strokes[0].Points[^1].X, 1e-3f);
            Assert.AreEqual(55f, strokes[1].Points[0].X, 1e-3f);
            Assert.AreEqual(new Vector2(100, 50), strokes[1].Points[^1]);
            Assert.AreEqual(SketchColour.Red, strokes[1].Colour, "the pieces keep the colour");
        }

        [TestMethod]
        public void TheEraserRubsOutTheEndOfAStrokeAndWhatIsLeftTooShort()
        {
            MapDocument document = Drawn(Map(), null, Line(SketchColour.Red, (0, 50), (100, 50)), Line(SketchColour.Blue, (0, 10), (3, 10)));

            MapDocument end = document.Erase("Sheet 1", [new Vector2(98, 50)], 6, false, [], out _);
            Assert.AreEqual(2, end.Sketch.Sheets[0].Strokes.Length);
            Assert.AreEqual(92f, end.Sketch.Sheets[0].Strokes[0].Points[^1].X, 1e-3f, "the end is gone");

            MapDocument small = document.Erase("Sheet 1", [new Vector2(0, 10), new Vector2(2, 10)], 2, false, [], out _);
            Assert.AreEqual(1, small.Sketch.Sheets[0].Strokes.Length, "the blue stroke's remnant of a pixel is no stroke");

            MapDocument all = document.Erase("Sheet 1", [new Vector2(50, 50), new Vector2(0, 50), new Vector2(100, 50)], 6, false, [], out int count);
            Assert.AreEqual(1, count);
            Assert.AreEqual(1, all.Sketch.Sheets[0].Strokes.Length, "a long drag along a stroke rubs all of it out");
            Assert.AreEqual(SketchColour.Blue, all.Sketch.Sheets[0].Strokes[0].Colour);
        }

        [TestMethod]
        public void WithAltTheEraserRemovesWholeStrokesItTouches()
        {
            MapDocument document = Drawn(Map(), null,
                Line(SketchColour.Red, (0, 50), (100, 50)), Line(SketchColour.Green, (0, 90), (100, 90)), Line(SketchColour.Blue, (60, 0), (60, 100)));

            MapDocument whole = document.Erase("Sheet 1", [new Vector2(50, 52)], 4, whole: true, [], out int count);

            Assert.AreEqual(1, count);
            CollectionAssert.AreEqual(new[] { SketchColour.Green, SketchColour.Blue }, whole.Sketch.Sheets[0].Strokes.Select(stroke => stroke.Colour).ToArray());

            MapDocument crossing = document.Erase("Sheet 1", [new Vector2(60, 45), new Vector2(60, 95)], 3, true, [], out int two);
            Assert.AreEqual(3, two, "a drag across the lot touches all three");
            Assert.IsTrue(crossing.Sketch.Sheets[0].Strokes.IsEmpty);

            Assert.AreSame(document, document.Erase("Sheet 1", [new Vector2(200, 200)], 4, true, [], out int none), "nothing touched, nothing changed");
            Assert.AreEqual(0, none);
        }

        [TestMethod]
        public void TheEraserIsMirroredLikeThePencil()
        {
            MapDocument document = Map();
            IReadOnlyList<MapSymmetryTransform> parts = Rotational(document);
            document = document.AddStrokes(null, SketchEdits.WithImages(Line(SketchColour.Red, (10, 50), (60, 50)), parts), out _);
            Assert.AreEqual(2, document.Sketch.Sheets[0].Strokes.Length);

            MapDocument erased = document.Erase("Sheet 1", [new Vector2(35, 50)], 4, false, parts, out int count);

            Assert.AreEqual(2, count, "both strokes were cut");
            Assert.AreEqual(4, erased.Sketch.Sheets[0].Strokes.Length);

            MapDocument alone = document.Erase("Sheet 1", [new Vector2(35, 50)], 4, false, [], out int one);
            Assert.AreEqual(1, one);
            Assert.AreEqual(3, alone.Sketch.Sheets[0].Strokes.Length);
        }

        [TestMethod]
        public void ADotIsRubbedOutWhole()
        {
            MapDocument document = Drawn(Map(), null, Line(SketchColour.White, (30, 30)));
            Assert.AreSame(document, document.Erase("Sheet 1", [new Vector2(40, 30)], 4, false, [], out _));
            Assert.IsTrue(document.Erase("Sheet 1", [new Vector2(33, 30)], 4, false, [], out int count).Sketch.Sheets[0].Strokes.IsEmpty);
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void ThePencilThinsAndSmoothsALineDrawnByHand()
        {
            List<Vector2> hand = [];
            for (int i = 0; i <= 200; i++)
            {
                // a gentle curve with a small tremor
                hand.Add(new Vector2(i, 50 + 10 * MathF.Sin(i / 30f) + (i % 2 == 0 ? 0.2f : -0.2f)));
            }

            ImmutableArray<Vector2> thin = SketchGeometry.Thin(hand, 2);
            Assert.IsTrue(thin.Length < hand.Count / 1.5f && thin.Length > 50, $"{thin.Length}");
            Assert.AreEqual(hand[0], thin[0]);
            Assert.AreEqual(hand[^1], thin[^1], "the line ends where the mouse did");

            ImmutableArray<Vector2> smooth = SketchGeometry.Smooth(hand, 1);
            Assert.IsTrue(smooth.Length < 25, $"few places: {smooth.Length}");
            Assert.AreEqual(hand[0], smooth[0]);
            Assert.AreEqual(hand[^1], smooth[^1]);
            foreach (Vector2 point in hand)
            {
                float far = Enumerable.Range(0, smooth.Length - 1).Min(i => SketchGeometry.DistanceToSegment(point, smooth[i], smooth[i + 1]));
                Assert.IsTrue(far < 2.5f, "the smoothed line stays near the drawn one");
            }

            Assert.AreEqual(1, SketchGeometry.Thin([new Vector2(5, 5), new Vector2(5, 5)], 2).Length, "a click is one dot");
            CollectionAssert.AreEqual(new[] { hand[0], hand[^1] }, SketchGeometry.Straight(hand).ToArray());
        }

        [TestMethod]
        public void EveryEditIsOneStepOfUndo()
        {
            EditSession session = new EditSession(Map());

            Assert.IsTrue(session.Edit("Draw", d => d.AddStrokes(null, [Line(SketchColour.Red, (1, 1), (9, 9))], out _)));
            Assert.IsFalse(session.Edit("Show", d => d.ShowSheet("Sheet 1", true)), "no change, no step");
            Assert.IsTrue(session.Edit("Erase", d => d.Erase("Sheet 1", [new Vector2(5, 5)], 1, false, [], out _)));
            Assert.AreEqual(2, session.History.Count);

            session.Undo();
            Assert.AreEqual(1, session.Document.Sketch.Sheets[0].Strokes.Length);
            session.Undo();
            Assert.IsTrue(session.Document.Sketch.IsEmpty);
            session.Redo();
            Assert.AreEqual(1, session.Document.Sketch.Sheets[0].Strokes.Length);
        }

        [TestMethod]
        public void TheSketchIsKeptInTheProjectAndComesBackAsItWas()
        {
            MapDocument document = Map();
            document = document.AddStrokes("Layout A", [Line(SketchColour.Red, (10.126f, 20.5f), (40, 20), (41.25f, -3)), Line(SketchColour.Black, (7, 7))], out _)
                .SetLegend("Layout A", SketchColour.Red, "spawns & <paths>")
                .SetSheetOpacity("Layout A", 0.6f)
                .AddSheet("Ideas", out _)
                .ShowSheet("Ideas", false);
            string project = ProjectFolders.New("sketch");

            MapProjectStore.Save(document, project);

            string file = Path.Combine(project, "sketch", "sketch.json");
            Assert.IsTrue(File.Exists(file));
            string text = File.ReadAllText(file);
            StringAssert.Contains(text, "\"points\": \"10.13,20.5 40,20 41.25,-3\"", "a stroke's places on one line, two decimals");
            StringAssert.Contains(text, "spawns & <paths>");

            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(2, loaded.Sketch.Sheets.Length);
            SketchSheet a = loaded.Sketch.Sheets[0];
            Assert.AreEqual("Layout A", a.Name);
            Assert.AreEqual(0.6f, a.Opacity);
            Assert.AreEqual("spawns & <paths>", a.LegendOf(SketchColour.Red));
            Assert.AreEqual("", a.LegendOf(SketchColour.Blue));
            Assert.AreEqual(2, a.Strokes.Length);
            Assert.AreEqual(SketchColour.Red, a.Strokes[0].Colour);
            Assert.AreEqual(SketchWidth.Medium, a.Strokes[0].Width);
            Assert.AreEqual(new Vector2(10.13f, 20.5f), a.Strokes[0].Points[0]);
            Assert.AreEqual(new Vector2(41.25f, -3), a.Strokes[0].Points[2]);
            Assert.AreEqual(1, a.Strokes[1].Points.Length, "a dot");
            Assert.IsFalse(loaded.Sketch.Sheets[1].Shown);
            Assert.IsTrue(loaded.Sketch.Sheets[1].Strokes.IsEmpty);
        }

        [TestMethod]
        public void AProjectWithoutASketchHasNoneAndTheFileGoesWhenTheSketchDoes()
        {
            MapDocument plain = Map();
            string project = ProjectFolders.New("sketch");
            MapProjectStore.Save(plain, project);

            Assert.IsFalse(Directory.Exists(Path.Combine(project, "sketch")), "a map without a sketch has no folder");
            Assert.IsTrue(MapProjectStore.Load(project).Sketch.IsEmpty, "an older project reads as if it held none");

            MapDocument drawn = Drawn(plain, null, Line(SketchColour.Red, (1, 1), (2, 2)));
            MapProjectStore.Save(drawn, project);
            Assert.IsTrue(File.Exists(Path.Combine(project, "sketch", "sketch.json")));

            MapProjectStore.Save(plain, project);
            Assert.IsFalse(File.Exists(Path.Combine(project, "sketch", "sketch.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(project, "sketch")), "the empty folder goes");

            Directory.CreateDirectory(Path.Combine(project, "sketch", "images"));
            File.WriteAllText(Path.Combine(project, "sketch", "images", "a.png"), "x");
            MapProjectStore.Save(drawn, project);
            MapProjectStore.Save(plain, project);
            Assert.IsTrue(File.Exists(Path.Combine(project, "sketch", "images", "a.png")), "what else lies in the folder stays");
        }

        [TestMethod]
        public void TheSketchIsReadAgainWhenItsFileChangesAndTheEditorsOwnSaveIsNoEdit()
        {
            MapDocument document = Drawn(Map(), null, Line(SketchColour.Red, (1.23456f, 2), (30, 40)));
            string project = ProjectFolders.New("sketch");
            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreSame(document, MapProjectStore.Reload(document, project, ["sketch/sketch.json"]), "the file holds what the document has, rounded as it is written");
            Assert.AreSame(loaded, MapProjectStore.Reload(loaded, project, ["sketch/sketch.json"]));

            string file = Path.Combine(project, "sketch", "sketch.json");
            File.WriteAllText(file, File.ReadAllText(file).Replace("\"Red\"", "\"Blue\""));
            MapDocument changed = MapProjectStore.Reload(loaded, project, ["sketch/sketch.json"]);
            Assert.AreNotSame(loaded, changed);
            Assert.AreEqual(SketchColour.Blue, changed.Sketch.Sheets[0].Strokes[0].Colour);

            File.Delete(file);
            Assert.IsTrue(MapProjectStore.Reload(changed, project, ["sketch/sketch.json"]).Sketch.IsEmpty, "a file taken away is a sketch taken away");
        }

        [TestMethod]
        public void AFileThatDoesNotReadSaysWhatIsWrong()
        {
            string project = ProjectFolders.New("sketch");
            MapProjectStore.Save(Map(), project);
            Directory.CreateDirectory(Path.Combine(project, "sketch"));
            string file = Path.Combine(project, "sketch", "sketch.json");

            File.WriteAllText(file, """{ "sheets": [ { "name": "A", "strokes": [ { "colour": "Red", "points": "1,2 three,4" } ] } ] }""");
            MapProjectException places = Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project));
            StringAssert.Contains(places.Message, "three,4");

            File.WriteAllText(file, """{ "sheets": [ { "name": "A" }, { "name": "a" } ] }""");
            StringAssert.Contains(Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project)).Message, "two sheets are named");

            File.WriteAllText(file, """{ "version": 2, "sheets": [] }""");
            StringAssert.Contains(Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project)).Message, "update the editor");

            File.WriteAllText(file, """{ "sheets": [ { "name": "A", "legend": ["x"], "strokes": [ { "colour": "Green", "points": "1,2" } ] } ] }""");
            SketchSheet read = MapProjectStore.Load(project).Sketch.Sheets[0];
            Assert.AreEqual("x", read.LegendOf(SketchColour.Red));
            Assert.AreEqual("", read.LegendOf(SketchColour.Black), "a short legend is filled up");
            Assert.AreEqual(SketchWidth.Medium, read.Strokes[0].Width, "a width left out is medium");
        }

        [TestMethod]
        public void TheExportNeverHoldsTheSketch()
        {
            MapDocument plain = Map();
            MapDocument drawn = Drawn(plain, null, Line(SketchColour.Red, (1, 1), (2, 2)));

            IReadOnlyDictionary<string, byte[]> without = MapExporter.ToBytes(plain);
            IReadOnlyDictionary<string, byte[]> with = MapExporter.ToBytes(drawn);

            CollectionAssert.AreEquivalent(without.Keys.ToList(), with.Keys.ToList());
            foreach ((string path, byte[] bytes) in without)
            {
                CollectionAssert.AreEqual(bytes, with[path], path);
            }
            Assert.IsFalse(drawn.Files.Keys.Any(path => path.Contains("sketch", StringComparison.OrdinalIgnoreCase)), "not among the files copied as they are");
        }

        [TestMethod]
        public void ResizingTheMapMovesTheSketchWithIt()
        {
            MapDocument document = Drawn(Map(), null, Line(SketchColour.Red, (10, 20), (30, 40)));

            MapResizeResult result = MapResize.Resize(document, new MapResizeSettings { Width = 512, Height = 512, Anchor = MapAnchor.Centre });

            Assert.AreEqual(new Vector2(138, 148), result.Document.Sketch.Sheets[0].Strokes[0].Points[0], "by the offset of the old map in the new one (128, 128)");
            Assert.AreEqual(new Vector2(158, 168), result.Document.Sketch.Sheets[0].Strokes[0].Points[1]);
        }
    }
}
