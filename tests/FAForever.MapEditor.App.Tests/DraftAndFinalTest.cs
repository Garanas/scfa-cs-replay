using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The draft and final heightmaps in the editor (user story T-9): the switch, brushes on the
    /// shown one, the list of the shown stack with its filters and uses, the files for World
    /// Machine and Gaea, the export taking the final.
    /// </summary>
    [TestClass]
    public class DraftAndFinalTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "draft-final");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string NewFolder() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) => model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static float FinalAt(MainViewModel model, int x, int z) => model.Map!.Document.Terrain.Heightmap.GetHeight(x, z);

        private static float DraftAt(MainViewModel model, int x, int z) => model.Map!.Document.DraftHeightmap.GetHeight(x, z);

        private static string[] Listed(MainViewModel model) => [.. model.HeightStack.Items.Select(item => item.Name)];

        private static void Sculpt(MainViewModel model, float x, float z)
        {
            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Mode = HeightBrushMode.Raise;
            sculpt.Radius = 10;
            sculpt.Strength = 2;
            model.Stroke([(x, z), (x + 4, z)]);
        }

        /// <summary>
        /// A flat output of a procedural tool at <paramref name="height"/> in the project's procedural/temp.
        /// </summary>
        private static string WriteOutput(string project, float height, string name = "gaea.png")
        {
            string file = Path.Combine(project, MapProjectLayout.ProceduralTemp, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            HeightmapFiles.Write(file, new Heightmap { Scale = 1f / 128, Samples = TiledBuffer<ushort>.Create(129, 129, (ushort)(height * 128)) });
            return file;
        }

        [TestMethod]
        public async Task TheSwitchChoosesWhatTheViewsShowAndBrushesWrite()
        {
            MainViewModel model = await Open();
            Assert.AreEqual(HeightStackKind.Final, model.ShownHeights);
            Assert.AreSame(model.Map!.Document, model.ViewDocument, "the final is the document itself");

            CommandRun switched = await Run(model, "terrain.switchHeights");
            Assert.AreEqual("draft", switched.Result?.ToString());
            Assert.AreEqual(HeightStackKind.Draft, model.ShownHeights);
            Assert.AreEqual(HeightStackKind.Draft, model.Map.Session.Heights, "brushes write the draft");
            StringAssert.Contains(model.Status, "draft");
            Assert.AreEqual("Ctrl+Shift+D", model.Commands.Find("terrain.switchHeights")!.Gesture);

            // a map without layers: the draft is the map's heights, a stroke changes them
            float before = FinalAt(model, 100, 100);
            Sculpt(model, 100, 100);
            Assert.IsTrue(FinalAt(model, 100, 100) > before + 1);

            await Run(model, "terrain.switchHeights", new JsonObject { ["to"] = "final" });
            Assert.AreEqual(HeightStackKind.Final, model.Map.Session.Heights);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "terrain.switchHeights", new JsonObject { ["to"] = "sketch" }));
        }

        [TestMethod]
        public async Task LayersAreSharedUntilUsedInOneAndBrushesWriteTheShownStack()
        {
            MainViewModel model = await Open();
            await Run(model, "terrain.addLayer");
            Assert.AreEqual(LayerUse.Shared, HeightStacks.UseOf(model.Map!.Document, "Sculpt"), "while the final shares the draft's base");
            Assert.AreEqual(2, model.Map.Document.DraftStack.Count);

            // only in the draft: the final loses it, sculpting it in the draft leaves the final
            await Run(model, "terrain.setLayer", new JsonObject { ["use"] = "Draft only" });
            Assert.AreEqual(LayerUse.Draft, HeightStacks.UseOf(model.Map.Document, "Sculpt"));
            CollectionAssert.AreEqual(new[] { "Base" }, Listed(model), "the final's list");
            StringAssert.Contains(model.DraftFinal.ElsewhereFacts, "Sculpt");

            await Run(model, "terrain.switchHeights", new JsonObject { ["to"] = "draft" });
            CollectionAssert.AreEqual(new[] { "Sculpt", "Base" }, Listed(model), "the draft's list");
            await Run(model, "terrain.chooseLayer", new JsonObject { ["layer"] = "Sculpt" });
            float final = FinalAt(model, 100, 100);
            float draft = DraftAt(model, 100, 100);
            Sculpt(model, 100, 100);
            Assert.IsTrue(DraftAt(model, 100, 100) > draft + 1, "the draft rose");
            Assert.AreEqual(final, FinalAt(model, 100, 100), 1e-3, "the final did not");
            Assert.AreSame(model.Map.Document.DraftHeightmap.Samples, model.ViewDocument!.Terrain.Heightmap.Samples, "the views draw the draft");
            Assert.AreSame(model.ViewDocument, model.ViewDocument, "one view per version");
            Assert.AreEqual(FinalAt(model, 30, 30), model.ViewDocument.Terrain.Heightmap.GetHeight(30, 30), 1e-3, "away from the stroke the same ground");

            // the draft's on/off is its own
            await Run(model, "terrain.setLayer", new JsonObject { ["use"] = "shared" });
            await Run(model, "terrain.setLayer", new JsonObject { ["visible"] = false });
            Assert.IsFalse(model.Map.Document.DraftStack.Find("Sculpt")!.Visible);
            Assert.IsTrue(model.Map.Document.HeightStack.Find("Sculpt")!.Visible);
            Assert.AreEqual(draft, DraftAt(model, 100, 100), 1e-3);
            Assert.IsTrue(FinalAt(model, 100, 100) > final + 1, "shared, the final has the sculpting");

            // every change was a step of undo
            model.Undo();
            model.Undo();
            Assert.AreEqual(LayerUse.Draft, HeightStacks.UseOf(model.Map.Document, "Sculpt"));
        }

        [TestMethod]
        public async Task TheListFiltersByUseAndSource()
        {
            MainViewModel model = await Open();
            await Run(model, "terrain.addLayer");
            await Run(model, "terrain.addCurve", new JsonObject { ["points"] = new JsonArray(new JsonArray(40, 40), new JsonArray(90, 60)) });
            await Run(model, "terrain.setLayer", new JsonObject { ["layer"] = "Ridge", ["use"] = "final" });
            CollectionAssert.AreEqual(new[] { "Ridge", "Sculpt", "Base" }, Listed(model), string.Join(", ", Listed(model)));

            await Run(model, "terrain.filterLayers", new JsonObject { ["source"] = "curve" });
            CollectionAssert.AreEqual(new[] { "Ridge" }, Listed(model));
            Assert.IsTrue(model.HeightStack.SourceChips.Single(chip => chip.Value == "curve").IsOn);
            await Run(model, "terrain.filterLayers", new JsonObject { ["source"] = "all", ["use"] = "shared" });
            CollectionAssert.AreEqual(new[] { "Sculpt", "Base" }, Listed(model));
            await Run(model, "terrain.filterLayers", new JsonObject { ["use"] = "final" });
            CollectionAssert.AreEqual(new[] { "Ridge" }, Listed(model));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "terrain.filterLayers", new JsonObject { ["use"] = "everything" }));

            // a layer the filter hides can still be chosen, and adding one chooses it
            await Run(model, "terrain.chooseLayer", new JsonObject { ["layer"] = "Sculpt" });
            Assert.AreEqual("Sculpt", model.HeightStack.ChosenLayer?.Name);

            JsonObject state = DraftFinalState.Describe(model);
            Assert.AreEqual("final", state["shown"]!.GetValue<string>());
            Assert.AreEqual(3, state["final"]!.AsArray().Count);
            Assert.AreEqual(2, state["draft"]!.AsArray().Count);
            Assert.IsTrue(state["draftOwnHeights"]!.GetValue<bool>(), "the ridge makes the final differ");
        }

        [TestMethod]
        public async Task AnOutputBecomesTheFinalsBaseAndTheExportTakesTheFinal()
        {
            MainViewModel model = await Open();
            string project = NewFolder();
            await model.SaveAsAsync(project);
            string output = WriteOutput(project, 20);

            CommandRun used = await Run(model, "terrain.useAsFinalBase", new JsonObject { ["path"] = output });
            Assert.AreEqual(HeightStacks.FinalBaseName, used.Result?.ToString());
            Assert.AreEqual(20, FinalAt(model, 100, 100), 1e-3, "the final is the output");
            Assert.AreNotEqual(20, DraftAt(model, 100, 100), 1, "the draft keeps the map's heights");
            Assert.AreEqual("procedural/temp/gaea.png", HeightStacks.FinalBase(model.Map!.Document)!.Path);
            Assert.IsTrue(model.DraftFinal.HasOwnBase);
            StringAssert.Contains(model.DraftFinal.BaseFacts, "procedural/temp/gaea.png");
            CollectionAssert.AreEqual(new[] { "procedural/temp/gaea.png" }, DraftFinalState.Describe(model)["outputs"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());

            // a new layer while the final is shown goes into the final only
            await Run(model, "terrain.addLayer", new JsonObject { ["name"] = "Touch-ups" });
            Assert.AreEqual(LayerUse.Final, HeightStacks.UseOf(model.Map.Document, "Touch-ups"));

            string exported = await model.ExportAsync(NewFolder());
            string scmapFile = Directory.GetFiles(exported, "*.scmap").Single();
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(scmapFile));
            CollectionAssert.AreEqual(model.Map.Document.Terrain.Heightmap.Samples.ToArray(), scmap.Heightmap.Samples, "the game gets the final");

            await Run(model, "terrain.shareDraftBase");
            Assert.IsFalse(model.DraftFinal.HasOwnBase);
            Assert.AreEqual(DraftAt(model, 100, 100), FinalAt(model, 100, 100), 1e-3);
        }

        [TestMethod]
        public async Task TheDraftIsWrittenForTheToolsAndAgainWhenItChanges()
        {
            MainViewModel model = await Open();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "terrain.writeDraft"), "a map that is no project yet");
            string project = NewFolder();
            await model.SaveAsAsync(project);
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "rivers" });

            await Run(model, "terrain.setDraftOut", new JsonObject { ["masks"] = new JsonArray("rivers") });
            CommandRun written = await Run(model, "terrain.writeDraft");
            Assert.IsTrue(written.Ran);
            Assert.IsTrue(File.Exists(Path.Combine(project, ProceduralFiles.DraftHeightmap)));
            Assert.IsTrue(File.Exists(Path.Combine(project, ProceduralFiles.DraftHeightmapRaw)));
            Assert.IsTrue(File.Exists(Path.Combine(project, ProceduralFiles.DraftMask("rivers"))));
            Assert.AreEqual(1, model.DraftWrites);

            // written again a moment after the draft changes
            model.DraftWriteDelay = TimeSpan.FromMilliseconds(10);
            await Run(model, "terrain.setDraftOut", new JsonObject { ["auto"] = true });
            Assert.IsTrue(model.Map!.Document.Procedural.WriteDraftOnChange);
            await model.WhenDraftWrittenAsync();
            int writes = model.DraftWrites;
            await Run(model, "terrain.switchHeights", new JsonObject { ["to"] = "draft" });
            Sculpt(model, 100, 100);
            await model.WhenDraftWrittenAsync();
            Assert.AreEqual(writes + 1, model.DraftWrites, "the stroke wrote it again");
            (_, _, ushort[] png) = HeightmapFiles.Read(Path.Combine(project, ProceduralFiles.DraftHeightmap));
            CollectionAssert.AreEqual(model.Map.Document.DraftHeightmap.Samples.ToArray(), png);

            // kept in the project
            await model.SaveAsync();
            MapDocument reopened = MapProjectStore.Load(project);
            Assert.IsTrue(reopened.Procedural.WriteDraftOnChange);
            CollectionAssert.AreEqual(new[] { "rivers" }, reopened.Procedural.DraftMasks.ToArray());
        }
    }
}
