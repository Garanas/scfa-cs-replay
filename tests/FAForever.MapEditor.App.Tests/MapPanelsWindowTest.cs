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

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The panels of the map and its atmosphere in the real window, headless: every field has a
    /// label, a value typed and Enter changes the map, the areas tool draws in the top view and the
    /// page of a new map makes one.
    /// </summary>
    [TestClass]
    public sealed class MapPanelsWindowTest
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

        private static T Labelled<T>(MainWindow window, string label) where T : Control => window.GetVisualDescendants().OfType<T>()
            .First(control => control.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(control) == label);

        private static void Click(MainWindow window, Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        [TestMethod]
        [DataRow("map", "No-rush radius")]
        [DataRow("water", "Sun shininess")]
        [DataRow("lighting", "Azimuth")]
        [DataRow("paint", "Albedo scale")]
        public Task EveryFieldOfThePanelsHasALabel(string mode, string field) => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode(mode);
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
            Assert.AreEqual(1, model.History.Count, "showing the fields changes nothing: a slider that sets its own value gives nothing");
            StringAssert.Contains(ui.ToJsonString(), $"\"label\":\"{field}\"");
            window.Close();
        });

        [TestMethod]
        public Task AValueTypedAndEnterChangesTheMap() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("map");
            Dispatcher.UIThread.RunJobs();
            TextBox author = Labelled<TextBox>(window, "Author");
            Click(window, author);
            author.SelectAll();
            window.KeyTextInput("Jip");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("Jip", model.Map!.Document.Scenario.Author);
            Assert.AreEqual("Map author", model.History[^1].Description);
            window.Close();
        });

        [TestMethod]
        public Task TheAreasToolDrawsAnAreaInTheTopView() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectTool("map.areas");
            Dispatcher.UIThread.RunJobs();
            Control view = window.MapView;
            Point middle = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;
            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(middle + new Vector(30, 10));
            window.MouseMove(middle + new Vector(60, 40));
            window.MouseUp(middle + new Vector(60, 40), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(1, model.Map!.Document.Markers.Areas.Length, "drawn on empty ground");
            Assert.AreEqual(1, model.AreaList.Items.Count);
            Assert.AreEqual(8, model.Tool<Tools.AreaTool>().Marks!.Handles.Count, "the new area is selected, with its handles");
            Assert.AreEqual("Add the area AREA_1", model.History[^1].Description, "showing its fields in the inspector changes nothing");
            window.Close();
        });

        [TestMethod]
        public Task ThePageOfANewMapMakesOne() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await model.Commands.RunAsync("file.new");
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.GetVisualDescendants().OfType<NewMapPage>().Single().IsEffectivelyVisible);
            Labelled<TextBox>(window, "Name of the new map").Text = "Frozen lake";
            Click(window, window.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && Equals(button.Content, "Create")));

            Assert.AreEqual("Frozen lake", model.Map!.Document.Scenario.Name);
            Assert.AreEqual(512, model.Map.Document.Width);
            Assert.IsFalse(model.NewMapPage.IsOpen);
            window.Close();
        });
    }
}
