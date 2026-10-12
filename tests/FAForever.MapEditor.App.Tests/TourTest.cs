using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Tours;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The guided tours (user story H-1): the editor's own tours written into the workspace's tours folder, read from
    /// it, and every step of every shipped tour played in the real window: the target found, shown and reachable by the
    /// mouse, the card inside the window, what a step waits for made true by Do it for me.
    /// </summary>
    [TestClass]
    public sealed class TourTest
    {
        private static string ToursFolder() => Path.Combine(AppContext.BaseDirectory, "tours", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Runs a task while drawing frames: the mouse hits what the last frame drew.
        /// </summary>
        internal static async Task<T> WithFrames<T>(Task<T> task)
        {
            while (!task.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.WhenAny(task, Task.Delay(5));
            }
            return await task;
        }

        internal static async Task WithFrames(Task task)
        {
            while (!task.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.WhenAny(task, Task.Delay(5));
            }
            await task;
        }

        internal static async Task<(MainWindow Window, MainViewModel Model, string Folder)> Open(int width = 1280, int height = 800, string? folder = null)
        {
            folder ??= ToursFolder();
            MainWindow window = new MainWindow { Width = width, Height = height };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            // the palette the Units tour places from: slot 3 is the Aeon Mass Extractor, as in the workspace's example palette
            PresetLibrary presets = new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N")));
            presets.Save(UnitPalettes.ToPreset("Aeon T1 base", UnitPalettes.SetSlot(UnitPalettes.Empty, 2, UnitPaletteSlot.OfUnit("uab1103"))));
            model.Presets.Use(presets);
            await model.OpenAsync(TestAssets.SandboxProject());
            string assets = TestAssets.Folder();
            string unit = Path.Combine(assets, "units", "uab1103", "uab1103_unit.json");
            Directory.CreateDirectory(Path.GetDirectoryName(unit)!);
            File.WriteAllText(unit, """{ "id": "UAB1103", "name": "Mass Extractor", "faction": "Aeon", "categories": ["AEON", "STRUCTURE"], "footprint": { "sizeX": 1, "sizeZ": 1 } }""");
            model.Assets.Use(AssetCatalog.Load(assets));
            window.DataContext = model;
            window.Show();
            model.Tour.Open(folder);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return (window, model, folder);
        }

        [TestMethod]
        public void TheEditorsTourIsWrittenIntoTheFolderOnceAndADeletedOneStaysDeleted()
        {
            string folder = ToursFolder();
            CollectionAssert.Contains(EditorTours.Names.ToList(), "window.tour.json");

            CollectionAssert.AreEqual(EditorTours.Names.ToList(), EditorTours.Ensure(folder).ToList(), "the first time everything is written");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "window.tour.json")));
            CollectionAssert.Contains(File.ReadAllLines(Path.Combine(folder, ".examples")).ToList(), "window.tour.json");
            Assert.AreEqual(0, EditorTours.Ensure(folder).Count, "nothing again");

            File.Delete(Path.Combine(folder, "window.tour.json"));
            Assert.AreEqual(0, EditorTours.Ensure(folder).Count, "a tour the person deleted is not written again");
            Assert.IsFalse(File.Exists(Path.Combine(folder, "window.tour.json")));

            string own = Path.Combine(folder, "own.tour.json");
            File.WriteAllText(own, "{}");
            Assert.AreEqual(0, EditorTours.Ensure(folder).Count, "a tour of one's own is left alone");
        }

        [TestMethod]
        public void ATourFileThatCannotBeReadIsListedWithItsErrorAndTheOthersStillWork()
        {
            string folder = ToursFolder();
            EditorTours.Ensure(folder);
            File.WriteAllText(Path.Combine(folder, "broken.tour.json"), "{ not json");
            File.WriteAllText(Path.Combine(folder, "refuses.tour.json"), """
                { "id": "refuses", "title": "Exports", "steps": [ { "id": "go", "title": "Go", "text": "Go.", "target": { "part": "dock" }, "until": { "path": "tab", "value": "3d" }, "doItForMe": { "run": "file.export" } } ] }
                """);
            File.WriteAllText(Path.Combine(folder, "unknown.tour.json"), """
                { "id": "unknown", "title": "Unknown", "steps": [ { "id": "go", "title": "Go", "text": "Go.", "before": [ { "run": "no.such.command" } ] } ] }
                """);

            IReadOnlyList<TourEntry> entries = TourCatalog.Load(folder);

            Assert.AreEqual("window", entries[0].Tour!.Id, "the tour that works comes first");
            Dictionary<string, string?> errors = entries.Where(entry => !entry.IsOk).ToDictionary(entry => Path.GetFileName(entry.File), entry => entry.Error);
            StringAssert.Contains(errors["broken.tour.json"], "not a tour");
            StringAssert.Contains(errors["refuses.tour.json"], "file.export");
        }

        [TestMethod]
        public Task TheModelListsErrorsAndReadsAgainWhenAFileChanges() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            File.WriteAllText(Path.Combine(folder, "unknown.tour.json"), """
                { "id": "unknown", "title": "Unknown", "steps": [ { "id": "go", "title": "Go", "text": "Go.", "before": [ { "run": "no.such.command" } ] } ] }
                """);
            model.Tour.Reload();

            TourEntry bad = model.Tour.Entries.Single(entry => !entry.IsOk);
            StringAssert.Contains(bad.Error, "no.such.command", "a command the editor lacks is an error of the tour");
            Assert.IsTrue(model.Tour.HasErrors);
            JsonObject state = model.Tour.Describe();
            Assert.AreEqual("unknown.tour.json", state["errors"]![0]!["file"]!.GetValue<string>());
            Assert.IsTrue(model.Diagnostics.Console.Any(entry => entry.Message.Contains("unknown.tour.json", StringComparison.Ordinal)), "the Console shows why the file cannot be used");

            File.WriteAllText(Path.Combine(folder, "unknown.tour.json"), """
                { "id": "unknown", "title": "Unknown", "steps": [ { "id": "go", "title": "Go", "text": "Go.", "before": [ { "run": "view.top" } ] } ] }
                """);
            for (int wait = 0; wait < 60 && model.Tour.HasErrors; wait++)
            {
                // the folder's watcher reports a changed file once it was quiet
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.IsFalse(model.Tour.HasErrors, "the file was read again after it changed");
            Assert.IsTrue(model.Tour.Entries.Any(entry => entry.Tour?.Id == "unknown"));
            window.Close();
        });

        [TestMethod]
        [DataRow(1280, 800)]
        [DataRow(1024, 700)]
        public Task EveryStepOfEveryShippedTourFindsItsTargetAndPlacesItsCard(int width, int height) => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open(width, height);
            Assert.IsFalse(model.Tour.HasErrors, string.Join("; ", model.Tour.Entries.Where(entry => !entry.IsOk).Select(entry => entry.Error)));
            Assert.IsTrue(model.Tour.Entries.Count >= 1);

            foreach (TourEntry entry in model.Tour.Entries)
            {
                Tour tour = entry.Tour!;
                for (int step = 0; step < tour.Steps.Count; step++)
                {
                    TourStep each = tour.Steps[step];
                    string at = $"{tour.Id} step {step + 1} ({each.Id}) at {width}x{height}";
                    await WithFrames(model.Tour.StartAsync(tour.Id, (step + 1).ToString()));

                    Assert.AreEqual(each.Id, model.Tour.Step!.Id, at);
                    Assert.IsTrue(model.Tour.Place.Found, $"{at}: the target {each.Target} is not found: {model.Tour.Place.Why}");
                    Assert.IsNull(model.Tour.Place.Why, $"{at}: the target {each.Target} cannot be reached by the mouse: {model.Tour.Place.Why}");
                    Rect all = new Rect(0, 0, width, height);
                    if (each.Target is not null)
                    {
                        Rect bounds = model.Tour.Place.Bounds ?? throw new AssertFailedException($"{at}: a target without an area");
                        Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0, $"{at}: the target has no area");
                        Assert.IsTrue(all.Contains(bounds.TopLeft) && all.Contains(bounds.BottomRight - new Point(0.01, 0.01)), $"{at}: the target {bounds} is outside the window");
                    }
                    Rect card = model.Tour.CardBounds ?? throw new AssertFailedException($"{at}: no card");
                    Assert.IsTrue(all.Contains(card.TopLeft) && all.Contains(card.BottomRight - new Point(0.01, 0.01)), $"{at}: the card {card} is not inside the window");
                    if (model.Tour.Place.Bounds is Rect target && target.Width < width / 2 && target.Height < height / 2)
                    {
                        Assert.IsFalse(card.Intersects(target), $"{at}: the card {card} covers the small target {target}");
                    }
                    Assert.IsFalse(string.IsNullOrWhiteSpace(each.Title) || string.IsNullOrWhiteSpace(each.Text), at);
                }
                await WithFrames(model.Tour.EndAsync());
            }
            window.Close();
        });

        [TestMethod]
        public Task DoItForMeMakesWhatAStepWaitsForTrueAndTheStepSaysSo() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            foreach (Tour tour in model.Tour.Entries.Select(entry => entry.Tour!))
            {
                for (int step = 0; step < tour.Steps.Count; step++)
                {
                    TourStep each = tour.Steps[step];
                    if (!each.Acts)
                    {
                        continue;
                    }
                    string at = $"{tour.Id} step {step + 1} ({each.Id})";
                    await WithFrames(model.Tour.StartAsync(tour.Id, (step + 1).ToString()));
                    Assert.IsFalse(model.Tour.IsDone, $"{at}: it waits for the person at first");
                    Assert.IsTrue(model.Tour.IsWaiting && model.Tour.HasDoItForMe, at);
                    Assert.AreEqual("Skip", model.Tour.NextText, at);

                    await WithFrames(model.Tour.DoItAsync());

                    Assert.IsTrue(model.Tour.IsDone, $"{at}: Do it for me did not make {each.Until!.Path} = {each.Until.Value} (problem: {model.Tour.Problem}; status: {model.Status}; value: {TourState.Read(model, each.Until.Path)})");
                    Assert.IsTrue(model.Tour.ShowsDone, at);
                    Assert.IsNull(model.Tour.Problem, $"{at}: {model.Tour.Problem}");
                    await WithFrames(model.Tour.EndAsync());
                }
            }
            window.Close();
        });

        [TestMethod]
        public Task AStepThatWaitsForAnEditWaitsForANewOneAndABrushStrokeIsOne() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            File.WriteAllText(Path.Combine(folder, "edits.tour.json"), """
                {
                  "id": "edits", "title": "Edits",
                  "steps": [
                    { "id": "stroke", "title": "Stroke", "text": "Make a stroke.", "until": { "path": "edits", "value": 1 },
                      "doItForMe": { "run": "brush.stroke", "points": [[100, 100], [112, 104], [124, 100]] } },
                    { "id": "raise", "title": "Raise", "text": "Raise the ground.", "until": { "path": "undo", "value": "Raise terrain" },
                      "doItForMe": { "run": "brush.stroke", "points": [[60, 60], [72, 64]] } },
                    { "id": "layer", "title": "Layer", "text": "Add a layer.", "until": { "path": "undo", "value": "Add the height layer Sculpt", "prefix": true },
                      "doItForMe": { "run": "terrain.addLayer", "kind": "sculpt" } }
                  ]
                }
                """);
            model.Tour.Reload();
            Assert.IsFalse(model.Tour.HasErrors, string.Join("; ", model.Tour.Entries.Where(entry => !entry.IsOk).Select(entry => entry.Error)));
            int edits = model.Map!.Session.Edits;

            // the stroke the person makes (the mouse: press, drag, release) is one edit and finishes the step
            await WithFrames(model.Tour.StartAsync("edits", "stroke"));
            Assert.IsFalse(model.Tour.IsDone);
            model.Stroke([(100, 100), (112, 104), (124, 100)]);
            Assert.IsTrue(model.Tour.Evaluate(), "a stroke is an edit made since the step began");
            Assert.AreEqual(edits + 1, model.Map.Session.Edits);
            Assert.AreEqual("Raise terrain", model.Map.Session.UndoDescription);

            // the map's last step already has the name the step waits for: only a new step finishes it
            await WithFrames(model.Tour.StartAsync("edits", "raise"));
            Assert.IsFalse(model.Tour.IsDone, "the last step of the map is a Raise terrain made before the step began");
            Assert.IsFalse(model.Tour.Evaluate());
            await WithFrames(model.Tour.DoItAsync());
            Assert.IsTrue(model.Tour.IsDone);
            Assert.AreEqual(edits + 2, model.Map.Session.Edits, "brush.stroke is one edit");
            Assert.AreEqual(2, model.Map.Session.History.Count(step => step.Description == "Raise terrain") , "and one step of undo each");

            // a layer named again is Sculpt2: the step waits for the beginning of the name
            await WithFrames(model.Tour.StartAsync("edits", "layer"));
            await WithFrames(model.Tour.DoItAsync());
            Assert.IsTrue(model.Tour.IsDone);
            await WithFrames(model.Tour.StartAsync("edits", "layer"));
            Assert.IsFalse(model.Tour.IsDone, "the last step is the layer added a moment ago");
            await WithFrames(model.Tour.DoItAsync());
            Assert.IsTrue(model.Tour.IsDone);
            StringAssert.StartsWith(model.Map.Session.UndoDescription, "Add the height layer Sculpt");

            await WithFrames(model.Tour.EndAsync());
            window.Close();
        });

        [TestMethod]
        public void AShippedTourIsWrittenOverAChangedCopyOnlyWhenRefreshIsAskedFor()
        {
            string folder = ToursFolder();
            EditorTours.Ensure(folder);
            string path = Path.Combine(folder, "terrain.tour.json");
            File.WriteAllText(path, "{ \"id\": \"terrain\", \"title\": \"Mine\", \"steps\": [] }");

            Assert.AreEqual(0, EditorTours.Ensure(folder).Count, "a changed copy is the person's");
            CollectionAssert.AreEqual(new[] { "terrain.tour.json" }, EditorTours.Ensure(folder, refresh: true).ToArray(), "a developer's refresh writes the shipped one again");
            Assert.AreEqual(EditorTours.Text("terrain.tour.json").Replace("\r\n", "\n"), File.ReadAllText(path).Replace("\r\n", "\n"));

            File.Delete(path);
            Assert.AreEqual(0, EditorTours.Ensure(folder, refresh: true).Count, "a tour the person deleted stays deleted");
        }

        [TestMethod]
        public void AConditionMayNameTheBeginningOfATextAndAFileThatSaysSoIsRead()
        {
            Tour tour = TourScript.Parse("""
                { "id": "p", "title": "P", "steps": [ { "id": "a", "title": "A", "text": "A.", "until": { "path": "undo", "value": "Add the", "prefix": true }, "doItForMe": { "run": "view.top" } } ] }
                """, out string? error)!;
            Assert.IsNull(error);
            Assert.IsTrue(tour.Steps[0].Until!.Prefix);
            Assert.IsTrue(TourState.Known("edits"));
        }

        [TestMethod]
        public void TheRefusedCommandsAreRefusedAndTheOthersAreNot()
        {
            string[] refused = ["file.upload", "file.export", "file.open", "file.import", "file.new", "file.push", "file.pull", "file.commit", "file.save", "file.enableVersionControl",
                "file.restoreCommit", "help.reportProblem", "diagnostics.upload", "workspace.convert", "help.workspaceFolder", "help.tour", "help.tourNext", "plugins.reload", "plugin.scatter-rocks.scatter"];
            foreach (string id in refused)
            {
                Assert.IsNotNull(TourRefusals.Why(id), $"{id} may not run in a tour");
            }
            foreach (string id in new[] { "view.3d", "view.top", "terrain.mode", "tool.terrain.sculpt", "view.togglePanel", "units.mode", "edit.undo", "plugins.manage", "help.shortcuts" })
            {
                Assert.IsNull(TourRefusals.Why(id), $"{id} is an ordinary command");
            }
            Assert.IsNotNull(TourScript.Parse("""{ "id": "a", "title": "A", "steps": [ { "id": "s", "title": "S", "text": "T" } ] }""", out string? fine), fine);
            Assert.IsNull(TourScript.Parse("""{ "id": "a", "title": "A", "start": [ { "run": "file.new" } ], "steps": [ { "id": "s", "title": "S", "text": "T" } ] }""", out string? error));
            StringAssert.Contains(error, "file.new");
        }

        [TestMethod]
        public Task AStepsCommandThatTheEditorRefusesAtRunTimeIsReportedOnTheCard() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            // an action whose command cannot run now (nothing is selected to delete) is not an error of the tour: it says why on the card
            File.WriteAllText(Path.Combine(folder, "cannot.tour.json"), """
                { "id": "cannot", "title": "Cannot", "steps": [ { "id": "go", "title": "Go", "text": "Go.", "before": [ { "run": "edit.redo" } ] } ] }
                """);
            model.Tour.Reload();

            await WithFrames(model.Tour.StartAsync("cannot"));

            Assert.IsTrue(model.Tour.IsRunning);
            StringAssert.Contains(model.Tour.Problem, "edit.redo");
            await WithFrames(model.Tour.EndAsync());
            window.Close();
        });

        [TestMethod]
        public Task FinishingATourIsKeptInTheWorkspaceAndTheOfferIsAnsweredOnce() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            Assert.IsTrue(model.Tour.OfferShown, "a map is open and the offer was not answered");
            Assert.IsTrue(model.Tour.Describe()["offer"]!["shown"]!.GetValue<bool>(), "the state says so");

            await model.Tour.AnswerOfferAsync(TourOffer.Later);
            Assert.IsFalse(model.Tour.OfferShown, "not now: gone for this session");
            Assert.IsFalse(File.Exists(Path.Combine(folder, ".progress.json")), "and nothing is remembered");

            (MainWindow window2, MainViewModel again, _) = await Open(folder: folder);
            Assert.IsTrue(again.Tour.OfferShown, "offered again at the next start");
            await WithFrames(again.Tour.AnswerOfferAsync(TourOffer.Start));
            Assert.IsTrue(again.Tour.IsRunning && again.Tour.Running!.Id == "window", "Start runs the tour");
            Assert.IsTrue(File.Exists(Path.Combine(folder, ".progress.json")), "Start is remembered in the workspace");
            Assert.IsFalse(again.Tour.OfferShown);

            await WithFrames(again.Tour.StartAsync("window", "end"));
            Assert.AreEqual("end", again.Tour.Step!.Id);
            await WithFrames(again.Tour.NextAsync());
            Assert.IsFalse(again.Tour.IsRunning, "Finish ends the tour");
            Assert.IsTrue(again.Tour.Progress.Finished.ContainsKey("window"));
            Assert.IsTrue(again.Tour.Items.Single(item => item.Id == "window").IsFinished, "the list shows the check");
            window2.Close();

            (MainWindow window3, MainViewModel third, _) = await Open(folder: folder);
            Assert.IsFalse(third.Tour.OfferShown, "Start was an answer: not offered again");
            Assert.IsTrue(third.Tour.Items.Single(item => item.Id == "window").IsFinished, "finished tours are read from the progress file");
            window3.Close();
            window.Close();
        });

        [TestMethod]
        public Task DontAskAgainIsRemembered() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            await model.Tour.AnswerOfferAsync(TourOffer.Never);
            Assert.IsFalse(model.Tour.OfferShown);
            Assert.IsTrue(TourProgress.Read(folder).Offered);
            window.Close();

            (MainWindow window2, MainViewModel again, _) = await Open(folder: folder);
            Assert.IsFalse(again.Tour.OfferShown);
            window2.Close();
        });

        [TestMethod]
        public Task AutomationStateSaysWhereTheTargetAndTheCardAreAndTheTourCommandsWork() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            async Task<JsonObject> Send(string command, JsonObject? arguments = null)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                JsonObject answer = (JsonObject)await WithFrames(automation.HandleAsync(command, arguments ?? []));
                Dispatcher.UIThread.RunJobs();
                return answer;
            }

            JsonObject before = (JsonObject)(await Send("state"))["tour"]!;
            Assert.IsFalse(before["running"]!.GetValue<bool>());
            Assert.IsTrue(before["offer"]!["shown"]!.GetValue<bool>(), "the offer shows in an editor started for automation too");
            Assert.AreEqual("window", before["tours"]![0]!["id"]!.GetValue<string>());

            JsonObject started = await Send("run", new JsonObject { ["id"] = "help.tour", ["args"] = new JsonObject { ["id"] = "window", ["step"] = 2 } });
            Assert.IsTrue(started["ran"]!.GetValue<bool>(), started.ToJsonString());
            JsonObject tour = (JsonObject)(await Send("state"))["tour"]!;
            Assert.AreEqual(2, tour["step"]!.GetValue<int>());
            Assert.AreEqual(12, tour["of"]!.GetValue<int>());
            Assert.AreEqual("modes", tour["stepId"]!.GetValue<string>());
            Assert.IsTrue(tour["target"]!["found"]!.GetValue<bool>());
            Assert.AreEqual(4, tour["target"]!["bounds"]!.AsArray().Count);
            Assert.AreEqual(4, tour["card"]!["bounds"]!.AsArray().Count);
            Assert.IsFalse(tour["offer"]!["shown"]!.GetValue<bool>(), "the offer goes while a tour runs");

            await Send("run", new JsonObject { ["id"] = "help.tourNext" });
            Assert.AreEqual(3, ((JsonObject)(await Send("state"))["tour"]!)["step"]!.GetValue<int>());
            await Send("run", new JsonObject { ["id"] = "help.tourBack" });
            Assert.AreEqual(2, ((JsonObject)(await Send("state"))["tour"]!)["step"]!.GetValue<int>());

            await Send("run", new JsonObject { ["id"] = "help.tour", ["args"] = new JsonObject { ["id"] = "window", ["step"] = "try-3d", ["card"] = false } });
            tour = (JsonObject)(await Send("state"))["tour"]!;
            Assert.IsTrue(tour["waiting"]!.GetValue<bool>());
            Assert.IsFalse(tour["card"]!["shown"]!.GetValue<bool>(), "card=false draws the ring only");
            Assert.IsNull(tour["card"]!["bounds"]);
            await Send("run", new JsonObject { ["id"] = "help.tourDo" });
            tour = (JsonObject)(await Send("state"))["tour"]!;
            Assert.IsTrue(tour["done"]!.GetValue<bool>());
            Assert.AreEqual("3d", (await Send("state", new JsonObject { ["path"] = "tab" }))["tab"]!.GetValue<string>());

            await Send("run", new JsonObject { ["id"] = "help.tourEnd" });
            Assert.IsFalse(((JsonObject)(await Send("state"))["tour"]!)["running"]!.GetValue<bool>());
            Assert.AreEqual("top", model.View == MapViewTab.Top ? "top" : "other", "the view is put back");
            window.Close();
        });

        [TestMethod]
        public Task ThePathsATourWaitsOnReadTheSameAsAutomationsState() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string folder) = await Open();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            model.SelectMode("props");
            await model.Commands.RunAsync("view.3d", null, interactive: false);
            Dispatcher.UIThread.RunJobs();
            JsonObject state = (JsonObject)await automation.HandleAsync("state", []);

            foreach (string path in new[] { "tab", "mode", "tool", "dock", "palette", "selection.count", "show.markers", "layout.modePanel", "layout.inspector", "layout.dock", "units.place.unit", "undo", "edits", "coverageStacks.shown" })
            {
                Assert.AreEqual(AutomationServer.At(state, path)?.ToJsonString(), TourState.Read(model, path)?.ToJsonString(), $"the path {path}");
            }
            foreach (JsonNode? row in (JsonArray)state["procedural"]!["rows"]!)
            {
                Assert.AreEqual(row!["state"]!.GetValue<string>(), TourState.Read(model, $"generated.{row["id"]!.GetValue<string>()}")!.GetValue<string>(), $"the result {row["id"]}");
            }
            model.SelectMode("paint");
            Dispatcher.UIThread.RunJobs();
            state = (JsonObject)await automation.HandleAsync("state", []);
            Assert.AreEqual("stratum-1", AutomationServer.At(state, "coverageStacks.shown")?.GetValue<string>());
            Assert.AreEqual("stratum-1", TourState.Read(model, "coverageStacks.shown")?.GetValue<string>());
            Assert.IsTrue(TourState.Known("show.grid") && TourState.Known("generated.walkability") && !TourState.Known("nothing.here"));
            window.Close();
        });

        [TestMethod]
        public void ATourFileIsReadWithItsActionsArguments()
        {
            Tour tour = TourScript.Parse("""
                {
                  // a comment, and a trailing comma
                  "id": "own", "title": "Own", "order": 3, "needs": ["Map"],
                  "start": [ { "run": "view.togglePanel", "panel": "inspector", "on": true }, { "run": "generate.auto", "args": { "id": "navigationMesh", "on": false } } ],
                  "steps": [ { "id": "a", "title": "A", "text": "A.", "target": { "label": "Modes", "type": "Button", "under": "Rail", "index": 2 }, "guide": { "more": "Longer.", "margin": 8, "skip": false, "after": true }, },
                             { "id": "b", "title": "B", "text": "B.", "target": { "ref": "unit:UNIT_1" } },
                             { "id": "c", "title": "C", "text": "C.", "target": { "x": 10, "z": 20 } } ]
                }
                """, out string? error)!;

            Assert.IsNull(error);
            Assert.AreEqual(3, tour.Order);
            Assert.AreEqual("inspector", tour.Start[0].Arguments()["panel"]!.GetValue<string>());
            Assert.IsTrue(tour.Start[0].Arguments()["on"]!.GetValue<bool>());
            Assert.AreEqual("navigationMesh", tour.Start[1].Arguments()["id"]!.GetValue<string>(), "an argument called id goes in args");
            Assert.AreEqual(2, tour.Steps[0].Target!.Index);
            Assert.AreEqual(8, tour.Steps[0].Guide!.Margin);
            Assert.AreEqual("unit:UNIT_1", tour.Steps[1].Target!.Ref);
            Assert.AreEqual(20, tour.Steps[2].Target!.Z);

            Assert.IsNull(TourScript.Parse("""{ "id": "a", "title": "A", "steps": [ { "id": "s", "title": "S", "text": "T", "untill": {} } ] }""", out error));
            StringAssert.Contains(error, "untill", "a misspelt field is an error, not ignored");
            Assert.IsNull(TourScript.Parse("""{ "id": "a", "title": "A", "steps": [ { "id": "s", "title": "S", "text": "T", "until": { "path": "tab", "value": "3d" } } ] }""", out error));
            StringAssert.Contains(error, "doItForMe", "a step that waits needs a way out");
            Assert.IsNull(TourScript.Parse("""{ "id": "a", "title": "A", "steps": [ { "id": "s", "title": "S", "text": "T", "until": { "path": "foo.bar", "value": 1 }, "doItForMe": { "run": "view.top" } } ] }""", out error));
            StringAssert.Contains(error, "foo.bar");
        }

        [TestMethod]
        public void TheCardLiesBesideTheTargetOnTheRightThenTheLeftBelowAboveAndAlwaysInsideTheWindow()
        {
            Size window = new Size(1280, 800);
            Size card = new Size(320, 200);

            Rect right = TourCardPlacement.Place(window, new Rect(0, 36, 52, 735), card, 14, 12);
            Assert.AreEqual(66, right.X, "right of the rail");

            Rect left = TourCardPlacement.Place(window, new Rect(980, 36, 300, 735), card, 14, 12);
            Assert.AreEqual(980 - 14 - 320, left.X, "left of the inspector: the right has no room");

            Rect below = TourCardPlacement.Place(window, new Rect(0, 4, 1280, 28), card, 14, 12);
            Assert.AreEqual(46, below.Y, "under the menu bar, which is as wide as the window");

            Rect above = TourCardPlacement.Place(window, new Rect(0, 771, 1280, 29), card, 14, 12);
            Assert.AreEqual(771 - 14 - 200, above.Y, "above the status bar");

            Rect middle = TourCardPlacement.Place(window, null, card, 14, 12);
            Assert.AreEqual(new Rect(480, 300, 320, 200), middle, "no target: the middle");

            Rect big = TourCardPlacement.Place(window, new Rect(337, 116, 638, 460), new Size(340, 300), 14, 12);
            Assert.IsTrue(new Rect(0, 0, 1280, 800).Contains(big.TopLeft) && new Rect(0, 0, 1280, 800).Contains(big.BottomRight), "a target too large for any side: inside the window");

            Rect corner = TourCardPlacement.Place(new Size(300, 200), new Rect(250, 150, 40, 40), new Size(320, 200), 14, 12);
            Assert.IsTrue(corner.Left >= 12 && corner.Top >= 12, "a window smaller than the card keeps it as far in as it can");
        }
    }
}
