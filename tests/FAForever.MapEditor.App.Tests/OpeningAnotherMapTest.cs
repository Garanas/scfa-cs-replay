using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Opening another map leaves nothing of the one before on screen: the window is back at the Map
    /// mode and every list of entries shows its first, and the Procedural mode's pictures are the new map's.
    /// </summary>
    [TestClass]
    public sealed class OpeningAnotherMapTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";
        private const string Theta = "assets/maps/theta_passage_-_faf_version.v0001";

        [TestMethod]
        public async Task OpeningAnotherMapChoosesTheMapModeAndEveryListsFirstEntry()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            Assert.AreEqual("terrain", model.SelectedMode!.Definition.Id, "the window starts at the Terrain mode");

            model.SelectMode("procedural");
            model.PanelPartLists["procedural"].Choose("procedural.preview");
            model.SelectMode("water");
            model.WaterSettings.Parts.Choose("water.foam");
            model.SelectMode("lighting");
            model.LightingSettings.Parts.Choose(model.LightingSettings.Parts.Items[^1].Id);
            model.SelectMode("procedural");

            await model.OpenAsync(Theta);

            Assert.AreEqual("map", model.SelectedMode!.Definition.Id);
            Assert.IsTrue(model.SelectedMode.IsSelected);
            foreach ((string mode, PanelParts list) in model.PanelPartLists)
            {
                Assert.AreEqual(list.Items[0].Id, list.Chosen.Id, $"the {mode} list shows its first entry");
            }
        }

        [TestMethod]
        public async Task TheProceduralPicturesAreTheNewMapsAfterItIsOpened()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            PreviewPicture? before = model.ProceduralList.Results.First(result => result.GeneratorId == "preview").Picture;
            Assert.IsNotNull(before);

            await model.OpenAsync(Theta);

            PreviewPicture? after = model.ProceduralList.Results.First(result => result.GeneratorId == "preview").Picture;
            Assert.IsNotNull(after);
            Assert.AreNotSame(before, after);
            Assert.AreSame(model.Map!.Document.Terrain.Preview, ((ColourGridPicture)after).Grid, "the picture is of the map that is open");
        }
    }
}
