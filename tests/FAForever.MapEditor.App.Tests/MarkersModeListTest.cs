using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Markers mode as "pick on the left, edit in the inspector" (wave 14B, user stories R-1 to
    /// R-6): the panel lists the kinds with their counts and show check boxes, the layers of path
    /// nodes, the chains (kept in step with the map) and the adaptive map; the inspector shows the
    /// chosen entry, with Select them and Clear (<c>markers.clear</c>: every kind, one step of undo).
    /// </summary>
    [TestClass]
    public sealed class MarkersModeListTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            await model.OpenAsync(HardFfa);
            model.SelectMode("markers");
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        /// <summary>
        /// Lays a path of land nodes, each linked to the one before, and an air node.
        /// </summary>
        private static async Task<int[]> LayLandPath(MainViewModel model, int count)
        {
            List<int> ids = [];
            for (int i = 0; i < count; i++)
            {
                JsonObject arguments = new JsonObject { ["kind"] = "path", ["x"] = 20 + i * 8, ["z"] = 30, ["layer"] = "Land" };
                if (ids.Count > 0)
                {
                    arguments["linkTo"] = ids[^1];
                }
                CommandRun placed = await Run(model, "markers.place", arguments);
                Assert.IsTrue(placed.Ran, placed.Why);
                ids.Add(ObjectRef.Parse(((JsonObject)placed.Result!)["ref"]!.GetValue<string>()).Id);
            }
            Assert.IsTrue((await Run(model, "markers.place", new JsonObject { ["kind"] = "path", ["x"] = 60, ["z"] = 60, ["layer"] = "Air" })).Ran);
            return [.. ids];
        }

        [TestMethod]
        public async Task TheListHasAKindEachALayerEachTheChainsAndTheAdaptiveMap()
        {
            MainViewModel model = await Open();
            PanelParts parts = model.Markers.Parts;
            MapDocument document = model.ShownDocument!;

            CollectionAssert.AreEqual(new[] { "Markers", "Path nodes", "Chains and options" }, parts.Groups.Select(group => group.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "markers.kind.spawns", "markers.kind.mass", "markers.kind.hydrocarbon", "markers.kind.ai", "markers.kind.weather" },
                parts.Groups[0].Parts.Select(part => part.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "markers.paths.land", "markers.paths.amphibious", "markers.paths.water", "markers.paths.air" },
                parts.Groups[1].Parts.Select(part => part.Id).ToArray());
            Assert.AreEqual(document.Markers.Spawns.Length.ToString(), parts.Find("markers.kind.spawns")!.Detail, "a kind's count beside it");
            Assert.AreEqual(document.Markers.Resources.Count(r => r.Kind == ResourceKind.Mass).ToString(), parts.Find("markers.kind.mass")!.Detail);
            Assert.AreEqual("off", parts.Find(MarkersViewModel.AdaptivePartId)!.Detail);
            Assert.IsNotNull(model.DescribePanelParts()["markers"], "state.panelParts lists the Markers mode's list");

            // a layer's entry counts its nodes and links
            int[] path = await LayLandPath(model, 3);
            MarkerKindItem land = (MarkerKindItem)parts.Find("markers.paths.land")!.Content;
            Assert.AreEqual(3, land.Count);
            Assert.AreEqual(2, land.Links, "a link both ways counts once");
            Assert.AreEqual("3", parts.Find("markers.paths.land")!.Detail);

            // a chain is an entry of its own, chosen when made, renamed with its entry, gone when deleted
            Assert.AreEqual("Chain_1", (await Run(model, "markers.addChain", new JsonObject { ["markers"] = new JsonArray(path[0], path[1]) })).Result);
            Assert.AreEqual("markers.chain.Chain_1", parts.Chosen.Id, "the new chain is chosen");
            Assert.AreEqual("2 markers", parts.Chosen.Detail);
            ChainPart chain = (ChainPart)parts.Chosen.Content;
            Assert.AreEqual(2, chain.Chain!.Entries.Count);
            CollectionAssert.AreEqual(new[] { "markers.chain.Chain_1", MarkersViewModel.AdaptivePartId }, parts.Groups[2].Parts.Select(part => part.Id).ToArray(), "the chains, then the adaptive map");

            chain.NameText = "Patrol_Chain";
            await Task.Yield();
            Assert.AreEqual("Patrol_Chain", model.ShownDocument!.Markers.Chains[0].Name);
            Assert.AreEqual("markers.chain.Patrol_Chain", parts.Chosen.Id, "the renamed chain stays chosen");
            Assert.AreEqual("Patrol_Chain", model.Markers.SelectedChain?.Name, "and is the chain tool's");

            await ((ChainPart)parts.Chosen.Content).DeleteCommand.ExecuteAsync(null);
            Assert.IsNull(parts.Find("markers.chain.Patrol_Chain"));
            Assert.AreEqual(MarkersViewModel.AdaptivePartId, parts.Chosen.Id, "the heading's first entry is chosen");

            Assert.IsTrue((await Run(model, "markers.adaptive", new JsonObject { ["on"] = true })).Ran);
            StringAssert.StartsWith(parts.Find(MarkersViewModel.AdaptivePartId)!.Detail, "on");
        }

        [TestMethod]
        public async Task ClearRemovesTheLayerAsOneStepOfUndoAndSaysHowManyWent()
        {
            MainViewModel model = await Open();
            int[] path = await LayLandPath(model, 4);
            await Run(model, "markers.addChain", new JsonObject { ["markers"] = new JsonArray(path[0], model.ShownDocument!.Markers.Resources[0].Id) });
            MapDocument before = model.ShownDocument!;
            int land = before.CountKind(MarkerKinds.LandPaths);
            int steps = model.History.Count;

            CommandRun cleared = await Run(model, "markers.clear", new JsonObject { ["kind"] = "path", ["layer"] = "land" });

            Assert.IsTrue(cleared.Ran, cleared.Why);
            Assert.AreEqual(land, cleared.Result);
            Assert.AreEqual(0, model.ShownDocument!.CountKind(MarkerKinds.LandPaths));
            Assert.AreEqual(1, model.ShownDocument!.CountKind(MarkerKinds.AirPaths), "another layer stays");
            CollectionAssert.AreEqual(new[] { before.Markers.Resources[0].Id }, model.ShownDocument!.Markers.Chains[0].MarkerIds.ToArray(), "the chain lost its node");
            Assert.AreEqual($"Removed {land} land path nodes", model.Status);
            Assert.AreEqual(steps + 1, model.History.Count, "one step of undo");
            Assert.AreEqual($"Remove {land} land path nodes", model.Map!.Session.UndoDescription);
            Assert.AreEqual("0", model.Markers.Parts.Find("markers.paths.land")!.Detail);

            Assert.IsTrue(model.Undo());
            Assert.AreSame(before, model.ShownDocument, "undo brings them back");

            // nothing to clear: no step
            Assert.IsTrue((await Run(model, "markers.clear", new JsonObject { ["kind"] = "path", ["layer"] = "water" })).Ran);
            Assert.AreEqual("The map has no water path nodes to remove", model.Status);
            Assert.AreSame(before, model.ShownDocument);

            // every path node; every spawn with its playable army
            Assert.AreEqual(land + 1, (await Run(model, "markers.clear", new JsonObject { ["kind"] = "path" })).Result);
            Assert.AreEqual(0, model.ShownDocument!.Markers.PathNodes.Length);
            int spawns = model.ShownDocument.Markers.Spawns.Length;
            Assert.AreEqual(spawns, (await Run(model, "markers.clear", new JsonObject { ["kind"] = "spawns" })).Result);
            Assert.AreEqual(0, model.ShownDocument!.Scenario.PlayableArmies.Length);
            Assert.AreEqual($"Removed {spawns} spawns", model.Status);
        }

        [TestMethod]
        public async Task TheInspectorsButtonsSelectAndClearTheChosenKind()
        {
            MainViewModel model = await Open();
            await LayLandPath(model, 3);
            model.InspectPart("markers.kind.mass");
            MarkerKindItem mass = (MarkerKindItem)model.Markers.Parts.Chosen.Content;
            int deposits = model.ShownDocument!.CountKind(MarkerKinds.Mass);

            model.Markers.Show(MarkerKinds.Mass, false);
            await mass.SelectCommand.ExecuteAsync(null);
            Assert.AreEqual(deposits, model.Selection.Items.Count());
            Assert.IsTrue(model.Markers.Shows(MarkerKinds.Mass), "selecting a kind shows it");
            Assert.AreEqual($"Selected {deposits} mass deposits", model.Status);
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner, "the selection takes the inspector");

            await mass.ClearCommand.ExecuteAsync(null);
            Assert.AreEqual(0, model.ShownDocument!.CountKind(MarkerKinds.Mass));
            Assert.IsTrue(model.Selection.IsEmpty, "what went is no longer selected");

            MarkerKindItem air = (MarkerKindItem)model.Markers.Parts.Find("markers.paths.air")!.Content;
            await air.ClearEveryPathNodeCommand.ExecuteAsync(null);
            Assert.AreEqual(0, model.ShownDocument!.Markers.PathNodes.Length, "every layer");
        }

        [TestMethod]
        public void KindsAreNamedAsShowKindNamesThemAndPathNodesByLayer()
        {
            Assert.AreEqual(MarkerKinds.LandPaths, MarkersViewModel.KindsOf("path", "Land"));
            Assert.AreEqual(MarkerKinds.PathNodes, MarkersViewModel.KindsOf("path", null));
            Assert.AreEqual(MarkerKinds.AirPaths, MarkersViewModel.KindsOf("airPaths", null));
            Assert.AreEqual(MarkerKinds.Spawns, MarkersViewModel.KindsOf("spawn", null), "as markers.place names it");
            Assert.AreEqual(MarkerKinds.AiMarkers, MarkersViewModel.KindsOf("AI", ""));
            Assert.ThrowsException<ArgumentException>(() => MarkersViewModel.KindsOf("path", "hover"));
            Assert.ThrowsException<ArgumentException>(() => MarkersViewModel.KindsOf("mass", "land"));
            Assert.ThrowsException<ArgumentException>(() => MarkersViewModel.KindsOf("trees", null));
            Assert.AreEqual("1 spawn", MarkersViewModel.Noun(MarkerKinds.Spawns, 1, number: true));
            Assert.AreEqual("412 land path nodes", MarkersViewModel.Noun(MarkerKinds.LandPaths, 412));
        }

        [TestMethod]
        public void ALayersEntrySaysHowTheExportTreatsTheGeneratedNetwork()
        {
            Assert.AreEqual("not written", MarkersViewModel.Export(PathNodeSettings.Default, PathLayer.Land).Fact);
            PathNodeSettings written = PathNodeSettings.Default with { OnExport = true };
            Assert.AreEqual("joined at export", MarkersViewModel.Export(written, PathLayer.Land).Fact);
            Assert.AreEqual("replace at export", MarkersViewModel.Export(written with { Replace = true }, PathLayer.Water).Fact);
            Assert.AreEqual("no network", MarkersViewModel.Export(written with { Layers = [PathLayer.Land] }, PathLayer.Air).Fact);
        }

        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> OpenWindow()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            Assert.IsTrue((await model.Commands.RunAsync("markers.mode", null, interactive: false)).Ran);
            return (window, model, automation);
        }

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            Dispatcher.UIThread.RunJobs();
            return answer;
        }

        private static Control? InInspector(MainWindow window, string label) => window.GetVisualDescendants().OfType<Inspector>().Single()
            .GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Avalonia.Automation.AutomationProperties.GetName(control) == label && control.IsEffectivelyVisible);

        [TestMethod]
        public Task ClickingALayerShowsItInTheInspectorWhoseClearRemovesIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await OpenWindow();
            await LayLandPath(model, 3);
            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);
            Dispatcher.UIThread.RunJobs();

            await Send(automation, "click", new JsonObject { ["label"] = "Land path nodes" });

            Assert.AreEqual("markers.paths.land", model.Markers.Parts.Chosen.Id);
            Assert.IsFalse(model.InspectsSelection, "the chosen entry has the inspector");
            Assert.IsTrue(model.Selection.HasAny, "and the selection stays");
            Assert.AreEqual("3", ((TextBlock)InInspector(window, "How many markers of the kind")!).Text);
            Assert.AreEqual("2", ((TextBlock)InInspector(window, "How many links the layer has")!).Text);

            await Send(automation, "click", new JsonObject { ["label"] = "Clear the layer" });
            Assert.AreEqual(0, model.ShownDocument!.CountKind(MarkerKinds.LandPaths));
            Assert.AreEqual("Removed 3 land path nodes", model.Status);
            Assert.AreEqual("0", ((TextBlock)InInspector(window, "How many markers of the kind")!).Text);
            window.Close();
        });

        [TestMethod]
        public Task TheCheckBoxesShowAKindAndTheirHeadingsOverlay() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await OpenWindow();
            Assert.IsTrue(model.Markers.Shows(MarkerKinds.Mass));

            await Send(automation, "click", new JsonObject { ["label"] = "Show mass" });
            Assert.IsFalse(model.Markers.Shows(MarkerKinds.Mass), "a kind's check box hides it");
            Assert.AreEqual("markers.kind.spawns", model.Markers.Parts.Chosen.Id, "and chooses no entry");

            // an entry is named apart from the tool of the same title
            await Send(automation, "click", new JsonObject { ["label"] = "Mass deposits" });
            Assert.AreEqual("markers.kind.mass", model.Markers.Parts.Chosen.Id);
            Assert.AreEqual("markers.select", model.ActiveTool?.Definition.Id, "the Mass tool stays unchosen");

            bool paths = model.ShowPathNodes;
            await Send(automation, "click", new JsonObject { ["label"] = "Show path nodes" });
            Assert.AreEqual(!paths, model.ShowPathNodes, "the heading's check box is the overlay's");
            CheckBox heading = window.GetVisualDescendants().OfType<CheckBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Show path nodes");
            Assert.AreEqual(!paths, heading.IsChecked);
            window.Close();
        });
    }
}
