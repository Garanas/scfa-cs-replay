using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Commands run without a person (automation, Lua plugins) never lose the open map's unsaved
    /// changes without being told to.
    /// </summary>
    [TestClass]
    public sealed class UnsavedChangesTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        [TestMethod]
        [DataRow("file.open", "folder")]
        [DataRow("file.import", "folder")]
        [DataRow("file.new", "name")]
        public async Task ACommandWithoutAPersonRefusesToDiscardChangesUnlessTold(string id, string argument)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.EditMap("A change", document => document with { Scenario = document.Scenario with { Description = "changed" } });
            string value = argument == "folder" ? HardFfa : "Fresh";

            CommandRun refused = await model.Commands.TryRunAsync(id, new JsonObject { [argument] = value }, interactive: false);
            Assert.IsFalse(refused.Ran);
            StringAssert.Contains(refused.Why, "unsaved changes");
            StringAssert.Contains(refused.Why, "discard: true");
            Assert.IsTrue(model.Map!.Session.IsModified, "the changes are still there");
            Assert.AreEqual("changed", model.Map.Document.Scenario.Description);

            CommandRun discarded = await model.Commands.TryRunAsync(id, new JsonObject { [argument] = value, ["discard"] = true }, interactive: false);
            Assert.IsTrue(discarded.Ran, discarded.Why);
            if (id == "file.new")
            {
                Assert.AreEqual("Fresh", model.Map!.Document.Scenario.Name, "a new map, unsaved until it is saved as a project");
                return;
            }
            Assert.IsFalse(model.Map!.Session.IsModified);

            // a saved (unchanged) map is replaced without the argument
            CommandRun again = await model.Commands.TryRunAsync(id, new JsonObject { [argument] = value }, interactive: false);
            Assert.IsTrue(again.Ran, again.Why);
        }
    }
}
