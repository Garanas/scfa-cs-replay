using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Point = Avalonia.Point;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Automation's commands run against the real window without a screen, as an agent sends them:
    /// the input goes through the window's own input (<see cref="WindowInput"/>), the same path the
    /// operating system's would take.
    /// </summary>
    [TestClass]
    public sealed class AutomationTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static Task Run(Func<Task> test) => HeadlessApp.Run(test);

        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(HardFfa);
            model.Assets.Use(TestAssets.Catalog());
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model, new AutomationServer(window, model, port: 0));
        }

        /// <summary>
        /// Sends a command after a frame: the mouse finds what lies under it in the last frame drawn,
        /// which a running editor draws by itself.
        /// </summary>
        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            Dispatcher.UIThread.RunJobs();
            return answer;
        }

        [TestMethod]
        public Task AClickOnAViewPointSelectsAndKeysTurnAndDelete() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.Top;
            model.SelectTool("markers.select");
            Dispatcher.UIThread.RunJobs();
            ObjectRef spawn = ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id);

            JsonObject point = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["ref"] = spawn.ToString() }))["point"]!;
            JsonObject top = (JsonObject)point["top"]!;
            Assert.IsTrue(top["inside"]!.GetValue<bool>(), "the whole map shows in the top view");
            await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = top["x"]!.DeepClone(), ["y"] = top["y"]!.DeepClone() });
            Assert.IsTrue(model.Selection.Contains(spawn), "a click on its pixel selects it");

            await Send(automation, "key", new JsonObject { ["key"] = "E" });
            Assert.AreEqual("Turn the selection", model.Map!.Session.UndoDescription, "E turns it: the view has the keyboard after the click");
            JsonObject answer = await Send(automation, "key", new JsonObject { ["key"] = "Delete" });
            Assert.IsFalse(model.ShownDocument!.Exists(spawn), "Delete removes it");
            Assert.IsTrue(answer["ok"]!.GetValue<bool>());
            window.Close();
        });

        [TestMethod]
        public Task AShortcutOpensThePaletteAndTextIsTypedIntoIt() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();

            await Send(automation, "key", new JsonObject { ["key"] = "K", ["modifiers"] = new JsonArray("Control") });
            Assert.IsTrue(model.Palette.IsOpen, "Ctrl+K, through the window's key bindings");
            await Send(automation, "type", new JsonObject { ["text"] = "undo" });
            Assert.AreEqual("undo", model.Palette.Query);
            await Send(automation, "key", new JsonObject { ["key"] = "Escape" });
            Assert.IsFalse(model.Palette.IsOpen);
            window.Close();
        });

        [TestMethod]
        public Task ACommandStartedWithoutWaitingOpensThePickerWhichKeysDrive() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("units");

            JsonObject started = await Send(automation, "run", new JsonObject { ["id"] = "units.pick", ["interactive"] = true, ["wait"] = false });
            Assert.AreEqual("units.pick", started["started"]!.GetValue<string>());
            Assert.IsFalse(started["finished"]!.GetValue<bool>(), "it waits for the person");
            Assert.IsTrue(model.Picker.IsOpen);
            await Send(automation, "type", new JsonObject { ["text"] = "snoop" });
            await Send(automation, "key", new JsonObject { ["key"] = "Enter" });

            Assert.IsFalse(model.Picker.IsOpen);
            Assert.AreEqual("Snoop", model.Assets.Shown?.Name, "Enter chose the one tile left");
            window.Close();
        });

        /// <summary>
        /// A group placed from a unit preset (user story U-9) in the state and in <c>get</c>, and a
        /// double click on one of its units in the top view selecting the whole instance.
        /// </summary>
        [TestMethod]
        public Task TheStateCarriesAPlacedInstanceAndADoubleClickSelectsIt() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.Presets.Use(new FAForever.MapEditor.Project.Workspaces.PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            model.View = MapViewTab.Top;
            model.SelectMode("units");
            foreach (int x in new[] { 60, 70 })
            {
                await Send(automation, "run", new JsonObject { ["id"] = "units.add", ["blueprint"] = "uec1101", ["x"] = x, ["z"] = 60, ["army"] = "ARMY_1", ["group"] = "INITIAL/Town" });
            }
            await Send(automation, "run", new JsonObject { ["id"] = "units.saveGroupAsPreset", ["army"] = "ARMY_1", ["group"] = "INITIAL/Town", ["name"] = "Village" });
            JsonObject placed = await Send(automation, "run", new JsonObject { ["id"] = "units.placePreset", ["name"] = "Village", ["x"] = 120, ["z"] = 120, ["army"] = "ARMY_2" });
            string unit = placed["result"]!["units"]![0]!.GetValue<string>();

            JsonObject state = await Send(automation, "state");
            JsonObject group = state["units"]!["tree"]!["groups"]!.AsArray().Select(node => (JsonObject)node!).Single(node => node["group"]!.GetValue<string>() == "INITIAL/Village");
            Assert.AreEqual("Village", group["instance"]!["preset"]!.GetValue<string>());
            Assert.AreEqual("Current", group["instance"]!["state"]!.GetValue<string>());
            Assert.IsNull(state["units"]!["tree"]!["groups"]!.AsArray().Select(node => (JsonObject)node!).Single(node => node["group"]!.GetValue<string>() == "INITIAL/Town")["instance"], "a group made by hand has none");
            Assert.AreEqual(1, state["presets"]!["instances"]!["Village"]!["placed"]!.GetValue<int>());
            JsonObject described = (JsonObject)(await Send(automation, "get", new JsonObject { ["ref"] = $"unit:{unit}" }))["object"]!;
            Assert.AreEqual("Village", described["instance"]!["preset"]!.GetValue<string>());
            Assert.AreEqual("INITIAL/Village", described["instance"]!["group"]!.GetValue<string>());

            // a double click on a unit of the instance selects the whole instance; on another unit, that unit alone
            await Send(automation, "tool", new JsonObject { ["id"] = "units.select" });
            JsonObject top = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["ref"] = $"unit:{unit}" }))["point"]!["top"]!;
            await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = top["x"]!.DeepClone(), ["y"] = top["y"]!.DeepClone(), ["action"] = "doubleClick" });
            Assert.AreEqual(2, model.Selection.Count, "the instance's two units");
            StringAssert.Contains(model.Status, "an instance of the preset Village");
            JsonObject town = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["ref"] = "unit:UNIT_1" }))["point"]!["top"]!;
            await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = town["x"]!.DeepClone(), ["y"] = town["y"]!.DeepClone(), ["action"] = "doubleClick" });
            Assert.AreEqual(1, model.Selection.Count, "a unit of a group made by hand");
            window.Close();
        });

        [TestMethod]
        public Task DataDraggedOntoTheTopViewIsDroppedThere() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();
            List<AssetDropEventArgs> drops = [];
            model.AssetDropped += (_, drop) =>
            {
                drops.Add(drop);
                drop.Handled = true;
            };
            Point at = window.MapView.MapToControl(100, 120);

            JsonObject answer = await Send(automation, "drag", new JsonObject
            {
                ["view"] = "top",
                ["from"] = new JsonArray(10, 10),
                ["to"] = new JsonArray(at.X, at.Y),
                ["data"] = "env/evergreen/props/rocks/rock01_prop.bp",
            });

            Assert.AreEqual("Copy", answer["dropped"]!.GetValue<string>());
            AssetDropEventArgs drop = drops.Single();
            Assert.AreEqual(100, drop.X, 0.01);
            Assert.AreEqual(120, drop.Z, 0.01);
            window.Close();
        });

        [TestMethod]
        public Task AViewPointIn3DIsWhereTheCameraShowsThePlace() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.ThreeD;
            Dispatcher.UIThread.RunJobs();
            model.Camera.Look(x: 128, z: 128, distance: 300, yaw: 30, pitch: 50);

            JsonObject point = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["x"] = 90, ["z"] = 140 }))["point"]!;
            JsonObject threeD = (JsonObject)point["3d"]!;
            Assert.IsTrue(threeD["inside"]!.GetValue<bool>());
            Size size = window.View3D.Bounds.Size;
            model.Camera.Aspect = (float)(size.Width / size.Height);
            Vector2 device = new Vector2((float)(2 * threeD["x"]!.GetValue<double>() / size.Width - 1), (float)(1 - 2 * threeD["y"]!.GetValue<double>() / size.Height));
            Vector3 back = model.Camera.PointAt(device)!.Value;

            Assert.AreEqual(90, back.X, 0.5, "the terrain under that pixel is the place");
            Assert.AreEqual(140, back.Z, 0.5);
            window.Close();
        });

        [TestMethod]
        public Task TheMouseWheelZoomsThe3DView() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.ThreeD;
            Dispatcher.UIThread.RunJobs();
            float before = model.Camera.Now.Distance;

            JsonObject answer = await Send(automation, "mouse", new JsonObject { ["view"] = "3d", ["x"] = 300, ["y"] = 200, ["action"] = "wheel", ["notches"] = 2 });
            for (int i = 0; i < 100 && model.Camera.Update(0.1f); i++)
            {
            }

            Assert.IsTrue(model.Camera.Now.Distance < before, $"the wheel moved the camera in: from {before} to {model.Camera.Now.Distance}, {answer["mouse"]!.ToJsonString()}");
            window.Close();
        });

        [TestMethod]
        public Task GetAndObjectsDescribeWhatLiesOnTheMap() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Spawn spawn = model.ShownDocument!.Markers.Spawns[0];

            JsonObject thing = (JsonObject)(await Send(automation, "get", new JsonObject { ["ref"] = $"marker:{spawn.Id}" }))["object"]!;
            Assert.AreEqual("Marker", thing["kind"]!.GetValue<string>());
            Assert.AreEqual(spawn.Name, thing["name"]!.GetValue<string>());
            Assert.AreEqual("Spawn", thing["type"]!.GetValue<string>());
            Assert.AreEqual(spawn.Position.X, thing["place"]![0]!.GetValue<double>(), 0.01);

            JsonObject objects = (JsonObject)(await Send(automation, "objects", new JsonObject
            {
                ["kind"] = "markers",
                ["near"] = new JsonArray(spawn.Position.X, spawn.Position.Z),
                ["count"] = 3,
            }))["objects"]!;
            Assert.AreEqual($"marker:{spawn.Id}", objects["items"]![0]!["ref"]!.GetValue<string>(), "the nearest first");
            Assert.AreEqual(3, objects["items"]!.AsArray().Count);
            Assert.IsTrue(objects["total"]!.GetValue<int>() >= 3);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => automation.HandleAsync("get", new JsonObject { ["ref"] = "prop:999999" }));
            window.Close();
        });

        [TestMethod]
        public Task TheStateHasTheViewsAndWhatIsLoading() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            JsonObject state = await Send(automation, "state");

            JsonObject views = (JsonObject)state["views"]!;
            Assert.AreEqual(1, views["scaling"]!.GetValue<double>());
            Assert.IsTrue(views["top"]!["visible"]!.GetValue<bool>());
            Assert.IsTrue(views["top"]!["bounds"]![2]!.GetValue<double>() > 100, "the top view's width");
            JsonObject loading = (JsonObject)state["loading"]!;
            Assert.IsTrue(loading["assetsLoaded"]!.GetValue<bool>());
            Assert.IsTrue(loading["assets"]!["Prop"]!.GetValue<int>() > 0);
            Assert.AreEqual(Environment.ProcessId, state["pid"]!.GetValue<int>());

            JsonObject settled = await Send(automation, "screenshot", new JsonObject
            {
                ["path"] = Path.Combine(AppContext.BaseDirectory, "automation-results", "settled.png"),
                ["settle"] = true,
            });
            Assert.IsTrue(settled["settled"]!["settled"]!.GetValue<bool>(), settled["settled"]!.ToJsonString());
            window.Close();
        });

        [TestMethod]
        public Task TheStateDescribesThePlaceToolsWayAndSettings() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            JsonObject before = (JsonObject)(await Send(automation, "state"))["units"]!["place"]!;
            Assert.AreEqual("Single", before["mode"]!.GetValue<string>());

            JsonObject set = await Send(automation, "run", new JsonObject { ["id"] = "units.placeMode", ["mode"] = "rectangle", ["hollow"] = true, ["spacing"] = 2.5, ["count"] = 9 });
            Assert.IsTrue(set["ran"]!.GetValue<bool>(), set.ToJsonString());
            JsonObject place = (JsonObject)(await Send(automation, "state"))["units"]!["place"]!;
            Assert.AreEqual("Rectangle", place["mode"]!.GetValue<string>());
            Assert.IsTrue(place["hollow"]!.GetValue<bool>());
            Assert.AreEqual(2.5, place["spacing"]!.GetValue<double>());
            Assert.AreEqual(9, place["count"]!.GetValue<int>());
            Assert.IsFalse(place["dragging"]!.GetValue<bool>());
            Assert.AreEqual("units.place", model.ActiveTool!.Definition.Id);

            // the segmented control's chips say which way is on
            JsonArray ui = (JsonArray)(await Send(automation, "ui", new JsonObject { ["under"] = "How to place" }))["ui"]!;
            Dictionary<string, bool> chips = [];
            void Collect(JsonArray nodes)
            {
                foreach (JsonObject node in nodes.Cast<JsonObject>())
                {
                    if (node["text"] is JsonNode text && node["checked"] is JsonNode on)
                    {
                        chips[text.GetValue<string>()] = on.GetValue<bool>();
                    }
                    if (node["children"] is JsonArray children)
                    {
                        Collect(children);
                    }
                }
            }
            Collect(ui);
            Assert.IsTrue(chips.GetValueOrDefault("Rectangle"), ui.ToJsonString());
            Assert.IsFalse(chips.GetValueOrDefault("Line", true));
            Assert.AreEqual(5, chips.Count, "Single, Line, Rectangle, Ring, Scatter");
            JsonObject clicked = await Send(automation, "click", new JsonObject { ["label"] = "Scatter with a brush" });
            Assert.AreEqual("Scatter", model.Tool<FAForever.MapEditor.App.Tools.UnitPlaceTool>().Mode.ToString(), clicked.ToJsonString());
            window.Close();
        });

        [TestMethod]
        public Task UiUnderGivesOnePartOfTheWindow() => Run(async () =>
        {
            (MainWindow window, _, AutomationServer automation) = await Open();
            JsonArray all = (JsonArray)(await Send(automation, "ui"))["ui"]!;
            JsonArray part = (JsonArray)(await Send(automation, "ui", new JsonObject { ["under"] = "MainMenu" }))["ui"]!;

            Assert.AreEqual(1, part.Count, "the menu and what lies in it");
            Assert.AreEqual("MainMenu", part[0]!["name"]!.GetValue<string>());
            Assert.IsTrue(part.ToJsonString().Length < all.ToJsonString().Length / 2);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => automation.HandleAsync("ui", new JsonObject { ["under"] = "nothing by this name" }));
            window.Close();
        });

        [TestMethod]
        [DataRow("E", Key.E, RawInputModifiers.None)]
        [DataRow("delete", Key.Delete, RawInputModifiers.None)]
        [DataRow("Esc", Key.Escape, RawInputModifiers.None)]
        [DataRow("5", Key.D5, RawInputModifiers.None)]
        [DataRow("F5", Key.F5, RawInputModifiers.None)]
        [DataRow("Ctrl+K", Key.K, RawInputModifiers.Control)]
        [DataRow("Ctrl+Shift+Z", Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift)]
        public void KeysAreReadByNameOrAsGestures(string text, Key key, RawInputModifiers modifiers)
        {
            Assert.AreEqual((key, modifiers), WindowInput.ParseKey(text));
        }

        [TestMethod]
        public Task ACommandThatCannotRunSaysWhy() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();

            JsonObject answer = await Send(automation, "undo");

            Assert.IsFalse(answer["ran"]!.GetValue<bool>());
            StringAssert.Contains(answer["why"]!.GetValue<string>(), "nothing to undo");
            TestCommands.RegisterNotBuilt(model);
            JsonObject notBuilt = await Send(automation, "run", new JsonObject { ["id"] = TestCommands.NotBuiltId });
            StringAssert.Contains(notBuilt["why"]!.GetValue<string>(), "not built yet");
            window.Close();
        });

        [TestMethod]
        public Task ZoomingTheTopViewKeepsOrMovesItsCentre() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();

            await Send(automation, "view", new JsonObject { ["x"] = 40, ["z"] = 60, ["zoom"] = 4 });
            Point centre = window.MapView.Centre;
            Assert.AreEqual(40, centre.X, 0.01);
            Assert.AreEqual(60, centre.Y, 0.01);

            await Send(automation, "view", new JsonObject { ["zoom"] = 2 });
            Assert.AreEqual(2, window.MapView.Zoom);
            Assert.AreEqual(40, window.MapView.Centre.X, 0.01, "the zoom alone keeps the place in the middle");
            Assert.AreEqual(60, window.MapView.Centre.Y, 0.01);

            window.Height = 700;
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(60, window.MapView.Centre.Y, 0.01, "a smaller view stays centred on it");

            JsonObject point = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["x"] = 40, ["z"] = 60 }))["point"]!;
            Assert.IsNull(point["3d"], "the 3D view never shown has no size, so no point (and the answer is valid JSON)");
            window.Close();
        });

        [TestMethod]
        public Task ManyPropsAreDrawnSparseWhenZoomedOutAndStillPicked() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.Top;
            // a forest: 4000 props on 40 by 40 ogrids
            List<Prop> forest = [.. Enumerable.Range(0, 4000).Select(i => new Prop
            {
                Id = 0,
                Blueprint = "/env/evergreen/props/rocks/rock01_prop.bp",
                Position = new Vector3(60 + i % 63 * 0.63f, 20, 60 + i / 63 * 0.63f),
                Scale = Vector3.One,
            })];
            model.EditMap("A forest", document => document.AddProps(forest, out _));
            await Send(automation, "show", new JsonObject { ["props"] = true });

            await Send(automation, "view", new JsonObject { ["x"] = 80, ["z"] = 80, ["zoom"] = 1 });
            await Send(automation, "state"); // a frame drawn at that zoom
            int sparse = window.MapView.PropDotsDrawn;
            int props = model.ShownDocument!.Props.Length;
            Assert.IsTrue(sparse > 0 && sparse < props / 3, $"zoomed out a dot per few pixels: {sparse} of {props}");

            await Send(automation, "view", new JsonObject { ["x"] = 80, ["z"] = 80, ["zoom"] = 8 });
            await Send(automation, "state");
            Assert.IsTrue(window.MapView.PropDotsDrawn > 3000, $"zoomed in every prop in the view has its dot: {window.MapView.PropDotsDrawn}");

            model.SelectTool("props.select");
            await Send(automation, "view", new JsonObject { ["x"] = 80, ["z"] = 80, ["zoom"] = 1 });
            JsonObject point = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["x"] = 70, ["z"] = 70 }))["point"]!;
            await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = point["top"]!["x"]!.DeepClone(), ["y"] = point["top"]!["y"]!.DeepClone() });
            Assert.AreEqual(1, model.Selection.Count, "a click still picks a prop, whatever dot shows");
            window.Close();
        });

        [TestMethod]
        public Task AKeyHeldDownGoesWithTheMouseUntilItIsLetGo() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.Top;
            model.SelectTool("markers.select");
            Dispatcher.UIThread.RunJobs();
            async Task ClickOn(ObjectRef thing)
            {
                JsonObject top = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["ref"] = thing.ToString() }))["point"]!["top"]!;
                await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = top["x"]!.DeepClone(), ["y"] = top["y"]!.DeepClone() });
            }
            ObjectRef first = ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id);
            ObjectRef second = ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[1].Id);
            ObjectRef third = ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[2].Id);

            await ClickOn(first);
            JsonObject down = await Send(automation, "key", new JsonObject { ["key"] = "Shift", ["action"] = "down", ["paths"] = new JsonArray("views.keysHeld") });
            CollectionAssert.AreEqual(new[] { "LeftShift" }, ((JsonArray)down["held"]!).Select(key => key!.GetValue<string>()).ToArray());
            Assert.AreEqual("LeftShift", down["views.keysHeld"]![0]!.GetValue<string>(), "the state says what is held");
            await ClickOn(second);
            CollectionAssert.AreEquivalent(new[] { first, second }, model.Selection.Items.ToArray(), "shift held: the click adds");

            JsonObject up = await Send(automation, "key", new JsonObject { ["key"] = "Shift", ["action"] = "up" });
            Assert.AreEqual(0, ((JsonArray)up["held"]!).Count);
            await ClickOn(third);
            CollectionAssert.AreEqual(new[] { third }, model.Selection.Items.ToArray(), "let go: the click replaces");
            window.Close();
        });

        [TestMethod]
        public Task ThingsAreFoundByNameAndWaitPollsTheState() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Spawn spawn = model.ShownDocument!.Markers.Spawns[0];

            JsonObject got = (JsonObject)(await Send(automation, "get", new JsonObject { ["name"] = spawn.Name.ToLowerInvariant() }))["object"]!;
            Assert.AreEqual(ObjectRef.Marker(spawn.Id).ToString(), got["ref"]!.GetValue<string>(), "by its name, ignoring case");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => automation.HandleAsync("get", new JsonObject { ["name"] = "nothing by this name" }));

            JsonArray masses = (JsonArray)(await Send(automation, "objects", new JsonObject { ["kind"] = "markers", ["name"] = "mass", ["count"] = 1000 }))["objects"]!["items"]!;
            Assert.AreEqual(model.ShownDocument.Markers.Resources.Count(resource => resource.Name.Contains("Mass", StringComparison.OrdinalIgnoreCase)), masses.Count);
            Assert.IsTrue(masses.All(item => item!["what"]!.GetValue<string>().Contains("Mass", StringComparison.OrdinalIgnoreCase)));

            JsonObject state = await Send(automation, "state");
            Assert.IsFalse(string.IsNullOrEmpty(state["build"]!["version"]?.GetValue<string>()), "the build's version");
            Assert.IsTrue(DateTime.TryParse(state["build"]!["built"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _), "and when it was built");

            model.Selection.Set([ObjectRef.Marker(spawn.Id)]);
            JsonObject matched = (JsonObject)(await Send(automation, "wait", new JsonObject { ["path"] = "selection.count", ["value"] = 1 }))["wait"]!;
            Assert.IsTrue(matched["matched"]!.GetValue<bool>());
            Assert.AreEqual(1, matched["reads"]!.GetValue<int>(), "already so: one read");
            JsonObject stage = (JsonObject)(await Send(automation, "wait", new JsonObject { ["path"] = "stage", ["value"] = model.Setup.Stage.ToString() }))["wait"]!;
            Assert.IsTrue(stage["matched"]!.GetValue<bool>(), "a text");

            Task<JsonNode> waiting = automation.HandleAsync("wait", new JsonObject { ["path"] = "selection.count", ["value"] = 2, ["timeout"] = 5 });
            model.Selection.Apply([ObjectRef.Marker(model.ShownDocument.Markers.Spawns[1].Id)], SelectionChange.Add);
            JsonObject later = (JsonObject)(await waiting)["wait"]!;
            Assert.IsTrue(later["matched"]!.GetValue<bool>(), "it polls until the value matches");

            JsonObject never = (JsonObject)(await Send(automation, "wait", new JsonObject { ["path"] = "selection.count", ["value"] = 99, ["timeout"] = 0.3 }))["wait"]!;
            Assert.IsFalse(never["matched"]!.GetValue<bool>(), "the time ran out");
            Assert.AreEqual(2, never["value"]!.GetValue<int>(), "with the value last read");
            window.Close();
        });

        [TestMethod]
        public Task TheStateHasTheUnitPalettesAndASlotIsPickedByCommand() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.Assets.Use(UnitsTest.Catalog());
            model.Presets.Use(UnitPalettesTest.Library());
            model.SelectTool("units.place");

            JsonObject palette = (JsonObject)((JsonObject)(await Send(automation, "state"))["units"]!)["palette"]!;
            CollectionAssert.AreEqual(new[] { "Base", "Other" }, ((JsonArray)palette["names"]!).Select(name => name!.GetValue<string>()).ToArray());
            Assert.AreEqual("Base", palette["chosen"]!.GetValue<string>());
            Assert.IsNull(palette["chosenSlot"]);
            JsonArray slots = (JsonArray)palette["slots"]!;
            Assert.AreEqual(10, slots.Count);
            Assert.AreEqual("uec1201", slots[0]!["unit"]!.GetValue<string>());
            Assert.AreEqual("Village", slots[1]!["preset"]!.GetValue<string>());
            Assert.AreEqual("0", slots[9]!["key"]!.GetValue<string>());

            JsonObject picked = (JsonObject)(await Send(automation, "run", new JsonObject { ["id"] = "units.slot", ["n"] = 1 }))["result"]!;
            Assert.IsTrue(picked["picked"]!.GetValue<bool>());
            Assert.AreEqual("uec1201", model.Tool<FAForever.MapEditor.App.Tools.UnitPlaceTool>().Unit?.Key);
            JsonObject again = (JsonObject)((JsonObject)(await Send(automation, "state"))["units"]!)["palette"]!;
            Assert.AreEqual(0, again["chosenSlot"]!.GetValue<int>());
            Assert.IsTrue(((JsonArray)again["slots"]!)[0]!["chosen"]!.GetValue<bool>());
            window.Close();
        });
    }
}
