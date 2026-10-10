using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The panels' lists, the rail's bottom and the shortcuts page with the mouse and keys, in the
    /// real window, headless.
    /// </summary>
    [TestClass]
    public sealed class PanelsAndShellWindowTest
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

        private static ModePanel PanelOf(MainWindow window) => window.GetVisualDescendants().OfType<ModePanel>().Single();

        private static void Click(MainWindow window, Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None)
        {
            window.KeyPress(key, modifiers, physical, null);
            window.KeyRelease(key, modifiers, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        [TestMethod]
        public Task AnEntryClickedInTheWaterPanelShowsInTheInspector() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("water");
            Dispatcher.UIThread.RunJobs();

            Click(window, Labelled<Button>(window, "Flatness"));
            Assert.AreEqual("water.flatness", model.WaterSettings.Parts.Chosen.Id);
            Assert.AreEqual(WaterMask.Flatness, model.Tool<WaterPaintTool>().Painted.Mask, "the brush paints the map chosen");
            Assert.AreEqual("Flatness", InspectorOf(window).GetVisualDescendants().OfType<TextBlock>()
                .First(block => Avalonia.Automation.AutomationProperties.GetName(block) == "Inspected part").Text);

            Click(window, Labelled<Button>(window, "Surface and reflection"));
            Assert.IsNotNull(Labelled<TextBox>(InspectorOf(window), "Sun shininess"), "its fields are the inspector's");
            Assert.IsFalse(PanelOf(window).GetVisualDescendants().OfType<TextBox>().Any(box => box.IsEffectivelyVisible
                && Avalonia.Automation.AutomationProperties.GetName(box) == "Sun shininess"), "and not the panel's");
            window.Close();
        });

        [TestMethod]
        public Task TheLightingPanelListsAndTheInspectorEdits() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectMode("lighting");
            Dispatcher.UIThread.RunJobs();

            Assert.IsNotNull(Labelled<TextBox>(InspectorOf(window), "Azimuth"), "the sun first");
            Click(window, Labelled<Button>(window, "Sky and cube maps"));
            Assert.IsNotNull(Labelled<TextBox>(InspectorOf(window), "Sky cube map"));
            Assert.IsNotNull(Labelled<Button>(PanelOf(window), "Show the folder of the Sky presets"), "the sky's presets with their folder");
            window.Close();
        });

        [TestMethod]
        public Task ShiftF1ShowsTheShortcutsWhoseSearchNarrowsThemAndEscapeCloses() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Press(window, Key.F1, RawInputModifiers.Shift, PhysicalKey.F1);
            Assert.IsTrue(model.Shortcuts.IsOpen);
            TextBox search = Labelled<TextBox>(window, "Search the shortcuts");
            Assert.IsTrue(search.IsFocused, "the page takes the keyboard, in its search");

            window.KeyTextInput("palette");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("palette", model.Shortcuts.Query);
            Assert.IsTrue(model.Shortcuts.Sections[0].Items.Any(item => item.Keys == "Ctrl+K"));

            Press(window, Key.Escape, physical: PhysicalKey.Escape);
            Assert.IsFalse(model.Shortcuts.IsOpen);
            window.Close();
        });

        [TestMethod]
        public Task ThePluginsEntryAtTheRailsBottomOpensTheirPage() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Button plugins = Labelled<Button>(window, "Plugins");
            Button procedural = Labelled<Button>(window, "Procedural");
            Assert.IsTrue(plugins.TranslatePoint(default, window)!.Value.Y > procedural.TranslatePoint(default, window)!.Value.Y, "the plugins under the Procedural mode");
            Assert.IsTrue(procedural.TranslatePoint(default, window)!.Value.Y > Labelled<Button>(window, "Units").TranslatePoint(default, window)!.Value.Y + 100,
                "at the rail's bottom");

            Click(window, plugins);
            Assert.IsTrue(model.Plugins.IsOpen);
            model.ClosePage();
            Click(window, procedural);
            Assert.AreEqual("procedural", model.SelectedMode!.Definition.Id);
            window.Close();
        });
    }
}
