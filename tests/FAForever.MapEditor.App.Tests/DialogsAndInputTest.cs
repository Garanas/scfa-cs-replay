using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The questions over the window, the tools' keys wherever the keyboard is, the brushes' keys
    /// and wheel, undo bringing back the selection, and the texts and reads that go with them.
    /// </summary>
    [TestClass]
    public sealed class DialogsAndInputTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static Task Run(Func<Task> test) => HeadlessApp.Run(test);

        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open(string map = HardFfa)
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(map);
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model, new AutomationServer(window, model, port: 0));
        }

        private static void Click(MainWindow window, Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None, string? symbol = null)
        {
            window.KeyPress(key, modifiers, physical, symbol);
            window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }

        private static Button ButtonReading(Visual root, string text) => root.GetVisualDescendants().OfType<Button>()
            .First(button => button.IsEffectivelyVisible && (button.Content as string == text || Avalonia.Automation.AutomationProperties.GetName(button) == text));

        private static void Modify(MainViewModel model) =>
            model.EditMap("A change", document => document with { Scenario = document.Scenario with { Description = "changed" } });

        [TestMethod]
        public Task TheUnsavedChangesQuestionShowsInTheWindowAndTakesTheKeyboard() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            Modify(model);
            Task<bool> closing = model.ConfirmCloseAsync();
            Dispatcher.UIThread.RunJobs();

            QuestionDialog dialog = window.GetVisualDescendants().OfType<QuestionDialog>().Single();
            Assert.IsTrue(dialog.IsEffectivelyVisible, "the question is part of the window");
            Assert.AreEqual("Unsaved changes", model.Question.Title);
            Button save = ButtonReading(dialog, "Save");
            Assert.IsTrue(save.IsFocused, "Save has the keyboard first");
            string? undo = model.Map!.Session.UndoDescription;

            Press(window, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
            Assert.AreEqual(undo, model.Map.Session.UndoDescription, "no shortcut behind the question");
            Assert.IsFalse(closing.IsCompleted);

            Press(window, Key.Escape, physical: PhysicalKey.Escape);
            Assert.IsFalse(await closing, "Escape cancels");
            Assert.IsFalse(model.Question.IsOpen);

            closing = model.ConfirmCloseAsync();
            Dispatcher.UIThread.RunJobs();
            Press(window, Key.Tab, physical: PhysicalKey.Tab);
            Assert.IsTrue(ButtonReading(dialog, "Don't save").IsFocused, "Tab goes to the next button");
            Press(window, Key.Enter, physical: PhysicalKey.Enter);
            Assert.IsTrue(await closing, "Enter pressed Don't save: the map may close");
            window.Close();
        });

        [TestMethod]
        public Task AConfirmationIsAQuestionInTheWindowWithCancelFirst() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            WindowDialogs dialogs = new WindowDialogs(window);
            Task<bool> asking = dialogs.ConfirmAsync("Upload to the vault", "Upload it?", "Upload");
            Dispatcher.UIThread.RunJobs();

            QuestionDialog dialog = window.GetVisualDescendants().OfType<QuestionDialog>().Single();
            Assert.IsTrue(ButtonReading(dialog, "Cancel").IsFocused, "Cancel has the keyboard first");
            Click(window, ButtonReading(dialog, "Upload"));
            Assert.IsTrue(await asking);
            window.Close();
        });

        [TestMethod]
        public Task AutomationReadsTheQuestionAndPressesItsButtons() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Modify(model);
            Task<bool> closing = model.ConfirmCloseAsync();
            Dispatcher.UIThread.RunJobs();

            JsonObject state = (JsonObject)await automation.HandleAsync("state", []);
            Assert.AreEqual("Unsaved changes", state["dialog"]!["title"]!.GetValue<string>());
            Assert.AreEqual(3, state["dialog"]!["buttons"]!.AsArray().Count);
            JsonObject ui = (JsonObject)await automation.HandleAsync("ui", new JsonObject { ["under"] = "Unsaved changes" });
            StringAssert.Contains(ui.ToJsonString(), "Cancel");

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => automation.HandleAsync("dialog", new JsonObject { ["button"] = "Maybe" }));
            JsonObject answer = (JsonObject)await automation.HandleAsync("dialog", new JsonObject { ["button"] = "don't save" });
            Assert.IsTrue(await closing);
            Assert.IsNull(answer["dialog"], "the question is answered");
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => automation.HandleAsync("dialog", new JsonObject { ["button"] = "Save" }));
            window.Close();
        });

        [TestMethod]
        public Task ClickByLabelClicksTheControlOneCanUse() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync("click", new JsonObject { ["label"] = "Curve", ["under"] = "Tools" });
            Assert.AreEqual("terrain.curve", model.ActiveTool?.Definition.Id);
            Assert.AreEqual("Button", answer["clicked"]!["type"]!.GetValue<string>());
            Assert.AreEqual("HeightmapView Heightmap", answer["clicked"]!["focused"]!.GetValue<string>(), "after a tool's button the view has the keyboard");
            window.Close();
        });

        [TestMethod]
        public Task TheToolsKeysWorkRightAfterItsButtonIsClicked() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.SelectMode("markers");
            Dispatcher.UIThread.RunJobs();
            Click(window, ButtonReading(window.GetVisualDescendants().OfType<ModePanel>().Single(), "Select"));
            Assert.AreEqual("markers.select", model.ActiveTool?.Definition.Id);
            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);

            Press(window, Key.Escape, physical: PhysicalKey.Escape);

            Assert.IsTrue(model.Selection.IsEmpty, "Escape went to the select tool: it selects nothing");
            window.Close();
        });

        [TestMethod]
        public Task AKeyNothingTookGoesToTheToolWhileACheckBoxHasTheKeyboard() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.SelectTool("terrain.sculpt");
            Dispatcher.UIThread.RunJobs();
            CheckBox limits = window.GetVisualDescendants().OfType<CheckBox>().First(box => box.Content as string == "Limits");
            limits.Focus();
            double radius = model.Tool<SculptTool>().Radius;

            Press(window, Key.OemCloseBrackets, physical: PhysicalKey.BracketRight, symbol: "]");

            Assert.IsTrue(model.Tool<SculptTool>().Radius > radius);
            window.Close();
        });

        [TestMethod]
        public Task BracketsAndControlWithTheWheelChangeTheBrush() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Radius = 10;
            sculpt.Strength = 1;
            window.MapView.Focus();
            Dispatcher.UIThread.RunJobs();

            Press(window, Key.OemCloseBrackets, physical: PhysicalKey.BracketRight, symbol: "]");
            Assert.AreEqual(12, sculpt.Radius);
            Press(window, Key.OemOpenBrackets, physical: PhysicalKey.BracketLeft, symbol: "[");
            Assert.AreEqual(10, sculpt.Radius);
            Press(window, Key.OemCloseBrackets, RawInputModifiers.Shift, PhysicalKey.BracketRight, "}");
            Assert.AreEqual(1.25, sculpt.Strength, 1e-6, "shift: the strength by a twentieth of 0.01 to 5");
            Assert.AreEqual("Brush strength: 1.25", model.Status);

            double zoom = window.MapView.Zoom;
            Point middle = window.MapView.TranslatePoint(new Point(window.MapView.Bounds.Width / 2, window.MapView.Bounds.Height / 2), window)!.Value;
            window.MouseMove(middle);
            window.MouseWheel(middle, new Vector(0, 1), RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(12, sculpt.Radius, "control and the wheel: the radius");
            Assert.AreEqual(zoom, window.MapView.Zoom, "and no zoom");

            model.SelectTool("paint.strata");
            StrataTool strata = model.Tool<StrataTool>();
            double before = strata.Radius;
            Press(window, Key.OemOpenBrackets, physical: PhysicalKey.BracketLeft, symbol: "[");
            Assert.IsTrue(strata.Radius < before, "every brush");
            window.Close();
        });

        [TestMethod]
        public void TheRadiusStepsByAFifthWithinTheSliders()
        {
            Assert.AreEqual(2, BrushKeys.RadiusStep(1, 1), "at least one ogrid");
            Assert.AreEqual(1, BrushKeys.RadiusStep(1, -1));
            Assert.AreEqual(120, BrushKeys.RadiusStep(100, 1));
            Assert.AreEqual(128, BrushKeys.RadiusStep(120, 1));
            Assert.AreEqual(0.01, BrushKeys.StrengthStep(0.03, -1, 0.01, 1));
            Assert.IsFalse(BrushKeys.Key(new SelectTool(null!, ObjectKinds.All), "OemOpenBrackets", ToolModifiers.None, out _), "a tool without a brush");
        }

        [TestMethod]
        public Task UndoOfADeleteSelectsWhatCameBackAndSaysWhatWasUndone() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            ObjectRef[] things = [.. model.ShownDocument!.Markers.Resources.Take(2).Select(resource => ObjectRef.Marker(resource.Id))];
            model.Selection.Set(things);
            await model.RunAsync("edit.delete");
            Assert.IsTrue(model.Selection.IsEmpty);

            Assert.IsTrue(model.Undo());
            CollectionAssert.AreEquivalent(things, model.Selection.Items.ToArray());
            StringAssert.StartsWith(model.Status, "Undone: ");

            Assert.IsTrue(model.Redo());
            Assert.IsTrue(model.Selection.IsEmpty, "redo: the selection the step left");
            StringAssert.StartsWith(model.Status, "Redone: ");
            window.Close();
        });

        [TestMethod]
        public Task ACommandThatSaysNothingLeavesItsTitleInTheStatusLine() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Resources[0].Id)]);
            model.Status = "Copied 1 marker";
            await model.RunAsync("edit.duplicate");
            StringAssert.StartsWith(model.Status, "Done: ");
            window.Close();
        });

        [TestMethod]
        public Task EachModePanelOpensAtItsTop() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            window.Height = 500;
            Dispatcher.UIThread.RunJobs();
            ScrollViewer scroller = window.GetVisualDescendants().OfType<ModePanel>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();
            scroller.Offset = new Vector(0, 200);
            Dispatcher.UIThread.RunJobs();
            Assert.AreNotEqual(0, scroller.Offset.Y, "the panel scrolled");

            model.SelectMode("paint");
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(0, scroller.Offset.Y);
            window.Close();
        });

        [TestMethod]
        public Task TheSunToolShowsTheAnglesBeforeTheFirstDrag() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.SelectTool("lighting.sun");
            Dispatcher.UIThread.RunJobs();
            StringAssert.Contains(model.Tool<SunTool>().Angles, "azimuth");
            window.Close();
        });

        [TestMethod]
        public Task ThePresetSectionsSayWhatTheyKeep() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            foreach (ModeDefinition mode in EditorModes.All)
            {
                foreach (PresetSection section in mode.Sections.OfType<PresetSection>())
                {
                    Assert.IsFalse(section.EmptyText.Contains("tool's settings", StringComparison.Ordinal), $"{mode.Id}: {section.EmptyText}");
                    Assert.AreNotEqual("Save the settings as a preset", section.SaveLabel, $"{mode.Id} {section.Kind}");
                }
            }
            model.SelectMode("units");
            Dispatcher.UIThread.RunJobs();
            Assert.IsNotNull(ButtonReading(window, "Save the selected units as a preset"));
            window.Close();
        });

        [TestMethod]
        public void AChoiceTheMapHasThatIsNoneOfTheChoicesStillShows()
        {
            ChoiceSetting setting = new ChoiceSetting("Clouds 2", "markers.setWeather", "cloudType", ["None", "Cirrus"], _ => "cumulus", (_, _) => Task.CompletedTask);
            setting.Refresh(Project.NewMap.Create(new Project.NewMapSettings()));
            Assert.AreEqual("cumulus", setting.Chosen);
            CollectionAssert.Contains(setting.Choices.ToArray(), "cumulus");

            ChoiceSetting known = new ChoiceSetting("Clouds 1", "markers.setWeather", "cloudType", ["None", "Cirrus"], _ => "cirrus", (_, _) => Task.CompletedTask);
            known.Refresh(Project.NewMap.Create(new Project.NewMapSettings()));
            Assert.AreEqual("Cirrus", known.Chosen, "a choice in another case is that choice");
            Assert.AreEqual(2, known.Choices.Count);
        }

        [TestMethod]
        public Task TheUiFindsTheLabelledControlAndKeepsUnderscores() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            StackPanel panel = new StackPanel();
            TextBlock label = new TextBlock { Text = "Combine" };
            ComboBox combo = new ComboBox { ItemsSource = new[] { new Choice("Add"), new Choice("Maximum") }, SelectedIndex = 1 };
            Avalonia.Automation.AutomationProperties.SetName(combo, "Combine");
            TextBlock texture = new TextBlock { Text = "rocklight_albedo" };
            panel.Children.AddRange([label, combo, texture]);
            Window other = new Window { Content = panel, Width = 400, Height = 300 };
            other.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.AreSame(combo, UiTree.Find(other, "Combine"));
            string ui = UiTree.Describe(other).ToJsonString();
            StringAssert.Contains(ui, "rocklight_albedo");
            StringAssert.Contains(ui, "\"selected\":\"Maximum\"");
            other.Close();
            window.Close();
        });

        [TestMethod]
        public Task TheChipsSayWhetherTheyAreOnAndSetTurnsThem() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject ui = (JsonObject)await automation.HandleAsync("ui", new JsonObject { ["under"] = "Show Markers" });
            Assert.IsTrue(ui["ui"]![0]!["checked"]!.GetValue<bool>());

            JsonObject answer = (JsonObject)await automation.HandleAsync("set", new JsonObject { ["label"] = "Show Markers", ["value"] = false, ["full"] = true });

            Assert.IsFalse(model.ShowMarkers);
            Assert.IsFalse(answer["overlays"]!["markers"]!.GetValue<bool>());
            window.Close();
        });

        [TestMethod]
        public Task ADragOnASplitterMovesIt() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Control splitter = (Control)UiTree.Find(window, "Resize the inspector")!;
            Point at = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, 300), window)!.Value;
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            await automation.HandleAsync("mouse", new JsonObject { ["x"] = at.X, ["y"] = at.Y, ["action"] = "drag", ["to"] = new JsonArray(at.X - 60, at.Y), ["steps"] = 4 });

            Assert.IsNotNull(model.Layout.InspectorWidth);
            Assert.IsTrue(model.Layout.InspectorWidth > 330, $"{model.Layout.InspectorWidth}");
            window.Close();
        });

        [TestMethod]
        public void TheTundraWeatherHasTheCloudsOfItsTemplate()
        {
            WeatherDefinition definition = WeatherStyles.NewDefinition("Weather", default);
            foreach (WeatherChance chance in definition.Types)
            {
                CollectionAssert.Contains(WeatherStyles.TypesOf(definition.MapStyle).ToArray(), chance.Type);
            }
        }

        private sealed record Choice(string Title);

        [TestMethod]
        public void TheProblemReportsPathHasOneKindOfSlash()
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "reports", Guid.NewGuid().ToString("N")).Replace('\\', '/');
            string path = Diagnostics.ProblemReport.Write(folder, null, new Diagnostics.ProblemReportContent("", 0, null, null, false), DateTimeOffset.Now);
            Assert.AreEqual(Path.GetFullPath(path), path);
            Assert.IsFalse(path.Contains('/') && path.Contains('\\'), path);
        }
    }
}
