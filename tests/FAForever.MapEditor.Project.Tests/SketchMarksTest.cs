using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The sketch's symbols and labels and the selecting of items (user story N-4): stamped, written,
    /// mirrored, moved, recoloured, deleted, rubbed out by the eraser with Alt, drawn as lines, and kept
    /// in <c>sketch/sketch.json</c> (version 2) with the older file still reading.
    /// </summary>
    [TestClass]
    public class SketchMarksTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Map() => NewMap.Create(new NewMapSettings { Size = 256, Height = 20, Water = false });

        private static SketchSymbol Symbol(SketchSymbolKind kind, float x, float z, SketchColour colour = SketchColour.Red, float size = 10, float turn = 0) =>
            new SketchSymbol(kind, new Vector2(x, z), colour, size, turn);

        private static SketchStroke Line(SketchColour colour, params (float X, float Z)[] points) =>
            new SketchStroke(colour, SketchWidth.Medium, [.. points.Select(point => new Vector2(point.X, point.Z))]);

        private static IReadOnlyList<MapSymmetryTransform> Rotational(MapDocument document) =>
            MapSymmetry.Transforms(MapSymmetryKind.Rotational, 2, document.Width, document.Height);

        [TestMethod]
        public void ASymbolAndALabelGoIntoASheetAndTheFirstOneMakesTheSheet()
        {
            MapDocument document = Map().AddSymbols(null, [Symbol(SketchSymbolKind.Spawn, 30, 40)], out string sheet)
                .AddLabel(null, new SketchLabel("  spawn 1 ", new Vector2(30, 60), SketchColour.Blue, 16), out _);

            Assert.AreEqual("Sheet 1", sheet);
            SketchSheet held = document.Sketch.Sheets[0];
            Assert.AreEqual(1, held.Symbols.Length);
            Assert.AreEqual("spawn 1", held.Labels[0].Text, "the text is trimmed");
            Assert.AreEqual(2, held.ItemCount);
            Assert.AreEqual(0, held.Strokes.Length);
            CollectionAssert.AreEqual(new[] { SketchColour.Red, SketchColour.Blue }, held.ColoursUsed.ToArray());
            Assert.ThrowsException<ArgumentException>(() => document.AddLabel(null, new SketchLabel("  ", Vector2.Zero, SketchColour.Red, 16), out _));
            Assert.ThrowsException<ArgumentException>(() => document.AddLabel(null, new SketchLabel(new string('x', SketchMarkEdits.LabelLength + 1), Vector2.Zero, SketchColour.Red, 16), out _));
            Assert.ThrowsException<ArgumentException>(() => document.AddSymbols(null, [Symbol(SketchSymbolKind.Cross, 1, 1, size: 0)], out _));
            Assert.ThrowsException<ArgumentException>(() => document.AddSymbols("Nope", [Symbol(SketchSymbolKind.Cross, 1, 1)], out _));
        }

        [TestMethod]
        public void ASymbolIsRepeatedByTheSymmetryAndAnArrowPointsTheWayItsImageDoes()
        {
            MapDocument document = Map();
            IReadOnlyList<SketchSymbol> images = SketchMarkEdits.WithImages(Symbol(SketchSymbolKind.Arrow, 50, 60, turn: 30), Rotational(document));

            Assert.AreEqual(2, images.Count);
            Assert.AreEqual(new Vector2(256 - 50, 256 - 60), images[1].Place);
            Assert.AreEqual(210f, images[1].Turn, 0.01, "a half turn about the centre points the other way");
            Assert.AreEqual(1, SketchMarkEdits.WithImages(Symbol(SketchSymbolKind.Mass, 128, 128), Rotational(document)).Count, "on the centre it is its own image");
            Assert.AreEqual(1, SketchMarkEdits.WithImages(Symbol(SketchSymbolKind.Cross, 50, 60), []).Count);
        }

        [TestMethod]
        public void ASymbolIsAFewLinesInItsColourAndTheSheetsLinesHoldTheStrokesFirst()
        {
            foreach (SketchSymbolKind kind in Enum.GetValues<SketchSymbolKind>())
            {
                ImmutableArray<SketchStroke> lines = SketchLines.Of(Symbol(kind, 100, 100, SketchColour.Green, 20));
                Assert.IsTrue(lines.Length >= 1 && lines.Length <= 3, kind.ToString());
                Assert.IsTrue(lines.All(line => line.Colour == SketchColour.Green && line.Points.All(point => Vector2.Distance(point, new Vector2(100, 100)) <= 20)), kind.ToString());
            }
            ImmutableArray<SketchStroke> arrow = SketchLines.Of(Symbol(SketchSymbolKind.Arrow, 0, 0, size: 10, turn: 90));
            Assert.AreEqual(0f, arrow[0].Points[0].X, 1e-4, "turned a quarter turn it points towards +z");
            Assert.AreEqual(-5f, arrow[0].Points[0].Y, 1e-4);
            Assert.AreEqual(5f, arrow[0].Points[1].Y, 1e-4);
            ImmutableArray<SketchStroke> cross = SketchLines.Of(Symbol(SketchSymbolKind.Cross, 0, 0, size: 10, turn: 0));
            CollectionAssert.AreEqual(cross[0].Points.ToArray(), SketchLines.Of(Symbol(SketchSymbolKind.Cross, 0, 0, size: 10, turn: 77))[0].Points.ToArray(), "other kinds ignore the turn");

            SketchSheet sheet = new SketchSheet { Name = "A", Strokes = [Line(SketchColour.Red, (1, 1), (2, 2))], Symbols = [Symbol(SketchSymbolKind.Cross, 5, 5)] };
            SketchLine[] all = [.. SketchLines.Of(sheet)];
            Assert.AreEqual(1 + 2, all.Length);
            Assert.AreEqual(sheet.Strokes[0].Points, all[0].Points, "a stroke is a line of its own places");
        }

        [TestMethod]
        public void TheShownSheetsAreThoseThatHoldSomethingAtTheirOpacityOrFullInTheMode()
        {
            MapDocument document = Map().AddSymbols("A", [Symbol(SketchSymbolKind.Cross, 5, 5)], out _)
                .AddSheet("Empty", out _).AddSheet("Hidden", out _).AddSymbols("Hidden", [Symbol(SketchSymbolKind.Cross, 5, 5)], out _).ShowSheet("Hidden", false);

            (SketchSheet Sheet, float Opacity)[] outside = [.. SketchLines.Shown(document.Sketch, full: false)];
            Assert.AreEqual(1, outside.Length);
            Assert.AreEqual(SketchSheet.DefaultOpacity, outside[0].Opacity);
            Assert.AreEqual(1f, SketchLines.Shown(document.Sketch, full: true).Single().Opacity);
            Assert.AreEqual(0, SketchLines.Shown(document.SetSheetOpacity("A", 0).Sketch, full: false).Count(), "a transparent sheet is left out");
        }

        [TestMethod]
        public void ClickingPicksTheTopmostItemAndABoxTakesWhatItCrosses()
        {
            MapDocument document = Map().AddStrokes("A", [Line(SketchColour.Red, (10, 10), (60, 10)), Line(SketchColour.Red, (10, 50), (60, 50))], out _)
                .AddSymbols("A", [Symbol(SketchSymbolKind.Mass, 100, 100, size: 10)], out _)
                .AddLabel("A", new SketchLabel("ramp", new Vector2(150, 150), SketchColour.Green, 16), out _);
            SketchSheet sheet = document.Sketch.Sheets[0];

            Assert.AreEqual(new SketchItem(SketchItemKind.Stroke, 0), SketchPicking.Pick(sheet, new Vector2(30, 11), 2));
            Assert.IsNull(SketchPicking.Pick(sheet, new Vector2(30, 20), 2));
            Assert.AreEqual(new SketchItem(SketchItemKind.Symbol, 0), SketchPicking.Pick(sheet, new Vector2(104, 100), 1));
            Assert.AreEqual(new SketchItem(SketchItemKind.Label, 0), SketchPicking.Pick(sheet, new Vector2(150, 150), 1));

            ImmutableArray<SketchItem> box = SketchPicking.InBox(sheet, new Vector2(0, 0), new Vector2(70, 30));
            CollectionAssert.AreEqual(new[] { new SketchItem(SketchItemKind.Stroke, 0) }, box.ToArray());
            Assert.AreEqual(4, SketchPicking.InBox(sheet, new Vector2(200, 200), new Vector2(0, 0)).Length, "any corner first: everything");
            CollectionAssert.AreEqual(new[] { new SketchItem(SketchItemKind.Stroke, 0) }, SketchPicking.InBox(sheet, new Vector2(30, 0), new Vector2(32, 20)).ToArray(), "a stroke that passes through the box with no place in it");
        }

        [TestMethod]
        public void ItemsAreMovedRecolouredResizedAndDeletedAsOneEditEach()
        {
            MapDocument document = Map().AddStrokes("A", [Line(SketchColour.Red, (10, 10), (60, 10)), Line(SketchColour.Red, (10, 50), (60, 50))], out _)
                .AddSymbols("A", [Symbol(SketchSymbolKind.Mass, 100, 100)], out _)
                .AddLabel("A", new SketchLabel("ramp", new Vector2(150, 150), SketchColour.Green, 16), out _);
            SketchItem[] items = [new SketchItem(SketchItemKind.Stroke, 1), new SketchItem(SketchItemKind.Symbol, 0), new SketchItem(SketchItemKind.Label, 0)];

            MapDocument moved = document.MoveItems("A", items, 5, -3);
            SketchSheet sheet = moved.Sketch.Sheets[0];
            Assert.AreEqual(new Vector2(15, 47), sheet.Strokes[1].Points[0]);
            Assert.AreEqual(new Vector2(10, 10), sheet.Strokes[0].Points[0], "what was not selected stays");
            Assert.AreEqual(new Vector2(105, 97), sheet.Symbols[0].Place);
            Assert.AreEqual(new Vector2(155, 147), sheet.Labels[0].Place);
            Assert.AreSame(document, document.MoveItems("A", items, 0, 0));
            Assert.AreSame(document, document.MoveItems("A", [], 4, 4));

            MapDocument blue = document.RecolourItems("A", items, SketchColour.Blue);
            Assert.AreEqual(SketchColour.Blue, blue.Sketch.Sheets[0].Strokes[1].Colour);
            Assert.AreEqual(SketchColour.Red, blue.Sketch.Sheets[0].Strokes[0].Colour);
            Assert.AreEqual(SketchColour.Blue, blue.Sketch.Sheets[0].Labels[0].Colour);
            Assert.AreSame(blue, blue.RecolourItems("A", items, SketchColour.Blue), "the colour they have changes nothing");

            MapDocument bigger = document.ResizeItems("A", items, 30);
            Assert.AreEqual(30f, bigger.Sketch.Sheets[0].Symbols[0].Size);
            Assert.AreEqual(30f, bigger.Sketch.Sheets[0].Labels[0].Size);
            Assert.ThrowsException<ArgumentException>(() => document.ResizeItems("A", items, 0));

            MapDocument deleted = document.DeleteItems("A", items);
            Assert.AreEqual(1, deleted.Sketch.Sheets[0].Strokes.Length);
            Assert.AreEqual(new Vector2(10, 10), deleted.Sketch.Sheets[0].Strokes[0].Points[0]);
            Assert.AreEqual(0, deleted.Sketch.Sheets[0].Symbols.Length);
            Assert.AreEqual(0, deleted.Sketch.Sheets[0].Labels.Length);
            Assert.ThrowsException<ArgumentException>(() => document.DeleteItems("A", [new SketchItem(SketchItemKind.Label, 3)]));
        }

        [TestMethod]
        public void ALabelsTextIsChangedAndClearingASheetTakesEverything()
        {
            MapDocument document = Map().AddLabel("A", new SketchLabel("ramp", new Vector2(10, 10), SketchColour.Green, 16), out _)
                .AddSymbols("A", [Symbol(SketchSymbolKind.Cross, 1, 1)], out _);

            Assert.AreEqual("ramp up", document.SetLabelText("A", 0, "ramp up").Sketch.Sheets[0].Labels[0].Text);
            Assert.AreSame(document, document.SetLabelText("A", 0, "ramp"));
            Assert.ThrowsException<ArgumentException>(() => document.SetLabelText("A", 0, " "));
            Assert.ThrowsException<ArgumentException>(() => document.SetLabelText("A", 2, "x"));
            Assert.IsTrue(document.ClearSheet("A").Sketch.Sheets[0].IsBlank);
        }

        [TestMethod]
        public void WithAltTheEraserAlsoRemovesTheSymbolsAndLabelsItTouches()
        {
            MapDocument document = Map().AddStrokes("A", [Line(SketchColour.Red, (0, 0), (0, 20))], out _)
                .AddSymbols("A", [Symbol(SketchSymbolKind.Cross, 100, 100, size: 10), Symbol(SketchSymbolKind.Cross, 200, 200, size: 10)], out _)
                .AddLabel("A", new SketchLabel("ramp", new Vector2(100, 130), SketchColour.Green, 16), out _);

            MapDocument rubbed = document.Erase("A", [new Vector2(70, 100), new Vector2(110, 100)], 3, whole: false, [], out int none);
            Assert.AreEqual(0, none);
            Assert.AreSame(document, rubbed, "without Alt the symbols stay");

            MapDocument whole = document.Erase("A", [new Vector2(70, 100), new Vector2(110, 100)], 3, whole: true, [], out int erased);
            Assert.AreEqual(1, erased);
            Assert.AreEqual(1, whole.Sketch.Sheets[0].Symbols.Length);
            Assert.AreEqual(200f, whole.Sketch.Sheets[0].Symbols[0].Place.X);
            Assert.AreEqual(1, whole.Sketch.Sheets[0].Labels.Length);

            MapDocument label = document.Erase("A", [new Vector2(100, 128)], 2, whole: true, [], out erased);
            Assert.AreEqual(1, erased);
            Assert.AreEqual(0, label.Sketch.Sheets[0].Labels.Length);

            MapDocument mirrored = document.Erase("A", [new Vector2(100, 100)], 3, whole: true, Rotational(document), out erased);
            Assert.AreEqual(1, erased, "the image of the eraser is at 156,156: it reaches nothing more");
            Assert.AreEqual(1, mirrored.Sketch.Sheets[0].Symbols.Length);
        }

        [TestMethod]
        public void SymbolsAndLabelsAreKeptInVersion2OfTheFileAndAStrokesOnlySketchStaysVersion1()
        {
            MapDocument strokes = Map().AddStrokes("A", [Line(SketchColour.Red, (1, 1), (2, 2))], out _);
            string project = ProjectFolders.New("marks");
            MapProjectStore.Save(strokes, project);
            string file = Path.Combine(project, "sketch", "sketch.json");
            StringAssert.Contains(File.ReadAllText(file), "\"version\": 1");
            Assert.IsFalse(File.ReadAllText(file).Contains("symbols") || File.ReadAllText(file).Contains("labels"));

            MapDocument document = strokes
                .AddSymbols("A", [Symbol(SketchSymbolKind.Hydrocarbon, 30.126f, 40, SketchColour.Yellow, 14), Symbol(SketchSymbolKind.Arrow, 5, 5, turn: 45.25f)], out _)
                .AddLabel("A", new SketchLabel("shallow, hover only", new Vector2(70, 80.5f), SketchColour.Blue, 26), out _);
            MapProjectStore.Save(document, project);

            string text = File.ReadAllText(file);
            StringAssert.Contains(text, "\"version\": 2");
            StringAssert.Contains(text, "\"place\": \"30.13,40\"");
            MapDocument loaded = MapProjectStore.Load(project);
            SketchSheet sheet = loaded.Sketch.Sheets[0];
            Assert.AreEqual(2, sheet.Symbols.Length);
            Assert.AreEqual(new SketchSymbol(SketchSymbolKind.Hydrocarbon, new Vector2(30.13f, 40), SketchColour.Yellow, 14, 0), sheet.Symbols[0]);
            Assert.AreEqual(SketchSymbolKind.Arrow, sheet.Symbols[1].Kind);
            Assert.AreEqual(45.25f, sheet.Symbols[1].Turn, 0.06f);
            Assert.AreEqual(new SketchLabel("shallow, hover only", new Vector2(70, 80.5f), SketchColour.Blue, 26), sheet.Labels[0]);
            Assert.AreEqual(1, sheet.Strokes.Length);

            // a version 1 file written by wave 23 reads as a sheet without symbols and labels
            File.WriteAllText(file, """{ "version": 1, "sheets": [ { "name": "A", "strokes": [ { "colour": "Red", "points": "1,2" } ] } ] }""");
            SketchSheet old = MapProjectStore.Load(project).Sketch.Sheets[0];
            Assert.IsTrue(old.Symbols.IsEmpty && old.Labels.IsEmpty);

            File.WriteAllText(file, """{ "version": 2, "sheets": [ { "name": "A", "symbols": [ { "kind": "Cross", "place": "oops" } ] } ] }""");
            StringAssert.Contains(Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project)).Message, "oops");
        }

        [TestMethod]
        public void ResizingTheMapMovesSymbolsAndLabelsWithTheStrokes()
        {
            MapDocument document = Map().AddSymbols("A", [Symbol(SketchSymbolKind.Cross, 10, 20)], out _)
                .AddLabel("A", new SketchLabel("x", new Vector2(30, 40), SketchColour.Red, 16), out _);

            MapResizeResult result = MapResize.Resize(document, new MapResizeSettings { Width = 512, Height = 512, Anchor = MapAnchor.Centre });

            Assert.AreEqual(new Vector2(138, 148), result.Document.Sketch.Sheets[0].Symbols[0].Place);
            Assert.AreEqual(new Vector2(158, 168), result.Document.Sketch.Sheets[0].Labels[0].Place);
        }

        [TestMethod]
        public void TheNamesOfSymbolsAndSizesAreParsed()
        {
            Assert.AreEqual(SketchSymbolKind.Hydrocarbon, SketchLook.ParseSymbol("hydro"));
            Assert.AreEqual(SketchSymbolKind.Question, SketchLook.ParseSymbol("?"));
            Assert.AreEqual(SketchSymbolKind.Spawn, SketchLook.ParseSymbol(" Spawn "));
            Assert.AreEqual(SketchSize.Large, SketchLook.ParseSize("large"));
            StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => SketchLook.ParseSymbol("star")).Message, "spawn, mass, hydrocarbon, arrow, cross, question");
            Assert.IsTrue(SketchLook.SymbolOgrids(SketchSize.Small) < SketchLook.SymbolOgrids(SketchSize.Large));
            Assert.IsTrue(SketchLook.LabelOgrids(SketchSize.Small) < SketchLook.LabelOgrids(SketchSize.Large));
        }
    }
}
