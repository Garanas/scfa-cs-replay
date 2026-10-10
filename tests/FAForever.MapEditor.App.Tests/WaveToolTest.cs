using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Placing the water's wave generators with the mouse (user story W-1): the Waves tool's click,
    /// drag and keys in the real window, and the command that places one at a place.
    /// </summary>
    [TestClass]
    public sealed class WaveToolTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static Point At(MainWindow window, float x, float z) => window.MapView.TranslatePoint(window.MapView.MapToControl(x, z), window)!.Value;

        [TestMethod]
        public void TheDirectionIsKeptAsTheGameKeepsIt()
        {
            // abhor.v0004: a rotation of 112.4 degrees rolls at 67.6 degrees from +z
            float rotation = 112.4f * MathF.PI / 180;
            Vector2 direction = new Vector2(MathF.Sin(67.6f * MathF.PI / 180), MathF.Cos(67.6f * MathF.PI / 180));
            Assert.AreEqual(rotation, WaveGeneratorEdits.RotationOf(direction), 1e-4);
            WaveGenerator still = new WaveGenerator { Texture = "", Ramp = "", Rotation = rotation };
            Assert.AreEqual(direction.X, WaveGeneratorEdits.DirectionOf(still).X, 1e-4, "without a velocity, the rotation says");
            Assert.AreEqual(direction.Y, WaveGeneratorEdits.DirectionOf(still).Y, 1e-4);

            WaveGenerator aimed = WaveGeneratorEdits.Aimed(still, new Vector2(0, -3), 0.02f);
            Assert.AreEqual(0, WaveGeneratorEdits.HeadingOf(WaveGeneratorEdits.DirectionOf(aimed)), 1e-3, "towards -z is north");
            Assert.AreEqual(0.02f, new Vector2(aimed.Velocity.X, aimed.Velocity.Z).Length(), 1e-6);
            Assert.AreEqual(90, WaveGeneratorEdits.HeadingOf(WaveGeneratorEdits.DirectionOfHeading(90)), 1e-3);
        }

        [TestMethod]
        public void TheSymmetryPlacesItsImagesTurnedOrMirrored()
        {
            MapDocument document = Project.NewMap.Create(new Project.NewMapSettings());
            WaveGenerator generator = WaveGeneratorEdits.Aimed(new WaveGenerator { Texture = "t", Ramp = "r", Position = new Vector3(10, 5, 20) }, new Vector2(1, 0), 0.02f);

            MapDocument mirrored = document.AddWaveGenerators(generator, MapSymmetry.Transforms(MapSymmetryKind.MirroredLeftRight, 2, document.Width, document.Height), out int[] added);

            Assert.AreEqual(2, added.Length);
            WaveGenerator image = mirrored.Water.WaveGenerators[added[1]];
            Assert.AreEqual(document.Width - 10, image.Position.X, 1e-3);
            Assert.AreEqual(20, image.Position.Z, 1e-3);
            Assert.AreEqual(-1, WaveGeneratorEdits.DirectionOf(image).X, 1e-4, "mirrored left to right, the waves roll the other way");
            Assert.AreEqual(5, image.Position.Y, "on the same height");
        }

        [TestMethod]
        public Task AClickPlacesADragAimsAndASquareMoves() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await model.Commands.RunAsync("water.placeWaveGenerator", [], interactive: true);
            Assert.AreEqual("water.waves", model.ActiveTool!.Definition.Id, "the menu's command chooses the tool");
            int before = model.ShownDocument!.Water.WaveGenerators.Length;

            // a drag from the place eastwards aims the waves east
            Point from = At(window, 100, 100);
            Point to = At(window, 140, 100);
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + (to - from) / 2);
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(before + 1, model.ShownDocument!.Water.WaveGenerators.Length);
            WaveGenerator placed = model.ShownDocument.Water.WaveGenerators[^1];
            Assert.AreEqual(100, placed.Position.X, 1);
            Assert.AreEqual(100, placed.Position.Z, 1);
            Assert.AreEqual(model.ShownDocument.Water.Elevation, placed.Position.Y, "on the water's surface");
            Assert.AreEqual(90, WaveGeneratorEdits.HeadingOf(WaveGeneratorEdits.DirectionOf(placed)), 2, "aimed east by the drag");
            Assert.AreEqual(before, model.WaterSettings.SelectedGenerator?.Index, "the placed one is selected in the inspector");
            Assert.AreEqual("Place a wave generator", model.History[^1].Description);
            int steps = model.History.Count;

            // its square dragged moves it, one step
            Point square = At(window, placed.Position.X, placed.Position.Z);
            Point moved = At(window, 120, 160);
            window.MouseMove(square);
            window.MouseDown(square, MouseButton.Left);
            window.MouseMove(moved);
            window.MouseUp(moved, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(before + 1, model.ShownDocument!.Water.WaveGenerators.Length, "a drag on a square moves rather than places");
            Assert.AreEqual(120, model.ShownDocument.Water.WaveGenerators[^1].Position.X, 1);
            Assert.AreEqual(160, model.ShownDocument.Water.WaveGenerators[^1].Position.Z, 1);
            Assert.AreEqual(steps + 1, model.History.Count);

            // a click on it only selects it; Delete removes it
            model.WaterSettings.SelectedGenerator = null;
            window.MouseMove(moved);
            window.MouseDown(moved, MouseButton.Left);
            window.MouseUp(moved, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(before, model.WaterSettings.SelectedGenerator?.Index);
            Assert.AreEqual(steps + 1, model.History.Count, "a click that moves nothing is no step");
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            for (int i = 0; i < 50 && model.ShownDocument!.Water.WaveGenerators.Length != before; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.AreEqual(before, model.ShownDocument!.Water.WaveGenerators.Length, "Delete removes the selected one");
            window.Close();
        });

        [TestMethod]
        public Task TheToolDrawsTheGeneratorsAndTheCommandPlacesWithTheSymmetry() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await model.Commands.RunAsync("select.symmetry", new JsonObject { ["kind"] = "leftRight" }, interactive: false);
            model.Symmetry.MirrorsEdits = true;
            int before = model.ShownDocument!.Water.WaveGenerators.Length;

            CommandRun run = await model.Commands.RunAsync("water.placeWaveGenerator", new JsonObject { ["x"] = 50, ["z"] = 60, ["heading"] = 90 }, interactive: false);

            int[] placed = (int[])run.Result!;
            Assert.AreEqual(2, placed.Length, "with its image in the other half");
            MapDocument document = model.ShownDocument!;
            Assert.AreEqual(before + 2, document.Water.WaveGenerators.Length);
            Assert.AreEqual(document.Width - 50, document.Water.WaveGenerators[placed[1]].Position.X, 1e-3);
            Assert.AreEqual(270, WaveGeneratorEdits.HeadingOf(WaveGeneratorEdits.DirectionOf(document.Water.WaveGenerators[placed[1]])), 1e-2);

            model.SelectTool("water.waves");
            IReadOnlyList<ToolShape> shapes = model.Tool<WaveTool>().Shapes(ToolView.Flat(1));
            Assert.AreEqual(before + 2, shapes.OfType<ToolSquare>().Count(), "a square per generator");
            Assert.IsTrue(shapes.OfType<ToolTriangle>().Count() >= 2, "with an arrow the way its waves roll");
            window.Close();
        });
    }
}
