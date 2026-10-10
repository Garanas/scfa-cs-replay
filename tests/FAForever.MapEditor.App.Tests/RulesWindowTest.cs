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
    /// The rule editors in the real window, headless (user stories K-2, P-4): a mask's Rules...
    /// button opens its rules in the inspector, the Add buttons add rules, every field has a label,
    /// and showing the fields changes nothing.
    /// </summary>
    [TestClass]
    public sealed class RulesWindowTest
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
            control.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static List<string> Unlabelled(MainWindow window)
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
            return unlabelled;
        }

        private static async Task Settle()
        {
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
        }

        [TestMethod]
        public Task TheRulesButtonOfAMaskOpensItsRulesAndAddAddsOne() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await model.Commands.RunAsync("paint.addMask", new JsonObject { ["name"] = "forest" }, interactive: false);
            model.SelectMode("paint");
            Dispatcher.UIThread.RunJobs();

            Click(window, Labelled<Button>(window, "Rules of the mask forest"));
            await Settle();
            Assert.AreEqual("forest", model.MaskRules.Editing);

            int steps = model.History.Count;
            Click(window, Labelled<Button>(window, "Add a rule of the kind chosen"));
            await Settle();
            Assert.AreEqual(1, model.Map!.Document.MaskRules["forest"].Rules.Length, "a slope rule, the first kind");
            Assert.AreEqual(steps + 1, model.History.Count);
            Labelled<TextBox>(window, "From");
            Labelled<Button>(window, "Generate the masks from their rules");

            Click(window, Labelled<Button>(window, "Generate the masks from their rules"));
            await Settle();
            Assert.AreEqual(steps + 2, model.History.Count, "generated: one step");
            CollectionAssert.AreEqual(Array.Empty<string>(), Unlabelled(window));
            window.Close();
        });

        [TestMethod]
        public Task ThePropsPanelAddsARuleThatTheInspectorEdits() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("props");
            Dispatcher.UIThread.RunJobs();
            int steps = model.History.Count;

            Click(window, Labelled<Button>(window, "Add a rule with the palette"));
            await Settle();
            Assert.AreEqual(1, model.Map!.Document.PropRules.Length);
            Assert.AreEqual(steps + 1, model.History.Count, "showing the rule's fields changes nothing: a slider that sets its own value gives nothing");
            Labelled<TextBox>(window, "Density");
            Labelled<ComboBox>(window, "Clustering");

            Click(window, Labelled<Button>(window, "Add the layer to the rule"));
            await Settle();
            Assert.AreEqual(1, model.Map.Document.PropRules[0].Conditions.Length);
            Assert.AreEqual(steps + 2, model.History.Count);
            CollectionAssert.AreEqual(Array.Empty<string>(), Unlabelled(window));
            window.Close();
        });
    }
}
