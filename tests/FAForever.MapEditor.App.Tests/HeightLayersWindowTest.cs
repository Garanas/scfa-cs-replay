using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The height layers and curves in the window (user stories T-5, T-4): the panel and the
    /// inspector with their labels, and the curve tool with the mouse in the top view.
    /// </summary>
    [TestClass]
    public sealed class HeightLayersWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers { SaveChoice = SaveChoice.Discard });
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static void Click(MainWindow window, Point at, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.MouseMove(at, modifiers);
            window.MouseDown(at, MouseButton.Left, modifiers);
            window.MouseUp(at, MouseButton.Left, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        private static Point OnMap(MainWindow window, double x, double z) =>
            window.MapView.TranslatePoint(window.MapView.MapToControl(x, z), window)!.Value;

        [TestMethod]
        public Task ThePanelAndInspectorShowTheLayersWithLabels() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("terrain");
            await model.Commands.RunAsync("terrain.addLayer", [], interactive: false);
            await model.Commands.RunAsync("terrain.addCurve", new JsonObject { ["points"] = new JsonArray(new JsonArray(40, 40), new JsonArray(90, 60)) }, interactive: false);
            Dispatcher.UIThread.RunJobs();

            List<string> unlabelled = [];
            void Check(JsonArray nodes)
            {
                foreach (JsonObject node in nodes.Cast<JsonObject>())
                {
                    string type = node["type"]!.GetValue<string>();
                    if (type is "Slider" or "ComboBox" or "TextBox" or "ListBox" or "Button" or "CheckBox" && node["label"] is null && node["text"] is null)
                    {
                        unlabelled.Add($"{type} at {node["bounds"]!.ToJsonString()}");
                    }
                    if (node["children"] is JsonArray children)
                    {
                        Check(children);
                    }
                }
            }
            JsonArray ui = UiTree.Describe(window, hidden: false);
            Check(ui);
            CollectionAssert.AreEqual(Array.Empty<string>(), unlabelled);
            string text = ui.ToJsonString();
            foreach (string label in new[] { "Height layers", "Strength", "Combine", "Mask", "Profile", "Falloff", "Bake the layers into the heightmap" })
            {
                StringAssert.Contains(text, $"\"label\":\"{label}\"", label);
            }
            int steps = model.History.Count;
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(steps, model.History.Count, "showing the fields changes nothing");

            // choosing a layer in the list makes it the sculpt tool's
            ListBox list = window.GetVisualDescendants().OfType<ListBox>().First(box => box.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(box) == "Height layers");
            list.SelectedItem = model.HeightStack.Items.Single(item => item.Name == "Sculpt");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("Sculpt", model.Map!.Session.SculptLayer);
            window.Close();
        });

        [TestMethod]
        public Task TheCurveToolDrawsAndMovesPointsWithTheMouse() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectTool("terrain.curve");
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();

            Click(window, OnMap(window, 60, 60));
            Click(window, OnMap(window, 120, 80));
            HeightCurve curve = model.Map!.Document.HeightStack.Layers[^1].Curve!;
            Assert.AreEqual(2, curve.Points.Length, "two clicks, two points");
            StringAssert.Contains(model.Map.Session.UndoDescription, "add a point", "the history says a point was added");
            Assert.AreEqual(60, curve.Points[0].X, 1.5);

            // dragging the second point moves it, one step of undo
            int steps = model.History.Count;
            Point from = OnMap(window, curve.Points[1].X, curve.Points[1].Z);
            Point to = OnMap(window, 150, 130);
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + (to - from) / 2);
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(steps + 1, model.History.Count);
            CurvePoint moved = model.Map.Document.HeightStack.Layers[^1].Curve!.Points[1];
            Assert.AreEqual(150, moved.X, 1.5);
            Assert.AreEqual(130, moved.Z, 1.5);

            // control and a click removes it
            Click(window, to, RawInputModifiers.Control);
            Assert.AreEqual(1, model.Map.Document.HeightStack.Layers[^1].Curve!.Points.Length);
            window.Close();
        });
    }
}
