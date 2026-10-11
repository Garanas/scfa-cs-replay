using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Editing many units at once (user story U-11): group and ungroup, the group as a choice in the
    /// inspector, replace, convert to another faction, moves that keep structures on the build grid,
    /// distribute.
    /// </summary>
    [TestClass]
    public class EditManyUnitsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        /// <summary>
        /// Units of the four factions as the conversion writes them: power generators and energy
        /// storages (1 by 1 ogrid), land factories (3 by 3 UEF, 4 by 4 Cybran), a UEF scout and a
        /// Cybran structure no other faction has a match for.
        /// </summary>
        private static AssetCatalog Catalog()
        {
            string folder = TestAssets.Folder();
            void Unit(string id, string name, string faction, int tech, string categories, string footprint = "")
            {
                string file = Path.Combine(folder, "units", id, id + "_unit.json");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, $$"""{ "id": "{{id.ToUpperInvariant()}}", "name": "{{name}}", "faction": "{{faction}}", "techLevel": {{tech}}, "categories": [{{categories}}]{{footprint}} }""");
            }
            foreach ((string id, string faction, string letters) in new[] { ("ueb", "Uef", "UEF"), ("urb", "Cybran", "CYBRAN"), ("uab", "Aeon", "AEON"), ("xsb", "Seraphim", "SERAPHIM") })
            {
                Unit(id + "1101", faction + " Power Generator", faction, 1, $"\"{letters}\", \"STRUCTURE\", \"TECH1\", \"ENERGYPRODUCTION\", \"ECONOMIC\"");
                Unit(id + "1105", faction + " Energy Storage", faction, 1, $"\"{letters}\", \"STRUCTURE\", \"TECH1\", \"ENERGYSTORAGE\", \"ECONOMIC\"");
            }
            Unit("ueb0101", "UEF Land Factory", "Uef", 1, "\"UEF\", \"STRUCTURE\", \"TECH1\", \"FACTORY\"", ", \"footprint\": { \"sizeX\": 3, \"sizeZ\": 3 }");
            Unit("urb0101", "Cybran Land Factory", "Cybran", 1, "\"CYBRAN\", \"STRUCTURE\", \"TECH1\", \"FACTORY\"", ", \"footprint\": { \"sizeX\": 4, \"sizeZ\": 4 }");
            Unit("uel0101", "UEF Land Scout", "Uef", 1, "\"UEF\", \"LAND\", \"MOBILE\", \"INTELLIGENCE\"");
            Unit("xrl0002", "Cybran Crab Egg", "Cybran", 3, "\"CYBRAN\", \"STRUCTURE\", \"TECH3\", \"EGG\"");
            return AssetCatalog.Load(folder);
        }

        private static async Task<MainViewModel> Open(string tool = "units.select")
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(Catalog());
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static async Task<string> Add(MainViewModel model, string blueprint, float x, float z, string army = "ARMY_1", string? group = null, float? heading = null)
        {
            JsonObject arguments = new JsonObject { ["blueprint"] = blueprint, ["x"] = x, ["z"] = z, ["army"] = army };
            if (group is not null)
            {
                arguments["group"] = group;
            }
            if (heading is not null)
            {
                arguments["heading"] = heading;
            }
            CommandRun run = await Run(model, "units.add", arguments);
            Assert.IsTrue(run.Ran, run.Why);
            return ((JsonObject)run.Result!)["name"]!.GetValue<string>();
        }

        private static Unit UnitNamed(MainViewModel model, string name) => model.ShownDocument!.FindUnit(name)!.Unit;

        private static void Select(MainViewModel model, params string[] names) => model.Selection.Set([.. names.Select(ObjectRef.Unit)]);

        private static UnitSelection Section(MainViewModel model) => model.Selection.Inspector.Sections.OfType<UnitSelection>().Single();

        [TestMethod]
        public async Task GroupingPutsTheSelectedUnitsIntoANewGroupChosenInTheTreeAndUndoesAsOneStep()
        {
            MainViewModel model = await Open();
            string a = await Add(model, "ueb1101", 50.5f, 50.5f, group: "INITIAL/Base");
            string b = await Add(model, "ueb1101", 52.5f, 50.5f, group: "INITIAL/Base");
            string c = await Add(model, "ueb1101", 54.5f, 50.5f, group: "INITIAL/Base");
            Select(model, a, b);
            int steps = model.History.Count;

            CommandRun run = await Run(model, "units.group");

            Assert.IsTrue(run.Ran, run.Why);
            JsonObject result = (JsonObject)run.Result!;
            Assert.AreEqual(2, result["count"]!.GetValue<int>());
            Assert.AreEqual("INITIAL/Base/GROUP_1", result["groups"]![0]!["group"]!.GetValue<string>());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base", "GROUP_1" }, model.ShownDocument!.FindUnit(a)!.Group.ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base", "GROUP_1" }, model.ShownDocument.FindUnit(b)!.Group.ToArray());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base" }, model.ShownDocument.FindUnit(c)!.Group.ToArray());
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            Assert.AreEqual("Group the selected units", model.Map!.Session.UndoDescription);
            StringAssert.Contains(model.Status, "Grouped 2 units into INITIAL/Base/GROUP_1 of ARMY_1");
            Assert.AreEqual("INITIAL/Base/GROUP_1", model.Armies.ChosenGroup?.PathText, "chosen in the tree");

            Assert.IsTrue(model.Undo());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base" }, model.ShownDocument!.FindUnit(a)!.Group.ToArray());
            Assert.IsNull(model.ShownDocument.FindGroup("ARMY_1", ["INITIAL", "Base", "GROUP_1"]));
        }

        [TestMethod]
        public async Task GroupingUnitsOfSeveralArmiesMakesAGroupInEachWithTheNameGiven()
        {
            MainViewModel model = await Open();
            string a = await Add(model, "ueb1101", 50.5f, 50.5f, "ARMY_1");
            string b = await Add(model, "ueb1101", 52.5f, 50.5f, "ARMY_2");
            Select(model, a, b);

            CommandRun run = await Run(model, "units.group", new JsonObject { ["name"] = "Power" });

            Assert.IsTrue(run.Ran, run.Why);
            Assert.IsNotNull(model.ShownDocument!.FindGroup("ARMY_1", ["INITIAL", "Power"]));
            Assert.IsNotNull(model.ShownDocument.FindGroup("ARMY_2", ["INITIAL", "Power"]));
            Assert.AreEqual(2, ((JsonArray)((JsonObject)run.Result!)["groups"]!).Count);
        }

        [TestMethod]
        public async Task UngroupingGivesTheGroupsUnitsToTheGroupAboveAsOneStepAndLeavesTheGamesOwnGroups()
        {
            MainViewModel model = await Open();
            string a = await Add(model, "ueb1101", 50.5f, 50.5f, group: "INITIAL/Base");
            string b = await Add(model, "ueb1101", 52.5f, 50.5f, group: "INITIAL/Base");
            Select(model, a, b);
            int steps = model.History.Count;

            CommandRun run = await Run(model, "units.ungroup");

            Assert.IsTrue(run.Ran, run.Why);
            CollectionAssert.AreEqual(new[] { "INITIAL" }, model.ShownDocument!.FindUnit(a)!.Group.ToArray());
            Assert.IsNull(model.ShownDocument.FindGroup("ARMY_1", ["INITIAL", "Base"]));
            Assert.AreEqual(steps + 1, model.History.Count);
            StringAssert.Contains(model.Status, "Ungrouped INITIAL/Base of ARMY_1: 2 units in INITIAL now");
            Assert.IsTrue(model.Undo());
            Assert.IsNotNull(model.ShownDocument!.FindGroup("ARMY_1", ["INITIAL", "Base"]));

            // INITIAL is the game's: asking for it, by the selection or by name, is refused
            Select(model, a);
            await Run(model, "units.moveToGroup", new JsonObject { ["group"] = "INITIAL" });
            ArgumentException refused = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.ungroup"));
            StringAssert.Contains(refused.Message, "INITIAL is a group of the game");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.ungroup", new JsonObject { ["army"] = "ARMY_1", ["group"] = "NOWHERE" }));
        }

        [TestMethod]
        public async Task TheInspectorsGroupIsAChoiceOfTheArmysGroupsAndADashForSeveralArmies()
        {
            MainViewModel model = await Open();
            string a = await Add(model, "ueb1101", 50.5f, 50.5f, "ARMY_1", "INITIAL/Base");
            string b = await Add(model, "ueb1101", 52.5f, 50.5f, "ARMY_1", "INITIAL/Outpost");
            string c = await Add(model, "ueb1101", 54.5f, 50.5f, "ARMY_2");
            Select(model, a, b);
            UnitSelection section = Section(model);

            Assert.IsTrue(section.CanChooseGroup);
            CollectionAssert.IsSubsetOf(new[] { "", "INITIAL", "INITIAL/Base", "INITIAL/Outpost" }, section.Groups.Select(group => group.Path).ToArray());
            Assert.IsNull(section.GroupChoice, "they are in different groups: the combo shows a dash");

            section.GroupChoice = new UnitGroupChoice("INITIAL/Outpost");
            await Task.Yield();

            CollectionAssert.AreEqual(new[] { "INITIAL", "Outpost" }, model.ShownDocument!.FindUnit(a)!.Group.ToArray());
            Assert.AreEqual("INITIAL/Outpost", section.GroupChoice?.Path, "now they share it");
            Assert.AreEqual("Move the selected units to a group", model.Map!.Session.UndoDescription);

            Select(model, a, c);
            Assert.IsFalse(section.CanChooseGroup, "two armies: read only");
            Assert.AreEqual(0, section.Groups.Count);
            Assert.IsNull(section.GroupChoice);
        }

        [TestMethod]
        public async Task ReplacingTheBlueprintKeepsNameHeadingArmyAndGroupAndAStructureStaysOnItsGrid()
        {
            MainViewModel model = await Open();
            string a = await Add(model, "ueb1101", 50.5f, 50.5f, "ARMY_2", "INITIAL/Base", heading: 90);
            string b = await Add(model, "ueb1101", 60.5f, 50.5f, "ARMY_2", "INITIAL/Base");
            Select(model, a, b);
            int steps = model.History.Count;

            CommandRun run = await Run(model, "units.replace", new JsonObject { ["blueprint"] = "urb0101" });

            Assert.IsTrue(run.Ran, run.Why);
            Assert.AreEqual(2, run.Result);
            PlacedUnit placed = model.ShownDocument!.FindUnit(a)!;
            Assert.AreEqual("urb0101", placed.Unit.Blueprint);
            Assert.AreEqual(a, placed.Unit.Name);
            Assert.AreEqual("ARMY_2", placed.Army);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Base" }, placed.Group.ToArray());
            Assert.AreEqual(MathF.PI / 2, placed.Unit.Orientation.Y, 1e-4);
            Assert.AreEqual(0, placed.Unit.Position.X % 1, "4 by 4: a whole ogrid");
            Assert.AreEqual(0, placed.Unit.Position.Z % 1, "4 by 4: a whole ogrid");
            Assert.AreEqual(steps + 1, model.History.Count);
            StringAssert.Contains(model.Status, "2 units are Cybran Land Factory now");

            Assert.IsTrue(model.Undo());
            Assert.AreEqual("ueb1101", UnitNamed(model, a).Blueprint);
            Assert.AreEqual(50.5f, UnitNamed(model, a).Position.X);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.replace", new JsonObject { ["blueprint"] = "nosuchunit" }));
        }

        [TestMethod]
        public async Task ConvertingToAFactionKeepsTheKindAndCountsTheUnitsWithoutACounterpart()
        {
            MainViewModel model = await Open();
            string power = await Add(model, "ueb1101", 50.5f, 50.5f);
            string storage = await Add(model, "ueb1105", 52.5f, 50.5f);
            string egg = await Add(model, "xrl0002", 56.5f, 50.5f);
            string cybran = await Add(model, "urb1101", 60.5f, 50.5f);
            Select(model, power, storage, egg, cybran);
            int steps = model.History.Count;

            CommandRun run = await Run(model, "units.toFaction", new JsonObject { ["faction"] = "Cybran" });

            Assert.IsTrue(run.Ran, run.Why);
            JsonObject result = (JsonObject)run.Result!;
            Assert.AreEqual((2, 0, 2), (result["converted"]!.GetValue<int>(), result["left"]!.GetValue<int>(), result["already"]!.GetValue<int>()));
            Assert.AreEqual("urb1101", UnitNamed(model, power).Blueprint);
            Assert.AreEqual("urb1105", UnitNamed(model, storage).Blueprint);
            Assert.AreEqual("xrl0002", UnitNamed(model, egg).Blueprint);
            Assert.AreEqual(steps + 1, model.History.Count);
            StringAssert.Contains(model.Status, "Converted 2 units to Cybran");

            // the Cybran egg has no UEF counterpart: it stays, and the status says so
            Select(model, egg, power);
            CommandRun uef = await Run(model, "units.toFaction", new JsonObject { ["faction"] = "uef" });
            Assert.AreEqual(1, ((JsonObject)uef.Result!)["left"]!.GetValue<int>());
            Assert.AreEqual("xrl0002", UnitNamed(model, egg).Blueprint);
            Assert.AreEqual("ueb1101", UnitNamed(model, power).Blueprint);
            StringAssert.Contains(model.Status, "Converted 1 unit to UEF; 1 left as it is: no UEF counterpart");

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.toFaction", new JsonObject { ["faction"] = "romans" }));
            Assert.IsTrue(model.Undo());
            Assert.AreEqual("urb1101", UnitNamed(model, power).Blueprint);
        }

        [TestMethod]
        public async Task ANudgeOfAFactoryKeepsItOnTheBuildGridUnlessTheSettingIsOff()
        {
            MainViewModel model = await Open();
            string factory = await Add(model, "ueb0101", 50.5f, 50.5f);
            string scout = await Add(model, "uel0101", 70.3f, 40.2f);
            Select(model, factory, scout);
            Assert.IsTrue(model.Handles.KeepOnGrid, "on by default");

            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 0.4f, ["dz"] = 0 });

            Assert.AreEqual(50.5f, UnitNamed(model, factory).Position.X, "a 3 by 3 factory stays on a half ogrid");
            Assert.AreEqual(70.3f, UnitNamed(model, scout).Position.X, 1e-4, "no move at all: the rounding went to the factory's 0");

            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 8, ["dz"] = 0 });
            Assert.AreEqual(58.5f, UnitNamed(model, factory).Position.X);
            Assert.AreEqual(78.3f, UnitNamed(model, scout).Position.X, 1e-4, "every other thing moves by the same amount");

            JsonObject handles = (JsonObject)(await Run(model, "select.handles", new JsonObject { ["keepOnGrid"] = false })).Result!;
            Assert.IsFalse(handles["keepOnGrid"]!.GetValue<bool>());
            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 0.4f, ["dz"] = 0 });
            Assert.AreEqual(58.9f, UnitNamed(model, factory).Position.X, 1e-4, "off: the move is as it is");
        }

        [TestMethod]
        public async Task DraggingTheHandleOfAFactoryKeepsItOnTheBuildGrid()
        {
            MainViewModel model = await Open();
            string factory = await Add(model, "ueb0101", 50.5f, 50.5f);
            Select(model, factory);
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            HandleLayout layout = HandleLayout.Of(tool.Frame!, ToolInput.Default.OgridsPerPixel);
            Vector3 arrow = Vector3.Lerp(layout.ArrowX.From, layout.ArrowX.To, 0.5f);
            ToolInput input = ToolInput.Default;

            tool.Press(arrow.X, arrow.Z, input);
            tool.Drag(arrow.X + 3.3f, arrow.Z, input);
            tool.Release();

            Assert.AreEqual(53.5f, UnitNamed(model, factory).Position.X, "3.3 ogrids became 3");
        }

        [TestMethod]
        public async Task EditPlaceKeepsTheFirstStructureOnTheGrid()
        {
            MainViewModel model = await Open();
            string factory = await Add(model, "ueb0101", 50.5f, 50.5f);
            Select(model, factory);

            await Run(model, "edit.place", new JsonObject { ["x"] = 70.2f });

            Assert.AreEqual(70.5f, UnitNamed(model, factory).Position.X);
            Assert.AreEqual(50.5f, UnitNamed(model, factory).Position.Z, "an axis left out stays");
        }

        [TestMethod]
        public async Task DistributingSpreadsTheSelectionEvenlyAsOneStepAndNeedsThreeUnits()
        {
            MainViewModel model = await Open();
            string a = await Add(model, "ueb1101", 40.5f, 50.5f);
            string b = await Add(model, "ueb1101", 41.5f, 50.5f);
            string c = await Add(model, "ueb1101", 46.5f, 50.5f);
            string d = await Add(model, "ueb1101", 60.5f, 51.5f);
            Select(model, a, b);
            Assert.IsFalse(Section(model).CanDistribute);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.distribute"), "two units are no crowd");

            Select(model, d, b, a, c);
            Assert.IsTrue(Section(model).CanDistribute);
            int steps = model.History.Count;
            CommandRun run = await Run(model, "units.distribute");

            Assert.IsTrue(run.Ran, run.Why);
            Assert.AreEqual("x", ((JsonObject)run.Result!)["axis"]!.GetValue<string>(), "the longer extent");
            Assert.AreEqual(40.5f, UnitNamed(model, a).Position.X);
            Assert.AreEqual(60.5f, UnitNamed(model, d).Position.X);
            Assert.AreEqual(47.5f, UnitNamed(model, b).Position.X, "on the half ogrid nearest 47.17");
            Assert.AreEqual(53.5f, UnitNamed(model, c).Position.X, "on the half ogrid below 53.83");
            Assert.AreEqual(steps + 1, model.History.Count);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.distribute", new JsonObject { ["axis"] = "y" }));
            await Run(model, "units.distribute", new JsonObject { ["axis"] = "z" });
            Assert.AreEqual(51.5f, UnitNamed(model, c).Position.Z, "along z the last of the three on 50.5 goes a third of the way to d, on the half ogrid");
        }

        [TestMethod]
        public Task TheInspectorOffersTheGroupBlueprintAndDistributeAndCtrlGGroups() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(Catalog());
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            model.SelectTool("units.select");
            string a = await Add(model, "ueb1101", 40.5f, 50.5f);
            string b = await Add(model, "ueb1101", 44.5f, 50.5f);
            string c = await Add(model, "ueb1101", 50.5f, 50.5f);
            Select(model, a, b, c);
            Dispatcher.UIThread.RunJobs();

            Control? Find(string label) => window.GetVisualDescendants().OfType<Inspector>().Single()
                .GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Avalonia.Automation.AutomationProperties.GetName(control) == label && control.IsEffectivelyVisible);

            Assert.IsInstanceOfType<ComboBox>(Find("Group of the selected units"));
            Assert.AreEqual("ueb1101 Uef Power Generator", ((TextBlock)Find("Blueprint of the selected units")!).Text);
            foreach (string label in new[] { "Replace the units' blueprint", "Convert the units to a faction", "Group the selected units", "Ungroup the selected units", "Distribute the selected units evenly" })
            {
                Assert.IsNotNull(Find(label), label);
            }
            Assert.IsTrue(Find("Distribute the selected units evenly")!.IsEnabled, "three units");
            Assert.IsFalse(Find("Ungroup the selected units")!.IsEnabled, "they are in the game's INITIAL");
            Assert.IsTrue(window.GetVisualDescendants().OfType<CheckBox>().Any(box => box.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(box) == "Keep structures on the build grid"), "in the select tool's options");

            // Ctrl+G with the view's keyboard groups the selection
            window.KeyPress(Key.G, RawInputModifiers.Control, PhysicalKey.G, "g");
            window.KeyRelease(Key.G, RawInputModifiers.Control, PhysicalKey.G, "g");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            CollectionAssert.AreEqual(new[] { "INITIAL", "GROUP_1" }, model.ShownDocument!.FindUnit(a)!.Group.ToArray());
            Assert.IsTrue(Find("Ungroup the selected units")!.IsEnabled, "now they share a group of their own");

            // the Group button's twin in the inspector: Distribute spreads them
            EditorDriver driver = EditorDriver.InProcess(new AutomationServer(window, model, port: 0));
            await driver.Send("click", new JsonObject { ["label"] = "Distribute the selected units evenly" });
            Assert.AreEqual(45.5f, UnitNamed(model, b).Position.X, "the middle of 40.5 and 50.5");
        });
    }
}
