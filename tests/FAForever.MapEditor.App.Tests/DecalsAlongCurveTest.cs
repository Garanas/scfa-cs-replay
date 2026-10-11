using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Decals along a height curve (user story T-4, D2): <c>decals.alongCurve</c> with the picker, the
    /// section's settings and the curve's symmetry, one step of undo; and the curve inspector's
    /// "Decals along the curve" in the window.
    /// </summary>
    [TestClass]
    public class DecalsAlongCurveTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// A decal of the test assets, an albedo of one pixel.
        /// </summary>
        private const string Mud = "env/evergreen/decals/e_mud001_albedo.dds";

        private static async Task<(MainViewModel Model, MainViewModelTest.Answers Answers)> Open(string? symmetry = null)
        {
            MainViewModelTest.Answers answers = new MainViewModelTest.Answers();
            MainViewModel model = new MainViewModel(answers);
            await model.OpenAsync(HardFfa);
            model.Assets.Use(AssetCatalog.Load(TestAssets.Folder()));
            if (symmetry is not null)
            {
                await Run(model, "select.symmetry", new JsonObject { ["kind"] = symmetry, ["count"] = 2 });
            }
            return (model, answers);
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        /// <summary>
        /// A straight curve along +x at z 100, from x 40 to 200.
        /// </summary>
        private static async Task<string> AddCurve(MainViewModel model)
        {
            await Run(model, "terrain.addLayer");
            CommandRun run = await Run(model, "terrain.addCurve", new JsonObject { ["points"] = new JsonArray(new JsonArray(40, 100), new JsonArray(200, 100)), ["height"] = 4 });
            Assert.IsTrue(run.Ran, run.Error?.Message);
            return (string)run.Result!;
        }

        private static Vector3 Centre(MainViewModel model, ObjectRef thing) => model.ShownDocument!.PlaceOf(thing)!.Value;

        [TestMethod]
        public async Task TheCommandAsksForTheDecalAndPlacesThemAlongTheCurveInOneStep()
        {
            (MainViewModel model, MainViewModelTest.Answers answers) = await Open();
            await AddCurve(model);
            int decals = model.ShownDocument!.Decals.Length;
            int steps = model.History.Count;

            answers.Assets.Enqueue(Mud);
            CommandRun run = await model.Commands.RunAsync("decals.alongCurve", new JsonObject { ["spacing"] = 20, ["width"] = 6, ["offset"] = 5, ["alternate"] = true }, interactive: true);

            Assert.IsTrue(run.Ran, run.Error?.Message);
            Assert.AreEqual(AssetKind.Decal, answers.AssetQuestions.Single().Kind, "the picker asks without a key");
            Assert.AreEqual(8, (int)run.Result!, "160 ogrids in pieces of 20");
            Assert.AreEqual(decals + 8, model.ShownDocument!.Decals.Length);
            Assert.AreEqual(steps + 1, model.History.Count, "one step of undo");
            Assert.AreEqual(Mud, model.DecalsAlong.Decal, "the section keeps the decal");
            ObjectRef[] selected = [.. model.Selection.Items];
            Assert.AreEqual(8, selected.Length, "the new decals are selected");
            for (int i = 0; i < selected.Length; i++)
            {
                Vector3 centre = Centre(model, selected[i]);
                Assert.AreEqual(50 + 20 * i, centre.X, 0.05, $"decal {i}");
                Assert.AreEqual(i % 2 == 0 ? 95 : 105, centre.Z, 0.05, "on the left (-z), then the right");
                Decal decal = (Decal)model.ShownDocument.Find(selected[i])!;
                Assert.AreEqual(6, decal.Scale.X, 1e-4);
                Assert.AreEqual(-MathF.PI / 2, decal.Rotation.Y, 1e-4, "its length along the curve");
            }
            StringAssert.Contains(model.Status, "Placed 8 decals");

            Assert.IsTrue(model.Undo());
            Assert.AreEqual(decals, model.ShownDocument!.Decals.Length, "undone at once");
        }

        [TestMethod]
        public async Task WhatTheArgumentsLeaveOutComesFromTheSectionAndTheCurveItsSymmetry()
        {
            (MainViewModel model, _) = await Open("leftRight");
            await AddCurve(model);
            Assert.IsNotNull(HeightLayers.Find(model.ShownDocument!, model.HeightStack.Chosen)!.Curve!.Symmetry, "drawn while the symmetry mirrors edits");
            model.DecalsAlong.Spacing = 40;
            model.DecalsAlong.Width = 10;
            int decals = model.ShownDocument!.Decals.Length;

            CommandRun run = await Run(model, "decals.alongCurve", new JsonObject { ["key"] = Mud });

            Assert.IsTrue(run.Ran, run.Error?.Message);
            Assert.AreEqual(8, (int)run.Result!, "four along the curve, four images");
            ObjectRef[] selected = [.. model.Selection.Items];
            Assert.AreEqual(8, selected.Length, "the images are selected too");
            for (int i = 0; i < 4; i++)
            {
                Vector3 centre = Centre(model, selected[i]);
                Vector3 image = Centre(model, selected[4 + i]);
                Assert.AreEqual(60 + 40 * i, centre.X, 0.05);
                Assert.AreEqual(256 - centre.X, image.X, 0.05, "mirrored left to right");
                Assert.AreEqual(centre.Z, image.Z, 0.05);
            }
            Assert.AreEqual(decals + 8, model.ShownDocument!.Decals.Length);
            JsonObject state = (JsonObject)model.DecalsAlong.Describe();
            Assert.AreEqual(40, state["spacing"]!.GetValue<double>());
        }

        [TestMethod]
        public async Task WithoutACurveOrADecalNothingIsPlaced()
        {
            (MainViewModel model, MainViewModelTest.Answers answers) = await Open();
            CommandRun none = await model.Commands.TryRunAsync("decals.alongCurve", new JsonObject { ["key"] = Mud }, interactive: false);
            Assert.IsFalse(none.Ran, "no curve");
            StringAssert.Contains(none.Error?.Message, "no curve");

            await AddCurve(model);
            // the chosen layer is not a curve: the topmost curve is used
            await Run(model, "terrain.chooseLayer", new JsonObject { ["layer"] = "Base" });
            int steps = model.History.Count;
            answers.Assets.Enqueue(null);
            CommandRun cancelled = await model.Commands.RunAsync("decals.alongCurve", [], interactive: true);
            Assert.IsTrue(cancelled.Ran);
            Assert.AreEqual(0, (int)cancelled.Result!, "the picker cancelled");
            Assert.AreEqual(steps, model.History.Count, "no step");

            CommandRun placed = await Run(model, "decals.alongCurve", new JsonObject { ["key"] = Mud, ["spacing"] = 80 });
            Assert.AreEqual(2, (int)placed.Result!, "along the topmost curve");
            CommandRun wrong = await model.Commands.TryRunAsync("decals.alongCurve", new JsonObject { ["layer"] = "Base", ["key"] = Mud }, interactive: false);
            Assert.IsFalse(wrong.Ran, "a named layer must be a curve");
        }

        [TestMethod]
        public Task TheSectionOfTheCurvesInspectorPlacesThem() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.Assets.Use(AssetCatalog.Load(TestAssets.Folder()));
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.SelectMode("terrain");
            await AddCurve(model);
            Dispatcher.UIThread.RunJobs();
            EditorDriver editor = EditorDriver.InProcess(new AutomationServer(window, model, port: 0));

            await editor.Run("decals.chooseAlongCurve", new JsonObject { ["key"] = Mud });
            await editor.Set("Spacing of decals along the curve", "40");
            await editor.Set("Width of decals along the curve", "12");
            await editor.Set("Offset of decals beside the curve", "-3");
            await editor.Set("Decals along the curve alternate sides", false);
            int decals = model.ShownDocument!.Decals.Length;
            await editor.Send("click", new JsonObject { ["label"] = "Place decals along the curve" });
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(decals + 4, model.ShownDocument!.Decals.Length, model.Status);
            Decal last = model.ShownDocument.Decals[^1];
            Assert.AreEqual(12, last.Scale.X, 1e-4);
            Assert.AreEqual(103, MapObjects.DecalCentre(last).Z, 0.05, "on the right of the curve");
            Assert.AreEqual(4, model.Selection.Items.Count());
            window.Close();
        });
    }
}
