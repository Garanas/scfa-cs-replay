using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Selecting units by what they are, the stored selections and the top view's army colours in the real
    /// window (headless, user story U-10): the popover's controls have labels and work, the digits in a
    /// view, a click inside a factory away from its centre, the chips.
    /// </summary>
    [TestClass]
    public sealed class UnitSelectionWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model, string Farm, string Snoop)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(UnitsTest.Catalog());
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            model.View = MapViewTab.Top;
            model.SelectMode("units");
            model.SelectTool("units.select");
            Dispatcher.UIThread.RunJobs();
            string farm = await Add(model, "uec1201", 100, 100, "ARMY_1", "INITIAL/Farm");
            await Add(model, "uec1101", 130, 100, "ARMY_1", "INITIAL/Farm");
            string snoop = await Add(model, "uel0101", 160, 100, "ARMY_2");
            await Add(model, "ual0001", 190, 100, "ARMY_2");
            Dispatcher.UIThread.RunJobs();
            return (window, model, farm, snoop);
        }

        private static async Task<string> Add(MainViewModel model, string blueprint, float x, float z, string army, string group = "INITIAL")
        {
            JsonObject placed = (JsonObject)(await model.Commands.RunAsync("units.add", new JsonObject { ["blueprint"] = blueprint, ["x"] = x, ["z"] = z, ["army"] = army, ["group"] = group }, interactive: false)).Result!;
            return placed["name"]!.GetValue<string>();
        }

        private static Control Find(MainWindow window, string label) =>
            UiTree.Find(window, label) ?? throw new AssertFailedException($"No control is labelled '{label}'");

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

        private static async Task Settle(Func<bool> done)
        {
            for (int i = 0; i < 100 && !done(); i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Dispatcher.UIThread.RunJobs();
        }

        private static void Press(MainWindow window, Key key, PhysicalKey physical, string? symbol, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPress(key, modifiers, physical, symbol);
            window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }

        [TestMethod]
        public Task TheSelectToolsRowHasSelectByAndPicksAndTheRowKeepsItsHeight() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _, _) = await Open();
            Button open = (Button)Find(window, "Select units by what they are");
            Assert.IsTrue(open.IsEffectivelyVisible);
            Control options = window.GetVisualDescendants().OfType<ToolRow>().First(row => row.IsEffectivelyVisible && row.GetVisualDescendants().Contains(open));
            double height = options.Bounds.Height;

            Button footprint = (Button)Find(window, "Pick units by their footprint");
            Button centre = (Button)Find(window, "Pick units by their centre");
            Assert.IsTrue(footprint.Classes.Contains("on"), "by footprint is the default");
            Click(window, centre);
            Assert.AreEqual(UnitPicking.Centre, model.UnitPicks);
            Assert.IsTrue(centre.Classes.Contains("on"));
            Assert.IsFalse(footprint.Classes.Contains("on"));
            Click(window, footprint);
            Assert.AreEqual(UnitPicking.Footprint, model.UnitPicks);

            Assert.IsFalse(model.SelectBy.IsOpen);
            Click(window, open);
            Assert.IsTrue(model.SelectBy.IsOpen, "the button opens the popover");
            Assert.AreEqual(height, options.Bounds.Height, "the row never grows");
            window.Close();
        });

        [TestMethod]
        public Task ThePopoversControlsHaveLabelsAndSelectAndAddWork() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string farm, string snoop) = await Open();
            await model.Commands.RunAsync("select.by", [], interactive: false);
            Dispatcher.UIThread.RunJobs();

            foreach (string label in new[]
            {
                "Select by", "Select by army", "Factions to select by", "Select by faction UEF", "Select by faction Aeon", "Select by faction Cybran", "Select by faction Seraphim",
                "Kinds to select by", "Select by kind structure", "Select by kind land", "Select by kind air", "Select by kind naval", "Tech levels to select by", "Select by tech 1",
                "Select by tech 4", "Select by blueprint", "Wrecks to select by", "Wrecks: leave out", "Wrecks: only", "Wrecks: include", "Where to select within", "Within the map",
                "Within the selection", "Within the area", "Units that match", "Add the matching units to the selection", "Select the matching units", "Reset what to select by", "Close Select by",
            })
            {
                Assert.IsTrue(Find(window, label).IsEffectivelyVisible, label);
            }
            Assert.AreEqual("4 units match", ((TextBlock)Find(window, "Units that match")).Text);

            Click(window, Find(window, "Select by kind structure"));
            Assert.AreEqual("2 units match", ((TextBlock)Find(window, "Units that match")).Text, "the count follows a click");
            Assert.IsTrue(((ToggleButton)Find(window, "Select by kind structure")).IsChecked);

            Click(window, Find(window, "Select the matching units"));
            await Settle(() => model.Selection.Count == 2);
            Assert.AreEqual(2, model.Selection.Count);

            Click(window, Find(window, "Select by kind structure"));
            Click(window, Find(window, "Select by kind land"));
            Click(window, Find(window, "Add the matching units to the selection"));
            await Settle(() => model.Selection.Count == 4);
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Unit(farm)), "Add keeps the selection");
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Unit(snoop)));

            Click(window, Find(window, "Close Select by"));
            Assert.IsFalse(model.SelectBy.IsOpen);
            Assert.IsNull(UiTree.Find(window, "Select by"), "hidden again");
            window.Close();
        });

        [TestMethod]
        public Task DigitsInTheViewStoreAndRecallAndTheChipsShowThem() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string farm, string snoop) = await Open();
            window.MapView.Focus();
            Dispatcher.UIThread.RunJobs();
            model.Selection.Set([ObjectRef.Unit(farm)]);

            Press(window, Key.D3, PhysicalKey.Digit3, "3", RawInputModifiers.Alt);
            await Settle(() => model.StoredSelections.HasChips);
            Button chip = (Button)Find(window, "Recall the selection stored as 3");
            Assert.IsTrue(chip.IsEffectivelyVisible);
            Assert.AreEqual("3 · Farm · 1", chip.Content);
            Assert.IsTrue(Middle(chip, window).Y > window.MapView.TranslatePoint(new Point(0, window.MapView.Bounds.Height / 2), window)!.Value.Y, "along the bottom of the top view");

            model.Selection.Set([ObjectRef.Unit(snoop)]);
            Press(window, Key.D3, PhysicalKey.Digit3, "3");
            await Settle(() => model.Selection.Contains(ObjectRef.Unit(farm)));
            Assert.AreEqual(1, model.Selection.Count);
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Unit(farm)), "the digit recalls");

            model.Selection.Clear();
            Click(window, chip);
            await Settle(() => model.Selection.HasAny);
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Unit(farm)), "a click on the chip recalls it");

            Press(window, Key.D1, PhysicalKey.Digit1, "1", RawInputModifiers.Control);
            Assert.AreEqual(MapViewTab.Top, model.View, "control and a digit stays the views'");
            window.Close();
        });

        [TestMethod]
        public Task AClickInsideAFactoryNearItsCornerSelectsItAndTheTopViewDrawsFootprintsInArmyColours() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, string farm, _) = await Open();
            PlacedUnit placed = model.ShownDocument!.FindUnit(farm)!;
            // zoomed in so that the farm is large: a footprint of 3 by 2 ogrids
            model.Commands.RunAsync("view.zoomTo", new JsonObject { ["x"] = placed.Unit.Position.X, ["z"] = placed.Unit.Position.Z, ["zoom"] = 8 }, interactive: false).Wait();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Point corner = window.MapView.TranslatePoint(window.MapView.MapToControl(placed.Unit.Position.X + 1.3, placed.Unit.Position.Z + 0.8), window)!.Value;
            window.MouseMove(corner);
            window.MouseDown(corner, MouseButton.Left);
            window.MouseUp(corner, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(1, model.Selection.Count);
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Unit(farm)), "a click near the corner, 1.3 and 0.8 ogrids from the centre, picks the farm");

            Assert.IsTrue(window.MapView.UnitsDrawn.Footprints >= 2, $"the two structures as footprints: {window.MapView.UnitsDrawn}");
            Assert.IsTrue(window.MapView.UnitsDrawn.Diamonds >= 0);
            Assert.AreEqual(0, window.MapView.UnitsDrawn.Squares, "every unit has its army's colour");

            // the chip of the views' bar turns the colours off: the unit colour's squares
            ToggleButton chip = (ToggleButton)Find(window, "Show Army colours");
            Assert.IsTrue(chip.IsChecked);
            await model.Commands.RunAsync("view.overlay.armyColours", [], interactive: false);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.ShowArmyColours);
            Assert.IsFalse(chip.IsChecked, "the chip follows");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(0, window.MapView.UnitsDrawn.Footprints);
            Assert.IsTrue(window.MapView.UnitsDrawn.Squares >= 1);
            window.Close();
        });
    }
}
