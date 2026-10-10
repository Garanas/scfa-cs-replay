using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.App.Commands;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The window's shortcuts and the keyboard: a text field takes its own keys first, a page takes
    /// no shortcut and closes on Escape.
    /// </summary>
    [TestClass]
    public sealed class ShellKeysTest
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

        private static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None, string? symbol = null)
        {
            window.KeyPress(key, modifiers, physical, symbol);
            window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }

        private static TextBox Field(Visual root, string label) => root.GetVisualDescendants().OfType<TextBox>()
            .First(box => box.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(box) == label);

        [TestMethod]
        public Task CtrlAInATextFieldSelectsItsText() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.NewMapPage.Open();
            Dispatcher.UIThread.RunJobs();
            TextBox name = Field(window, "Name of the new map");
            name.Focus();
            Dispatcher.UIThread.RunJobs();

            Press(window, Key.A, RawInputModifiers.Control, PhysicalKey.A);
            Assert.AreEqual(0, model.Selection.Count, "nothing selected behind the page");
            Assert.AreEqual(name.Text!.Length, name.SelectionEnd - name.SelectionStart, "the field's text is selected");
            window.Close();
        });

        [TestMethod]
        public Task ATextFieldOfAPanelKeepsItsEditingKeysAndShortcutsWorkElsewhere() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            Modify(model);
            TextBox field = window.GetVisualDescendants().OfType<TextBox>().First(box => box.IsEffectivelyVisible && box.IsEffectivelyEnabled);
            field.Focus();
            field.Text = "abc";
            Dispatcher.UIThread.RunJobs();
            string? undo = model.Map!.Session.UndoDescription;

            Press(window, Key.A, RawInputModifiers.Control, PhysicalKey.A);
            Assert.AreEqual(0, model.Selection.Count, "Ctrl+A is the field's");
            Assert.AreEqual(3, field.SelectionEnd - field.SelectionStart);
            Press(window, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
            Assert.AreEqual(undo, model.Map.Session.UndoDescription, "Ctrl+Z is the field's too");
            Press(window, Key.F2, physical: PhysicalKey.F2);
            Assert.AreEqual("terrain", model.SelectedMode?.Definition.Id, "a function key types nothing: its shortcut runs");

            window.FocusShownView();
            Dispatcher.UIThread.RunJobs();
            Press(window, Key.A, RawInputModifiers.Control, PhysicalKey.A);
            Assert.IsTrue(model.Selection.Count > 0, "with the view's keyboard Ctrl+A selects all");
            Press(window, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
            Assert.AreNotEqual(undo, model.Map.Session.UndoDescription, "and Ctrl+Z undoes");
            window.Close();
        });

        [TestMethod]
        [DataRow("versionControl")]
        [DataRow("diagnostics")]
        [DataRow("plugins")]
        [DataRow("report")]
        [DataRow("newMap")]
        public Task APageTakesNoShortcutsAndEscapeClosesIt(string page) => Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Modify(model);
            window.FocusShownView();
            Dispatcher.UIThread.RunJobs();
            string? undo = model.Map!.Session.UndoDescription;
            string? mode = model.SelectedMode?.Definition.Id;
            switch (page)
            {
                case "versionControl": model.VersionControl.IsOpen = true; break;
                case "diagnostics": model.Diagnostics.IsOpen = true; break;
                case "plugins": model.Plugins.IsOpen = true; break;
                case "report": model.Report.IsOpen = true; break;
                default: model.NewMapPage.Open(); break;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(page, model.OpenPageName);

            Press(window, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
            Press(window, Key.F7, physical: PhysicalKey.F7);
            Press(window, Key.A, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.A);
            Press(window, Key.Delete, physical: PhysicalKey.Delete);
            Assert.AreEqual(undo, model.Map.Session.UndoDescription, "no undo behind the page");
            Assert.AreEqual(mode, model.SelectedMode?.Definition.Id, "no mode behind the page");
            Assert.AreEqual(page, model.OpenPageName, "the page stays");

            Press(window, Key.Escape, physical: PhysicalKey.Escape);
            Assert.IsFalse(model.IsPageOpen, "Escape closes the page");
            Assert.AreEqual(0, model.Selection.Count);
            Dispatcher.UIThread.RunJobs();
            Press(window, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
            Assert.AreNotEqual(undo, model.Map.Session.UndoDescription, "the shortcuts work again");
            window.Close();
        });

        [TestMethod]
        public Task EnterInTheNewMapPagesNamePressesCreate() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.NewMapPage.Open();
            Dispatcher.UIThread.RunJobs();
            TextBox name = Field(window, "Name of the new map");
            Assert.IsTrue(name.IsFocused || window.FocusManager?.GetFocusedElement() is Control, "the page has the keyboard");
            name.Focus();
            name.Text = "Entered";
            Dispatcher.UIThread.RunJobs();

            Press(window, Key.Enter, physical: PhysicalKey.Enter);
            for (int i = 0; i < 100 && model.Map?.Document.Scenario.Name != "Entered"; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.AreEqual("Entered", model.Map!.Document.Scenario.Name);
            Assert.IsFalse(model.NewMapPage.IsOpen);
            window.Close();
        });

        [TestMethod]
        public Task TheTopViewFitsANewlyOpenedLargeMapColouredOnThePool() => Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            double small = window.MapView.Zoom;
            await model.Commands.RunAsync("file.new", new System.Text.Json.Nodes.JsonObject { ["name"] = "Large", ["size"] = 1024, ["players"] = 2 }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            for (int i = 0; i < 300 && window.MapView.IsColouring; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(window.MapView.IsColouring);
            Assert.AreEqual(small / 4, window.MapView.Zoom, small / 100, "a map four times as wide shows whole at a quarter of the zoom");
            Point centre = window.MapView.ControlToMap(new Point(window.MapView.Bounds.Width / 2, window.MapView.Bounds.Height / 2));
            Assert.AreEqual(512, centre.X, 2, "its middle in the middle");
            Assert.AreEqual(512, centre.Y, 2);
            window.Close();
        });

        [TestMethod]
        public Task SaveAsATemplateAsksForTheNameAndWhatInTheWindow() => Run(async () =>
        {
            string root = Path.Combine(AppContext.BaseDirectory, "workspaces", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "game"));
            Directory.CreateDirectory(Path.Combine(root, "vault"));
            FAForever.MapEditor.Project.Workspaces.Workspace workspace = FAForever.MapEditor.Project.Workspaces.Workspace.Create(Path.Combine(root, "workspace"), Path.Combine(root, "game"), null, Path.Combine(root, "vault"));
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open(TestAssets.SandboxProject());
            model.Setup.Use(workspace);

            // from the sandbox, whose own name a template cannot have: a name is suggested
            Task running = model.RunAsync("file.saveAsTemplate");
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.Question.IsOpen, "asked over the window");
            Assert.AreEqual("My Sandbox", model.Question.FieldText);
            TextBox field = Field(window, "Name of the template");
            Assert.IsTrue(field.IsFocused, "the name has the keyboard");
            Press(window, Key.A, RawInputModifiers.Control, PhysicalKey.A);
            Assert.AreEqual(field.Text!.Length, field.SelectionEnd - field.SelectionStart);
            field.Text = "Sandbox";
            Press(window, Key.Enter, physical: PhysicalKey.Enter);
            Assert.IsTrue(model.Question.IsOpen, "asked again: the sandbox's own name");
            StringAssert.Contains(model.Question.Message, "the editor's own template");
            field.Text = "Practice";
            Press(window, Key.Enter, physical: PhysicalKey.Enter);
            await running;
            Assert.IsTrue(File.Exists(Path.Combine(workspace.TemplatesFolder, "Practice.json")), "Enter saves the settings");

            running = model.RunAsync("file.saveAsTemplate");
            Dispatcher.UIThread.RunJobs();
            System.Text.Json.Nodes.JsonObject state = (System.Text.Json.Nodes.JsonObject)await automation.HandleAsync("state", []);
            Assert.AreEqual("Name of the template", state["dialog"]!["field"]!["label"]!.GetValue<string>());
            await automation.HandleAsync("dialog", new System.Text.Json.Nodes.JsonObject { ["text"] = "Whole practice", ["button"] = "Save the whole map" });
            await running;
            Assert.IsTrue(Directory.Exists(Path.Combine(workspace.TemplatesFolder, "Whole practice")), "the whole map in a folder");

            running = model.RunAsync("file.saveAsTemplate");
            Dispatcher.UIThread.RunJobs();
            Press(window, Key.Escape, physical: PhysicalKey.Escape);
            await running;
            Assert.AreEqual("Saving as a template cancelled", model.Status);

            // without a person it asks nothing: the map's name, or the arguments
            CommandRun quiet = await model.Commands.TryRunAsync("file.saveAsTemplate", new System.Text.Json.Nodes.JsonObject { ["name"] = "Quiet" }, interactive: false);
            Assert.IsTrue(quiet.Ran);
            window.Close();
        });

        private static void Modify(MainViewModel model) =>
            model.EditMap("A change", document => document with { Scenario = document.Scenario with { Description = "changed" } });
    }
}
