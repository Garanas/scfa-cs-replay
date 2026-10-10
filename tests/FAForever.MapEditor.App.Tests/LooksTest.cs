using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The map's look: the terrain shader and the textured preview (user story S-4), the sky's
    /// cube maps (L-1), the grid and decals as overlays (V-3), the units' icons (U-1) and reading
    /// one asset again (F-8).
    /// </summary>
    [TestClass]
    public sealed class LooksTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// The test assets with cube maps of the sky and of reflections, and an icon for the Snoop.
        /// </summary>
        private static string AssetsWithSkyAndIcons()
        {
            string assets = TestAssets.Folder();
            void Png(string path, int width, int height)
            {
                string file = Path.Combine(assets, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                using MagickImage image = new MagickImage(MagickColors.SkyBlue, (uint)width, (uint)height);
                File.WriteAllBytes(file, image.ToByteArray(MagickFormat.Png));
            }
            Png("textures/environment/skycube_evergreen01a.png", 24, 4);
            Png("textures/environment/envcube_evergreen01a.png", 24, 4);
            Png("textures/engine/watercubemap.png", 24, 4);
            Png("textures/ui/common/icons/units/uel0101_icon.png", 4, 4);
            return assets;
        }

        private static async Task<MainViewModel> Open(AssetCatalog? catalog = null)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(catalog ?? TestAssets.Catalog());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRunResult> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false).ContinueWith(task => new CommandRunResult(task.Result.Ran, task.Result.Result, task.Result.Why));

        private sealed record CommandRunResult(bool Ran, object? Result, string? Why);

        /// <summary>
        /// Whether running the command fails with an argument the person gave wrong.
        /// </summary>
        private static async Task<bool> Refuses(MainViewModel model, string id, JsonObject arguments)
        {
            try
            {
                await model.Commands.RunAsync(id, arguments, interactive: false);
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        [TestMethod]
        public async Task ChoosesTheTerrainShaderOneStepOfUndo()
        {
            MainViewModel model = await Open();
            string before = model.ShownDocument!.Terrain.Shader;

            await Run(model, "paint.shader", new JsonObject { ["name"] = "terrain250" });

            Assert.AreEqual("Terrain250", model.ShownDocument!.Terrain.Shader, "the name as the game writes it");
            Assert.AreEqual("Terrain250", model.LayerTextures.Shader?.Name, "the Paint panel shows it");
            CollectionAssert.Contains(model.LayerTextures.ShaderUses.ToList(), "A stratum's mask counts from its middle up");
            model.Undo();
            Assert.AreEqual(before, model.ShownDocument!.Terrain.Shader);

            // the panel's list runs the command
            model.LayerTextures.Shader = TerrainShaderCatalog.Find("Terrain000");
            await Task.Yield();
            Assert.AreEqual("Terrain000", model.ShownDocument!.Terrain.Shader);

            // a mod's shader is kept, and said to be unknown
            await Run(model, "paint.shader", new JsonObject { ["name"] = "MyShader" });
            Assert.AreEqual("MyShader", model.ShownDocument!.Terrain.Shader);
            StringAssert.Contains(model.Status, "does not know");
            Assert.AreEqual("MyShader", model.LayerTextures.Shader?.Name, "the list holds the map's own");
            StringAssert.Contains(model.LayerTextures.ShaderSummary, "Not a shader the editor knows");

            Assert.IsTrue(await Refuses(model, "paint.shader", new JsonObject { ["name"] = "two words" }));
        }

        [TestMethod]
        public async Task ThePreviewIsOutOfDateWhenTheShaderOrStrataChange()
        {
            MainViewModel model = await Open();
            await model.GenerateAsync(MapPreview.GeneratorId);
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.States.Single(state => state.Generator.Id == MapPreview.GeneratorId).Status);

            await Run(model, "paint.shader", new JsonObject { ["name"] = "Terrain000" });

            GeneratedState preview = model.Generation.States.Single(state => state.Generator.Id == MapPreview.GeneratorId);
            Assert.AreEqual(GeneratedStatus.Stale, preview.Status);
            CollectionAssert.Contains(preview.ChangedInputs.ToList(), "terrain shader");
        }

        [TestMethod]
        public void ThePreviewWithAWorkspaceIsDrawnWithTheTextures()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "looks-workspace", Guid.NewGuid().ToString("N"));
            Workspace workspace = Workspace.Create(root, root, null, root);
            MapDocument document = Project.MapImporter.ImportFolder(HardFfa);
            // the lower layer's texture in the workspace: one red pixel; a dark night lights nothing but the ambience
            string lower = Path.Combine(workspace.AssetsFolder, WorkspaceLayout.AssetOf(document.Terrain.Layers[0].AlbedoPath));
            Directory.CreateDirectory(Path.GetDirectoryName(lower)!);
            using (MagickImage red = new MagickImage(MagickColors.Red, 4, 4))
            {
                File.WriteAllBytes(lower, red.ToByteArray(MagickFormat.Png));
            }
            document = document with
            {
                Water = document.Water with { HasWater = false },
                Terrain = document.Terrain with { StratumMasks = document.Terrain.StratumMasks with { Layers = [.. document.Terrain.StratumMasks.Layers.Select(mask => TiledBuffer<byte>.Create(mask.Width, mask.Height, 0))] } },
                Lighting = document.Lighting with { SunColor = new System.Numerics.Vector3(0), SunAmbience = new System.Numerics.Vector3(1), ShadowFillColor = new System.Numerics.Vector3(0), Multiplier = 1 },
            };

            TiledBuffer<Color32> textured = (TiledBuffer<Color32>)new MapPreview(() => workspace).Generate(document);
            TiledBuffer<Color32> plain = (TiledBuffer<Color32>)new MapPreview().Generate(document);

            Assert.AreEqual((256, 256), (textured.Width, textured.Height));
            Color32 middle = textured[128, 128];
            Assert.IsTrue(middle.Red > 200 && middle.Green < 40 && middle.Blue < 40, $"the lower layer's red: {middle}");
            Assert.AreNotEqual(middle, plain[128, 128], "without a workspace the picture of heights");
        }

        [TestMethod]
        public async Task ChangesAndChoosesTheSkysCubeMaps()
        {
            MainViewModel model = await Open(AssetCatalog.Load(AssetsWithSkyAndIcons()));

            await Run(model, "lighting.setSky", new JsonObject { ["cubemap"] = "/textures/environment/skycube_test.dds", ["background"] = "/textures/environment/black.dds" });
            Assert.AreEqual("/textures/environment/skycube_test.dds", model.ShownDocument!.Sky.Cubemap);
            Assert.AreEqual("/textures/environment/black.dds", model.ShownDocument.Sky.Background);
            Assert.AreEqual("/textures/environment/skycube_test.dds", ((TextureSetting)model.LightingSettings.Sky.Settings[0]).Text, "the inspector shows it");
            Assert.IsTrue(model.Assets.AllMissing.Any(missing => missing.Kind == AssetKind.Cubemap && missing.Path == "textures/environment/skycube_test.dds"),
                "a cube map the workspace lacks is missing (F-8)");

            await Run(model, "lighting.chooseCubemap", new JsonObject { ["target"] = "environment", ["key"] = "textures/environment/envcube_evergreen01a.dds" });
            Sky sky = model.ShownDocument!.Sky;
            Assert.AreEqual("/textures/environment/envcube_evergreen01a.dds", (sky.EnvironmentCubemaps.FirstOrDefault(cubemap => cubemap.Name == "<default>") ?? sky.EnvironmentCubemaps[0]).Texture);

            await Run(model, "lighting.chooseCubemap", new JsonObject { ["target"] = "water", ["key"] = "/textures/engine/WaterCubemap.dds" });
            Assert.AreEqual("/textures/engine/watercubemap.dds", model.ShownDocument!.Water.Cubemap);
            model.Undo();
            Assert.AreNotEqual("/textures/engine/watercubemap.dds", model.ShownDocument!.Water.Cubemap, "one step of undo");

            Assert.IsTrue(await Refuses(model, "lighting.chooseCubemap", new JsonObject { ["target"] = "sky", ["key"] = "uel0101" }), "a unit is no cube map");
            Assert.IsTrue(await Refuses(model, "lighting.chooseCubemap", new JsonObject { ["target"] = "moon", ["key"] = "textures/engine/watercubemap.dds" }));

            // the map without environment cube maps gets the default one
            model.EditMap("No reflections", document => document with { Sky = document.Sky with { EnvironmentCubemaps = [] } });
            await Run(model, "lighting.setSky", new JsonObject { ["environmentCubemap"] = "/textures/environment/envcube_evergreen01a.dds" });
            Assert.AreEqual("<default>", model.ShownDocument!.Sky.EnvironmentCubemaps.Single().Name);
        }

        [TestMethod]
        public async Task TheShelfListsTheCubeMapsInTheLightingMode()
        {
            MainViewModel model = await Open(AssetCatalog.Load(AssetsWithSkyAndIcons()));

            model.SelectMode("lighting");

            Assert.AreEqual(AssetKind.Cubemap, model.Assets.Kind);
            CollectionAssert.AreEquivalent(new[] { "skycube_evergreen01a", "envcube_evergreen01a" }, model.Assets.Tiles.Select(tile => tile.Title).ToArray(), "the sky and reflections at first");
            model.Assets.Scope = AssetScope.Everything;
            Assert.AreEqual(3, model.Assets.Tiles.Count, "the water's too");
        }

        [TestMethod]
        public async Task AUnitShowsTheGamesIconInThePickerAndInspector()
        {
            MainViewModel model = await Open(AssetCatalog.Load(AssetsWithSkyAndIcons()));

            model.Assets.Select("uel0101");

            Assert.IsTrue(model.Assets.HasShownIcon);
            StringAssert.EndsWith(model.Assets.ShownIcon, "uel0101_icon.png");
            model.Assets.Scope = AssetScope.Everything;
            AssetTile snoop = model.Assets.Tiles.Single(tile => tile.Entry.Key == "uel0101");
            Assert.IsTrue(snoop.HasIcon);
            Assert.IsFalse(model.Assets.Tiles.Single(tile => tile.Entry.Key == "uec1101").HasIcon, "a unit without one");
        }

        [TestMethod]
        public async Task ReadsOneAssetAgain()
        {
            string assets = AssetsWithSkyAndIcons();
            MainViewModel model = await Open(AssetCatalog.Load(assets));
            AssetCatalog before = model.Assets.Catalog;
            model.Assets.Select("uel0101");
            File.WriteAllText(Path.Combine(assets, "units/uel0101/uel0101_unit.json"), """{ "id": "UEL0101", "name": "Snoop Mark II", "faction": "Uef", "techLevel": 2, "categories": ["UEF", "LAND"] }""");

            AssetEntry? entry = model.Assets.ReloadOne("uel0101", assets);

            Assert.AreEqual("Snoop Mark II", entry?.Name);
            Assert.AreEqual("Snoop Mark II", model.Assets.Shown?.Name, "the inspector shows it as it is now");
            Assert.AreSame(before, model.Assets.Catalog.ReloadedFrom, "the rest of the catalogue is kept");
            Assert.ThrowsException<ArgumentException>(() => model.Assets.ReloadOne("nothing", assets));
        }

        [TestMethod]
        public async Task TheGridHasASpacingAndTheDecalsAChip()
        {
            MainViewModel model = await Open();
            OverlayItem grid = model.Overlays.Single(overlay => overlay.Id == "grid");
            OverlayItem decals = model.Overlays.Single(overlay => overlay.Id == "decals");
            Assert.IsFalse(grid.IsIncomplete);
            Assert.IsFalse(model.ShowGrid);
            Assert.AreEqual("Grid 16", grid.Text);

            await Run(model, "view.gridSpacing", new JsonObject { ["spacing"] = 8 });
            Assert.IsTrue(model.ShowGrid, "setting the spacing shows the grid");
            Assert.AreEqual("Grid 8", grid.Text);
            Assert.IsTrue(grid.IsOn);
            Assert.IsTrue(await Refuses(model, "view.gridSpacing", new JsonObject { ["spacing"] = 0 }));

            await Run(model, "view.overlay.grid");
            Assert.IsFalse(model.ShowGrid);

            Assert.IsTrue(decals.IsOn);
            await Run(model, "view.overlay.decals", new JsonObject { ["on"] = false });
            Assert.IsFalse(model.ShowDecals);
            Assert.IsFalse(decals.IsOn);
        }

        [TestMethod]
        public Task TheTopViewDrawsTheGridAndTheDecals() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(TestAssets.SandboxProject());
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            async Task<JsonObject> Send(string command, JsonObject? arguments = null)
            {
                Dispatcher.UIThread.RunJobs();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
                Dispatcher.UIThread.RunJobs();
                return answer;
            }
            Assert.IsTrue(model.ShownDocument!.Decals.Length > 0, "the sandbox has a decal");

            await Send("view", new JsonObject { ["zoom"] = 2 });
            JsonObject looks = (JsonObject)(await Send("state"))["looks"]!;
            Assert.IsTrue(window.MapView.DecalOutlinesDrawn > 0, "the decals' outlines");
            Assert.AreEqual(0, window.MapView.GridLinesDrawn, "no grid at first");
            Assert.AreEqual(model.ShownDocument.Terrain.Shader, (string?)looks["shader"]?["name"]);

            // a decal 24 ogrids wide at a pixel an ogrid: too small for an outline outside the Decals mode,
            // where one works with them it shows
            await Send("view", new JsonObject { ["zoom"] = 1 });
            await Send("state");
            Assert.AreEqual(0, window.MapView.DecalOutlinesDrawn, "small decals are left out outside the Decals mode");
            await Send("mode", new JsonObject { ["id"] = "decals" });
            await Send("state");
            Assert.AreEqual(1, window.MapView.DecalOutlinesDrawn, "in the Decals mode they show");
            await Send("mode", new JsonObject { ["id"] = "terrain" });
            await Send("view", new JsonObject { ["zoom"] = 2 });

            await Send("run", new JsonObject { ["id"] = "view.gridSpacing", ["spacing"] = 16 });
            await Send("state");
            Assert.IsTrue(window.MapView.GridLinesDrawn > 0, "the grid's lines");

            // the decals chip, clicked as a person does
            await Send("click", new JsonObject { ["label"] = "Show Decals" });
            looks = (JsonObject)(await Send("state"))["looks"]!;
            Assert.AreEqual(false, (bool?)looks["decals"]?["shown"]);
            Assert.AreEqual(0, window.MapView.DecalOutlinesDrawn, "hidden, none are drawn");

            // too close together the lines are left out
            await Send("run", new JsonObject { ["id"] = "view.gridSpacing", ["spacing"] = 1 });
            await Send("view", new JsonObject { ["zoom"] = 0.5 });
            await Send("state");
            Assert.AreEqual(0, window.MapView.GridLinesDrawn);
            window.Close();
        });
    }
}
