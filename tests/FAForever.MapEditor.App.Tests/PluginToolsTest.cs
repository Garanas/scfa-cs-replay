using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Plugins;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The tools of Lua plugins (user story F-15): a tool joins its mode and the Plugins menu, the
    /// left mouse calls its handlers, a stroke is one step of undo, a failing or runaway handler
    /// changes nothing, its fields are its options, and the plateau example works in the window.
    /// </summary>
    [TestClass]
    public sealed class PluginToolsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static string Folder(string id, string lua)
        {
            string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", "plugins-" + Guid.NewGuid().ToString("N"))).FullName;
            File.WriteAllLines(Path.Combine(folder, ".examples"), ExamplePlugins.Names);
            Directory.CreateDirectory(Path.Combine(folder, id));
            File.WriteAllText(Path.Combine(folder, id, "plugin.lua"), lua);
            return folder;
        }

        private static async Task<MainViewModel> Open(string folder, string map = HardFfa)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(map);
            model.Assets.Use(TestAssets.Catalog());
            model.Plugins.Open(folder);
            return model;
        }

        private const string Raise = """
            plugin.tool{
              id = "raise", title = "Raise a square", mode = "terrain", description = "Raises where it is dragged",
              radius = "size",
              fields = { { key = "size", label = "Size", type = "number", default = 2, min = 1, max = 8 } },
              press = function(x, z, keys)
                presses = (presses or 0) + 1
                shift = keys.shift
                local s = plugin.value("size")
                map.setHeight(x - s, z - s, x + s, z + s, function(sx, sz, h) return h + 5 end)
              end,
              drag = function(x, z)
                local s = plugin.value("size")
                map.setHeight(x - s, z - s, x + s, z + s, function(sx, sz, h) return h + 5 end)
              end,
              release = function(x, z) released = { x, z } end,
            }
            plugin.command{ id = "report", title = "Report", run = function() return { presses = presses, shift = shift, released = released } end }
            """;

        [TestMethod]
        public async Task AToolJoinsItsModeAndAStrokeIsOneStepOfUndo()
        {
            MainViewModel model = await Open(Folder("raiser", Raise));
            LuaPlugin plugin = model.Plugins.Find("raiser")!;
            Assert.IsTrue(plugin.IsLoaded, plugin.ErrorText);
            StringAssert.Contains(plugin.Registered, "1 tool");

            EditorCommand choose = model.Commands.Find("tool.plugin.raiser.raise")!;
            Assert.AreEqual("Plugins", choose.Menu, "its command is in the Plugins menu");
            Assert.AreEqual("raiser", choose.Owner);
            await model.Commands.RunAsync("tool.plugin.raiser.raise", [], interactive: false);
            Assert.AreEqual("terrain", model.SelectedMode!.Definition.Id);
            Assert.AreEqual("plugin.raiser.raise", model.ActiveTool!.Definition.Id);
            Assert.IsTrue(model.Tools.Any(tool => tool.Definition.Id == "plugin.raiser.raise"), "the Terrain mode lists it after its own tools");
            PluginTool tool = (PluginTool)model.ActiveTool.Tool!;
            Assert.AreEqual(2, tool.CursorRadius, "its radius is its field's value");

            MapDocument before = model.Map!.Document;
            float height = before.Terrain.Heightmap.GetHeight(100, 100);
            model.Stroke([(100, 100), (110, 100), (120, 100)]);

            MapDocument after = model.Map.Document;
            Assert.AreEqual(height + 5, after.Terrain.Heightmap.GetHeight(100, 100), 0.05, "press raised where it was pressed");
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight(110, 100) + 5, after.Terrain.Heightmap.GetHeight(110, 100), 0.05, "drag raised where it was dragged");
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight(140, 100), after.Terrain.Heightmap.GetHeight(140, 100), "nothing elsewhere");
            Assert.AreEqual("raiser: Raise a square", model.Map.Session.UndoDescription, "one step, named after the plugin and the tool");
            JsonObject report = (JsonObject)(await model.Commands.RunAsync("plugin.raiser.report", [], interactive: false)).Result!;
            Assert.AreEqual(1, report["presses"]!.GetValue<int>());
            Assert.AreEqual(120, report["released"]![0]!.GetValue<double>(), "release gets the last place");

            Assert.IsTrue(model.Undo());
            Assert.AreEqual(height, model.Map.Document.Terrain.Heightmap.GetHeight(100, 100), 0.01, "the whole stroke goes back at once");
            Assert.IsFalse(model.Map.Session.CanUndo);
        }

        [TestMethod]
        public async Task WithTheSymmetryTheHandlersAreCalledForEveryImage()
        {
            MainViewModel model = await Open(Folder("raiser", Raise));
            await model.Commands.RunAsync("select.symmetry", new JsonObject { ["kind"] = "leftRight" }, interactive: false);
            model.Symmetry.MirrorsEdits = true;
            model.SelectTool("plugin.raiser.raise");
            MapDocument before = model.Map!.Document;
            float mirroredX = before.Width - 100;

            model.Stroke([(100, 100)]);

            MapDocument after = model.Map.Document;
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight(100, 100) + 5, after.Terrain.Heightmap.GetHeight(100, 100), 0.05, "where it was pressed");
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight((int)mirroredX, 100) + 5, after.Terrain.Heightmap.GetHeight((int)mirroredX, 100), 0.05, "and in the mirrored half");
            JsonObject report = (JsonObject)(await model.Commands.RunAsync("plugin.raiser.report", [], interactive: false)).Result!;
            Assert.AreEqual(2, report["presses"]!.GetValue<int>(), "press is called once per image");
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight((int)mirroredX, 100), model.Map.Document.Terrain.Heightmap.GetHeight((int)mirroredX, 100), 0.01, "one step for both");

            // a tool that does its own symmetry gets the mouse's place only
            MainViewModel own = await Open(Folder("raiser", Raise.Replace("radius = \"size\",", "radius = \"size\", symmetry = false,")));
            await own.Commands.RunAsync("select.symmetry", new JsonObject { ["kind"] = "leftRight" }, interactive: false);
            own.Symmetry.MirrorsEdits = true;
            own.SelectTool("plugin.raiser.raise");
            own.Stroke([(100, 100)]);
            JsonObject once = (JsonObject)(await own.Commands.RunAsync("plugin.raiser.report", [], interactive: false)).Result!;
            Assert.AreEqual(1, once["presses"]!.GetValue<int>());
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight((int)mirroredX, 100), own.Map!.Document.Terrain.Heightmap.GetHeight((int)mirroredX, 100), 0.01);
        }

        [TestMethod]
        public async Task ItsFieldsAreItsOptionsAndTheKeysHeldReachIt()
        {
            MainViewModel model = await Open(Folder("raiser", Raise));
            model.SelectTool("plugin.raiser.raise");
            PluginTool tool = (PluginTool)model.ActiveTool!.Tool!;
            Assert.AreEqual(1, tool.Settings.Count);
            Assert.AreEqual("Size", tool.Settings[0].Label);

            await model.Commands.RunAsync("plugins.setField", new JsonObject { ["plugin"] = "raiser", ["field"] = "size", ["value"] = 5 }, interactive: false);
            Assert.AreEqual(5, tool.CursorRadius, "the radius follows the field");
            Assert.AreEqual(5, ((NumberSetting)tool.Settings[0]).Value, "and so does its option");

            tool.Press(60, 60, new ToolInput(ToolModifiers.Shift, 0.5f));
            tool.Release();
            JsonObject report = (JsonObject)(await model.Commands.RunAsync("plugin.raiser.report", [], interactive: false)).Result!;
            Assert.IsTrue(report["shift"]!.GetValue<bool>(), "the keys held reach press");
        }

        [TestMethod]
        public async Task AFailingOrRunawayHandlerThrowsTheStrokeAway()
        {
            string folder = Folder("broken", """
                plugin.tool{ id = "fail", title = "Fail", mode = "props",
                  press = function(x, z) map.setHeight(x, z, x + 2, z + 2, 200) end,
                  drag = function(x, z) error("the tool broke") end }
                plugin.tool{ id = "loop", title = "Loop", mode = "props",
                  press = function(x, z) map.setHeight(x, z, x + 2, z + 2, 200) while true do end end }
                """);
            MainViewModel model = await Open(folder);
            model.Plugins.CallLimit = TimeSpan.FromMilliseconds(200);
            MapDocument before = model.Map!.Document;

            model.SelectTool("plugin.broken.fail");
            Assert.AreEqual("props", model.SelectedMode!.Definition.Id);
            model.Stroke([(50, 50), (60, 60), (70, 70)]);

            Assert.AreSame(before, model.Map.Document, "what press edited is thrown away");
            Assert.IsNull(model.StrokeInProgress);
            Assert.IsFalse(model.Map.Session.CanUndo);
            StringAssert.Contains(model.Status, "the tool broke");
            StringAssert.Contains(model.Status, "nothing was changed");
            StringAssert.Contains(model.Plugins.Find("broken")!.ErrorText, "plugin.lua:3: the tool broke");

            model.SelectTool("plugin.broken.loop");
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            model.Stroke([(50, 50), (60, 60)]);
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), $"stopped after {watch.Elapsed}");
            Assert.AreSame(before, model.Map.Document);
            StringAssert.Contains(model.Status, "took longer than 0.2 s");
        }

        [TestMethod]
        public async Task ATurnedOffPluginsToolLeavesItsMode()
        {
            MainViewModel model = await Open(Folder("raiser", Raise));
            model.SelectTool("plugin.raiser.raise");

            model.Plugins.SetOn("raiser", false);

            Assert.IsNull(model.Commands.Find("tool.plugin.raiser.raise"));
            Assert.IsFalse(model.Tools.Any(tool => tool.Definition.Id == "plugin.raiser.raise"));
            Assert.AreEqual("terrain.sculpt", model.ActiveTool!.Definition.Id, "the mode's first tool takes its place");
            Assert.ThrowsException<ArgumentException>(() => model.SelectTool("plugin.raiser.raise"));

            model.Plugins.SetOn("raiser", true);
            model.SelectTool("plugin.raiser.raise");
            Assert.AreEqual("plugin.raiser.raise", model.ActiveTool!.Definition.Id, "on again, it is back");
        }

        [TestMethod]
        public async Task AToolWithAWrongModeOrFieldFailsToLoad()
        {
            MainViewModel model = await Open(Folder("wrong", """
                plugin.tool{ id = "t", title = "T", mode = "kitchen", press = function() end }
                """));
            StringAssert.Contains(model.Plugins.Find("wrong")!.ErrorText, "There is no mode 'kitchen'");

            MainViewModel other = await Open(Folder("nofield", """
                plugin.tool{ id = "t", title = "T", fields = { "size" }, press = function() end }
                """));
            StringAssert.Contains(other.Plugins.Find("nofield")!.ErrorText, "names a field 'size'");
        }

        [TestMethod]
        public Task ThePlateauExampleFlattensWhereTheMouseDragsInTheWindow() => HeadlessApp.Run(async () =>
        {
            string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", "plugins-" + Guid.NewGuid().ToString("N"))).FullName;
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(TestMaps.SlopeProject(degrees: 30));
            model.Assets.Use(TestAssets.Catalog());
            window.DataContext = model;
            window.Show();
            model.Plugins.Open(folder);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.Plugins.Find("plateau-brush")!.IsLoaded, model.Plugins.Find("plateau-brush")!.ErrorText);
            EditorDriver editor = EditorDriver.InProcess(new AutomationServer(window, model, port: 0));
            await editor.Tab("top");
            await editor.Tool("plugin.plateau-brush.plateau");

            JsonArray row = (JsonArray)(await editor.Send("ui", new JsonObject { ["under"] = "Plateau: settings" }))["ui"]!;
            StringAssert.Contains(row.ToJsonString(), "Plateau radius", "the options row shows its fields");
            await editor.Set("Plateau radius", 4);
            Assert.AreEqual(4, ((PluginTool)model.ActiveTool!.Tool!).CursorRadius, "set through its option");

            // press on the flat ground below the ramp, drag up onto the ramp: the ramp is cut down to it
            JsonObject low = (JsonObject)(await editor.Send("view-point", new JsonObject { ["x"] = 90, ["z"] = 128 }))["point"]!["top"]!;
            JsonObject up = (JsonObject)(await editor.Send("view-point", new JsonObject { ["x"] = 120, ["z"] = 128 }))["point"]!["top"]!;
            float flat = model.ShownDocument!.Terrain.Heightmap.GetHeight(90, 128);
            float ramp = model.ShownDocument.Terrain.Heightmap.GetHeight(120, 128);
            Assert.IsTrue(ramp > flat + 10, "the ramp stands above the flat ground");
            await editor.Send("mouse", new JsonObject
            {
                ["view"] = "top", ["x"] = low["x"]!.DeepClone(), ["y"] = low["y"]!.DeepClone(), ["action"] = "drag",
                ["to"] = new JsonArray(up["x"]!.DeepClone(), up["y"]!.DeepClone()), ["steps"] = 20,
            });

            Assert.AreEqual(flat, model.Map!.Document.Terrain.Heightmap.GetHeight(120, 128), 0.5, "the ramp under the drag is at the pressed height");
            Assert.AreEqual(ramp, model.Map.Document.Terrain.Heightmap.GetHeight(120, 150), 0.01, "beyond the radius nothing changed");
            Assert.AreEqual("Plateau brush: Plateau", model.Map.Session.UndoDescription, "one step");
            window.Close();
        });
    }
}
