using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Procedural mode's panel and its Regions tool in the real window (headless).
    /// </summary>
    [TestClass]
    public sealed class ProceduralWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.AutoGeneration.Delay = TimeSpan.FromMilliseconds(20);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.SelectMode("procedural");
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static T Named<T>(MainWindow window, string name) where T : Control =>
            window.GetVisualDescendants().OfType<T>().Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == name);

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

        private static async Task Until(Func<bool> done)
        {
            for (int i = 0; i < 500 && !done(); i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.IsTrue(done(), "it did not finish");
        }

        [TestMethod]
        public Task TheCheckBoxTurnsWalkabilityRegeneratingOnItsOwnOn() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();

            Click(window, Named<CheckBox>(window, "Regenerate the walkability on its own"));

            Assert.IsTrue(model.Map!.Document.Procedural.RegeneratesOnItsOwn("walkability"));
            await Until(() => model.Walkability.Navigation is not null && !model.AutoGeneration.IsPending("walkability"));
            Assert.IsTrue(Named<CheckBox>(window, "Regenerate the walkability on its own").IsChecked);
            window.Close();
        });

        [TestMethod]
        public Task GenerateShowsTheRegionsAndAClickChoosesOne() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();

            Click(window, Named<Button>(window, "Generate the navigation mesh"));
            await Until(() => model.Navigation.Mesh is not null && !model.Generation.IsRunning("navigationMesh"));
            Dispatcher.UIThread.RunJobs();
            // its entry shows it in the inspector, with its regions
            Click(window, Named<Button>(window, "Navigation mesh"));

            Assert.IsTrue(model.ShowNavigation);
            Assert.IsTrue(Named<CheckBox>(window, "Preview the navigation mesh in the views").IsChecked);
            NavRegionRow first = model.Navigation.Regions[0];
            Click(window, Named<Button>(window, first.Label));
            Assert.AreEqual(first.Region.Id, model.Navigation.ChosenRegion, "the list chooses a region");

            // the Regions tool: a click in the top view chooses the region under the mouse
            model.SelectTool("procedural.regions");
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();
            Point at = window.MapView.TranslatePoint(window.MapView.MapToControl(20, 20), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(model.Navigation.Mesh!.RegionAt(model.Navigation.Layer, 20.5f, 20.5f)?.Id, model.Navigation.ChosenRegion);
            window.Close();
        });
    }
}
