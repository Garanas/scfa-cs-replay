using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Terrain panel's generated results and the Problems tab in the real window (headless).
    /// </summary>
    [TestClass]
    public sealed class GenerationWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static Point Middle(Control control, MainWindow window) =>
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        private static void Click(MainWindow window, Control control)
        {
            Point at = Middle(control, window);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// Lets work on the pool and its continuations on the window's thread finish.
        /// </summary>
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
        public Task TheTerrainPanelGeneratesWalkabilityAndShowsHowItStands() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("terrain");
            Dispatcher.UIThread.RunJobs();
            Button generate = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Generate the walkability");

            Click(window, generate);
            await Until(() => model.Walkability.Navigation is not null && !model.Generation.IsRunning("walkability"));

            Assert.IsTrue(model.ShowWalkability);
            Button again = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Generate the walkability");
            Assert.AreEqual("Generate again", again.Content);
            TextBlock status = window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "Up to date");
            Assert.IsTrue(status.IsVisible);
            Assert.IsTrue(window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Show walkability").IsChecked);
            window.Close();
        });

        [TestMethod]
        public Task ADoubleClickOrEnterOnAProblemGoesToIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Spawn spawn = model.Map!.Document.Markers.Spawns[0];
            Vector2 at = new Vector2(spawn.Position.X, spawn.Position.Z);
            model.Map.Session.Edit("Take the mass away", document => document with
            {
                Markers = document.Markers with
                {
                    Resources = [.. document.Markers.Resources.Where(resource => Vector2.Distance(new Vector2(resource.Position.X, resource.Position.Z), at) > MapChecks.MassNearSpawn)],
                },
            });
            model.Problems.Run();
            model.Dock = DockTab.Problems;
            Dispatcher.UIThread.RunJobs();
            TabItem tab = window.GetVisualDescendants().OfType<TabItem>().First(item => item.Header is string header && header.StartsWith("Problems"));
            Assert.AreEqual(model.Problems.Header, tab.Header);
            ListBox list = window.GetVisualDescendants().OfType<ListBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Problems");
            int index = model.Problems.Items.First(item => item.Problem.Thing == ObjectRef.Marker(spawn.Id)).Index;
            ListBoxItem line = (ListBoxItem)list.ContainerFromIndex(index)!;

            // a double click
            Point middle = Middle(line, window);
            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            await Until(() => model.Selection.Count == 1);
            CollectionAssert.AreEqual(new[] { ObjectRef.Marker(spawn.Id) }, model.Selection.Items.ToArray());

            // Enter on the chosen line
            model.Selection.Clear();
            line.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => model.Selection.Count == 1);
            Assert.AreEqual(model.Problems.Items[index].Message, model.Status);
            window.Close();
        });
    }
}
