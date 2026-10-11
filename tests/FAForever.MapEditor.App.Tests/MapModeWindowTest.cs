using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Map mode's list in the real window, headless (wave 14A): its entries and buttons clicked as
    /// a person does, the chosen entry's fields in the inspector, every control labelled.
    /// </summary>
    [TestClass]
    public sealed class MapModeWindowTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            Assert.IsTrue((await model.Commands.RunAsync("map.mode", null, interactive: false)).Ran);
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            return (window, model, automation);
        }

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            return answer;
        }

        private static Control? InInspector(MainWindow window, string label) => window.GetVisualDescendants().OfType<Inspector>().Single()
            .GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Avalonia.Automation.AutomationProperties.GetName(control) == label && control.IsEffectivelyVisible);

        private static string[] Unlabelled(MainWindow window)
        {
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
            Check(UiTree.Describe(window, hidden: false));
            return [.. unlabelled];
        }

        [TestMethod]
        public Task TheEntriesShowTheirFieldsInTheInspector() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            ModePanel panel = window.GetVisualDescendants().OfType<ModePanel>().Single();
            Assert.IsFalse(panel.GetVisualDescendants().OfType<Control>().Any(control => control.IsEffectivelyVisible
                && Avalonia.Automation.AutomationProperties.GetName(control) is "Author" or "No-rush radius" or "Name of the chosen army"), "the panel lists; the inspector edits");
            Assert.IsNotNull(InInspector(window, "Author"), "Information is chosen first: its fields are in the inspector");
            Assert.IsNotNull(InInspector(window, "Preview of the map"));
            Assert.AreEqual("HardFFA.v0001", ((TextBlock)InInspector(window, "Folder of the map")!).Text, "the map's facts fold into Information");

            await Send(automation, "click", new JsonObject { ["label"] = "ARMY_2" });
            Assert.AreEqual("ARMY_2", ((TextBox)InInspector(window, "Name of the chosen army")!).Text);
            Assert.AreEqual("ARMY_2", ((TextBlock)InInspector(window, "Inspected part")!).Text);
            CollectionAssert.AreEqual(Array.Empty<string>(), Unlabelled(window));

            await Send(automation, "click", new JsonObject { ["label"] = "Size and playable area" });
            Assert.AreEqual("map.size", model.ActiveTool!.Definition.Id, "the entry chooses the Map size tool");
            StringAssert.StartsWith(((TextBlock)InInspector(window, "Size of the map now")!).Text, "5 km");
            CollectionAssert.AreEqual(Array.Empty<string>(), Unlabelled(window));

            await Send(automation, "click", new JsonObject { ["label"] = "Script and files" });
            Assert.IsNotNull(InInspector(window, "Script of the map"));
            window.Close();
        });

        [TestMethod]
        public Task ThePanelsButtonsAddAnArmyAndAnAreaWhoseEntriesShow() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            int armies = model.Map!.Document.Armies.Length;
            await Send(automation, "click", new JsonObject { ["label"] = "Add a playable army" });
            Assert.AreEqual(armies + 1, model.Map.Document.Armies.Length);
            string added = model.Map.Document.Armies[^1].Name;
            Assert.AreEqual($"map.army.{added}", model.MapParts.Parts.Chosen.Id);
            Assert.AreEqual(added, ((TextBox)InInspector(window, "Name of the chosen army")!).Text);

            await Send(automation, "click", new JsonObject { ["label"] = "Add an area" });
            Assert.AreEqual("map.area.AREA_1", model.MapParts.Parts.Chosen.Id);
            StringAssert.StartsWith(((TextBlock)InInspector(window, "Rectangle of the chosen area")!).Text, "96, 96 to 160, 160");
            Assert.IsNotNull(InInspector(window, "The chosen area is the playable area"));
            CollectionAssert.AreEqual(Array.Empty<string>(), Unlabelled(window));

            // another area chosen in the list is selected on the map with its handles, the entry in the inspector
            await Send(automation, "click", new JsonObject { ["label"] = "Add an area" });
            await Send(automation, "click", new JsonObject { ["label"] = "AREA_1" });
            CollectionAssert.AreEqual(new[] { ObjectRef.Area("AREA_1") }, model.Selection.Items.ToArray());
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);
            Assert.IsNotNull(InInspector(window, "Remove the chosen area"));

            await Send(automation, "click", new JsonObject { ["label"] = "Remove the chosen area" });
            Assert.IsFalse(model.Map.Document.Markers.Areas.Any(area => area.Name == "AREA_1"));
            Assert.AreEqual("map.area.AREA_2", model.MapParts.Parts.Chosen.Id);
            window.Close();
        });
    }
}
