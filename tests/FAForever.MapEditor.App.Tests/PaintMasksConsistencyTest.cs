using System.ComponentModel;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Paint and Masks modes show what the model holds (wave 30D): the rule editor only for a layer
    /// that has a mask, Close leaving the stack as the editor shows it, a stack's brush layer
    /// remembered per grid, the list of masks following renames, undo and another map.
    /// </summary>
    [TestClass]
    public sealed class PaintMasksConsistencyTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers { SaveChoice = SaveChoice.Discard });
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static async Task<object?> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            var run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {model.Status}");
            return run.Result;
        }

        [TestMethod]
        public async Task TheLowerAndUpperLayersShowNoRuleEditorAndNoPreviewOfTheLastStratum()
        {
            MainViewModel model = await Open();
            model.SelectMode("paint");
            model.LayerTextures.Choose(2);
            await Run(model, "layers.add", new JsonObject { ["source"] = "rules", ["name"] = "Hills" });
            Assert.IsTrue(model.StratumStack.IsRules);
            Assert.IsTrue(model.ShowsStratumRules, "a stratum with a rules layer chosen shows the editor");

            List<string?> changed = [];
            ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            model.LayerTextures.Choose(0);
            Assert.IsTrue(model.LayerTextures.Selected is { IsStratum: false });
            Assert.IsFalse(model.ShowsStratumRules, "the lower layer has no mask, so no rules");
            CollectionAssert.Contains(changed, nameof(MainViewModel.ShowsStratumRules));

            model.LayerTextures.Choose(model.LayerTextures.Items.Max(item => item.Index));
            Assert.IsFalse(model.ShowsStratumRules, "nor does the upper layer");

            model.LayerTextures.Choose(2);
            Assert.IsTrue(model.ShowsStratumRules, "the stratum shows its rules again");
        }

        [TestMethod]
        public async Task ClosingTheRuleEditorChoosesAnotherLayerOfTheStack()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "high" });
            model.SelectMode("masks");
            await Run(model, "layers.add", new JsonObject { ["source"] = "rules", ["name"] = "Hills" });
            Assert.IsTrue(model.MaskStack.IsRules);

            model.MaskRules.CloseCommand.Execute(null);
            Assert.IsNull(model.MaskRules.Editing);
            Assert.IsFalse(model.MaskStack.IsRules, "the rule editor shows while a rules layer is chosen, so closing it leaves the rules layer");
            Assert.AreEqual(CoverageLayers.PaintedName, model.MaskStack.Chosen);
            Assert.AreEqual(CoverageLayers.PaintedName, model.Map!.Session.PaintLayers["mask-high"]);
        }

        [TestMethod]
        public async Task AGridShownAgainHasTheLayerItsBrushPaintedChosen()
        {
            MainViewModel model = await Open();
            model.SelectMode("masks");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "a" });
            await Run(model, "layers.add", new JsonObject { ["source"] = "painted" });
            Assert.AreEqual("Painted 2", model.MaskStack.Chosen);
            model.MaskStack.Choose(CoverageLayers.PaintedName);
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "b" });
            Assert.AreEqual("mask-b", model.MaskStack.Target?.Key);

            await Run(model, "paint.chooseMask", new JsonObject { ["name"] = "a" });
            Assert.AreEqual(CoverageLayers.PaintedName, model.MaskStack.Chosen, "the layer chosen last for the mask");
            Assert.AreEqual(CoverageLayers.PaintedName, model.Map!.Session.PaintLayers["mask-a"], "and the one its brush paints");

            // the same for the strata
            model.SelectMode("paint");
            model.LayerTextures.Choose(1);
            await Run(model, "layers.add", new JsonObject { ["source"] = "painted" });
            model.StratumStack.Choose(CoverageLayers.PaintedName);
            model.LayerTextures.Choose(2);
            model.LayerTextures.Choose(1);
            Assert.AreEqual(CoverageLayers.PaintedName, model.StratumStack.Chosen);
        }

        [TestMethod]
        public async Task TheChosenMaskFollowsARenameAlsoWhenItIsUndone()
        {
            MainViewModel model = await Open();
            model.SelectMode("masks");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "forest" });
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "alpha" });
            await Run(model, "paint.chooseMask", new JsonObject { ["name"] = "forest" });
            await Run(model, "paint.renameMask", new JsonObject { ["newName"] = "woods" });
            Assert.AreEqual("woods", model.Masks.Chosen);

            await Run(model, "edit.undo");
            Assert.AreEqual("forest", model.Masks.Chosen, "undoing the rename leaves the same mask chosen");
            await Run(model, "edit.redo");
            Assert.AreEqual("woods", model.Masks.Chosen);
        }

        [TestMethod]
        public async Task RenameNeedsANameAndTheFieldIsForgottenWithTheMap()
        {
            MainViewModel model = await Open();
            model.SelectMode("masks");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "forest" });
            Assert.IsTrue(model.Masks.HasChosen);
            Assert.IsFalse(model.Masks.CanRename, "no new name typed");
            model.Masks.NewName = "woods";
            Assert.IsTrue(model.Masks.CanRename);

            await model.OpenAsync(HardFfa);
            Assert.AreEqual("", model.Masks.NewName, "what was typed for the other map is not left in the field");
            Assert.IsFalse(model.Masks.CanRename);
        }

        [TestMethod]
        public async Task TheListOfMasksShowsTheSizeOfTheMasksOfTheMapShown()
        {
            MainViewModel model = await Open();
            MapDocument full = MaskLayers.Add(model.ShownDocument!, "forest", half: false);
            MapDocument half = MaskLayers.Add(model.ShownDocument!, "forest", half: true);
            MasksViewModel masks = new MasksViewModel();
            masks.Refresh(full);
            string before = masks.Items.Single().Size;
            masks.Refresh(half);
            Assert.AreNotEqual(before, masks.Items.Single().Size, "another map's mask of the same name has its own size");
            Assert.AreEqual("forest", masks.Chosen);
        }
    }
}
