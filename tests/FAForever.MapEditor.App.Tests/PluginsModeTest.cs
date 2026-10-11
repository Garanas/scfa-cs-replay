using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Plugins;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Plugins mode (user story F-15, wave 14C): the plugins listed under their category (else the
    /// mode their tools join, else Other) with their kind, the list kept in step with every reload,
    /// the chosen plugin in the inspector, and <c>plugin.json</c>'s <c>category</c> and <c>kind</c>.
    /// </summary>
    [TestClass]
    public sealed class PluginsModeTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static string NewFolder() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", "plugins-mode-" + Guid.NewGuid().ToString("N"))).FullName;

        /// <summary>
        /// A plugins folder without the examples.
        /// </summary>
        private static string EmptyFolder()
        {
            string folder = NewFolder();
            File.WriteAllLines(Path.Combine(folder, ".examples"), ExamplePlugins.Names);
            return folder;
        }

        private static void Write(string folder, string id, string lua, string? manifest = null)
        {
            Directory.CreateDirectory(Path.Combine(folder, id));
            File.WriteAllText(Path.Combine(folder, id, "plugin.lua"), lua);
            if (manifest is not null)
            {
                File.WriteAllText(Path.Combine(folder, id, "plugin.json"), manifest);
            }
        }

        private static async Task<MainViewModel> Open(string folder)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.Assets.Use(TestAssets.Catalog());
            model.Plugins.Open(folder);
            return model;
        }

        private static string[] Headings(MainViewModel model) => [.. model.Plugins.Parts.Groups.Select(group => group.Title)];

        private static string[] Under(MainViewModel model, string heading) =>
            [.. model.Plugins.Parts.Groups.Single(group => group.Title == heading).Parts.Select(part => part.Title)];

        private const string Tool = """
            plugin.tool{ id = "dab", title = "Dab", mode = "props", press = function(x, z) end }
            """;

        [TestMethod]
        public async Task TheExamplesAreListedUnderTheirCategoriesWithTheirKinds()
        {
            MainViewModel model = await Open(NewFolder());

            CollectionAssert.AreEqual(new[] { "Workspace", "Checks", "Props", "Terrain" }, Headings(model), "the first entry, then the categories in order");
            Assert.AreEqual(PluginsViewModel.OverviewPart, model.Plugins.Parts.Items[0].Id);
            CollectionAssert.AreEqual(new[] { "Plateau brush" }, Under(model, "Terrain"));
            Assert.AreEqual("brush", model.Plugins.Find("plateau-brush")!.Kind);
            Assert.AreEqual("check", model.Plugins.Find("reclaim-per-army")!.Kind);
            Assert.AreEqual("generator", model.Plugins.Find("scatter-rocks")!.Kind);
            Assert.AreEqual("on · 1 tool", model.Plugins.Parts.Find("plugins.plugin.plateau-brush")!.Detail);
            Assert.AreEqual("on · 1 check", model.Plugins.Parts.Find("plugins.plugin.reclaim-per-army")!.Detail);

            JsonObject state = model.Plugins.Describe();
            JsonObject plateau = state["items"]!.AsArray().Cast<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "plateau-brush");
            Assert.AreEqual("Terrain", plateau["category"]!.GetValue<string>());
            Assert.AreEqual("brush", plateau["kind"]!.GetValue<string>());
        }

        [TestMethod]
        public async Task WithoutACategoryAPluginIsListedUnderItsToolsModeElseUnderOtherLast()
        {
            string folder = EmptyFolder();
            Write(folder, "dabber", Tool);
            Write(folder, "plain", """plugin.command{ id = "go", title = "Go", run = function() end }""");
            Write(folder, "zebra", """plugin.command{ id = "go", title = "Go", run = function() end }""", """{ "category": "Zebras" }""");

            MainViewModel model = await Open(folder);

            CollectionAssert.AreEqual(new[] { "Workspace", "Props", "Zebras", "Other" }, Headings(model), "alphabetical, Other last");
            Assert.AreEqual("other", model.Plugins.Find("plain")!.Kind, "no kind reads as other");
            Assert.AreEqual(0, model.Plugins.Find("plain")!.Errors.Count, "without a warning");

            // off, its tools are gone but it keeps its heading
            await model.Commands.RunAsync("plugins.enable", new JsonObject { ["plugin"] = "dabber", ["on"] = false }, interactive: false);
            CollectionAssert.AreEqual(new[] { "dabber" }, Under(model, "Props"));
            Assert.AreEqual("off", model.Plugins.Parts.Find("plugins.plugin.dabber")!.Detail);
        }

        [TestMethod]
        public async Task AKindTheEditorDoesNotKnowReadsAsOtherWithAWarning()
        {
            string folder = EmptyFolder();
            Write(folder, "odd", """plugin.command{ id = "go", title = "Go", run = function() end }""", """{ "name": "Odd", "kind": "Teleporter" }""");
            Write(folder, "fine", """plugin.command{ id = "go", title = "Go", run = function() end }""", """{ "kind": "Export" }""");

            MainViewModel model = await Open(folder);

            LuaPlugin odd = model.Plugins.Find("odd")!;
            Assert.AreEqual("other", odd.Kind);
            Assert.IsTrue(odd.IsLoaded, "it still loads");
            StringAssert.Contains(odd.ErrorText, "the kind 'Teleporter' is none of brush, check, command, generator, export, other");
            Assert.AreEqual("1 error", model.Plugins.Parts.Find("plugins.plugin.odd")!.Detail);
            Assert.AreEqual("export", model.Plugins.Find("fine")!.Kind, "a kind in capitals is known");
        }

        [TestMethod]
        public async Task TheListFollowsPluginsAddedRemovedRenamedAndMovedAndKeepsTheChosenOne()
        {
            string folder = EmptyFolder();
            Write(folder, "alpha", Tool, """{ "name": "Alpha" }""");
            Write(folder, "beta", """plugin.command{ id = "go", title = "Go", run = function() end }""");
            MainViewModel model = await Open(folder);
            model.InspectPart("plugins.plugin.alpha");
            PanelPart alpha = model.Plugins.Parts.Find("plugins.plugin.alpha")!;
            LuaPlugin before = model.Plugins.Find("alpha")!;

            // added
            Write(folder, "gamma", """plugin.command{ id = "go", title = "Go", run = function() end }""", """{ "category": "Analysis" }""");
            model.Plugins.OnFilesChanged(["gamma/plugin.lua"]);
            CollectionAssert.AreEqual(new[] { "Workspace", "Analysis", "Props", "Other" }, Headings(model), "a new heading in its place");

            // renamed in its plugin.json: the same entry, retitled
            File.WriteAllText(Path.Combine(folder, "alpha", "plugin.json"), """{ "name": "Alpha brush" }""");
            model.Plugins.OnFilesChanged(["alpha/plugin.json"]);
            Assert.AreSame(alpha, model.Plugins.Parts.Find("plugins.plugin.alpha"));
            Assert.AreEqual("Alpha brush", alpha.Title);
            Assert.AreEqual("plugins.plugin.alpha", model.Plugins.Parts.Chosen.Id, "still chosen");

            // moved to another heading: still chosen
            File.WriteAllText(Path.Combine(folder, "alpha", "plugin.json"), """{ "name": "Alpha brush", "category": "Analysis" }""");
            model.Plugins.OnFilesChanged(["alpha/plugin.json"]);
            CollectionAssert.AreEqual(new[] { "Alpha brush", "gamma" }, Under(model, "Analysis"));
            CollectionAssert.AreEqual(new[] { "Workspace", "Analysis", "Other" }, Headings(model), "the heading left empty is gone");
            Assert.AreEqual("plugins.plugin.alpha", model.Plugins.Parts.Chosen.Id, "still chosen after it moved");

            // read again whole: the entry shows the plugin read now
            model.Plugins.ReloadAll();
            PluginPart content = (PluginPart)model.Plugins.Parts.Chosen.Content;
            Assert.AreNotSame(before, model.Plugins.Find("alpha"));
            Assert.AreSame(model.Plugins.Find("alpha"), content.Plugin, "the inspector follows the plugin read again");

            // removed
            Directory.Delete(Path.Combine(folder, "alpha"), recursive: true);
            model.Plugins.OnFilesChanged(["alpha/plugin.lua"]);
            Assert.IsNull(model.Plugins.Parts.Find("plugins.plugin.alpha"));
            Assert.AreEqual("plugins.plugin.gamma", model.Plugins.Parts.Chosen.Id, "its heading's first is chosen");
        }

        [TestMethod]
        public async Task ManageChoosesTheModeAndAPluginAndTheInspectorShowsIt()
        {
            MainViewModel model = await Open(NewFolder());
            Assert.AreNotEqual("plugins", model.SelectedMode!.Definition.Id);

            Assert.IsTrue((await model.Commands.RunAsync("plugins.manage", new JsonObject { ["plugin"] = "scatter-rocks" }, interactive: false)).Ran);

            Assert.AreEqual("plugins", model.SelectedMode!.Definition.Id);
            Assert.AreEqual("plugins.plugin.scatter-rocks", model.Plugins.Parts.Chosen.Id);
            Assert.IsInstanceOfType<PluginDetailsSection>(model.InspectorParts.Single());
            Assert.IsTrue(model.InspectsMode);
            Assert.AreEqual("plugins.plugin.scatter-rocks", model.DescribePanelParts()["plugins"]!["chosen"]!.GetValue<string>());
            Assert.AreEqual("scatter-rocks", model.Plugins.Describe()["chosen"]!.GetValue<string>());
            Assert.IsTrue(model.Plugins.Describe()["open"]!.GetValue<bool>(), "the mode shows");

            // view.inspectPart reaches the plugins too, from another mode
            model.SelectMode("terrain");
            Assert.IsTrue((await model.Commands.RunAsync("view.inspectPart", new JsonObject { ["part"] = PluginsViewModel.OverviewPart }, interactive: false)).Ran);
            Assert.AreEqual("plugins", model.SelectedMode!.Definition.Id);
            Assert.IsInstanceOfType<PluginsOverviewPart>(model.Plugins.Parts.Chosen.Content);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("plugins.manage", new JsonObject { ["plugin"] = "nothing" }, interactive: false));
        }

        [TestMethod]
        public async Task ThePluginsCommandAndToggleRunFromItsPart()
        {
            MainViewModel model = await Open(NewFolder());
            model.InspectPart("plugins.plugin.scatter-rocks");
            PluginPart part = (PluginPart)model.Plugins.Parts.Chosen.Content;
            CollectionAssert.Contains(part.Plugin.Facts.Select(fact => $"{fact.Label}: {fact.Value}").ToArray(), "Command: Scatter rocks on steep slopes");
            Assert.IsTrue(model.Plugins.Find("plateau-brush")!.Facts.Any(fact => fact.Label == "Tool" && fact.Value.EndsWith("Terrain mode", StringComparison.Ordinal)),
                "a tool with its mode");

            await part.ToggleCommand.ExecuteAsync(null);
            Assert.IsFalse(model.Plugins.Find("scatter-rocks")!.IsOn);
            Assert.AreEqual("off", model.Plugins.Parts.Chosen.Detail);
            Assert.AreEqual(0, part.Plugin.Facts.Count, "off, it registers nothing");
            await part.ToggleCommand.ExecuteAsync(null);
            Assert.IsTrue(model.Plugins.Find("scatter-rocks")!.IsOn);
        }

        [TestMethod]
        public void AnUntouchedManifestWithoutCategoryAndKindIsBroughtUpToDate()
        {
            string folder = NewFolder();
            ExamplePlugins.Ensure(folder);
            string manifest = Path.Combine(folder, "plateau-brush", "plugin.json");
            File.WriteAllText(manifest, """
                {
                  "name": "Plateau brush",
                  "version": "1.0",
                  "description": "A tool of the Terrain mode: press at a height, drag to spread it; a stroke is one step of undo."
                }

                """.Replace("\r\n", "\n").Replace("\n", "\r\n"));

            CollectionAssert.AreEqual(new[] { "plateau-brush" }, ExamplePlugins.Ensure(folder).ToArray());
            PluginManifest read = PluginManifest.Read(Path.Combine(folder, "plateau-brush"));
            Assert.AreEqual("Terrain", read.Category);
            Assert.AreEqual("brush", read.Kind);
        }
    }
}
