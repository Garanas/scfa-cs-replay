using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using System.Text.Json.Nodes;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The tool options row (<see cref="ToolRow"/>): one row whatever the tool and the window's
    /// width, what does not fit behind More, shown over the views.
    /// </summary>
    [TestClass]
    public sealed class ToolOptionsTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model)> Open(double width = 1280)
        {
            MainWindow window = new MainWindow { Width = width, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            model.Assets.Use(TestAssets.Catalog());
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static ToolRow? RowOf(MainWindow window) => window.GetVisualDescendants().OfType<ToolRow>().FirstOrDefault(row => row.IsEffectivelyVisible);

        private static void Frame()
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        [TestMethod]
        public Task EveryToolKeepsTheViewsWhereTheyAre() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Rect views = window.MapView.Bounds;
            Point at = window.MapView.TranslatePoint(default, window)!.Value;

            foreach (ToolDefinition tool in EditorModes.All.SelectMany(mode => mode.Tools))
            {
                model.SelectTool(tool.Id);
                Frame();
                Assert.AreEqual(views.Size, window.MapView.Bounds.Size, $"{tool.Id} keeps the size of the views");
                Assert.AreEqual(at, window.MapView.TranslatePoint(default, window), $"{tool.Id} keeps their place");
                if (RowOf(window) is ToolRow row)
                {
                    foreach (Control item in row.Children.Where(child => child.IsVisible && child != row.MoreButton && !ToolRow.IsInOverflow(child) && !child.Classes.Contains("tooloverflow")))
                    {
                        Assert.IsTrue(item.Bounds.Right <= row.Bounds.Width + 0.5, $"{tool.Id}: what shows in the row fits in it");
                    }
                    Assert.AreEqual(row.OverflowCount > 0, row.MoreButton.IsVisible, $"{tool.Id}: More shows only when something does not fit");
                }
            }
            window.Close();
        });

        [TestMethod]
        public Task EveryToolsSettingsLieInsideTheBarAt1280() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Border bar = window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("toolbar") && border.Classes.Contains("options"));
            ContentControl options = bar.GetVisualDescendants().OfType<ContentControl>().First();
            foreach (ToolDefinition tool in EditorModes.All.SelectMany(mode => mode.Tools).Where(tool => !tool.IsIncomplete))
            {
                model.SelectTool(tool.Id);
                Frame();
                double right = bar.TranslatePoint(new Point(bar.Bounds.Width, 0), window)!.Value.X;
                // every setting a person sees in the row: what does not fit lies behind More, never past the bar's end
                IEnumerable<Control> shown = bar.GetVisualDescendants().OfType<Control>()
                    .Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0
                        && control is TextBlock or Button or ComboBox or Slider or TextBox or CheckBox or ItemsControl
                        && !ToolRow.IsInOverflow(control)
                        && !control.GetVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains("tooloverflow")));
                foreach (Control control in shown)
                {
                    double end = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X;
                    Assert.IsTrue(end <= right + 0.5, $"{tool.Id}: {control.GetType().Name} '{Avalonia.Automation.AutomationProperties.GetName(control)}{(control as TextBlock)?.Text}' ends at {end:0}, past the bar's end at {right:0}");
                }
                Control? root = options.Presenter?.Child;
                Assert.IsTrue(root is ToolRow or TextBlock, $"{tool.Id}: its settings are a controls:ToolRow (what does not fit goes behind More), not a {root?.GetType().Name}");
            }
            window.Close();
        });

        [TestMethod]
        public Task WhatDoesNotFitShowsBehindMoreOverTheViews() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            AutomationServer automation = new AutomationServer(window, model, 0);
            model.SelectTool("terrain.sculpt");
            Frame();
            ToolRow row = RowOf(window)!;
            Slider strength = row.GetVisualDescendants().OfType<Slider>()
                .Single(slider => Avalonia.Automation.AutomationProperties.GetName(slider) == "Height brush strength");
            Assert.IsTrue(row.OverflowCount > 0, "at 1280 pixels the sculpt tool's settings do not all fit");
            Assert.IsTrue(ToolRow.IsOverflowHidden(strength), "the strength lies behind More");
            Point middle = strength.TranslatePoint(new Point(strength.Bounds.Width / 2, strength.Bounds.Height / 2), window)!.Value;
            Assert.IsFalse(IsIn(window.InputHitTest(middle), strength), "closed, the mouse does not reach it");

            Point more = row.MoreButton.TranslatePoint(new Point(row.MoreButton.Bounds.Width / 2, row.MoreButton.Bounds.Height / 2), window)!.Value;
            Frame();
            await automation.HandleAsync("mouse", new JsonObject { ["x"] = more.X, ["y"] = more.Y });
            Frame();

            Assert.IsTrue(row.IsExpanded, "a click on More shows the rest");
            Assert.IsFalse(ToolRow.IsOverflowHidden(strength));
            Assert.IsTrue(IsIn(window.InputHitTest(middle), strength), "over the views, the mouse reaches it");

            model.SelectTool("terrain.ruler");
            model.SelectTool("terrain.sculpt");
            Frame();
            Assert.IsFalse(RowOf(window)!.IsExpanded, "another tool closes it");
            window.Close();
        });

        [TestMethod]
        public Task MoreClosesOnAClickElsewhereAndOnEscape() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            AutomationServer automation = new AutomationServer(window, model, 0);
            model.SelectTool("terrain.sculpt");
            Frame();
            ToolRow row = RowOf(window)!;
            Point Middle(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            async Task Click(Point at)
            {
                Frame();
                await automation.HandleAsync("mouse", new JsonObject { ["x"] = at.X, ["y"] = at.Y });
                Frame();
            }
            Slider strength = row.GetVisualDescendants().OfType<Slider>()
                .Single(slider => Avalonia.Automation.AutomationProperties.GetName(slider) == "Height brush strength");

            await Click(Middle(row.MoreButton));
            Assert.IsTrue(row.IsExpanded);
            await Click(Middle(strength));
            Assert.IsTrue(row.IsExpanded, "a click on what it shows keeps it open");
            await Click(Middle(window.FindControl<Control>("InspectorView")!));
            Assert.IsFalse(row.IsExpanded, "a click elsewhere closes it");

            await Click(Middle(row.MoreButton));
            Assert.IsTrue(row.IsExpanded);
            await Click(Middle(row.MoreButton));
            Assert.IsFalse(row.IsExpanded, "a second click on More closes it");

            await Click(Middle(row.MoreButton));
            model.Selection.Set([Project.Editing.ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);
            await automation.HandleAsync("key", new JsonObject { ["key"] = "Escape" });
            Frame();
            Assert.IsFalse(row.IsExpanded, "Escape closes it");
            Assert.IsFalse(model.Selection.IsEmpty, "and goes no further");
            window.Close();
        });

        [TestMethod]
        public Task AWideWindowShowsEverythingInTheRow() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open(width: 2400);
            model.SelectTool("terrain.sculpt");
            Frame();
            Assert.AreEqual(0, RowOf(window)!.OverflowCount);
            Assert.IsFalse(RowOf(window)!.MoreButton.IsVisible);
            window.Close();
        });

        private static bool IsIn(IInputElement? hit, Visual control) =>
            hit is Visual visual && (visual == control || visual.GetVisualAncestors().Contains(control));
    }
}
