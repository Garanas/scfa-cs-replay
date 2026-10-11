using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Map mode as "the panel lists, the inspector edits" (user stories M-1, M-3, M-4, M-5, M-6,
    /// wave 14A): the information, size and script entries, an entry per army and per area kept in
    /// step with the map, and what choosing one does besides showing it.
    /// </summary>
    [TestClass]
    public sealed class MapModeTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.SelectMode("map");
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) => model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static string[] Ids(MainViewModel model, string group) => [.. model.MapParts.Parts.Items.Where(part => part.Group == group).Select(part => part.Id)];

        [TestMethod]
        public async Task ThePanelListsTheMapItsArmiesAndItsAreas()
        {
            MainViewModel model = await Open();
            PanelParts parts = model.MapParts.Parts;

            CollectionAssert.AreEqual(new[] { "map.information", "map.size", "map.script" }, Ids(model, "Map"));
            CollectionAssert.AreEqual(model.Map!.Document.Armies.Select(army => $"map.army.{army.Name}").ToArray(), Ids(model, "Armies"));
            CollectionAssert.AreEqual(Array.Empty<string>(), Ids(model, "Areas"), "HardFFA has no areas");
            CollectionAssert.AreEqual(new[] { "Map", "Armies", "Areas" }, parts.Groups.Select(group => group.Title).ToArray(), "the Areas heading shows without entries");
            Assert.IsTrue(parts.Groups[2].ShowsEmptyText);
            Assert.AreEqual("map.information", parts.Chosen.Id, "the first entry until another is chosen");
            Assert.AreEqual(typeof(MapDetailsSection), model.InspectorParts.Single().GetType());

            StringAssert.EndsWith(parts.Find("map.information")!.Detail, "8 players");
            Assert.AreEqual("256 · whole map", parts.Find("map.size")!.Detail);
            StringAssert.Contains(parts.Find("map.script")!.Detail, "files");
            Assert.AreEqual("extra · 0 units", parts.Find("map.army.NEUTRAL_CIVILIAN")!.Detail);

            // an area added comes, the playable one; a second one says its size
            await Run(model, "map.addArea");
            await Run(model, "map.addArea", new JsonObject { ["x0"] = 0, ["z0"] = 0, ["x1"] = 96, ["z1"] = 64 });
            CollectionAssert.AreEqual(new[] { "map.area.AREA_1", "map.area.AREA_2" }, Ids(model, "Areas"));
            Assert.AreEqual("playable", parts.Find("map.area.AREA_1")!.Detail);
            Assert.AreEqual("96 × 64", parts.Find("map.area.AREA_2")!.Detail);
            Assert.AreEqual("256 · AREA_1", parts.Find("map.size")!.Detail);
            CollectionAssert.AreEqual(new[] { "Map", "Armies", "Areas" }, parts.Groups.Select(group => group.Title).ToArray());

            // an army renamed is its entry under the new name, one removed goes, the undo brings it back
            await Run(model, "map.renameArmy", new JsonObject { ["army"] = "ARMY_2", ["name"] = "NORTH" });
            Assert.IsNull(parts.Find("map.army.ARMY_2"));
            Assert.AreEqual("NORTH", parts.Find("map.army.NORTH")!.Title);
            await Run(model, "map.removeArmy", new JsonObject { ["army"] = "NORTH" });
            Assert.IsNull(parts.Find("map.army.NORTH"));
            await Run(model, "edit.undo");
            Assert.IsNotNull(parts.Find("map.army.NORTH"));
        }

        [TestMethod]
        public async Task TheSizeEntryChoosesTheMapSizeToolAndTheToolItsEntry()
        {
            MainViewModel model = await Open();
            model.InspectPart("map.size");
            Assert.AreEqual("map.size", model.ActiveTool!.Definition.Id);
            MapSizePart part = (MapSizePart)model.MapParts.Parts.Chosen.Content;
            StringAssert.StartsWith(part.Details.Size, "5 km, 256 by 256");
            StringAssert.StartsWith(part.Details.Playable, "The whole map");
            Assert.IsFalse(part.Details.HasAreas);

            model.InspectPart("map.information");
            Assert.AreEqual("map.size", model.ActiveTool!.Definition.Id, "another entry leaves the tool");
            model.SelectTool("map.select");
            model.SelectTool("map.size");
            Assert.AreEqual("map.size", model.MapParts.Parts.Chosen.Id, "the tool shows its entry");

            // the playable area chosen in the inspector
            await Run(model, "map.addArea");
            await Run(model, "map.addArea", new JsonObject { ["x0"] = 0, ["z0"] = 0, ["x1"] = 32, ["z1"] = 32 });
            model.InspectPart("map.size");
            CollectionAssert.AreEqual(new[] { "AREA_1", "AREA_2" }, part.Details.Areas.ToArray());
            Assert.AreEqual("AREA_1", part.Details.PlayableArea);
            part.Details.PlayableArea = "AREA_2";
            await Task.Yield();
            Assert.AreEqual("AREA_2", model.Map!.Document.PlayableArea?.Name);
            StringAssert.Contains(part.Details.Playable, "chosen as the playable area");
        }

        [TestMethod]
        public async Task AnArmyEntryShowsTheArmysFieldsAndFollowsTheArmyChosenElsewhere()
        {
            MainViewModel model = await Open();
            model.InspectPart("map.army.ARMY_3");
            Assert.AreEqual("ARMY_3", model.Armies.ChosenArmy?.Name);
            Assert.AreEqual("ARMY_3", model.Armies.ArmyName, "the inspector's fields are the army's");
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);

            // the Units mode's tree chooses another army: the entry shown follows
            Assert.IsTrue(model.Armies.Choose("ARMY_5", []));
            Assert.AreEqual("map.army.ARMY_5", model.MapParts.Parts.Chosen.Id);

            // renamed from the inspector, the army keeps its entry under its new name
            model.Armies.ArmyName = "SOUTH";
            await model.Armies.RenameArmyCommand.ExecuteAsync(null);
            Assert.AreEqual("map.army.SOUTH", model.MapParts.Parts.Chosen.Id);

            // the commands that show the armies show an army's entry
            await Run(model, "map.armies", new JsonObject { ["army"] = "ARMY_1" });
            Assert.AreEqual("map.army.ARMY_1", model.MapParts.Parts.Chosen.Id);
            await Run(model, "map.information");
            Assert.AreEqual("map.information", model.MapParts.Parts.Chosen.Id);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "map.armies", new JsonObject { ["army"] = "NOBODY" }));
        }

        [TestMethod]
        public async Task AnAreaEntrySelectsTheAreaAndKeepsTheInspector()
        {
            MainViewModel model = await Open();
            await Run(model, "map.addArea");
            await Run(model, "map.addArea", new JsonObject { ["x0"] = 0, ["z0"] = 0, ["x1"] = 32, ["z1"] = 32 });
            model.Selection.Clear();
            model.SelectTool("map.select");

            model.InspectPart("map.area.AREA_2");
            CollectionAssert.AreEqual(new[] { ObjectRef.Area("AREA_2") }, model.Selection.Items.ToArray(), "selected, so its handles show");
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);
            Assert.IsTrue(model.InspectsMode, "the entry has the inspector, the selection stays");
            MapAreaPart part = (MapAreaPart)model.MapParts.Parts.Chosen.Content;
            StringAssert.StartsWith(part.Rectangle, "0, 0 to 32, 32");
            Assert.IsFalse(part.Area.Playable);

            // the playable area from the inspector
            part.Area.Playable = true;
            await Task.Yield();
            Assert.AreEqual("AREA_2", model.Map!.Document.PlayableArea?.Name);
            Assert.AreEqual("playable", model.MapParts.Parts.Chosen.Detail);

            // renamed from the inspector: its entry under the new name keeps the inspector
            model.InspectPart("map.area.AREA_2");
            part.Area.Fields.Settings.OfType<TextSetting>().Single(setting => setting.Label == "Name").Text = "BASE";
            await Task.Yield();
            Assert.AreEqual("map.area.BASE", model.MapParts.Parts.Chosen.Id);
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);
            CollectionAssert.AreEqual(new[] { ObjectRef.Area("BASE") }, model.Selection.Items.ToArray());

            // an area selected on the map shows its entry, and the selection has the inspector
            model.Selection.Set([ObjectRef.Area("AREA_1")]);
            Assert.AreEqual("map.area.AREA_1", model.MapParts.Parts.Chosen.Id);
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner);

            // removed from the inspector: gone from the list
            model.InspectPart("map.area.AREA_1");
            await ((MapAreaPart)model.MapParts.Parts.Chosen.Content).RemoveCommand.ExecuteAsync(null);
            Assert.IsNull(model.MapParts.Parts.Find("map.area.AREA_1"));
            Assert.AreEqual("map.area.BASE", model.MapParts.Parts.Chosen.Id, "the heading's first is chosen");
        }

        [TestMethod]
        public async Task ThePanelsButtonsAddAnArmyOrAnAreaAndShowIt()
        {
            MainViewModel model = await Open();
            PanelPartGroup armies = model.MapParts.Parts.Groups.Single(group => group.Title == "Armies");
            CollectionAssert.AreEqual(new[] { "Add army", "Add extra" }, armies.Footer!.Actions.Select(action => action.Title).ToArray());

            string[] before = Ids(model, "Armies");
            armies.Footer.Actions[1].Command.Execute(armies.Footer.Actions[1].Parameter);
            await WaitFor(() => model.MapParts.Parts.Chosen.Group == "Armies");
            CollectionAssert.DoesNotContain(before, model.MapParts.Parts.Chosen.Id, "the new extra army is shown");
            Assert.AreEqual(before.Length + 1, Ids(model, "Armies").Length);
            Assert.IsFalse(model.Armies.ChosenArmy!.Playable);

            PanelPartGroup areas = model.MapParts.Parts.Groups.Single(group => group.Title == "Areas");
            areas.Footer!.Actions[0].Command.Execute(null);
            await WaitFor(() => model.MapParts.Parts.Chosen.Group == "Areas");
            Assert.AreEqual("map.area.AREA_1", model.MapParts.Parts.Chosen.Id);
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);
            Assert.AreEqual("map.areas", model.ActiveTool!.Definition.Id, "the areas tool, whose handles resize it");

            areas = model.MapParts.Parts.Groups.Single(group => group.Title == "Areas");
            Assert.AreEqual("tool.map.areas", areas.Footer!.Actions[1].Parameter, "Draw chooses the areas tool");
        }

        [TestMethod]
        public async Task AutomationNamesTheEntries()
        {
            MainViewModel model = await Open();
            JsonObject lists = model.DescribePanelParts();
            JsonObject map = (JsonObject)lists["map"]!;
            Assert.AreEqual("map.information", map["chosen"]!.GetValue<string>());
            Assert.AreEqual(nameof(MapInformationPart), map["content"]!.GetValue<string>());
            Assert.IsTrue(((JsonArray)map["items"]!).Any(item => item!["id"]!.GetValue<string>() == "map.army.ARMY_1"));
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++)
            {
                await Task.Delay(10);
            }
            Assert.IsTrue(condition());
        }
    }
}
