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
    /// The plugins in the window (user story F-15): their commands in the Plugins menu, the Plugins
    /// page with its check boxes, buttons and fields, as a person uses them.
    /// </summary>
    [TestClass]
    public sealed class PluginsWindowTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", "plugins-" + Guid.NewGuid().ToString("N"))).FullName;
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(TestAssets.SandboxProject());
            model.Assets.Use(TestAssets.Catalog());
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.Plugins.Open(folder);
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static void Click(MainWindow window, Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static MenuItem Plugins(MainWindow window) =>
            window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "_Plugins");

        [TestMethod]
        public Task ThePluginsCommandsAreInThePluginsMenuUntilTheyAreTurnedOff() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();

            string?[] items = [.. Plugins(window).Items.OfType<MenuItem>().Select(item => (string?)item.Header)];
            CollectionAssert.IsSubsetOf(new[] { "Plugins...", "Reload the plugins", "Count reclaim per army", "Scatter rocks on steep slopes" }, items);

            await model.Commands.RunAsync("plugins.enable", new JsonObject { ["plugin"] = "scatter-rocks", ["on"] = false }, interactive: false);
            Dispatcher.UIThread.RunJobs();

            items = [.. Plugins(window).Items.OfType<MenuItem>().Select(item => (string?)item.Header)];
            CollectionAssert.DoesNotContain(items, "Scatter rocks on steep slopes", "the menu is made again without it");
            CollectionAssert.Contains(items, "Count reclaim per army");
            window.Close();
        });

        [TestMethod]
        public Task ThePageListsThePluginsAndItsButtonsAndBoxesWork() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();

            await model.Commands.RunAsync("plugins.manage");
            Dispatcher.UIThread.RunJobs();
            PluginsPage page = window.GetVisualDescendants().OfType<PluginsPage>().Single();
            Assert.IsTrue(page.IsVisible);

            Button count = page.GetVisualDescendants().OfType<Button>().Single(button => (string?)button.Content == "Count reclaim per army");
            Click(window, count);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            StringAssert.StartsWith(model.Status, "Mass of reclaim per army:", "the button runs the plugin's command");

            CheckBox scatter = page.GetVisualDescendants().OfType<CheckBox>().Single(box => (string?)box.Content == "Scatter rocks 1.0");
            Assert.IsTrue(scatter.IsChecked);
            Click(window, scatter);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.Plugins.Find("scatter-rocks")!.IsOn, "the box turns it off");
            Assert.IsNull(model.Commands.Find("plugin.scatter-rocks.scatter"));
            Assert.IsFalse(scatter.IsChecked);

            TextBox tolerance = page.GetVisualDescendants().OfType<TextBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Tolerance (%)");
            Assert.AreEqual("25", tolerance.Text, "a field of the panel shows its value");

            List<string> unlabelled = [];
            void Check(JsonArray nodes)
            {
                foreach (JsonObject node in nodes.Cast<JsonObject>())
                {
                    if (node["type"]!.GetValue<string>() is "Slider" or "TextBox" or "Button" or "CheckBox" && node["label"] is null && node["text"] is null)
                    {
                        unlabelled.Add(node.ToJsonString());
                    }
                    if (node["children"] is JsonArray children)
                    {
                        Check(children);
                    }
                }
            }
            Check(UiTree.Describe(page, hidden: false));
            CollectionAssert.AreEqual(Array.Empty<string>(), unlabelled);
            window.Close();
        });
    }
}
