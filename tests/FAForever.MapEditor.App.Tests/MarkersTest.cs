using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Markers mode (user stories R-1 to R-6): placing markers per kind, the path tool, chains,
    /// the inspector's marker fields, the adaptive map, and showing markers per kind.
    /// </summary>
    [TestClass]
    public class MarkersTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static async Task<MainViewModel> Open(string tool)
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return model;
        }

        private static IMapTool ActiveTool(MainViewModel model) => model.ActiveTool!.Tool!;

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static Marker Selected(MainViewModel model) => (Marker)model.ShownDocument!.Find(model.Selection.Items.Single())!;

        private static async Task Click(IMapTool tool, float x, float z, ToolModifiers modifiers = ToolModifiers.None)
        {
            tool.Press(x, z, ToolInput.Default with { Modifiers = modifiers });
            tool.Release();
            await Task.Yield();
        }

        [TestMethod]
        public async Task ADepositToolPlacesOnAnOgridsCentreAsOneStepAndSelectsIt()
        {
            MainViewModel model = await Open("markers.mass");
            MapDocument opened = model.ShownDocument!;

            await Click(ActiveTool(model), 40.2f, 60.9f);

            ResourceMarker placed = (ResourceMarker)Selected(model);
            Assert.AreEqual(ResourceKind.Mass, placed.Kind);
            Assert.AreEqual(40.5f, placed.Position.X, "deposits sit on an ogrid's centre");
            Assert.AreEqual(60.5f, placed.Position.Z);
            Assert.AreEqual(opened.Terrain.Heightmap.HeightAt(40.5f, 60.5f), placed.Position.Y, 1e-3, "on the terrain");
            int highest = opened.Markers.All.Select(marker => int.TryParse(marker.Name.StartsWith("Mass ") ? marker.Name[5..] : "", out int number) ? number : 0).Max();
            Assert.AreEqual($"Mass {highest + 1:00}", placed.Name, "one more than the highest of its kind");
            Assert.AreEqual("Place a marker", model.Map!.Session.UndoDescription);
            Assert.AreEqual(2, model.History.Count, "opened, and the marker");
            Assert.IsNull(model.Markers.PlacementWarning, "flat ground");

            model.SelectTool("markers.hydrocarbon");
            await Click(ActiveTool(model), 80, 80);
            Assert.AreEqual(ResourceKind.Hydrocarbon, ((ResourceMarker)Selected(model)).Kind);
            Assert.IsTrue(model.Undo());
            Assert.IsTrue(model.Undo());
            Assert.AreSame(opened, model.ShownDocument);
        }

        [TestMethod]
        public async Task AClickOnADepositSelectsAndDragsItRatherThanStackingAnother()
        {
            MainViewModel model = await Open("markers.hydrocarbon");
            IMapTool tool = ActiveTool(model);
            await Click(tool, 80, 80);
            ResourceMarker first = (ResourceMarker)Selected(model);
            int markers = model.ShownDocument!.Markers.All.Count();
            int steps = model.History.Count;

            model.Selection.Clear();
            await Click(tool, 80.5f, 80.5f);
            Assert.AreEqual(markers, model.ShownDocument!.Markers.All.Count(), "no deposit stacked on the one there");
            Assert.AreEqual(first.Id, Selected(model).Id, "the one there is selected");
            Assert.AreEqual(steps, model.History.Count);

            // a drag from it moves it, one step
            tool.Press(80.5f, 80.5f, ToolInput.Default);
            tool.Drag(90.5f, 80.5f, ToolInput.Default);
            tool.Release();
            Assert.AreEqual(markers, model.ShownDocument!.Markers.All.Count());
            Assert.AreEqual(90.5f, model.ShownDocument.FindMarker(first.Id)!.Position.X, 1e-3, "moved along, on an ogrid's centre");
            Assert.AreEqual(steps + 1, model.History.Count);

            // placing one on its ogrid by command refuses and selects the one there
            model.Selection.Clear();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "markers.place", new JsonObject { ["kind"] = "mass", ["x"] = 90.2f, ["z"] = 80.7f }));
            Assert.AreEqual(first.Id, Selected(model).Id);
            Assert.AreEqual(markers, model.ShownDocument!.Markers.All.Count());
        }

        [TestMethod]
        public async Task ADepositOnSteepGroundWarns()
        {
            MainViewModel model = await Open("markers.mass");
            model.Map!.Session.Edit("A cliff", document =>
            {
                Heightmap heights = document.Terrain.Heightmap;
                TiledBuffer<ushort>.Builder builder = heights.Samples.ToBuilder();
                builder[31, 30] = (ushort)(heights.Samples[31, 30] + 30 / heights.Scale);
                return document with { Terrain = document.Terrain with { Heightmap = heights with { Samples = builder.ToBuffer() } } };
            });

            await Click(ActiveTool(model), 30.5f, 30.5f);

            StringAssert.Contains(model.Markers.PlacementWarning, "steep ground");
            Assert.AreEqual(model.Markers.PlacementWarning, model.Status, "the status line says it");
            MarkerSelection section = (MarkerSelection)model.Selection.Inspector.Sections.Single();
            Assert.IsTrue(section.HasWarning, "the inspector says it of the selected deposit");
        }

        [TestMethod]
        public async Task ASpawnIsTheNextPlayableArmyAndDeletingOneNumbersTheOthersDown()
        {
            MainViewModel model = await Open("markers.spawn");
            PlaceMarkerTool tool = (PlaceMarkerTool)ActiveTool(model);
            Assert.AreEqual(MarkerSnap.Centre, tool.Snap.Snap, "spawns snap to an ogrid's centre unless told otherwise");

            await Click(tool, 100.2f, 50.7f);

            Spawn spawn = (Spawn)Selected(model);
            Assert.AreEqual("ARMY_10", spawn.Name, "HardFFA has eight spawns and ARMY_9 for the civilians");
            Assert.AreEqual(100.5f, spawn.Position.X);
            CollectionAssert.Contains(model.ShownDocument!.Scenario.PlayableArmies.ToArray(), "ARMY_10");

            Spawn first = model.ShownDocument.Markers.Spawns.Single(s => s.Name == "ARMY_1");
            Assert.IsTrue((await Run(model, "select.set", new JsonObject { ["refs"] = new JsonArray($"marker:{first.Id}") })).Ran);
            Assert.IsTrue((await Run(model, "edit.delete")).Ran);
            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2", "ARMY_3", "ARMY_4", "ARMY_5", "ARMY_6", "ARMY_7", "ARMY_8" },
                model.ShownDocument!.Scenario.PlayableArmies.ToArray(), "the spawns are numbered in order again, around the civilians' ARMY_9");
            Assert.AreEqual("ARMY_8", model.ShownDocument.FindMarker(spawn.Id)!.Name, "ARMY_10 took the place of ARMY_8");
        }

        [TestMethod]
        public async Task TheAiToolPlacesItsTypeAndTheSnapChoiceApplies()
        {
            MainViewModel model = await Open("markers.ai");
            PlaceMarkerTool tool = (PlaceMarkerTool)ActiveTool(model);
            tool.AiType = "Expansion Area";
            tool.Snap = SnapChoice.Of(MarkerSnap.Half);

            await Click(tool, 20.3f, 20.8f);

            AiMarker marker = (AiMarker)Selected(model);
            Assert.AreEqual("Expansion Area", marker.Type);
            StringAssert.StartsWith(marker.Name, "Expansion Area ");
            Assert.AreEqual(20.5f, marker.Position.X);
            Assert.AreEqual(21f, marker.Position.Z);

            tool.Snap = SnapChoice.Of(MarkerSnap.Off);
            await Click(tool, 20.3f, 30.8f);
            Assert.AreEqual(20.3f, Selected(model).Position.X, 1e-4);
        }

        [TestMethod]
        public async Task ThePathToolLaysLinkedNodesAndLinksAndUnlinksByDragging()
        {
            MainViewModel model = await Open("markers.path");
            Assert.IsTrue(model.ShowPathNodes, "choosing the path tool shows the path nodes");
            PathTool tool = (PathTool)ActiveTool(model);
            tool.Layer = PathLayer.Water;

            await Click(tool, 10, 10);
            PathNode a = (PathNode)Selected(model);
            await Click(tool, 30, 10);
            PathNode b = (PathNode)Selected(model);
            await Click(tool, 30, 30, ToolModifiers.Shift);
            PathNode c = (PathNode)Selected(model);

            Assert.AreEqual(PathLayer.Water, a.Layer);
            StringAssert.StartsWith(a.Name, "WaterPN");
            Assert.IsTrue(model.ShownDocument!.AreLinked(a.Id, b.Id), "a click goes on from the selected node");
            Assert.IsFalse(model.ShownDocument.AreLinked(b.Id, c.Id), "shift starts a new path");
            Assert.AreEqual(4, model.History.Count, "opened, three nodes");

            // dragging from a node to another links them; with control, unlinks
            tool.Press(30, 30, ToolInput.Default);
            tool.Drag(20, 20, ToolInput.Default);
            tool.Drag(10, 10, ToolInput.Default);
            tool.Release();
            await Task.Yield();
            Assert.IsTrue(model.ShownDocument!.AreLinked(c.Id, a.Id));
            tool.Press(10, 10, ToolInput.Default with { Modifiers = ToolModifiers.Control });
            tool.Drag(30, 10, ToolInput.Default);
            tool.Release();
            await Task.Yield();
            Assert.IsFalse(model.ShownDocument!.AreLinked(a.Id, b.Id));

            // a click on a node goes on from it; Escape lets go
            await Click(tool, 30, 10);
            CollectionAssert.AreEqual(new[] { ObjectRef.Marker(b.Id) }, model.Selection.Items.ToArray());
            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None));
            await Task.Yield();
            Assert.IsTrue(model.Selection.IsEmpty);
        }

        [TestMethod]
        public async Task ChainsAreMadeFromTheSelectionOrderedRenamedAndDeleted()
        {
            MainViewModel model = await Open("markers.select");
            ResourceMarker[] deposits = [.. model.ShownDocument!.Markers.Resources.Take(3)];
            await Run(model, "select.set", new JsonObject { ["refs"] = new JsonArray([.. deposits.Reverse().Select(d => (JsonNode)JsonValue.Create($"marker:{d.Id}"))]) });

            CommandRun added = await Run(model, "markers.addChain");
            Assert.AreEqual("Chain_1", added.Result);
            ChainItem chain = model.Markers.SelectedChain!;
            Assert.AreEqual("Chain_1", chain.Name, "the new chain is chosen in the panel");
            CollectionAssert.AreEqual(deposits.Reverse().Select(d => d.Name).ToArray(), chain.Entries.Select(entry => entry.Name).ToArray(), "in the order they were selected");

            await model.Markers.MoveUpCommand.ExecuteAsync(chain.Entries[2]);
            CollectionAssert.AreEqual(new[] { deposits[2].Id, deposits[0].Id, deposits[1].Id }, model.ShownDocument!.Markers.Chains[0].MarkerIds.ToArray());
            await model.Markers.RemoveEntryCommand.ExecuteAsync(model.Markers.SelectedChain!.Entries[0]);
            Assert.AreEqual(2, model.ShownDocument!.Markers.Chains[0].MarkerIds.Length);

            model.Markers.ChainName = "Patrol_Chain";
            await Task.Yield();
            Assert.AreEqual("Patrol_Chain", model.ShownDocument!.Markers.Chains[0].Name);
            Assert.AreEqual("Patrol_Chain", model.Markers.SelectedChain?.Name, "it stays chosen");

            // the chain tool adds what it clicks to the chosen chain
            model.SelectTool("markers.chain");
            ResourceMarker another = model.ShownDocument.Markers.Resources[5];
            await Click(ActiveTool(model), another.Position.X, another.Position.Z);
            Assert.AreEqual(another.Id, model.ShownDocument!.Markers.Chains[0].MarkerIds[^1]);
            await Click(ActiveTool(model), another.Position.X, another.Position.Z, ToolModifiers.Control);
            Assert.IsFalse(model.ShownDocument!.Markers.Chains[0].MarkerIds.Contains(another.Id), "control takes it out");
            Assert.IsTrue(ActiveTool(model).Key("Escape", ToolModifiers.None));
            await Click(ActiveTool(model), another.Position.X, another.Position.Z);
            Assert.AreEqual(2, model.ShownDocument!.Markers.Chains.Length, "after Escape, a click starts another chain");

            await model.Markers.DeleteChainCommand.ExecuteAsync(null);
            Assert.AreEqual("Patrol_Chain", model.ShownDocument!.Markers.Chains.Single().Name);
        }

        [TestMethod]
        public async Task TheInspectorChangesTheSelectedMarkersFields()
        {
            MainViewModel model = await Open("markers.select");
            MarkerSet markers = model.ShownDocument!.Markers;
            ResourceMarker mass = markers.Resources.First(r => r.Kind == ResourceKind.Mass);
            ResourceMarker hydro = markers.Resources.First(r => r.Kind == ResourceKind.Hydrocarbon);
            model.Selection.Set([ObjectRef.Marker(mass.Id), ObjectRef.Marker(hydro.Id)]);
            MarkerSelection section = (MarkerSelection)model.Selection.Inspector.Sections.Single();

            Assert.IsTrue(section.HasResources);
            Assert.IsFalse(section.CanRename, "two markers have no one name");
            Assert.IsNull(section.Resource, "they differ");
            section.Resource = "Hydrocarbon";
            await Task.Yield();
            ResourceMarker changed = (ResourceMarker)model.ShownDocument!.FindMarker(mass.Id)!;
            Assert.AreEqual(ResourceKind.Hydrocarbon, changed.Kind);
            StringAssert.StartsWith(changed.Name, "Hydrocarbon ", "named after its new kind");
            Assert.AreEqual("Hydrocarbon", section.Resource);

            section.SpawnWithArmy = "ARMY_2";
            await Task.Yield();
            Assert.AreEqual("ARMY_2", ((ResourceMarker)model.ShownDocument!.FindMarker(hydro.Id)!).SpawnWithArmy);
            await section.WaitForNearestArmyCommand.ExecuteAsync(null);
            Spawn nearest = model.ShownDocument!.Markers.Spawns.OrderBy(s => System.Numerics.Vector3.Distance(s.Position, hydro.Position)).First();
            Assert.AreEqual(nearest.Army, ((ResourceMarker)model.ShownDocument!.FindMarker(hydro.Id)!).SpawnWithArmy);
            section.SpawnWithArmy = MarkerSelection.Always;
            await Task.Yield();
            Assert.IsNull(((ResourceMarker)model.ShownDocument!.FindMarker(hydro.Id)!).SpawnWithArmy);

            AiMarker ai = markers.AiMarkers[0];
            model.Selection.Set([ObjectRef.Marker(ai.Id)]);
            section = (MarkerSelection)model.Selection.Inspector.Sections.Single();
            Assert.IsTrue(section.CanRename);
            section.AiType = "Naval Area";
            await Task.Yield();
            Assert.AreEqual("Naval Area", ((AiMarker)model.ShownDocument!.FindMarker(ai.Id)!).Type);
            section.Name = "Rally of the north";
            await Task.Yield();
            Assert.AreEqual("Rally of the north", model.ShownDocument!.FindMarker(ai.Id)!.Name);
            section.Name = markers.Spawns[0].Name;
            await Task.Yield();
            Assert.AreEqual("Rally of the north", model.ShownDocument!.FindMarker(ai.Id)!.Name, "another marker's name is refused");

            Spawn spawn = markers.Spawns[0];
            model.Selection.Set([ObjectRef.Marker(spawn.Id)]);
            section = (MarkerSelection)model.Selection.Inspector.Sections.Single();
            Assert.IsFalse(section.CanRename, "a spawn keeps its army's name");
            section.NoRushX = "12";
            await Task.Yield();
            Assert.AreEqual(12, ((Spawn)model.ShownDocument!.FindMarker(spawn.Id)!).NoRushOffset!.Value.X);
            section.NoRushX = "";
            await Task.Yield();
            Assert.IsNull(((Spawn)model.ShownDocument!.FindMarker(spawn.Id)!).NoRushOffset, "nothing typed: around the spawn");
        }

        [TestMethod]
        public async Task APathNodePutOnAnotherLayerLosesItsLinks()
        {
            MainViewModel model = await Open("markers.path");
            await Click(ActiveTool(model), 10, 10);
            await Click(ActiveTool(model), 20, 10);
            PathNode b = (PathNode)Selected(model);

            CommandRun run = await Run(model, "markers.set", new JsonObject { ["layer"] = "Air" });

            Assert.AreEqual(1, run.Result);
            PathNode moved = (PathNode)model.ShownDocument!.FindMarker(b.Id)!;
            Assert.AreEqual(PathLayer.Air, moved.Layer);
            Assert.IsTrue(moved.AdjacentTo.IsEmpty);
        }

        [TestMethod]
        public async Task TheAdaptiveMapTurnsOnAndCountsTheDepositsPerArmy()
        {
            MainViewModel model = await Open("markers.select");
            Assert.IsFalse(model.Markers.IsAdaptive);

            await model.Markers.ToggleAdaptiveCommand.ExecuteAsync(null);
            Assert.IsTrue(model.ShownDocument!.Scenario.AdaptiveMap);
            Assert.IsTrue(model.Markers.IsAdaptive);
            Assert.AreEqual("Adaptive map", model.Map!.Session.UndoDescription);

            ResourceMarker mass = model.ShownDocument.Markers.Resources[0];
            model.Selection.Set([ObjectRef.Marker(mass.Id)]);
            await Run(model, "markers.set", new JsonObject { ["spawnWithArmy"] = "ARMY_3" });
            Assert.AreEqual(1, model.Markers.AdaptiveArmies.Single(army => army.Army == "ARMY_3").Deposits);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.set", new JsonObject { ["spawnWithArmy"] = "NOBODY" }), "an army the map does not have");
        }

        [TestMethod]
        public async Task AHiddenKindIsNeitherDrawnNorPicked()
        {
            MainViewModel model = await Open("markers.select");
            ResourceMarker mass = model.ShownDocument!.Markers.Resources.First(r => r.Kind == ResourceKind.Mass);
            Assert.AreEqual(ObjectRef.Marker(mass.Id), model.PickAt(mass.Position.X, mass.Position.Z, 0.5f));
            int overlays = 0;
            model.OverlaysChanged += (_, _) => overlays++;

            Assert.AreEqual(false, (await Run(model, "markers.showKind", new JsonObject { ["kind"] = "mass" })).Result);

            Assert.AreEqual(1, overlays, "the views draw again");
            Assert.IsFalse(model.ShownMarkerKinds.HasFlag(MarkerKinds.Mass));
            Assert.AreNotEqual(ObjectRef.Marker(mass.Id), model.PickAt(mass.Position.X, mass.Position.Z, 0.5f), "a hidden kind is not picked");
            await Run(model, "select.all");
            Assert.IsFalse(model.Selection.Items.Contains(ObjectRef.Marker(mass.Id)));
            Assert.IsFalse(model.Markers.Kinds.Single(kind => kind.Id == "mass").IsOn);
            StringAssert.StartsWith(model.Markers.Kinds.Single(kind => kind.Id == "mass").Label, "Mass (");

            await model.Markers.ToggleKindCommand.ExecuteAsync(model.Markers.Kinds.Single(kind => kind.Id == "mass"));
            Assert.IsTrue(model.ShownMarkerKinds.HasFlag(MarkerKinds.Mass));
            model.ShowMarkers = false;
            Assert.AreEqual(MarkerKinds.None, model.ShownMarkerKinds, "the overlays still hide them all; path nodes are hidden on HardFFA");
        }
    }
}
