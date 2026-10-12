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
    /// What the Props and Decals modes show follows the model (wave 30): the chosen prop rule across
    /// renames and undo, the paint tool's layers across a changed texture and another map, the decal
    /// groups' buttons, and the choices of the previous map.
    /// </summary>
    [TestClass]
    public class PropsAndDecalsConsistencyTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";
        private const string Mud = "env/evergreen/decals/e_mud001_albedo.dds";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {run.Why} {model.Status}");
            return run;
        }

        private static async Task<ObjectRef> PlaceDecal(MainViewModel model, float x, float z) =>
            ObjectRef.Parse((string)(await Run(model, "decals.place", new JsonObject { ["x"] = x, ["z"] = z, ["key"] = Mud })).Result!);

        [TestMethod]
        public async Task RenamingTheChosenRuleKeepsItChosenAlsoWhenItIsNotTheFirst()
        {
            MainViewModel model = await Open();
            await Run(model, "props.addRule", new JsonObject { ["name"] = "Forest" });
            await Run(model, "props.addRule", new JsonObject { ["name"] = "Rocks" });
            Assert.AreEqual("Rocks", model.PropRules.Chosen);

            await Run(model, "props.setRule", new JsonObject { ["name"] = "Stones" });
            Assert.AreEqual("Stones", model.PropRules.Chosen, "the rule renamed in the inspector stays chosen");
            Assert.AreEqual("Stones", model.PropRules.ChosenItem?.Name);

            model.Undo();
            Assert.AreEqual("Rocks", model.PropRules.Chosen, "undoing the rename chooses the rule under its old name");
            model.Redo();
            Assert.AreEqual("Stones", model.PropRules.Chosen, "redoing it chooses the new name");
        }

        [TestMethod]
        public async Task AnotherMapForgetsTheRuleNameTypedForANewRule()
        {
            MainViewModel model = await Open();
            await Run(model, "props.addRule", new JsonObject { ["name"] = "Forest" });
            model.PropRules.NewName = "Typed for a new rule";

            await model.OpenAsync(HardFfa);

            Assert.AreEqual("", model.PropRules.NewName);
            Assert.IsNull(model.PropRules.Chosen);
            Assert.AreEqual(0, model.PropRules.Items.Count);
        }

        [TestMethod]
        public async Task ThePaintToolsLayersFollowAStrataTextureAndLeaveWithTheMap()
        {
            MainViewModel model = await Open();
            PropPaintTool paint = model.Tool<PropPaintTool>();
            paint.OnlyOn = paint.LayerNamed("Layer 1");
            string other = model.Map!.Document.Terrain.Layers[2].AlbedoPath;
            Assert.AreNotEqual(model.StrataTool.Strata[0].Name, $"Layer 1: {Path.GetFileNameWithoutExtension(other)}", "the test needs another texture");

            await Run(model, "paint.setLayer", new JsonObject { ["layer"] = 1, ["albedo"] = other });

            Assert.AreEqual(model.StrataTool.Strata[0].Name, paint.Layers[1].Name, "the choices name the stratum as the Paint mode does");
            Assert.AreEqual(model.StrataTool.Strata[0].Name, paint.OnlyOn.Name, "the layer chosen stays chosen under its new name");

            await model.OpenAsync(HardFfa);
            Assert.IsTrue(paint.OnlyOn.IsNone, "a layer chosen on the previous map is anywhere on this one");
        }

        [TestMethod]
        public async Task AddingToAGroupWaitsForADecalToBeSelected()
        {
            MainViewModel model = await Open();
            model.SelectMode("decals");
            model.Selection.Clear();
            await Run(model, "decals.addGroup");
            DecalGroupItem group = model.Decals.Groups.Single();
            Assert.IsFalse(model.Decals.AddToGroupCommand.CanExecute(group), "nothing is selected");
            Assert.IsFalse(model.Decals.RemoveFromGroupCommand.CanExecute(group));
            Assert.IsNotNull(model.Decals.SelectionTip, "the disabled buttons say why");

            model.Selection.Set([await PlaceDecal(model, 60, 60)]);
            Assert.IsTrue(model.Decals.AddToGroupCommand.CanExecute(group));
            Assert.IsTrue(model.Decals.RemoveFromGroupCommand.CanExecute(group));
            Assert.IsNull(model.Decals.SelectionTip);

            model.Selection.Clear();
            Assert.IsFalse(model.Decals.AddToGroupCommand.CanExecute(group), "the selection was emptied");
        }

        [TestMethod]
        public async Task AResizedMapKeepsWhatItsDecalGroupsHidAndAnotherMapDoesNot()
        {
            MainViewModel model = await Open();
            model.SelectMode("decals");
            model.Selection.Set([await PlaceDecal(model, 60, 60)]);
            int id = (int)(await Run(model, "decals.addGroup")).Result!;
            await Run(model, "decals.showGroup", new JsonObject { ["group"] = id, ["on"] = false });
            Assert.AreEqual(1, model.Decals.HiddenDecals.Count);

            await Run(model, "map.resize", new JsonObject { ["width"] = model.Map!.Document.Width * 2, ["height"] = model.Map.Document.Height * 2, ["anchor"] = "TopLeft" });

            Assert.AreEqual(1, model.Decals.HiddenDecals.Count, "the same map, larger: the group stays hidden");
            Assert.IsFalse(model.Decals.Groups.Single().IsShown);
            model.Undo();
            Assert.AreEqual(1, model.Decals.HiddenDecals.Count, "and when the resize is undone");

            await model.OpenAsync(HardFfa);
            Assert.AreEqual(0, model.Decals.HiddenDecals.Count);
        }

        [TestMethod]
        public async Task AnotherMapDoesNotOfferADecalOfThePreviousMapsOwnFiles()
        {
            MainViewModel model = await Open();
            model.DecalPlaceTool.Decal = "maps/hardffa.v0001/env/decals/rock01_albedo.png";
            model.DecalsAlong.Decal = "/maps/hardffa.v0001/env/decals/rock01";

            await model.OpenAsync(HardFfa);

            Assert.IsNull(model.DecalPlaceTool.Decal);
            Assert.IsNull(model.DecalsAlong.Decal);

            model.DecalPlaceTool.Decal = Mud;
            model.DecalsAlong.Decal = Mud;
            await model.OpenAsync(HardFfa);
            Assert.AreEqual(Mud, model.DecalPlaceTool.Decal, "a decal of the game stays chosen");
            Assert.AreEqual(Mud, model.DecalsAlong.Decal);
        }

        [TestMethod]
        public async Task AGroupNameTheCommandRefusesIsShownAsTheModelHasIt()
        {
            MainViewModel model = await Open();
            model.SelectMode("decals");
            await Run(model, "decals.addGroup", new JsonObject { ["name"] = "Mud" });
            await Run(model, "decals.addGroup", new JsonObject { ["name"] = "Sand" });
            DecalGroupItem sand = model.Decals.Groups.Single(group => group.Name == "Sand");
            List<string?> told = [];
            sand.PropertyChanged += (_, e) => told.Add(e.PropertyName);

            sand.Name = "Mud";
            await Task.Delay(200);

            Assert.IsTrue(told.Contains(nameof(DecalGroupItem.Name)), "the box is told to show the name again");
            Assert.AreEqual(model.ShownDocument!.DecalGroups.Single(group => group.Id == sand.Id).Name, sand.Name);
        }

        [TestMethod]
        public async Task APaletteWeightBelowZeroIsZero()
        {
            MainViewModel model = await Open();
            PaletteItem item = model.Props.AddToPalette("env/evergreen/props/trees/pine06_prop.bp", 2);
            item.Weight = -3;
            Assert.AreEqual(0, item.Weight);
            item.Weight = double.NaN;
            Assert.AreEqual(0, item.Weight);
        }

        [TestMethod]
        public async Task ARulesSlidersHoldWhatThePlacingSectionCanGiveIt()
        {
            MainViewModel model = await Open();
            await Run(model, "props.addRule", new JsonObject { ["name"] = "Forest" });
            Dictionary<string, NumberSetting> settings = model.PropRules.Groups.SelectMany(group => group.Settings).OfType<NumberSetting>().ToDictionary(setting => setting.Label);
            Assert.AreEqual(PropsViewModel.MaxTilt, settings["Random tilt"].Maximum);
            Assert.AreEqual(PropsViewModel.MaxScale, settings["Largest scale"].Maximum);
            Assert.AreEqual(PropsViewModel.MaxSink, settings["Sink"].Maximum);
        }
    }
}
