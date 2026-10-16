using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Generate, Preview page (user story M-11): the page and its pictures written into a folder the
    /// map maker chooses, the map itself unchanged.
    /// </summary>
    [TestClass]
    public class PreviewPageCommandTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "preview-page");

        [TestMethod]
        public async Task WritesThePageIntoTheFolderGivenAndLeavesTheMapAsItWas()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            int steps = model.History.Count;
            string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));

            CommandRun run = await model.Commands.TryRunAsync("generate.previewPage", new JsonObject { ["folder"] = folder }, interactive: false);

            Assert.IsTrue(run.Ran, run.Why);
            string page = Path.Combine(folder, "index.html");
            Assert.AreEqual(Path.GetFullPath(page), run.Result);
            StringAssert.Contains(File.ReadAllText(page), "<h1>HardFFA");
            Assert.AreEqual(21, Directory.GetFiles(Path.Combine(folder, "assets")).Length, "seven pictures at three sizes");
            Assert.AreEqual(steps, model.History.Count, "no step of undo");
            Assert.IsFalse(model.Map!.Session.IsModified);
            Assert.AreEqual(Path.GetFullPath(folder), model.PreviewPageFolder, "the dialog starts there next time");
            StringAssert.StartsWith(model.Status, "Generated the preview page");
        }

        [TestMethod]
        public async Task AsksForAFolderOnlyWithAPerson()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);

            CommandRun run = await model.Commands.TryRunAsync("generate.previewPage", [], interactive: false);

            Assert.IsFalse(run.Ran);
            StringAssert.Contains(run.Why, "folder");
        }
    }
}
