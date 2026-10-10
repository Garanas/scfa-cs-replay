using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Plugins;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The workspace's Lua plugins (user story F-15): loading and reloading, the facade's reads and
    /// edits as one step of undo, a failing or runaway plugin changing nothing, commands, checks,
    /// fields and events, the sandbox of the Lua, and the example plugins.
    /// </summary>
    [TestClass]
    public class PluginsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static string NewFolder() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", "plugins-" + Guid.NewGuid().ToString("N"))).FullName;

        /// <summary>
        /// A plugins folder with one plugin, without the examples unless asked.
        /// </summary>
        private static string Folder(string id, string lua, bool examples = false, string? manifest = null)
        {
            string folder = NewFolder();
            if (!examples)
            {
                File.WriteAllLines(Path.Combine(folder, ".examples"), ExamplePlugins.Names);
            }
            Write(folder, id, lua, manifest);
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

        private static async Task<MainViewModel> Open(string folder, string map = HardFfa)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(map);
            model.Assets.Use(TestAssets.Catalog());
            model.Plugins.Open(folder);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task LoadsAPluginAndPutsItsCommandIntoThePluginsMenuThePaletteAndAutomation()
        {
            string folder = Folder("hello", """
                plugin.command{ id = "greet", title = "Greet", description = "Says hello", run = function() editor.status("Hello") end }
                plugin.check{ id = "nothing", title = "Nothing", run = function() return {} end }
                plugin.field{ key = "size", label = "Size", type = "number", default = 3, min = 1, max = 10 }
                editor.on("mapSaved", function() end)
                """, manifest: """{ "name": "Hello", "version": "1.2", "description": "Greets." }""");

            MainViewModel model = await Open(folder);

            LuaPlugin plugin = model.Plugins.Find("hello")!;
            Assert.IsTrue(plugin.IsLoaded, plugin.ErrorText);
            Assert.AreEqual("Hello 1.2", plugin.Title);
            StringAssert.Contains(plugin.Caption, "Greets. 1 command, 1 check, a panel of 1 field.");
            EditorCommand command = model.Commands.Find("plugin.hello.greet")!;
            Assert.AreEqual("Plugins", command.Menu);
            Assert.AreEqual("hello", command.Owner);
            CollectionAssert.Contains(Menus.WithPlugins(model.Commands.All).Single(menu => menu.Title == "Plugins").Items.ToList(), "plugin.hello.greet");
            Assert.IsTrue(model.Commands.Search("greet").Any(found => found.Id == "plugin.hello.greet"), "the palette finds it");
            Assert.IsTrue(model.Problems.Checks.All.Any(check => check.Id == "plugin.hello.nothing"));
            Assert.IsNotNull(plugin.Panel);

            Assert.IsTrue((await Run(model, "plugin.hello.greet")).Ran);
            Assert.AreEqual("Hello", model.Status);
            Assert.IsFalse(model.Map!.Session.CanUndo, "a run that changed nothing is no step");

            JsonObject state = model.Plugins.Describe();
            Assert.AreEqual("plugin.hello.greet", state["items"]![0]!["commands"]![0]!.GetValue<string>());
            Assert.AreEqual("3", state["items"]![0]!["fields"]!["size"]!.GetValue<string>());
            Assert.AreEqual("mapSaved", state["items"]![0]!["events"]![0]!.GetValue<string>());
        }

        [TestMethod]
        public async Task ReadsTheMapAsPlainTables()
        {
            string folder = Folder("reader", """
                plugin.command{ id = "read", title = "Read", run = function()
                  local width, height = map.size()
                  local props = map.props()
                  local spawns = map.markers("spawn")
                  local first = props[1]
                  return {
                    name = map.name(), width = width, height = height,
                    props = #props, spawns = #spawns, markers = #map.markers(), units = #map.units(), decals = #map.decals(),
                    firstRef = first.ref, firstBlueprint = first.blueprint, firstX = first.x,
                    sameAsGet = map.get(first.ref).blueprint == first.blueprint,
                    height = map.height(10, 20), slope = map.slope(10, 20),
                    spawnKind = spawns[1].kind,
                  }
                end }
                """);
            MainViewModel model = await Open(folder);
            MapDocument document = model.Map!.Document;

            JsonObject read = (JsonObject)(await Run(model, "plugin.reader.read")).Result!;

            Assert.AreEqual(document.Scenario.Name, read["name"]!.GetValue<string>());
            Assert.AreEqual(document.Width, read["width"]!.GetValue<int>());
            Assert.AreEqual(document.Props.Length, read["props"]!.GetValue<int>());
            Assert.AreEqual(document.Markers.Spawns.Length, read["spawns"]!.GetValue<int>());
            Assert.AreEqual(document.Markers.All.Count(), read["markers"]!.GetValue<int>());
            Assert.AreEqual(document.AllUnits().Count(), read["units"]!.GetValue<int>());
            Assert.AreEqual(document.Decals.Length, read["decals"]!.GetValue<int>());
            Assert.AreEqual($"prop:{document.Props[0].Id}", read["firstRef"]!.GetValue<string>());
            Assert.AreEqual(document.Props[0].Blueprint, read["firstBlueprint"]!.GetValue<string>());
            Assert.AreEqual(document.Props[0].Position.X, read["firstX"]!.GetValue<double>(), 1e-3);
            Assert.IsTrue(read["sameAsGet"]!.GetValue<bool>());
            Assert.AreEqual(document.Terrain.Heightmap.HeightAt(10, 20), read["height"]!.GetValue<double>(), 1e-3);
            Assert.AreEqual("spawn", read["spawnKind"]!.GetValue<string>());
        }

        [TestMethod]
        public async Task ARevisionStaysWhileItsPartIsTheSame()
        {
            string folder = Folder("revisions", """
                plugin.command{ id = "read", title = "Read", run = function()
                  return { props = map.revision("props"), markers = map.revision("markers"), map = map.revision() }
                end }
                """);
            MainViewModel model = await Open(folder);

            JsonObject first = (JsonObject)(await Run(model, "plugin.revisions.read")).Result!;
            await Run(model, "water.toggle");
            JsonObject second = (JsonObject)(await Run(model, "plugin.revisions.read")).Result!;
            model.Map!.Session.Edit("Move a prop", document => document.UpdateProp(document.Props[0].Id, prop => prop with { Position = Vector3.One }));
            JsonObject third = (JsonObject)(await Run(model, "plugin.revisions.read")).Result!;

            Assert.AreEqual(first["props"]!.GetValue<int>(), second["props"]!.GetValue<int>(), "the water changed, the props did not");
            Assert.AreEqual(first["markers"]!.GetValue<int>(), third["markers"]!.GetValue<int>());
            Assert.AreNotEqual(first["map"]!.GetValue<int>(), second["map"]!.GetValue<int>());
            Assert.AreNotEqual(second["props"]!.GetValue<int>(), third["props"]!.GetValue<int>());
        }

        [TestMethod]
        public async Task ARunsEditsAndTheCommandsItRunsAreOneStepOfUndo()
        {
            string folder = Folder("edit", """
                plugin.command{ id = "change", title = "Change everything", run = function(args)
                  local ref = map.addProp{ blueprint = "env/evergreen/props/rocks/rock01_prop.bp", x = 50, z = 60, heading = 90, scale = 2 }
                  map.move(ref, 5, 0)
                  map.setHeight(0, 0, 3, 3, function(x, z, h) return h + 10 end)
                  editor.run("water.toggle", { on = not map.water() })
                  local after = map.get(ref)
                  editor.select({ ref })
                  return { ref = ref, x = after.x, water = map.water() ~= nil, given = args.given }
                end }
                """);
            MainViewModel model = await Open(folder);
            MapDocument before = model.Map!.Document;
            int steps = model.Map.Session.History.Count;

            CommandRun run = await Run(model, "plugin.edit.change", new JsonObject { ["given"] = 7 });

            JsonObject result = (JsonObject)run.Result!;
            Assert.AreEqual(7, result["given"]!.GetValue<int>(), "the arguments reach the function as a table");
            Assert.AreEqual(55, result["x"]!.GetValue<double>(), 1e-3, "a command reads what the run edited before it");
            MapDocument after = model.Map.Document;
            Assert.AreEqual(before.Props.Length + 1, after.Props.Length);
            Prop added = after.Props[^1];
            Assert.AreEqual("/env/evergreen/props/rocks/rock01_prop.bp", added.Blueprint);
            Assert.AreEqual(2, added.Scale.X, 1e-4);
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight(2, 2) + 10, after.Terrain.Heightmap.GetHeight(2, 2), 0.01);
            Assert.AreNotEqual(before.Water.HasWater, after.Water.HasWater);
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count, "one step");
            Assert.AreEqual("edit: Change everything", model.Map.Session.UndoDescription);
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Parse(result["ref"]!.GetValue<string>())));

            Assert.IsTrue(model.Undo());
            Assert.AreEqual(before.Props.Length, model.Map.Document.Props.Length);
            Assert.AreEqual(before.Water.HasWater, model.Map.Document.Water.HasWater);
            Assert.AreEqual(before.Terrain.Heightmap.GetHeight(2, 2), model.Map.Document.Terrain.Heightmap.GetHeight(2, 2), "everything goes back at once");
        }

        [TestMethod]
        public async Task AFailingPluginChangesNothingAndShowsItsError()
        {
            string folder = Folder("broken", """
                plugin.command{ id = "fail", title = "Fail", run = function()
                  map.addProp{ blueprint = "/env/evergreen/props/rocks/rock01_prop.bp", x = 50, z = 60 }
                  editor.run("water.toggle", {})
                  error("the plugin broke")
                end }
                """);
            MainViewModel model = await Open(folder);
            MapDocument before = model.Map!.Document;

            InvalidOperationException exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "plugin.broken.fail"));

            StringAssert.Contains(exception.Message, "the plugin broke");
            StringAssert.Contains(exception.Message, "nothing was changed");
            Assert.AreSame(before, model.Map.Document, "the run's edits and the command it ran are undone");
            Assert.IsFalse(model.Map.Session.CanUndo);
            Assert.IsFalse(model.Map.Session.InGroup);
            LuaPlugin plugin = model.Plugins.Find("broken")!;
            StringAssert.Contains(plugin.ErrorText, "plugin.lua:4: the plugin broke");
            Assert.IsTrue(model.Diagnostics.Console.Any(entry => entry.Message.Contains("the plugin broke", StringComparison.Ordinal)), "the Console shows it");

            await model.RunAsync("plugin.broken.fail");
            StringAssert.Contains(model.Status, "the plugin broke", "the menu shows it in the status line");
        }

        [TestMethod]
        public async Task ARunawayScriptIsStoppedAfterItsTimeAndChangesNothing()
        {
            string folder = Folder("runaway", """
                plugin.command{ id = "loop", title = "Loop", run = function()
                  map.addProp{ blueprint = "/env/evergreen/props/rocks/rock01_prop.bp", x = 50, z = 60 }
                  while true do end
                end }
                """);
            MainViewModel model = await Open(folder);
            model.Plugins.RunLimit = TimeSpan.FromMilliseconds(300);
            MapDocument before = model.Map!.Document;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            InvalidOperationException exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "plugin.runaway.loop"));

            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), $"stopped after {watch.Elapsed}");
            StringAssert.Contains(exception.Message, "took longer than 0.3 s and was stopped");
            Assert.AreSame(before, model.Map.Document);

            string loading = Folder("hang", "while true do end");
            MainViewModel other = new MainViewModel(new MainViewModelTest.Answers());
            other.Plugins.CallLimit = TimeSpan.FromMilliseconds(200);
            other.Plugins.Open(loading);
            StringAssert.Contains(other.Plugins.Find("hang")!.ErrorText, "Loading took longer than 0.2 s");
        }

        [TestMethod]
        public async Task AChecksProblemsAreInTheProblemsTabAndAChecksOnlyRead()
        {
            string folder = Folder("checks", """
                plugin.check{ id = "spawns", title = "Spawns", run = function()
                  local problems = {}
                  for _, spawn in ipairs(map.markers("spawn")) do
                    problems[#problems + 1] = { severity = "error", message = "Look at " .. spawn.name, thing = spawn.ref }
                  end
                  problems[#problems + 1] = { message = "A place", x = 10, z = 20 }
                  problems[#problems + 1] = "Just a text"
                  return problems
                end }
                plugin.check{ id = "editing", title = "Editing", run = function()
                  map.addProp{ blueprint = "/a.bp", x = 1, z = 1 }
                end }
                """);
            MainViewModel model = await Open(folder);
            int spawns = model.Map!.Document.Markers.Spawns.Length;

            IReadOnlyList<Problem> problems = model.Problems.Run();

            Problem[] mine = [.. problems.Where(problem => problem.Check == "plugin.checks.spawns")];
            Assert.AreEqual(spawns + 2, mine.Length);
            Assert.AreEqual(ProblemSeverity.Error, mine[0].Severity);
            Assert.AreEqual(ObjectRef.Marker(model.Map.Document.Markers.Spawns[0].Id), mine[0].Thing);
            Assert.AreEqual(new Vector2(10, 20), mine.Single(problem => problem.Message == "A place").Place);
            Assert.AreEqual(ProblemSeverity.Warning, mine.Single(problem => problem.Message == "Just a text").Severity);
            StringAssert.Contains(model.Plugins.Find("checks")!.ErrorText, "may only read the map");
            Assert.AreEqual(0, model.Map.Session.History.Count);
        }

        [TestMethod]
        public async Task HandlersOfEventsHearTheEditorAndMayStopASave()
        {
            string folder = Folder("events", """
                heard = {}
                editor.on("changed", function(description, reason) heard[#heard + 1] = description .. "/" .. reason end)
                editor.on("beforeSave", function(folder) if #map.markers("spawn") > 0 then return "spawns are not allowed" end end)
                plugin.command{ id = "heard", title = "Heard", run = function() return heard end }
                """);
            MainViewModel model = await Open(folder);
            await Run(model, "water.toggle");

            JsonArray heard = (JsonArray)(await Run(model, "plugin.events.heard")).Result!;
            CollectionAssert.AreEqual(new[] { "Water on or off/edit" }, heard.Select(item => item!.GetValue<string>()).ToArray());

            OperationVetoedException vetoed = await Assert.ThrowsExceptionAsync<OperationVetoedException>(() => model.SaveAsAsync(NewFolder()));
            StringAssert.Contains(vetoed.Message, "events: spawns are not allowed");
        }

        [TestMethod]
        public async Task ReadsAPluginAgainWhenItChangesAndTurnsItOnAndOff()
        {
            string folder = Folder("live", """plugin.command{ id = "one", title = "One", run = function() end }""");
            MainViewModel model = await Open(folder);
            model.Diagnostics.Settings = new UserSettings(Path.Combine(NewFolder(), "settings.json"));
            Assert.IsNotNull(model.Commands.Find("plugin.live.one"));

            File.WriteAllText(Path.Combine(folder, "live", "plugin.lua"), """plugin.command{ id = "two", title = "Two", run = function() end }""");
            model.Plugins.OnFilesChanged(["live/plugin.lua"]);
            Assert.IsNull(model.Commands.Find("plugin.live.one"), "the old command is gone");
            Assert.IsNotNull(model.Commands.Find("plugin.live.two"));

            File.WriteAllText(Path.Combine(folder, "live", "plugin.lua"), "this is not Lua");
            model.Plugins.OnFilesChanged(["live/plugin.lua"]);
            Assert.IsNull(model.Commands.Find("plugin.live.two"), "a script that fails registers nothing");
            StringAssert.Contains(model.Plugins.Find("live")!.ErrorText, "Loading failed: plugin.lua:1:");

            Write(folder, "late", """plugin.command{ id = "late", title = "Late", run = function() end }""");
            model.Plugins.OnFilesChanged(["late/plugin.lua"]);
            Assert.IsNotNull(model.Commands.Find("plugin.late.late"), "a new plugin is loaded");

            Assert.IsTrue((await Run(model, "plugins.enable", new JsonObject { ["plugin"] = "late", ["on"] = false })).Ran);
            Assert.IsNull(model.Commands.Find("plugin.late.late"), "off, its command is gone");
            CollectionAssert.AreEqual(new[] { "late" }, model.Diagnostics.Settings.Plugins.Off);
            MainViewModel again = new MainViewModel(new MainViewModelTest.Answers()) { };
            again.Diagnostics.Settings = model.Diagnostics.Settings;
            again.Plugins.Open(folder);
            Assert.IsFalse(again.Plugins.Find("late")!.IsOn, "the settings remember it");
            Assert.IsNull(again.Commands.Find("plugin.late.late"));

            Directory.Delete(Path.Combine(folder, "late"), recursive: true);
            model.Plugins.OnFilesChanged(["late/plugin.lua"]);
            Assert.IsNull(model.Plugins.Find("late"), "a plugin deleted is gone");
        }

        [TestMethod]
        public async Task FieldsTakeValuesOfTheirTypeAndTheSettingsRememberThem()
        {
            string folder = Folder("fields", """
                plugin.field{ key = "count", label = "Count", type = "number", default = 5, min = 1, max = 10 }
                plugin.field{ key = "kind", label = "Kind", type = "choice", choices = { "rock", "tree" } }
                plugin.field{ key = "on", label = "On", type = "bool", default = true }
                plugin.command{ id = "values", title = "Values", run = function()
                  return { count = plugin.value("count"), kind = plugin.value("kind"), on = plugin.value("on") }
                end }
                """);
            MainViewModel model = await Open(folder);
            model.Diagnostics.Settings = new UserSettings(Path.Combine(NewFolder(), "settings.json"));

            Assert.IsTrue((await Run(model, "plugins.setField", new JsonObject { ["plugin"] = "fields", ["field"] = "count", ["value"] = 50 })).Ran);
            await Run(model, "plugins.setField", new JsonObject { ["plugin"] = "fields", ["field"] = "kind", ["value"] = "tree" });
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "plugins.setField", new JsonObject { ["plugin"] = "fields", ["field"] = "kind", ["value"] = "bush" }));

            JsonObject values = (JsonObject)(await Run(model, "plugin.fields.values")).Result!;
            Assert.AreEqual(10, values["count"]!.GetValue<int>(), "kept within its range");
            Assert.AreEqual("tree", values["kind"]!.GetValue<string>());
            Assert.IsTrue(values["on"]!.GetValue<bool>());
            Assert.AreEqual("tree", model.Diagnostics.Settings.Plugins.Values!["fields"]["kind"]);
            NumberSetting count = (NumberSetting)model.Plugins.Find("fields")!.Panel!.Settings[0];
            Assert.AreEqual("10", count.Text, "the page's field shows it");
        }

        [TestMethod]
        public async Task TheLuaReachesNoFilesButThePluginsOwn()
        {
            string folder = Folder("sandboxed", """
                plugin.command{ id = "probe", title = "Probe", run = function()
                  local outside = pcall(plugin.read, "../../secret.txt")
                  return {
                    io = io == nil, os = os == nil, dofile = dofile == nil, loadfile = loadfile == nil,
                    outside = not outside, own = plugin.read("data.txt"), module = require("helpers").answer,
                  }
                end }
                """);
            File.WriteAllText(Path.Combine(folder, "sandboxed", "data.txt"), "mine");
            File.WriteAllText(Path.Combine(folder, "sandboxed", "helpers.lua"), "return { answer = 42 }");
            MainViewModel model = await Open(folder);

            JsonObject probe = (JsonObject)(await Run(model, "plugin.sandboxed.probe")).Result!;

            foreach (string key in new[] { "io", "os", "dofile", "loadfile", "outside" })
            {
                Assert.IsTrue(probe[key]!.GetValue<bool>(), key);
            }
            Assert.AreEqual("mine", probe["own"]!.GetValue<string>());
            Assert.AreEqual(42, probe["module"]!.GetValue<int>());
        }

        [TestMethod]
        public async Task TheExamplesAreWrittenOnceAndWorkOnTheSandbox()
        {
            string folder = NewFolder();
            MainViewModel model = await Open(folder, TestAssets.SandboxProject());

            CollectionAssert.AreEqual(new[] { "plateau-brush", "reclaim-per-army", "scatter-rocks" }, model.Plugins.Items.Select(plugin => plugin.Id).ToArray());
            Assert.IsTrue(model.Plugins.Items.All(plugin => plugin.IsLoaded && !plugin.HasErrors), string.Join("; ", model.Plugins.Items.Select(p => p.ErrorText)));
            Directory.Delete(Path.Combine(folder, "scatter-rocks"), recursive: true);
            Assert.AreEqual(0, ExamplePlugins.Ensure(folder).Count, "an example deleted is not written again");

            MapDocument document = model.Map!.Document;
            JsonArray totals = (JsonArray)(await Run(model, "plugin.reclaim-per-army.count")).Result!;
            Assert.AreEqual(document.Markers.Spawns.Length, totals.Count);
            double expected = PropReclaim.Of(document.Props, model.Assets.Catalog).Mass;
            Assert.AreEqual(expected, totals.Sum(total => total!["mass"]!.GetValue<double>()), 1e-6, "every prop counts for one army");
            StringAssert.StartsWith(model.Status, "Mass of reclaim per army:");
            Assert.IsFalse(model.Problems.Run().Any(problem => problem.Message.Contains("mass of reclaim nearest", StringComparison.Ordinal)),
                "a few rocks more or less make no warning");
        }

        [TestMethod]
        public void AnUntouchedCopyOfAnEarlierExampleIsBroughtUpToDateAndAnEditedOneKept()
        {
            string folder = NewFolder();
            ExamplePlugins.Ensure(folder);
            string reclaim = Path.Combine(folder, "reclaim-per-army");
            string current = File.ReadAllText(Path.Combine(reclaim, "plugin.lua"));
            string earlier = Path.Combine(AppContext.BaseDirectory, "assets", "plugins", "reclaim-per-army-1.0");
            foreach (string file in new[] { "plugin.lua", "plugin.json" })
            {
                // with the line ends a Windows checkout gives
                File.WriteAllText(Path.Combine(reclaim, file), File.ReadAllText(Path.Combine(earlier, file)).Replace("\r\n", "\n").Replace("\n", "\r\n"));
            }

            CollectionAssert.AreEqual(new[] { "reclaim-per-army" }, ExamplePlugins.Ensure(folder).ToArray(), "version 1.0, untouched: brought up to date");
            Assert.AreEqual(current, File.ReadAllText(Path.Combine(reclaim, "plugin.lua")));
            StringAssert.Contains(current, "least");

            File.WriteAllText(Path.Combine(reclaim, "plugin.lua"), File.ReadAllText(Path.Combine(earlier, "plugin.lua")) + "-- mine\n");
            Assert.AreEqual(0, ExamplePlugins.Ensure(folder).Count, "a copy the user changed is theirs");
            StringAssert.EndsWith(File.ReadAllText(Path.Combine(reclaim, "plugin.lua")), "-- mine\n");
        }

        [TestMethod]
        public async Task TheScatterExamplePutsRocksOnTheSlopeAsOneStep()
        {
            string folder = NewFolder();
            ExamplePlugins.Ensure(folder);
            MainViewModel model = await Open(folder, TestMaps.SlopeProject(30));
            // the ramp is on one side only: no images in the other half (the symmetry is mirrored in PluginToolsTest)
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "off" });
            MapDocument before = model.Map!.Document;

            JsonObject result = (JsonObject)(await Run(model, "plugin.scatter-rocks.scatter", new JsonObject { ["minSlope"] = 25, ["count"] = 12 })).Result!;

            Assert.AreEqual(12, result["placed"]!.GetValue<int>());
            Prop[] rocks = [.. model.Map.Document.Props.Skip(before.Props.Length)];
            Assert.AreEqual(12, rocks.Length);
            Assert.IsTrue(rocks.All(rock => rock.Position.X >= TestMaps.RampStart - 1 && rock.Position.X <= TestMaps.RampEnd + 1), "only on the ramp");
            Assert.IsTrue(rocks.All(rock => rock.Blueprint.Contains("/rocks/", StringComparison.Ordinal)));
            Assert.AreEqual(12, model.Selection.Count, "the rocks are selected");
            Assert.AreEqual("Scatter rocks: Scatter rocks on steep slopes", model.Map.Session.UndoDescription);
            model.Undo();
            Assert.AreEqual(before.Props.Length, model.Map.Document.Props.Length);
        }

        [TestMethod]
        public void ConvertsBetweenJsonAndLua()
        {
            JsonObject original = new JsonObject { ["a"] = 1, ["b"] = "text", ["c"] = new JsonArray(1, 2, 3), ["d"] = new JsonObject { ["e"] = true } };

            JsonNode back = LuaConvert.ToJson(LuaConvert.ToLua(original))!;

            Assert.AreEqual(original.ToJsonString(), back.ToJsonString());
        }
    }
}
