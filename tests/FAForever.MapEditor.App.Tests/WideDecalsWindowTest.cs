using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The map-wide decals (user story D-1) with the mouse in the real window, headless: an entry of
    /// the Decals panel shows in the inspector, its source chosen there, Generate in the panel.
    /// </summary>
    [TestClass]
    public sealed class WideDecalsWindowTest
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

        private static T Labelled<T>(Visual root, string label) where T : Control => root.GetVisualDescendants().OfType<T>()
            .First(control => control.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(control) == label);

        private static Inspector InspectorOf(MainWindow window) => window.GetVisualDescendants().OfType<Inspector>().Single();

        private static void Click(MainWindow window, Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static async Task Settle()
        {
            for (int i = 0; i < 50; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }
        }

        [TestMethod]
        public Task AnEntryClickedShowsTheSlotInTheInspectorAndGenerateMakesItsPreview() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("decals");
            Dispatcher.UIThread.RunJobs();

            Click(window, Labelled<Button>(window, "Shadow decal"));
            Assert.AreEqual(MapWideDecalsViewModel.ShadowPart, model.WideDecals.Parts.Chosen.Id);
            Assert.AreEqual("Shadow decal", InspectorOf(window).GetVisualDescendants().OfType<TextBlock>()
                .First(block => Avalonia.Automation.AutomationProperties.GetName(block) == "Inspected part").Text);
            Assert.IsNotNull(Labelled<TextBox>(InspectorOf(window), "Radius"), "the shadow's settings are the inspector's");

            // the source chosen in the inspector: generated, and the panel's Generate appears
            Labelled<ComboBox>(InspectorOf(window), "Source").SelectedItem = "Generated";
            await Settle();
            Assert.AreEqual(MapWideSource.Generated, model.ShownDocument!.WideDecals.Shadow.Source);
            Click(window, Labelled<Button>(window.GetVisualDescendants().OfType<ModePanel>().Single(), "Generate the shadow decal now"));
            for (int i = 0; i < 100 && model.WideDecals.Shadow.Preview is null; i++)
            {
                await Settle();
            }
            Assert.IsNotNull(model.WideDecals.Shadow.Preview, "generated: its preview shows");
            Assert.IsTrue(window.GetVisualDescendants().OfType<ImagePreview>().Any(preview => preview.IsEffectivelyVisible
                && Avalonia.Automation.AutomationProperties.GetName(preview) == "Preview of the map-wide shadow decal" && preview.Picture is not null));
            window.Close();
        });
    }
}
