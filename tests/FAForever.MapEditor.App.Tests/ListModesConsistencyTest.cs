using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The list modes alike (wave 16B): one layout of an entry (its detail beside its title or under
    /// it, never cutting it short), the Units mode's tree and inspector as the other modes', one place
    /// that looks chosen, and the Map and Markers modes' fixes of the wave 15 walkthrough.
    /// </summary>
    [TestClass]
    public sealed class ListModesConsistencyTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// Answers a confirmation as told, and counts the questions.
        /// </summary>
        private sealed class Confirming : IEditorDialogs
        {
            public bool Answer { get; set; }

            public List<string> Questions { get; } = [];

            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);

            public Task<bool> ConfirmAsync(string title, string message, string action)
            {
                Questions.Add(message);
                return Task.FromResult(Answer);
            }
        }

        private static async Task<MainViewModel> Open(string mode, IEditorDialogs? dialogs = null)
        {
            MainViewModel model = new MainViewModel(dialogs ?? new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.SelectMode(mode);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null, bool interactive = false) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive);

        [TestMethod]
        public void ArmiesAreListedPlayableFirstByNumberThenTheExtraArmies()
        {
            // Archsimkats Valley's order: playable and extra armies mixed
            ArmyRow[] rows =
            [
                new ArmyRow("ARMY_4", true, null, true, 0, ""),
                new ArmyRow("ARMY_2", true, null, true, 0, ""),
                new ArmyRow("ARMY_17", false, null, false, 3, ""),
                new ArmyRow("NEUTRAL_CIVILIAN", false, null, false, 5, ""),
                new ArmyRow("JIP", true, null, true, 0, ""),
                new ArmyRow("ARMY_10", true, null, true, 0, ""),
                new ArmyRow("ARMY_1", true, null, true, 0, ""),
            ];
            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2", "ARMY_4", "ARMY_10", "JIP", "ARMY_17", "NEUTRAL_CIVILIAN" },
                MapPartsViewModel.InListOrder(rows).Select(row => row.Name).ToArray());
        }

        [TestMethod]
        public async Task AnArmyAddedAfterTheExtraArmiesIsListedWithThePlayableOnes()
        {
            MainViewModel model = await Open("map");
            Assert.IsTrue((await Run(model, "map.addArmy")).Ran);
            string[] armies = [.. model.MapParts.Parts.Items.Where(part => part.Group == MapPartsViewModel.ArmiesGroup).Select(part => part.Title)];
            // HardFFA's extra armies are NEUTRAL_CIVILIAN and ARMY_9; the new army is ARMY_10
            CollectionAssert.AreEqual(new[] { "ARMY_8", "ARMY_10", "NEUTRAL_CIVILIAN", "ARMY_9" }, armies[^4..], "the playable armies by number, then the extra ones");
        }

        [TestMethod]
        public void TheArmiesFactCountsInWords()
        {
            Assert.AreEqual("2 players, 1 other army", MapInformationViewModel.PlayersText(2, 1));
            Assert.AreEqual("1 player, 3 other armies", MapInformationViewModel.PlayersText(1, 3));
        }

        [TestMethod]
        public async Task AnAreaChosenWhileTheMapSizeToolIsActiveTakesTheAreasToolSoItsHandlesShow()
        {
            MainViewModel model = await Open("map");
            await Run(model, "map.addArea");
            model.InspectPart(MapPartsViewModel.SizePart);
            Assert.AreEqual("map.size", model.ActiveTool!.Definition.Id);
            Assert.AreEqual("Map mode: Map size", model.Status, "the status line follows the tool the entry chose");

            model.InspectPart(MapPartsViewModel.AreaPart("AREA_1"));
            Assert.AreEqual("map.areas", model.ActiveTool!.Definition.Id, "the Map size tool draws no area's handles");
            Assert.AreEqual("Map mode: Areas", model.Status);
            CollectionAssert.AreEqual(new[] { ObjectRef.Area("AREA_1") }, model.Selection.Items.ToArray());
            Assert.IsNotNull(model.ActiveTool.Tool!.Marks, "the handles of the selected area");
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);
        }

        [TestMethod]
        public async Task TheChosenEntryLooksChosenOnlyWhileTheInspectorShowsIt()
        {
            MainViewModel model = await Open("map");
            PanelPart information = model.MapParts.Parts.Find(MapPartsViewModel.InformationPart)!;
            Assert.IsTrue(information.LooksChosen);

            // a marker clicked on the map takes the inspector: the entry stays chosen but does not look it
            Assert.IsTrue((await Run(model, "select.set", new JsonObject { ["refs"] = new JsonArray("marker:" + model.ShownDocument!.Markers.Spawns[0].Id) })).Ran);
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner);
            Assert.IsTrue(information.IsChosen);
            Assert.IsFalse(information.LooksChosen, "one place looks chosen");
            Assert.IsFalse(model.MapParts.Parts.Items.Any(part => part.LooksChosen));

            // the entry chosen again gives it back
            model.InspectPart(MapPartsViewModel.InformationPart);
            Assert.IsTrue(information.LooksChosen);

        }

        [TestMethod]
        public void AnEntryAddedWhileTheListHasNotTheInspectorDoesNotLookChosen()
        {
            PanelParts parts = new PanelParts([new PanelPart("a", "A", "Things", new object())]);
            parts.HasInspector = false;
            parts.SyncGroup("More", [new PanelPartEntry("b", "B", () => new object())]);
            parts.Choose("b");
            Assert.IsTrue(parts.Find("b")!.IsChosen);
            Assert.IsFalse(parts.Find("b")!.LooksChosen);
            parts.HasInspector = true;
            Assert.IsTrue(parts.Find("b")!.LooksChosen);
            Assert.IsFalse(parts.Find("a")!.LooksChosen);
        }

        [TestMethod]
        public async Task AKindsShownFollowsTheOverlayOfItsHeading()
        {
            MainViewModel model = await Open("markers");
            MarkerKindItem land = model.Markers.Kinds.Single(kind => kind.Kind == MarkerKinds.LandPaths);
            MarkerKindItem mass = model.Markers.Kinds.Single(kind => kind.Kind == MarkerKinds.Mass);
            model.ShowPathNodes = false;
            Assert.IsFalse(land.IsOn, "the Path nodes chip is off: the layer is not shown");
            Assert.AreEqual("no", land.ShownText);
            Assert.IsTrue(model.Markers.Shows(MarkerKinds.LandPaths), "the list still has it on, for when the chip comes back");

            model.ShowPathNodes = true;
            Assert.IsTrue(land.IsOn);

            // the check box of a hidden layer shows it: the kind and its overlay
            model.ShowPathNodes = false;
            await land.ToggleShownCommand.ExecuteAsync(null);
            Assert.IsTrue(model.ShowPathNodes, "showing a layer turns its overlay on");
            Assert.IsTrue(land.IsOn);

            model.ShowMarkers = false;
            Assert.IsFalse(mass.IsOn);
            Assert.AreEqual(true, (await Run(model, "markers.showKind", new JsonObject { ["kind"] = "mass", ["on"] = true })).Result);
            Assert.IsTrue(model.ShowMarkers);
            Assert.AreEqual(false, (await Run(model, "markers.showKind", new JsonObject { ["kind"] = "mass" })).Result, "turned over: hidden");
            Assert.IsTrue(model.ShowMarkers, "hiding a kind leaves the overlay");
        }

        [TestMethod]
        public async Task ChoosingAChainLeavesTheOverlaysAsTheyAre()
        {
            MainViewModel model = await Open("markers");
            int[] ids = [model.ShownDocument!.Markers.Resources[0].Id, model.ShownDocument.Markers.Resources[1].Id];
            Assert.IsTrue((await Run(model, "markers.addChain", new JsonObject { ["markers"] = new JsonArray(ids[0], ids[1]) })).Ran);
            model.ShowPathNodes = false;
            string chain = model.Markers.Chains[^1].Name;

            model.InspectPart(MarkersViewModel.ChainPartId(chain));
            Assert.IsFalse(model.ShowPathNodes, "choosing a chain turns no overlay on");
            model.InspectPart("markers.kind.mass");
            Assert.IsFalse(model.ShowPathNodes);
        }

        [TestMethod]
        public async Task ClearingSpawnsThatTakeArmiesAsksFirstAndNamesTheArmies()
        {
            Confirming dialogs = new Confirming();
            MainViewModel model = await Open("markers", dialogs);
            MapDocument before = model.ShownDocument!;

            // answered no: nothing goes
            CommandRun kept = await Run(model, "markers.clear", new JsonObject { ["kind"] = "spawns" }, interactive: true);
            Assert.IsTrue(kept.Ran, kept.Why);
            Assert.AreEqual(0, kept.Result);
            Assert.AreEqual(1, dialogs.Questions.Count);
            StringAssert.Contains(dialogs.Questions[0], "ARMY_1");
            Assert.AreSame(before, model.ShownDocument);
            Assert.AreEqual("Kept the spawns", model.Status);

            // without a person it needs confirm
            CommandRun refused = await model.Commands.TryRunAsync("markers.clear", new JsonObject { ["kind"] = "spawns" }, interactive: false);
            Assert.IsFalse(refused.Ran);
            StringAssert.Contains(refused.Why, "confirm");

            // yes: the spawns and their armies, named on the status line, one step of undo
            dialogs.Answer = true;
            int steps = model.History.Count;
            CommandRun cleared = await Run(model, "markers.clear", new JsonObject { ["kind"] = "spawns" }, interactive: true);
            Assert.AreEqual(before.Markers.Spawns.Length, cleared.Result);
            StringAssert.StartsWith(model.Status, $"Removed {before.Markers.Spawns.Length} spawns and the armies ARMY_1, ARMY_2, ARMY_3");
            StringAssert.EndsWith(model.Status, "and ARMY_8", "by number");
            Assert.AreEqual(steps + 1, model.History.Count);
            Assert.IsTrue(model.Undo());
            Assert.AreSame(before, model.ShownDocument);

            // other kinds clear without a question
            int questions = dialogs.Questions.Count;
            Assert.IsTrue((await Run(model, "markers.clear", new JsonObject { ["kind"] = "mass" }, interactive: true)).Ran);
            Assert.AreEqual(questions, dialogs.Questions.Count);
            StringAssert.StartsWith(model.Status, "Removed");
        }

        [TestMethod]
        public void TheArmiesNamedReadAsAList()
        {
            Assert.AreEqual("the army ARMY_1", ClearMarkersHandler.Armies(["ARMY_1"]));
            Assert.AreEqual("the armies ARMY_1, ARMY_2 and ARMY_3", ClearMarkersHandler.Armies(["ARMY_1", "ARMY_2", "ARMY_3"]));
        }

        [TestMethod]
        public void BlankMarkersShowInAColourOfTheirOwnWhileTheExportKeepsTheGames()
        {
            AiMarker blank = new AiMarker { Id = 1, Name = "Blank Marker 00", Type = AiMarker.BlankType };
            Spawn spawn = new Spawn { Id = 2, Name = "ARMY_1" };
            Assert.AreEqual(spawn.Color, blank.Color, "the game writes both in its purple");
            Assert.AreNotEqual(MarkersViewModel.ShownColour(spawn), MarkersViewModel.ShownColour(blank), "the views tell them apart");
            Assert.AreEqual(MarkersViewModel.BlankColour, MarkersViewModel.Choices.Single(choice => choice.Kind == MarkerKinds.Blank).Colour, "the list's swatch as the views");
        }

        [TestMethod]
        public async Task NewMarkersAreNumberedFromZeroAsTheGamesEditorsNumberThem()
        {
            MainViewModel model = await Open("markers");
            JsonObject first = (JsonObject)(await Run(model, "markers.place", new JsonObject { ["kind"] = "blank", ["x"] = 40, ["z"] = 40 })).Result!;
            JsonObject second = (JsonObject)(await Run(model, "markers.place", new JsonObject { ["kind"] = "blank", ["x"] = 60, ["z"] = 40 })).Result!;
            Assert.AreEqual("Blank Marker 00", first["name"]!.GetValue<string>());
            Assert.AreEqual("Blank Marker 01", second["name"]!.GetValue<string>());
        }

        [TestMethod]
        public void TheMovedUnitsLineNamesTheArmy()
        {
            Assert.AreEqual("Moved 2 units to INITIAL/Town of ARMY_1", MoveUnitsToGroupHandler.Moved(2, ["INITIAL", "Town"], ["ARMY_1"]));
            Assert.AreEqual("Moved 1 unit to the root group of ARMY_2", MoveUnitsToGroupHandler.Moved(1, ImmutableArray<string>.Empty, ["ARMY_2"]));
            Assert.AreEqual("Moved 3 units to INITIAL of ARMY_1 and ARMY_2", MoveUnitsToGroupHandler.Moved(3, ["INITIAL"], ["ARMY_1", "ARMY_2"]));
        }

        [TestMethod]
        public async Task TheUnitsTreeAndInspectorReadAsTheOtherModes()
        {
            MainViewModel model = await Open("units");
            GroupNode army = model.Armies.Tree.First();
            Assert.AreEqual(army.Army, army.Title);
            Assert.AreEqual(MainViewModel.Units(army.Count), army.Detail, "the count as a detail, as Map and Markers show theirs");
            Assert.IsFalse(army.Title.Contains('('));

            model.Armies.ChosenGroup = army;
            Assert.AreEqual(army.Army, model.Armies.ChosenGroupTitle);
            StringAssert.StartsWith(model.Armies.ChosenGroupCaption, "An army");
            Assert.AreEqual("Unit presets", model.SelectedMode!.Definition.Sections.OfType<PresetSection>().Single().Title, "<Kind> presets, as every mode");
        }

        [TestMethod]
        public void EveryListModesPresetsAreNamedAfterTheirKind()
        {
            foreach (string mode in (string[])["map", "markers", "units", "water", "lighting"])
            {
                foreach (PresetSection presets in EditorModes.All.Single(definition => definition.Id == mode).Sections.OfType<PresetSection>())
                {
                    StringAssert.EndsWith(presets.Title, " presets", mode);
                }
            }
        }

        [TestMethod]
        public Task AnEntrysDetailGoesUnderItsTitleWhenBothDoNotFitAndTheListGoesAlike() => HeadlessApp.Run(() =>
        {
            StackPanel list = new StackPanel();
            ItemsControl items = new ItemsControl { ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel()) };
            PartEntryText shortEntry = new PartEntryText { Title = "ARMY_1", Detail = "playable" };
            PartEntryText longEntry = new PartEntryText { Title = "Size and playable area", Detail = "512 · whole map" };
            items.Items.Add(shortEntry);
            items.Items.Add(longEntry);
            list.Children.Add(items);
            Window window = new Window { Width = 400, Height = 300, Content = list };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(shortEntry.IsStacked, "both fit on one line");
            Assert.IsFalse(longEntry.IsStacked);

            window.Width = 160;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.IsTrue(longEntry.IsStacked, "the detail goes under the title");
            Assert.IsTrue(shortEntry.IsStacked, "the entries of one list go alike");
            TextBlock title = longEntry.GetVisualChildren().OfType<TextBlock>().First();
            Assert.AreEqual(longEntry.Bounds.Width, title.Bounds.Width, "the title has the whole width: the detail never cuts it short");

            window.Width = 400;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.IsFalse(longEntry.IsStacked, "and back on one line when there is room");
            Assert.IsFalse(shortEntry.IsStacked);
            window.Close();
            return Task.CompletedTask;
        });

        [TestMethod]
        public Task EnterInTheTeamFieldSetsTheTeam() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            Assert.IsTrue((await model.Commands.RunAsync("map.mode", null, interactive: false)).Ran);
            model.InspectPart(MapPartsViewModel.ArmyPart("ARMY_2"));
            Dispatcher.UIThread.RunJobs();

            await Send(automation, "click", new JsonObject { ["label"] = "Team of the chosen army" });
            await Send(automation, "type", new JsonObject { ["text"] = "3" });
            await Send(automation, "key", new JsonObject { ["key"] = "Enter" });
            Assert.AreEqual("3", model.Armies.Rows.Single(row => row.Name == "ARMY_2").Team, "Enter sets the team, as the Set button does");
            Assert.AreEqual("team 3", model.MapParts.Parts.Find(MapPartsViewModel.ArmyPart("ARMY_2"))!.Detail);
            window.Close();
        });

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject arguments)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments);
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            return answer;
        }
    }
}
