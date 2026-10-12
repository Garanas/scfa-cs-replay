using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Tours;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The guided tour in the window (user story H-1): the dimming takes the mouse except through the hole of a step that
    /// asks the person to act, Escape ends the tour, a panel the person hid or floated is put back for its step and
    /// hidden or floated again when the tour ends, and the view, mode and tool come back.
    /// </summary>
    [TestClass]
    public sealed class TourWindowTest
    {
        private static Task<(MainWindow Window, MainViewModel Model, string Folder)> Open() => TourTest.Open();

        private static async Task Start(MainViewModel model, string step) => await TourTest.WithFrames(model.Tour.StartAsync("window", step));

        private static Point CentreOf(MainWindow window, Rect area) => area.Center;

        private static async Task Frames(int count = 4)
        {
            for (int i = 0; i < count; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(5);
            }
        }

        [TestMethod]
        public Task TheHoleLetsAClickThroughOnAStepThatAsksToActAndTheDimmingTakesItOnAStepThatExplains() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            TourOverlay overlay = window.GetVisualDescendants().OfType<TourOverlay>().Single();

            // a step that explains: the rail is ringed and a click on a mode's button goes nowhere
            await Start(model, "modes");
            await Frames();
            Assert.IsTrue(overlay.Dimming.IsEffectivelyVisible);
            Assert.IsFalse(overlay.Dimming.LetThrough);
            Button paint = window.GetVisualDescendants().OfType<Button>().First(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Paint");
            Point at = paint.TranslatePoint(new Point(paint.Bounds.Width / 2, paint.Bounds.Height / 2), window)!.Value;
            Assert.IsInstanceOfType<TourSpotlight>(window.InputHitTest(at), "the dimming takes the mouse over the ringed part too");
            string mode = model.SelectedMode!.Definition.Id;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            await Frames();
            Assert.AreEqual(mode, model.SelectedMode.Definition.Id, "the click did not reach the button");

            // a step that asks to act: the 3D tab is reachable through the hole, nothing else is
            await Start(model, "try-3d");
            await Frames();
            Assert.IsTrue(overlay.Dimming.LetThrough);
            Rect target = model.Tour.Place.Bounds!.Value;
            Point tab = target.Center;
            Assert.IsNotInstanceOfType<TourSpotlight>(window.InputHitTest(tab), "the mouse passes the hole");
            Assert.IsInstanceOfType<TourSpotlight>(window.InputHitTest(new Point(tab.X, tab.Y + 200)), "and not the dimming around it");
            Assert.IsInstanceOfType<TourSpotlight>(window.InputHitTest(at), "the rail is dimmed again");
            Assert.IsFalse(model.Tour.IsDone);
            Assert.AreEqual(MapViewTab.Top, model.View);

            window.MouseMove(tab);
            window.MouseDown(tab, MouseButton.Left);
            window.MouseUp(tab, MouseButton.Left);
            for (int wait = 0; wait < 40 && !model.Tour.IsDone; wait++)
            {
                // the condition is looked at at most every 250 ms
                await Task.Delay(25);
                await Frames(1);
            }

            Assert.AreEqual(MapViewTab.ThreeD, model.View, "the click reached the tab");
            Assert.IsTrue(model.Tour.IsDone, "the step saw it done");
            Assert.IsTrue(model.Tour.ShowsDone);
            Assert.AreEqual("Next", model.Tour.NextText);
            await TourTest.WithFrames(model.Tour.EndAsync());
            window.Close();
        });

        [TestMethod]
        public Task KeysReachTheWindowWhileATourRunsAndEscapeEndsIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Start(model, "views");
            string mode = model.SelectedMode!.Definition.Id;

            // F6 chooses the Markers mode as without a tour
            window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            await Frames();
            Assert.AreEqual("markers", model.SelectedMode!.Definition.Id, "the window's shortcuts still work");
            Assert.IsTrue(model.Tour.IsRunning);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Frames(10);

            Assert.IsFalse(model.Tour.IsRunning, "Escape ends the tour");
            Assert.AreEqual(mode, model.SelectedMode.Definition.Id, "and the mode the person had comes back");
            Assert.IsFalse(window.GetVisualDescendants().OfType<TourOverlay>().Single().Dimming.IsEffectivelyVisible, "the dimming is gone");
            window.Close();
        });

        [TestMethod]
        public Task EscapeClosesWhatIsOpenFirst() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Start(model, "options");
            ToolRow row = window.GetVisualDescendants().OfType<ToolRow>().First(each => each.IsEffectivelyVisible && each.Children.Any(child => child.IsVisible));
            row.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Frames(10);

            Assert.IsFalse(row.IsExpanded, "the open More closed");
            Assert.IsTrue(model.Tour.IsRunning, "and the tour goes on");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Frames(10);
            Assert.IsFalse(model.Tour.IsRunning);
            window.Close();
        });

        [TestMethod]
        public Task AHiddenInspectorComesBackForItsStepAndIsHiddenAgainWhenTheTourEnds() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.Layout.Toggle(LayoutPanel.Inspector, false);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.Layout.ShowInspector);

            await Start(model, "inspector");
            await Frames();

            Assert.IsTrue(model.Layout.ShowInspector, "shown for its step");
            Assert.IsTrue(model.Tour.Place.Found, model.Tour.Place.Why);
            Assert.IsTrue(model.Tour.Place.Bounds!.Value.Width > 100, "and ringed");
            CollectionAssert.Contains(model.Tour.Describe()["layoutChanged"]!.AsArray().Select(node => node!.GetValue<string>()).ToList(), "inspector");

            await TourTest.WithFrames(model.Tour.EndAsync());
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(model.Layout.ShowInspector, "hidden again as the person had it");
            Assert.IsFalse(model.Tour.Ledger.Changed);
            window.Close();
        });

        [TestMethod]
        public Task AFloatingInspectorComesBackToItsPlaceForItsStepAndFloatsAgainAfterwards() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.Layout.Move(MovablePanel.Inspector, PanelPlace.Float);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(PanelPlace.Float, model.Layout.PlaceOf(MovablePanel.Inspector));

            await Start(model, "inspector");
            await Frames();

            Assert.AreEqual(PanelPlace.Right, model.Layout.PlaceOf(MovablePanel.Inspector), "back in the window for its step");
            Assert.IsTrue(model.Tour.Place.Found, model.Tour.Place.Why);

            await TourTest.WithFrames(model.Tour.EndAsync());
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(PanelPlace.Float, model.Layout.PlaceOf(MovablePanel.Inspector), "floating again");
            foreach (Window floating in window.FloatingWindows.ToList())
            {
                floating.Close();
            }
            window.Close();
        });

        [TestMethod]
        public Task TheViewTheModeAndTheToolComeBackWhenTheTourEnds() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.SelectMode("props");
            Dispatcher.UIThread.RunJobs();
            string tool = model.ActiveTool!.Definition.Id;
            Assert.AreEqual(MapViewTab.Top, model.View);

            await Start(model, "try-3d");
            Assert.AreEqual("terrain", model.SelectedMode!.Definition.Id, "the tour started in the Terrain mode");
            await TourTest.WithFrames(model.Tour.DoItAsync());
            Assert.AreEqual(MapViewTab.ThreeD, model.View);

            await TourTest.WithFrames(model.Tour.EndAsync());

            Assert.AreEqual(MapViewTab.Top, model.View);
            Assert.AreEqual("props", model.SelectedMode.Definition.Id);
            Assert.AreEqual(tool, model.ActiveTool!.Definition.Id);
            window.Close();
        });

        [TestMethod]
        public Task TheCardsButtonsGoThroughTheStepsAndTheOfferIsAtTheCorner() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Frames();
            Border offer = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Offer");
            Assert.IsTrue(offer.IsEffectivelyVisible, "the offer shows once a map is open");
            Rect place = new Rect(offer.TranslatePoint(default, window)!.Value, offer.Bounds.Size);
            Assert.IsTrue(place.Right <= 1280 && place.Bottom <= 800 - 29, $"inside the window above the status bar: {place}");
            Assert.IsTrue(place.X > 800 && place.Y > 500, $"at the corner: {place}");

            Click(window, ButtonNamed(window, "Start the tour"));
            await Frames(10);
            Assert.IsTrue(model.Tour.IsRunning);
            Assert.IsFalse(offer.IsEffectivelyVisible, "the offer is gone");
            Assert.AreEqual(0, model.Tour.StepIndex);
            Assert.AreEqual("Start", model.Tour.NextText);

            Click(window, ButtonNamed(window, "Next step"));
            await Frames(10);
            Assert.AreEqual(1, model.Tour.StepIndex);
            Assert.AreEqual("The modes", model.Tour.StepTitle);
            Click(window, ButtonNamed(window, "Back"));
            await Frames(10);
            Assert.AreEqual(0, model.Tour.StepIndex);
            Click(window, ButtonNamed(window, "End the tour"));
            await Frames(10);
            Assert.IsFalse(model.Tour.IsRunning);
            window.Close();
        });

        [TestMethod]
        public Task TheToursPageListsTheToursAndStartsOne() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await TourTest.WithFrames(model.Commands.RunAsync("help.tours"));
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("tours", model.OpenPageName);
            ToursPage page = window.GetVisualDescendants().OfType<ToursPage>().Single();
            Assert.IsTrue(page.IsEffectivelyVisible);
            StringAssert.Contains(string.Join("|", page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)), "A tour of the window");

            Click(window, ButtonNamed(window, "Start the tour A tour of the window"));
            await Frames(10);

            Assert.IsNull(model.OpenPageName, "the page closes");
            Assert.IsTrue(model.Tour.IsRunning);
            await TourTest.WithFrames(model.Tour.EndAsync());

            await TourTest.WithFrames(model.Commands.RunAsync("help.tours"));
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Frames(5);
            Assert.IsNull(model.OpenPageName, "Escape closes the page");
            window.Close();
        });

        [TestMethod]
        public Task ThePanelsOfTheViewsAndTheStatusBarAreRegionsAutomationNamesToo() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            Automation.UiLocator locator = new Automation.UiLocator(window, model);
            foreach (string part in new[] { "menu", "rail", "panel", "options", "views", "top", "dock", "inspector", "status" })
            {
                Control region = locator.Region(part);
                Assert.IsTrue(region.IsEffectivelyVisible && region.Bounds.Width > 0 && region.Bounds.Height > 0, $"the region {part}");
            }
            Assert.ThrowsException<ArgumentException>(() => locator.Region("nowhere"));
            window.Close();
            await Task.CompletedTask;
        });

        private static Button ButtonNamed(MainWindow window, string name) =>
            window.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(button) == name);

        private static void Click(MainWindow window, Control control)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
