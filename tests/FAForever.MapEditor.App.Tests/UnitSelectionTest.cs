using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Selecting units by what they are, the selections stored under the digits and picking a structure
    /// by its footprint (user story U-10), on the view model and its commands.
    /// </summary>
    [TestClass]
    public sealed class UnitSelectionTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static async Task<string> Add(MainViewModel model, string blueprint, float x, float z, string army, string group = "INITIAL", bool wreck = false)
        {
            JsonObject placed = (JsonObject)(await Run(model, "units.add", new JsonObject { ["blueprint"] = blueprint, ["x"] = x, ["z"] = z, ["army"] = army, ["group"] = group, ["wreck"] = wreck })).Result!;
            return placed["name"]!.GetValue<string>();
        }

        /// <summary>
        /// ARMY_1 with a farm (a structure of 3 by 2, UEF), a residence and a Snoop; ARMY_2 with an Aeon command unit and a
        /// wreck of a residence.
        /// </summary>
        private static async Task<(MainViewModel Model, string Farm, string Home, string Snoop, string Aeon, string Wreck)> Open(string folder = HardFfa)
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(UnitsTest.Catalog());
            await model.OpenAsync(folder);
            model.SelectTool("units.select");
            string farm = await Add(model, "uec1201", 100, 100, "ARMY_1", "INITIAL/Farm");
            string home = await Add(model, "uec1101", 130, 100, "ARMY_1", "INITIAL/Farm");
            string snoop = await Add(model, "uel0101", 160, 100, "ARMY_1");
            string aeon = await Add(model, "ual0001", 190, 100, "ARMY_2");
            string wreck = await Add(model, "uec1101", 220, 100, "ARMY_2", wreck: true);
            return (model, farm, home, snoop, aeon, wreck);
        }

        private static string[] Selected(MainViewModel model) => [.. model.Selection.Items.Select(thing => thing.Name!)];

        private static async Task<int> Where(MainViewModel model, JsonObject arguments) => (int)(await Run(model, "select.where", arguments)).Result!;

        [TestMethod]
        public async Task SelectWhereChoosesUnitsByEachFilterAndAnswersTheCount()
        {
            (MainViewModel model, string farm, string home, string snoop, string aeon, string wreck) = await Open();

            Assert.AreEqual(4, await Where(model, []), "every unit that is not a wreck");
            Assert.AreEqual(4, model.Selection.Count);
            Assert.AreEqual(3, await Where(model, new JsonObject { ["army"] = "ARMY_1" }));
            CollectionAssert.AreEquivalent(new[] { farm, home, snoop }, Selected(model));
            Assert.AreEqual(1, await Where(model, new JsonObject { ["faction"] = "aeon" }), "the blueprint's faction");
            Assert.AreEqual(3, await Where(model, new JsonObject { ["faction"] = new JsonArray("uef") }), "a list");
            Assert.AreEqual(4, await Where(model, new JsonObject { ["faction"] = "uef,aeon" }), "a text with commas");
            Assert.AreEqual(2, await Where(model, new JsonObject { ["kind"] = "structure" }));
            CollectionAssert.AreEquivalent(new[] { farm, home }, Selected(model));
            Assert.AreEqual(2, await Where(model, new JsonObject { ["kind"] = "land" }));
            Assert.AreEqual(2, await Where(model, new JsonObject { ["tech"] = 1 }), "tech as a number");
            CollectionAssert.AreEquivalent(new[] { home, snoop }, Selected(model));
            Assert.AreEqual(2, await Where(model, new JsonObject { ["blueprint"] = "uec1" }), "part of an id");
            Assert.AreEqual(1, await Where(model, new JsonObject { ["blueprint"] = "snoop" }), "part of a name");
            CollectionAssert.AreEqual(new[] { wreck }, await WreckOnly(model));
            Assert.AreEqual(5, await Where(model, new JsonObject { ["wrecks"] = "include" }));
            Assert.AreEqual(1, await Where(model, new JsonObject { ["army"] = "ARMY_2", ["kind"] = "structure", ["wrecks"] = "include" }), "the filters together");
            StringAssert.Contains(model.Status, "Selected 1 unit");
        }

        private static async Task<string[]> WreckOnly(MainViewModel model)
        {
            Assert.AreEqual(1, await Where(model, new JsonObject { ["wrecks"] = "only" }));
            return Selected(model);
        }

        [TestMethod]
        public async Task SelectWhereWorksWithinTheSelectionOrAnAreaAndCanAdd()
        {
            (MainViewModel model, string farm, string home, string snoop, string aeon, _) = await Open();
            model.Selection.Set([ObjectRef.Unit(farm), ObjectRef.Unit(snoop), ObjectRef.Unit(aeon)]);

            Assert.AreEqual(1, await Where(model, new JsonObject { ["within"] = "selection", ["kind"] = "structure" }), "of the selected units, the structure");
            CollectionAssert.AreEqual(new[] { farm }, Selected(model));

            model.Selection.Set([ObjectRef.Unit(farm)]);
            Assert.AreEqual(3, await Where(model, new JsonObject { ["kind"] = "land", ["add"] = true }), "adds to the selection: the farm, and the two land units");
            CollectionAssert.AreEquivalent(new[] { farm, snoop, aeon }, Selected(model));
            Assert.AreEqual(2, await Where(model, new JsonObject { ["kind"] = "land" }), "without add it replaces");
            _ = home;
        }

        [TestMethod]
        public async Task SelectWhereRefusesWhatItDoesNotKnow()
        {
            (MainViewModel model, _, _, _, _, _) = await Open();
            foreach (JsonObject arguments in new[]
            {
                new JsonObject { ["faction"] = "uuef" }, new JsonObject { ["kind"] = "boat" }, new JsonObject { ["tech"] = 5 }, new JsonObject { ["wrecks"] = "maybe" },
                new JsonObject { ["army"] = "ARMY_99" }, new JsonObject { ["within"] = "area:NOPE" }, new JsonObject { ["within"] = "the hill" },
            })
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "select.where", arguments), arguments.ToJsonString());
            }
        }

        [TestMethod]
        public async Task TheDigitsStoreAndRecallInTheSelectTool()
        {
            (MainViewModel model, string farm, string home, string snoop, _, _) = await Open();
            SelectTool select = (SelectTool)model.ActiveTool!.Tool!;
            model.Selection.Set([ObjectRef.Unit(farm), ObjectRef.Unit(home)]);

            Assert.IsTrue(select.Key("D3", ToolModifiers.Alt), "Alt and a digit stores");
            await Task.Yield();
            Assert.AreEqual(2, model.StoredSelections.Of(3).Length);
            Assert.AreEqual("3 · Farm · 2", model.StoredSelections.Chips.Single().Text, "the group all its units share, and how many");

            model.Selection.Set([ObjectRef.Unit(snoop)]);
            Assert.IsTrue(select.Key("D3", ToolModifiers.None), "a digit recalls");
            await Task.Yield();
            CollectionAssert.AreEquivalent(new[] { farm, home }, Selected(model), "replacing the selection");

            model.Selection.Set([ObjectRef.Unit(snoop)]);
            select.Key("D3", ToolModifiers.Shift);
            await Task.Yield();
            CollectionAssert.AreEquivalent(new[] { farm, home, snoop }, Selected(model), "shift adds");

            // units of more groups: the chip says how many
            await Run(model, "select.store", new JsonObject { ["slot"] = 4 });
            Assert.AreEqual("4 · 3", model.StoredSelections.Chips.Single(chip => chip.Slot == 4).Text);
            Assert.IsTrue(select.Key("NumPad4", ToolModifiers.None), "the numeric keypad too");

            Assert.IsFalse(select.Key("D3", ToolModifiers.Control), "control and a digit is the views'");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "select.recall", new JsonObject { ["slot"] = 7 }), "nothing is stored as 7");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "select.store", new JsonObject { ["slot"] = 10 }));
        }

        [TestMethod]
        public async Task TheActiveToolOwnsTheDigit()
        {
            (MainViewModel model, string farm, _, _, _, _) = await Open();
            model.Selection.Set([ObjectRef.Unit(farm)]);
            await Run(model, "select.store", new JsonObject { ["slot"] = 1 });
            model.Selection.Clear();

            model.SelectTool("units.place");
            Assert.IsFalse(model.Selection.HasAny);
            model.ToolKey("D1", ToolModifiers.None);
            await Task.Yield();
            Assert.IsFalse(model.Selection.HasAny, "in the place tool 1 picks a palette slot, it recalls nothing");

            model.SelectTool("units.select");
            model.ToolKey("D1", ToolModifiers.None);
            await Task.Yield();
            Assert.AreEqual(1, model.Selection.Count, "in the select tool 1 recalls");
        }

        [TestMethod]
        public async Task ARecalledSelectionDropsTheUnitsThatAreGone()
        {
            (MainViewModel model, string farm, string home, string snoop, _, _) = await Open();
            model.Selection.Set([ObjectRef.Unit(farm), ObjectRef.Unit(home), ObjectRef.Unit(snoop)]);
            await Run(model, "select.store", new JsonObject { ["slot"] = 2 });
            Assert.AreEqual("2 · 3", model.StoredSelections.Chips.Single().Text);

            model.Selection.Set([ObjectRef.Unit(home)]);
            await Run(model, "edit.delete");
            Assert.AreEqual("2 · 2", model.StoredSelections.Chips.Single().Text, "the chip counts what is left");

            Assert.AreEqual(2, (int)(await Run(model, "select.recall", new JsonObject { ["slot"] = 2 })).Result!);
            CollectionAssert.AreEquivalent(new[] { farm, snoop }, Selected(model));
            Assert.AreEqual(2, model.StoredSelections.Of(2).Length, "and the digit holds what is left");

            model.Selection.Clear();
            await Run(model, "select.store", new JsonObject { ["slot"] = 2 });
            Assert.AreEqual(0, model.StoredSelections.Chips.Count, "storing nothing empties the digit");
        }

        [TestMethod]
        public async Task StoredSelectionsAreKeptWithTheProjectInTheUserSettings()
        {
            string folder = TestMaps.SlopeProject();
            string settingsFile = Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", $"settings-{Guid.NewGuid():N}.json");
            UserSettings settings = new UserSettings(settingsFile);
            (MainViewModel model, string farm, string home, _, _, _) = await OpenProject(folder, settings);

            model.Selection.Set([ObjectRef.Unit(farm), ObjectRef.Unit(home)]);
            await Run(model, "select.store", new JsonObject { ["slot"] = 5 });
            Assert.AreEqual(2, settings.StoredSelections(folder)[5].Count, "written at once");
            await Run(model, "file.save");

            // another start of the editor, the same project
            MainViewModel again = new MainViewModel(new NoDialogs());
            again.Assets.Use(UnitsTest.Catalog());
            again.StoredSelections.Load(settings);
            await again.OpenAsync(folder);
            Assert.AreEqual("5 · Farm · 2", again.StoredSelections.Chips.Single().Text);

            // the map's own files hold nothing of it
            Assert.IsFalse(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(file => File.ReadAllText(file).Contains("storedSelections", StringComparison.OrdinalIgnoreCase)));
        }

        private static async Task<(MainViewModel Model, string Farm, string Home, string Snoop, string Aeon, string Wreck)> OpenProject(string folder, UserSettings settings)
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(UnitsTest.Catalog());
            model.StoredSelections.Load(settings);
            await model.OpenAsync(folder);
            model.SelectTool("units.select");
            ImmutableArray<string> armies = model.ShownDocument!.Armies.Select(army => army.Name).ToImmutableArray();
            string army = armies[0];
            string farm = await Add(model, "uec1201", 100, 100, army, "INITIAL/Farm");
            string home = await Add(model, "uec1101", 130, 100, army, "INITIAL/Farm");
            return (model, farm, home, "", "", "");
        }

        [TestMethod]
        public async Task AClickInsideAFactoryAwayFromItsCentrePicksItByFootprint()
        {
            (MainViewModel model, string farm, _, _, _, _) = await Open();
            // the farm is 3 by 2 on (100.5, 101) after snapping: a tenth of an ogrid a pixel reaches 0.7
            PlacedUnit placed = model.ShownDocument!.FindUnit(farm)!;
            float x = placed.Unit.Position.X + 1.3f;
            float z = placed.Unit.Position.Z + 0.8f;

            Assert.AreEqual(UnitPicking.Footprint, model.UnitPicks, "by footprint unless told otherwise");
            Assert.AreEqual(ObjectRef.Unit(farm), model.PickAt(x, z, 0.1f), "near its corner");
            Assert.IsNull(model.PickAt(x + 0.5f, z, 0.1f), "just outside");

            model.UnitPicks = UnitPicking.Centre;
            Assert.IsNull(model.PickAt(x, z, 0.1f), "by the centre, the corner is too far");
            Assert.AreEqual(ObjectRef.Unit(farm), model.PickAt(placed.Unit.Position.X + 0.3f, placed.Unit.Position.Z, 0.1f));

            // a box touching the footprint takes the structure only by footprint
            model.UnitPicks = UnitPicking.Footprint;
            model.SelectInside(x - 0.2f, z - 5, x + 3, z - 0.1f, SelectionChange.Replace);
            CollectionAssert.AreEqual(new[] { farm }, Selected(model));
            model.UnitPicks = UnitPicking.Centre;
            model.SelectInside(x - 0.2f, z - 5, x + 3, z - 0.1f, SelectionChange.Replace);
            Assert.AreEqual(0, model.Selection.Count);
        }

        [TestMethod]
        public async Task SelectByHasItsOwnStateAndCountsAsTheChoiceChanges()
        {
            (MainViewModel model, _, _, _, _, _) = await Open();
            SelectByViewModel by = model.SelectBy;
            await Run(model, "select.by", new JsonObject { ["open"] = true });
            Assert.IsTrue(by.IsOpen);
            Assert.AreEqual("4 units match", by.CountText);

            by.Factions.Single(chip => chip.Title == "Aeon").IsOn = true;
            Assert.AreEqual("1 unit match", by.CountText);
            by.Factions.Single(chip => chip.Title == "Aeon").IsOn = false;
            by.Kinds.Single(chip => chip.Title == "Structure").IsOn = true;
            Assert.AreEqual(2, by.Count);
            by.Wrecks.Single(chip => chip.Title == "Include").IsOn = true;
            Assert.AreEqual(3, by.Count);
            Assert.IsFalse(by.Wrecks.Single(chip => chip.Title == "Leave out").IsOn, "one of them");
            by.ArmyChoice = "ARMY_2";
            Assert.AreEqual(1, by.Count);
            by.Blueprint = "zzz";
            Assert.AreEqual("0 units match", by.CountText);

            JsonObject arguments = by.Arguments(add: true);
            Assert.AreEqual("ARMY_2", arguments["army"]!.GetValue<string>());
            Assert.AreEqual("structure", arguments["kind"]!.GetValue<string>());
            Assert.AreEqual("include", arguments["wrecks"]!.GetValue<string>());
            Assert.IsTrue(arguments["add"]!.GetValue<bool>());
            by.ResetCommand.Execute(null);
            Assert.AreEqual("4 units match", by.CountText);
            Assert.AreEqual(0, by.Arguments().Count, "nothing chosen asks nothing");

            model.SelectTool("units.place");
            Assert.IsFalse(by.IsOpen, "another tool closes it");
        }
    }
}
