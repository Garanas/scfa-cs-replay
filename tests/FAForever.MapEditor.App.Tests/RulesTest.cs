using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Masks and props from rules in the editor (user stories K-2, P-4, F-7): the commands, the rule
    /// editors' fields, generating when asked as one step of undo, and what is out of date.
    /// </summary>
    [TestClass]
    public class RulesTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";
        private const string Pine = "/env/evergreen/props/trees/pine06_prop.bp";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {model.Status}");
            return run;
        }

        private static GeneratedState StateOf(MainViewModel model, string id) => model.Generation.States.Single(state => state.Generator.Id == id);

        [TestMethod]
        public async Task EditsTheRulesOfAMaskAndShowsWhatTheyMakeAtOnce()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "flat" });
            await Run(model, "paint.maskRules", new JsonObject { ["name"] = "flat" });
            Assert.AreEqual("flat", model.MaskRules.Editing);
            Assert.AreEqual("masks", model.SelectedMode!.Definition.Id, "the rules are a layer of the mask's stack, in the Masks mode");
            Assert.IsTrue(model.ShowMask, "the mask shows over the terrain");

            await Run(model, "paint.addMaskRule", new JsonObject { ["kind"] = "Slope", ["max"] = 8 });
            await Run(model, "paint.addMaskRule", new JsonObject { ["kind"] = "distance to water", ["min"] = 2, ["max"] = 10000, ["combine"] = "Multiply" });
            Assert.AreEqual(2, model.MaskRules.Rules.Count);
            CollectionAssert.AreEqual(new[] { "From", "To", "Soft edge", "Inverted" }, model.MaskRules.Rules[0].Settings.Settings.Select(setting => setting.Label).ToArray(),
                "the first rule is the start: no way to combine");
            Assert.AreEqual("Combine", model.MaskRules.Rules[1].Settings.Settings[0].Label);

            // a field of the editor runs paint.setMaskRule
            NumberSetting to = (NumberSetting)model.MaskRules.Rules[0].Settings.Settings[1];
            Assert.AreEqual("8", to.Text);
            to.Text = "12";
            await Task.Yield();
            Assert.AreEqual(12, model.Map!.Document.MaskRules["flat"].Rules[0].Max);

            // editing the rules makes the mask from them in the same step
            byte[] mask = model.Map.Document.Masks["flat"].ToArray();
            Assert.IsTrue(mask.Any(value => value == 255) && mask.Any(value => value == 0), "flat ground away from the water, not the hills");
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, MaskRulesGenerator.GeneratorId).Status, "what the rules make is current");
            StringAssert.Contains(model.MaskRules.Status, "up to date");
            await Run(model, "paint.setMaskRule", new JsonObject { ["index"] = 1, ["invert"] = true });
            Assert.IsFalse(model.Map.Document.Masks["flat"].ToArray().SequenceEqual(mask), "the mask follows the rule changed");
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, MaskRulesGenerator.GeneratorId).Status);

            // what the rules read changes: out of date until generated, which is no step of undo (a derived result)
            model.Map.Session.Edit("Raise the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation + 4 } });
            Assert.AreEqual(GeneratedStatus.Stale, StateOf(model, MaskRulesGenerator.GeneratorId).Status, "the water level changed");
            StringAssert.Contains(model.Generation.Summary, "Masks from rules");
            int steps = model.History.Count;
            CommandRun generated = await Run(model, "generate.masks");
            Assert.AreEqual(1, generated.Result);
            Assert.AreEqual(steps, model.History.Count, "no step of undo");
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, MaskRulesGenerator.GeneratorId).Status);

            await Run(model, "paint.moveMaskRule", new JsonObject { ["index"] = 1, ["to"] = 0 });
            Assert.AreEqual(MaskRuleKind.WaterDistance, model.MaskRules.Rules[0].Kind);
            await Run(model, "paint.removeMaskRule", new JsonObject { ["index"] = 0 });
            await Run(model, "paint.removeMaskRule", new JsonObject { ["index"] = 0 });
            Assert.IsFalse(model.Map.Document.MaskRules.ContainsKey("flat"), "without rules the mask is painted by hand");
            Assert.IsFalse(model.MaskRules.HasRules);

            model.Undo();
            model.Undo();
            Assert.AreEqual(2, model.MaskRules.Rules.Count, "undo brings the rules back, and the editor shows them");
        }

        [TestMethod]
        public async Task ThePreviewShowsWhatTheRulesMakeInTheTopView()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "high" });
            await Run(model, "paint.addMaskRule", new JsonObject { ["mask"] = "high", ["kind"] = "Height", ["min"] = 0, ["max"] = 1000 });
            Assert.AreEqual("high", model.MaskRules.Editing, "adding a rule opens the mask's rules");
            for (int i = 0; i < 200 && model.MaskRules.PreviewOverlay is null; i++)
            {
                await Task.Delay(20);
            }
            Assert.IsNotNull(model.MaskRules.PreviewOverlay, "made on a thread of the pool");
            Assert.AreSame(model.MaskRules.PreviewOverlay, model.ShownGrid, "the top view shows the preview");
            TiledBuffer<byte> preview = model.ShownGrid!.Select(model.Map!.Document)!;
            Assert.IsTrue(preview.ToArray().Any(value => value == 255), "what the rules make");
            CollectionAssert.AreEqual(preview.ToArray(), model.Map.Document.Masks["high"].ToArray(), "the mask itself shows what the rules make too");

            model.MaskRules.ShowPreview = false;
            Assert.AreNotSame(preview, model.ShownGrid?.Select(model.Map.Document), "without the preview, the mask");
            model.MaskRules.Editing = null;
        }

        [TestMethod]
        public async Task ThePreviewBelongsToTheMasksModeAndLeavesOtherToolsTheirGrid()
        {
            MainViewModel model = await Open();
            model.SelectTool("paint.strata");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "high" });
            await Run(model, "paint.addMaskRule", new JsonObject { ["mask"] = "high", ["kind"] = "Height", ["min"] = 0, ["max"] = 1000 });
            Assert.AreEqual("masks", model.SelectedMode?.Definition.Id, "opening the rules shows the mask's stack in the Masks mode");
            Assert.IsTrue(model.MaskStack.IsRules, "with its rules layer chosen");
            for (int i = 0; i < 200 && model.MaskRules.PreviewOverlay is null; i++)
            {
                await Task.Delay(20);
            }
            Rendering.GridOverlay preview = model.MaskRules.PreviewOverlay!;
            Assert.AreSame(preview, model.ShownGrid);

            model.SelectTool("water.paint");
            Assert.AreEqual(model.Tool<Tools.WaterPaintTool>().Shown?.Key, model.ShownGrid?.Key, "painting foam shows the foam, not the preview");
            model.SelectMode("props");
            Assert.AreNotEqual(preview.Key, model.ShownGrid?.Key, "no other mode shows the preview");
            model.SelectTool("paint.strata");
            Assert.AreEqual(model.StrataTool.Shown?.Key, model.ShownGrid?.Key, "the strata tool shows the layer it paints");
            model.SelectTool("masks.paint");
            Assert.AreSame(preview, model.ShownGrid, "back in the Masks mode with the rules layer chosen, the preview shows again");
            model.MaskRules.Editing = null;
        }

        [TestMethod]
        public async Task AddsEditsAndGeneratesPropRulesKeepingThePropsPlacedByHand()
        {
            MainViewModel model = await Open();
            int byHand = model.Map!.Document.Props.Length;
            Prop first = model.Map.Document.Props[0];
            model.Selection.Set([ObjectRef.Prop(first.Id)]);

            CommandRun added = await Run(model, "props.addRule", new JsonObject { ["name"] = "Forest", ["density"] = 1 });
            Assert.AreEqual("Forest", added.Result);
            Assert.IsFalse(model.Selection.HasAny, "the inspector shows the new rule, not the selection");
            await Run(model, "props.addRule", new JsonObject { ["name"] = "Rocks" });
            model.Selection.Set([ObjectRef.Prop(first.Id)]);
            model.PropRules.ChosenItem = model.PropRules.Items.Single(item => item.Name == "Forest");
            Assert.IsFalse(model.Selection.HasAny, "a rule chosen in the list takes the inspector");
            await Run(model, "edit.undo");
            Assert.AreEqual("Forest", model.PropRules.Chosen);
            Assert.AreEqual("props", model.SelectedMode!.Definition.Id);
            await Run(model, "props.setRulePalette", new JsonObject { ["props"] = new JsonArray(Pine) });
            await Run(model, "props.addRuleCondition", new JsonObject { ["layer"] = "Layer 1", ["threshold"] = 0.2 });
            Assert.AreEqual(1, model.PropRules.Conditions.Count);
            Assert.AreEqual("stratum:1", model.Map.Document.PropRules[0].Conditions[0].Layer);
            Assert.IsTrue(model.PropRules.Groups.SelectMany(group => group.Settings).Any(setting => setting.Label == "Density"));
            StringAssert.Contains(model.PropRules.PaletteText, "pine06");

            CommandRun generated = await Run(model, "generate.props");
            int count = (int)generated.Result!;
            Assert.IsTrue(count > 100, $"{count} props");
            Assert.AreEqual(byHand + count, model.Map.Document.Props.Length);
            Assert.AreEqual(byHand, model.Map.Document.Props.Count(prop => prop.Rule is null));
            Assert.AreEqual(first, model.Map.Document.Props[0], "a prop placed by hand stays as it was");
            StringAssert.Contains(model.PropRules.Items[0].Summary, $"{count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)} props");

            await Run(model, "props.setRule", new JsonObject { ["density"] = 0.5, ["clustering"] = "Clusters" });
            Assert.AreEqual(GeneratedStatus.Stale, StateOf(model, PropRulesGenerator.GeneratorId).Status);
            CommandRun again = await Run(model, "generate.props");
            Assert.IsTrue((int)again.Result! < count);
            Assert.AreEqual(byHand + (int)again.Result!, model.Map.Document.Props.Length, "the rule's props replaced, the others kept");

            await Run(model, "props.setRule", new JsonObject { ["name"] = "Pines" });
            Assert.AreEqual("Pines", model.PropRules.Chosen, "the chosen rule follows its name");
            Assert.AreEqual((int)again.Result!, model.Map.Document.CountRuleProps("Pines"));

            Prop generatedProp = model.Map.Document.Props.First(prop => prop.Rule is not null);
            model.Selection.Set([ObjectRef.Prop(generatedProp.Id)]);
            await Run(model, "props.keepFromRules");
            Assert.IsNull(model.Map.Document.Props.Single(prop => prop.Id == generatedProp.Id).Rule, "kept: generating again leaves it");

            await Run(model, "props.removeRule");
            Assert.AreEqual(byHand + 1, model.Map.Document.Props.Length, "its props went with it, the kept one stays");
            Assert.IsNull(model.PropRules.Chosen);
            model.Undo();
            Assert.AreEqual("Pines", model.PropRules.Chosen);
        }

        [TestMethod]
        public async Task GeneratingWhatIsOutOfDateMakesTheMasksBeforeThePropsThatReadThem()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "flat" });
            await Run(model, "paint.maskRules", new JsonObject { ["name"] = "flat" });
            await Run(model, "paint.addMaskRule", new JsonObject { ["kind"] = "Slope", ["max"] = 8 });
            await Run(model, "props.addRule", new JsonObject { ["name"] = "Forest", ["density"] = 1 });
            await Run(model, "props.setRulePalette", new JsonObject { ["props"] = new JsonArray(Pine) });
            await Run(model, "props.addRuleCondition", new JsonObject { ["layer"] = "flat", ["threshold"] = 0.5 });
            await Run(model, "generate.masks");
            await Run(model, "generate.props");
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, PropRulesGenerator.GeneratorId).Status);

            // the mask's rule changes: the mask follows at once, the props that read it are out of date
            await Run(model, "paint.setMaskRule", new JsonObject { ["index"] = 0, ["max"] = 20 });
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, MaskRulesGenerator.GeneratorId).Status);
            Assert.AreEqual(GeneratedStatus.Stale, StateOf(model, PropRulesGenerator.GeneratorId).Status);
            int forest = model.Map!.Document.CountRuleProps("Forest");

            // and the terrain under both changes a little: both out of date
            model.Map.Session.Edit("Raise the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation + 0.01f } });
            Assert.AreEqual(GeneratedStatus.Stale, StateOf(model, MaskRulesGenerator.GeneratorId).Status);

            CommandRun run = await Run(model, "generate.stale");
            CollectionAssert.AreEqual(new[] { MaskRulesGenerator.GeneratorId, PropRulesGenerator.GeneratorId },
                ((JsonArray)run.Result!).Select(item => (string)item!["id"]!).Where(id => id is MaskRulesGenerator.GeneratorId or PropRulesGenerator.GeneratorId).ToArray(),
                "the props after the masks they read");
            Assert.AreEqual(0, model.Generation.Results.Stale(model.Map!.Document).Count, "nothing left out of date");
            Assert.IsTrue(model.Map.Document.CountRuleProps("Forest") > forest, "more flat ground, more trees");

            IReadOnlyList<IGenerator> order = model.Generation.Results.InOrder();
            int Index(string id) => order.ToList().FindIndex(generator => generator.Id == id);
            Assert.IsTrue(Index(MaskRulesGenerator.GeneratorId) < Index(PropRulesGenerator.GeneratorId));
            Assert.IsTrue(Index(WalkabilityGenerator.GeneratorId) < Index(PathNetworkGenerator.GeneratorId));
        }

        [TestMethod]
        public async Task ResultsKeptInTheProjectAreCurrentWhenItOpensAgain()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "flat" });
            await Run(model, "paint.addMaskRule", new JsonObject { ["mask"] = "flat", ["kind"] = "Slope", ["max"] = 8 });
            await Run(model, "props.addRule", new JsonObject { ["name"] = "Forest", ["density"] = 1 });
            await Run(model, "props.setRulePalette", new JsonObject { ["props"] = new JsonArray(Pine) });
            await Run(model, "generate.props");
            // a sky cube map among the map's files, as Generate leaves it
            byte[] rgba = new byte[6 * 16 * 16 * 4];
            Array.Fill(rgba, (byte)200);
            byte[] cube = SkyCubeMaps.Dds(new DecodedTexture(6 * 16, 16, rgba));
            model.Map!.Session.Edit("Generate the sky cube map", document => document with
            {
                Files = document.Files.SetItem(SkyCubeMaps.FilePath, [.. cube]),
            });
            string project = Path.Combine(AppContext.BaseDirectory, "rules-kept", Guid.NewGuid().ToString("N"));
            await model.SaveAsAsync(project);

            MainViewModel reopened = new MainViewModel(new MainViewModelTest.Answers());
            await reopened.OpenAsync(project);
            GeneratedStatus StatusOf(string id) => reopened.Generation.States.Single(state => state.Generator.Id == id).Status;
            Assert.AreEqual(GeneratedStatus.Current, StatusOf(MaskRulesGenerator.GeneratorId), "the rules layers hold what their rules made");
            Assert.AreEqual(GeneratedStatus.Current, StatusOf(PropRulesGenerator.GeneratorId), "the props of the rules are in the project");
            Assert.AreEqual(GeneratedStatus.Current, StatusOf("skyCube"), "the sky cube map is among the map's files");
            StringAssert.Contains(reopened.SkyCube.Status, "Up to date");
            Assert.AreEqual(16, reopened.SkyCube.Describe()["size"]!.GetValue<int>());

            // and out of date once what they are made from changes
            reopened.Map!.Session.Edit("Raise the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation + 2 } });
            reopened.Generation.Refresh(reopened.Map.Document);
            Assert.AreEqual(GeneratedStatus.Stale, StatusOf(MaskRulesGenerator.GeneratorId));
            Assert.AreEqual(GeneratedStatus.Stale, StatusOf(PropRulesGenerator.GeneratorId));
        }

        [TestMethod]
        public async Task RefusesWhatCannotBe()
        {
            MainViewModel model = await Open();
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("paint.addMaskRule", new JsonObject { ["kind"] = "Slope" }, interactive: false),
                "no mask to add a rule to");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "m" });
            ArgumentException badKind = await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("paint.addMaskRule", new JsonObject { ["kind"] = "Wiggle" }, interactive: false));
            StringAssert.Contains(badKind.Message, "Slope");
            await Run(model, "props.addRule", new JsonObject { ["name"] = "A" });
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("props.addRule", new JsonObject { ["name"] = "a" }, interactive: false),
                "names differ by more than case");
            CommandRun generated = await Run(model, "generate.props");
            Assert.AreEqual(0, generated.Result, "a rule without props places none");
        }
    }
}
