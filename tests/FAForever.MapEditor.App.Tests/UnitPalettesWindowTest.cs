using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Units mode's palette in the real window (headless, user story U-8): the section's
    /// controls have labels, a click on a slot picks it, the keys in a view and on a panel button,
    /// a tile dropped on a slot, a preset's context menu, the chips of the last placed.
    /// </summary>
    [TestClass]
    public sealed class UnitPalettesWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model, PresetLibrary Library)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(UnitsTest.Catalog());
            PresetLibrary library = UnitPalettesTest.Library();
            model.Presets.Use(library);
            await model.OpenAsync(HardFfa);
            model.SelectTool("units.place");
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model, library);
        }

        private static Control Find(MainWindow window, string label) =>
            UiTree.Find(window, label) ?? throw new AssertFailedException($"No control is labelled '{label}'");

        private static Point Middle(Control control, MainWindow window) =>
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        private static void Click(MainWindow window, Control control, MouseButton button = MouseButton.Left)
        {
            Point at = Middle(control, window);
            window.MouseMove(at);
            window.MouseDown(at, button);
            window.MouseUp(at, button);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Press(MainWindow window, Key key, PhysicalKey physical, string? symbol, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPress(key, modifiers, physical, symbol);
            window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// Lets a command started by a click or a key run.
        /// </summary>
        private static async Task Settle(Func<bool> done)
        {
            for (int i = 0; i < 100 && !done(); i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Dispatcher.UIThread.RunJobs();
        }

        [TestMethod]
        public Task TheSectionsControlsHaveLabelsAndAClickOnASlotPicksIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();

            ComboBox palette = (ComboBox)Find(window, "Palette");
            Assert.AreEqual("Base", palette.SelectedItem);
            Assert.IsNotNull(Find(window, "New palette"));
            Assert.IsNotNull(Find(window, "Palette menu"));
            Button first = (Button)Find(window, "Palette slot 1: UEF Farm (uec1201)");
            Assert.IsTrue(first.Classes.Contains("slot"));
            Assert.IsFalse(first.Classes.Contains("chosen"));
            Button second = (Button)Find(window, "Palette slot 2: preset Village");
            Button empty = (Button)Find(window, "Palette slot 3: empty");
            Assert.IsNotNull(Find(window, "Palette slot 0: Snoop (uel0101)"), "the tenth slot is the key 0");
            Assert.IsTrue(empty.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "drop here" && text.IsEffectivelyVisible));

            Click(window, first);
            await Settle(() => tool.Unit is not null);
            Assert.AreEqual("uec1201", tool.Unit?.Key, "a click on a slot picks its unit");
            Assert.AreEqual("units.place", model.ActiveTool!.Definition.Id);
            Assert.IsTrue(first.Classes.Contains("chosen"), "the slot is drawn chosen");

            Click(window, second);
            await Settle(() => tool.Preset is not null);
            Assert.AreEqual("Village", tool.Preset);
            Assert.IsFalse(first.Classes.Contains("chosen"));
            Assert.IsTrue(second.Classes.Contains("chosen"));

            // the combo box chooses another palette
            palette.SelectedItem = "Other";
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("Other", model.UnitPalettes.Chosen);
            Assert.IsNotNull(Find(window, "Palette slot 1: empty"));
            window.Close();
        });

        [TestMethod]
        public Task DigitsAndTabInAViewWorkThePaletteAndTabOnAPanelButtonMovesTheKeyboard() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();
            window.MapView.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.MapView.IsFocused);

            Press(window, Key.D2, PhysicalKey.Digit2, "2");
            await Settle(() => tool.Preset is not null);
            Assert.AreEqual("Village", tool.Preset, "2 in the view picks the second slot");

            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.AreEqual("Other", model.UnitPalettes.Chosen, "Tab in the view: the next palette");
            Assert.IsTrue(window.MapView.IsFocused, "the keyboard stays in the view");
            Press(window, Key.Tab, PhysicalKey.Tab, "\t", RawInputModifiers.Shift);
            Assert.AreEqual("Base", model.UnitPalettes.Chosen, "Shift+Tab: the one before");

            Press(window, Key.D1, PhysicalKey.Digit1, "1", RawInputModifiers.Control);
            Assert.AreEqual(MapViewTab.Top, model.View, "Ctrl+1 stays the top view's shortcut");
            Assert.AreEqual("Village", tool.Preset, "and picks no slot");

            // on a button of the panel, Tab moves the keyboard as everywhere and leaves the palette alone
            Button slot = (Button)Find(window, "Palette slot 1: UEF Farm (uec1201)");
            slot.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(slot.IsFocused);
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.AreEqual("Base", model.UnitPalettes.Chosen, "Tab on a button is the keyboard's own");
            Assert.IsFalse(slot.IsFocused, "it moved the keyboard on");
            window.Close();
        });

        [TestMethod]
        public Task ATileDroppedOnASlotPutsItsUnitThereAndThePresetsMenuPutsAPresetInOne() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, PresetLibrary library) = await Open();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            Button empty = (Button)Find(window, "Palette slot 3: empty");
            Point to = Middle(empty, window);

            // as a tile dragged from the shelf: the asset's key dropped on the slot
            JsonObject dropped = (JsonObject)await automation.HandleAsync("drag", new JsonObject
            {
                ["view"] = "window",
                ["from"] = new JsonArray(640, 400),
                ["to"] = new JsonArray(to.X, to.Y),
                ["data"] = "uec1101",
            });
            await Settle(() => model.UnitPalettes.Slots[2].IsUnit);
            Assert.AreEqual("Copy", dropped["dropped"]!.GetValue<string>());
            Assert.AreEqual("uec1101", model.UnitPalettes.Slots[2].Slot.Unit, "the slot holds the unit");
            Assert.AreEqual("uec1101", UnitPalettes.Read(library.Find(UnitPalettes.Kind, "Base")!).Slots[2].Unit, "and the file does");
            Assert.IsNotNull(Find(window, "Palette slot 3: UEF Residential Building (uec1101)"));
            Assert.IsFalse(empty.Classes.Contains("droptarget"), "lit only while the drag is over it");

            // a drag of something that is no unit lands nowhere
            Button fourth = (Button)Find(window, "Palette slot 4: empty");
            Point other = Middle(fourth, window);
            JsonObject refused = (JsonObject)await automation.HandleAsync("drag", new JsonObject
            {
                ["view"] = "window",
                ["from"] = new JsonArray(640, 400),
                ["to"] = new JsonArray(other.X, other.Y),
                ["data"] = "env/evergreen/props/trees/pine06_prop.bp",
            });
            Assert.AreEqual("None", refused["dropped"]!.GetValue<string>());
            Assert.IsTrue(model.UnitPalettes.Slots[3].IsEmpty);

            // a right-click on a units preset of the panel's list: a menu with a slot for each of the ten
            Button apply = (Button)Find(window, "Apply the preset Village");
            Click(window, apply, MouseButton.Right);
            Assert.IsNotNull(apply.ContextMenu, "the preset's button got its menu");
            Assert.IsTrue(apply.ContextMenu!.IsOpen, "and it opened");
            MenuItem into = (apply.ContextMenu.ItemsSource as IEnumerable<object>)!.OfType<MenuItem>().Single();
            Assert.AreEqual("Put in a slot of Base", into.Header);
            MenuItem[] slots = [.. (into.ItemsSource as IEnumerable<object>)!.OfType<MenuItem>()];
            Assert.AreEqual(10, slots.Length);
            Assert.AreEqual("Slot 4 (empty)", slots[3].Header);
            Assert.AreEqual("Slot 1: UEF Farm", slots[0].Header);
            apply.ContextMenu.Close();
            slots[3].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Settle(() => model.UnitPalettes.Slots[3].IsPreset);
            Assert.AreEqual("Village", model.UnitPalettes.Slots[3].Slot.Preset);
            Assert.IsNotNull(Find(window, "Palette slot 4: preset Village"));

            // a brush preset's button gets no such menu
            model.SelectMode("terrain");
            Dispatcher.UIThread.RunJobs();
            library.Save(Preset.Of(PresetKinds.Brushes, "Soft", new { Tool = "sculpt", Radius = 10 }));
            model.Presets.Refresh();
            Dispatcher.UIThread.RunJobs();
            Button brush = (Button)Find(window, "Apply the preset Soft");
            Click(window, brush, MouseButton.Right);
            Assert.IsNull(brush.ContextMenu);
            window.Close();
        });

        [TestMethod]
        public Task TheChipsOfTheLastPlacedChooseAUnitAgain() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            Assert.IsNull(UiTree.Find(window, "Last placed"), "nothing placed yet: no strip");

            await model.Commands.RunAsync("units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1" }, interactive: false);
            await model.Commands.RunAsync("units.add", new JsonObject { ["blueprint"] = "uel0101", ["x"] = 60, ["z"] = 50, ["army"] = "ARMY_1" }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            Button chip = (Button)Find(window, "Place UEF Residential Building again");
            Assert.IsNotNull(Find(window, "Place Snoop again"));

            Click(window, chip);
            await Settle(() => tool.Unit?.Key == "uec1101");
            Assert.AreEqual("uec1101", tool.Unit?.Key, "the chip chooses the unit for the place tool");
            window.Close();
        });
    }
}
