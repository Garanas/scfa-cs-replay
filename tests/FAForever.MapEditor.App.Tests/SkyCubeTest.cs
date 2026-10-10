using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The sky cube map made from the map (user story L-1): generated into the project's env folder,
    /// chosen with one click for the sky, the units and the water, its settings kept with the map, a
    /// skybox preset read into the sky, and the 3D view's faces.
    /// </summary>
    [TestClass]
    public class SkyCubeTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "sky-cube");

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static Task<CommandRun> TryRun(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.TryRunAsync(id, arguments ?? [], interactive: false);

        /// <summary>
        /// Faces of one colour, as a renderer that answers at once would give them.
        /// </summary>
        private sealed class SolidFaces(bool answers = true) : ISkyCubeFaces
        {
            public int Calls { get; private set; }

            public Vector3 Eye { get; private set; }

            public bool CanRender => true;

            public Task<DecodedTexture> RenderAsync(MapDocument document, Vector3 eye, DecodedTexture sky, CancellationToken cancel)
            {
                Calls++;
                Eye = eye;
                if (!answers)
                {
                    return new TaskCompletionSource<DecodedTexture>().Task;
                }
                byte[] rgba = new byte[sky.Rgba.Length];
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (10, 200, 30, 255);
                }
                return Task.FromResult(new DecodedTexture(sky.Width, sky.Height, rgba));
            }
        }

        [TestMethod]
        public void TheProceduralModeListsItWithItsSettingsInTheInspector()
        {
            ModeDefinition procedural = EditorModes.All.Single(mode => mode.Id == "procedural");

            Assert.IsTrue(procedural.Sections.OfType<SkyCubeProceduralSection>().Any());
            Assert.IsTrue(procedural.Inspector.OfType<SkyCubeDetailsSection>().Any());
        }

        [TestMethod]
        public async Task GeneratesTheCubeMapIntoTheProjectsEnvFolderAsOneStepOfUndo()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 512 });
            Assert.AreEqual("Not generated yet", model.SkyCube.Status);
            Assert.IsFalse(model.SkyCube.CanUse);

            CommandRun run = await Run(model, "generate.skyCube");

            MapDocument document = model.Map!.Document;
            Assert.IsTrue(document.Files.TryGetValue(SkyCubeMaps.FilePath, out var bytes));
            DecodedTexture strip = CubeMaps.Decode([.. bytes]);
            Assert.AreEqual(512, strip.Height, "a cube map of 512 a face that reads back");
            Assert.AreEqual(6 * 512, strip.Width);
            Assert.AreEqual("Generate the sky cube map", model.Map.Session.UndoDescription);
            StringAssert.StartsWith(model.Status, "Generated the sky cube map: 512 pixels a face, the map drawn with the processor");
            StringAssert.StartsWith(model.SkyCube.Status, "Up to date: 512 pixels a face");
            Assert.IsInstanceOfType<TexturePicture>(model.SkyCube.Preview);
            Assert.IsTrue(model.SkyCube.CanUse);
            Assert.AreEqual("generated", model.SkyCube.Part.Detail);
            JsonObject state = (JsonObject)run.Result!;
            Assert.AreEqual(512, (int)state["size"]!);
            Assert.AreEqual(SkyCubeMaps.GamePath(document), (string?)state["file"]);
            StringAssert.Contains(model.SkyCube.SkyLine, "made from the lighting", "HardFFA has no skybox");

            model.Undo();
            Assert.IsFalse(model.Map.Document.Files.ContainsKey(SkyCubeMaps.FilePath));
        }

        [TestMethod]
        public async Task OneClickMakesItTheSkysTheUnitsAndTheWatersCubeMap()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 512, ["terrain"] = false });
            CommandRun early = await TryRun(model, "generate.useSkyCube");
            Assert.IsFalse(early.Ran, "nothing to use before it is generated");

            await Run(model, "generate.skyCube");
            model.SkyCube.UseEverywhereCommand.Execute(null);
            await Task.Delay(50);

            MapDocument document = model.Map!.Document;
            string path = SkyCubeMaps.GamePath(document);
            Assert.AreEqual(path, document.Sky.Cubemap);
            Assert.IsTrue(SkyCubeViewModel.UsesOf(document).Environment, "the units reflect it");
            Assert.AreEqual(path, document.Water.Cubemap);
            Assert.AreEqual("Use the sky cube map for the sky, the units and the water", model.Map.Session.UndoDescription);
            Assert.IsTrue(model.SkyCube.IsUsed);
            Assert.IsFalse(model.SkyCube.CanUse);
            Assert.AreEqual("in use", model.SkyCube.Part.Detail);
            StringAssert.StartsWith(model.SkyCube.Status, "Up to date", "choosing it for the water leaves it current");

            // one of them only
            model.Undo();
            await Run(model, "generate.useSkyCube", new JsonObject { ["water"] = true });
            Assert.AreEqual(path, model.Map.Document.Water.Cubemap);
            Assert.AreNotEqual(path, model.Map.Document.Sky.Cubemap);
            StringAssert.Contains(model.SkyCube.UseLine, "the water");
        }

        [TestMethod]
        public async Task TheSettingsAreStepsOfUndoAndPutTheCubeMapOutOfDate()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 512, ["terrain"] = false });
            await Run(model, "generate.skyCube");

            await Run(model, "generate.setSkyCube", new JsonObject { ["eyeHeight"] = 80 });
            await Run(model, "generate.setSkyCube", new JsonObject { ["eyeHeight"] = 90 });

            Assert.AreEqual(90, model.Map!.Document.Procedural.SkyCube.EyeHeight);
            Assert.AreEqual("Sky cube map: eyeHeight", model.Map.Session.UndoDescription);
            StringAssert.StartsWith(model.SkyCube.Status, "Out of date: sky cube settings changed");
            Assert.AreEqual("out of date", model.SkyCube.Part.Detail);
            model.Undo();
            Assert.AreEqual(SkyCubeSettings.Default.EyeHeight, model.Map.Document.Procedural.SkyCube.EyeHeight, "the two steps of the same field are one");

            await Run(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 1500, ["sunDisc"] = false });
            Assert.AreEqual(1024, model.Map.Document.Procedural.SkyCube.Resolution, "the nearest power of two");
            Assert.IsFalse(model.Map.Document.Procedural.SkyCube.SunDisc);
            Assert.IsFalse((await TryRun(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 100 })).Ran);
            Assert.IsFalse((await TryRun(model, "generate.setSkyCube", new JsonObject { ["eyeHeight"] = -1 })).Ran);
        }

        private static void Raise(MainViewModel model) => model.Map!.Session.Edit("Raise", document =>
        {
            TiledBuffer<ushort>.Builder samples = document.Terrain.Heightmap.Samples.ToBuilder();
            samples[10, 10] = (ushort)(samples[10, 10] + 500);
            return document with { Terrain = document.Terrain with { Heightmap = document.Terrain.Heightmap with { Samples = samples.ToBuffer() } } };
        });

        [TestMethod]
        public async Task TheTerrainPutsItOutOfDateOnlyWhileItIsIncluded()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 512, ["terrain"] = false });
            await Run(model, "generate.skyCube");

            Raise(model);
            model.Generation.Refresh(model.Map.Document);
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.States.Single(s => s.Generator.Id == SkyCubeGenerator.GeneratorId).Status, "the sky alone does not show the terrain");

            await Run(model, "generate.setSkyCube", new JsonObject { ["terrain"] = true });
            await Run(model, "generate.skyCube");
            Raise(model);
            model.Generation.Refresh(model.Map.Document);
            GeneratedState state = model.Generation.States.Single(s => s.Generator.Id == SkyCubeGenerator.GeneratorId);
            Assert.AreEqual(GeneratedStatus.Stale, state.Status);
            CollectionAssert.Contains(state.ChangedInputs.ToArray(), "heightmap");
        }

        [TestMethod]
        public async Task The3DViewDrawsTheMapWhenItCanAndTheProcessorWhenItDoesNotAnswer()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 512 });
            SolidFaces faces = new SolidFaces();
            model.SkyCubeGenerator.Faces = faces;

            await Run(model, "generate.skyCube");

            Assert.AreEqual(1, faces.Calls);
            Assert.AreEqual(SkyCubeMaps.Eye(model.Map!.Document, model.Map.Document.Procedural.SkyCube), faces.Eye);
            StringAssert.Contains(model.Status, "the 3D renderer");
            DecodedTexture strip = CubeMaps.Decode([.. model.Map.Document.Files[SkyCubeMaps.FilePath]]);
            Assert.IsTrue(Math.Abs(strip.Rgba[1] - 200) < 8, "the view's faces are written");

            SolidFaces silent = new SolidFaces(answers: false);
            model.SkyCubeGenerator.Faces = silent;
            model.SkyCubeGenerator.FacesTimeout = TimeSpan.FromMilliseconds(100);
            await Run(model, "generate.skyCube");
            Assert.AreEqual(1, silent.Calls);
            StringAssert.Contains(model.Status, "the processor");
        }

        [TestMethod]
        public void EachFacesCameraSeesWhatTheFaceHolds()
        {
            Vector3 eye = new Vector3(100, 50, 200);
            for (int face = 0; face < 6; face++)
            {
                Matrix4x4 camera = TerrainViewGpu.FaceViewProjection(face, eye, 0.5f, 5000);
                foreach ((int column, int row) in new[] { (256, 256), (10, 20), (500, 30), (40, 480) })
                {
                    Vector3 direction = CubeFaces.PixelDirection(face, column, row, 512);
                    Vector4 clip = Vector4.Transform(new Vector4(eye + direction * 300, 1), camera);
                    Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
                    // the framebuffer's rows from the bottom
                    Assert.AreEqual(column + 0.5f, (ndc.X + 1) / 2 * 512, 1e-2f, $"face {face} column");
                    Assert.AreEqual(511 - row + 0.5f, (ndc.Y + 1) / 2 * 512, 1e-2f, $"face {face} row");
                    Assert.IsTrue(ndc.Z > -1 && ndc.Z < 1, "between near and far");
                }
                Vector4 behind = Vector4.Transform(new Vector4(eye - CubeFaces.Direction(face, 0, 0) * 10, 1), camera);
                Assert.IsTrue(behind.W < 0, "what lies behind the face is not drawn");
            }
        }

        [TestMethod]
        public async Task ReadsASkyboxPresetIntoTheMapsSky()
        {
            MainViewModel model = await Open();
            Directory.CreateDirectory(Root);
            string file = Path.Combine(Root, $"{Guid.NewGuid():N}.json");
            File.WriteAllText(file, """
                {"Data": {"Position": {"x": 128, "y": 0, "z": 128}, "Scale": 600, "HorizonHeight": -20, "ZenithHeight": 200,
                  "HorizonColor": {"r": 0.6, "g": 0.4, "b": 0.3, "a": 1}, "ZenithColor": {"r": 0.05, "g": 0.1, "b": 0.3, "a": 1},
                  "Albedo": "/maps/elsewhere.v0001/env/decals/sky.dds", "Glow": "", "DecalGlowMultiplier": 0.3,
                  "Planets": [{"Position": {"x": 0, "y": 300, "z": -4000}, "Rotation": 0.5, "Scale": {"x": 500, "y": 500}, "Uv": {"x": 0, "y": 0, "z": 1, "w": 1}}],
                  "MidRgbColor": {"r": 0, "g": 0, "b": 0, "a": 0}, "CirrusMultiplier": 1, "CirrusColor": {"r": 1, "g": 1, "b": 1, "a": 1},
                  "CirrusTexture": "", "CirrusLayers": [{"frequency": {"x": 0.001, "y": 0.001}, "Speed": 0.1, "Direction": {"x": 1, "y": 0}}], "Clouds7": 0}}
                """);

            await Run(model, "lighting.importSkybox", new JsonObject { ["path"] = file });

            Skybox sky = model.Map!.Document.Sky.Skybox!;
            Assert.AreEqual(1, sky.Planets.Length);
            Assert.AreEqual(new Vector3(0.6f, 0.4f, 0.3f), sky.HorizonColor);
            Assert.AreEqual(1, sky.CirrusLayers.Length);
            Assert.AreEqual($"Read the skybox {Path.GetFileName(file)}", model.Map.Session.UndoDescription);
            StringAssert.Contains(model.SkyCube.SkyLine, "the map's skybox, 1 sky object, 1 cirrus layer");
        }

        [TestMethod]
        public async Task AutomationReadsIt()
        {
            MainViewModel model = await Open();

            JsonObject state = model.SkyCube.Describe();

            Assert.AreEqual(1024, (int)state["settings"]!["resolution"]!);
            Assert.IsTrue((bool)state["settings"]!["terrain"]!);
            Assert.IsNull(state["file"]);
            Assert.IsFalse((bool)state["usedBy"]!["water"]!);
        }
    }
}
