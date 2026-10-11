using Avalonia.Threading;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The tints over the terrain (mask, walkability, regions) belong to the mode they were turned
    /// on in (issue 17 of the wave 10 walkthrough: the Paint mode's mask showed over the Lighting
    /// mode, walkability over the Masks mode).
    /// </summary>
    [TestClass]
    public sealed class ModeTintsTest
    {
        [TestMethod]
        public Task ATintStaysWithItsMode() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            model.SelectMode("paint");
            Assert.IsTrue((await model.Commands.RunAsync("view.overlay.mask", new System.Text.Json.Nodes.JsonObject { ["on"] = true }, interactive: false)).Ran);
            Assert.IsTrue(model.ShowMask);

            model.SelectMode("lighting");
            Assert.IsFalse(model.ShowMask, "the Paint mode's mask does not show over the Lighting mode");

            model.ShowWalkability = true;
            model.SelectMode("masks");
            Assert.IsFalse(model.ShowWalkability, "nor the Lighting mode's walkability over the Masks mode");
            Assert.IsFalse(model.ShowMask);

            model.SelectMode("paint");
            Assert.IsTrue(model.ShowMask, "back in the Paint mode its mask shows again");
            Assert.IsFalse(model.ShowWalkability);

            model.SelectMode("lighting");
            Assert.IsTrue(model.ShowWalkability, "and the Lighting mode's walkability");
            Assert.IsFalse(model.ShowMask, "one tint at a time");
            window.Close();
        });
    }
}
