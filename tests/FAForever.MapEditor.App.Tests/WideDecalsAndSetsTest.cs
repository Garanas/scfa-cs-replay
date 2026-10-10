using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The map-wide decals (user story D-1: albedo, normal and shadow slots, generated or from a file,
    /// written at export as Kaali's), decal sets on the shelf and as one decal on the map, and the
    /// decal-masks layers that follow them.
    /// </summary>
    [TestClass]
    public class WideDecalsAndSetsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "wide-decals");

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static string Picture(string name, MagickColor colour, uint width = 64, uint height = 64)
        {
            Directory.CreateDirectory(Root);
            string file = Path.Combine(Root, $"{Guid.NewGuid():N}-{name}");
            using MagickImage image = new MagickImage(colour, width, height);
            File.WriteAllBytes(file, image.ToByteArray(MagickFormat.Png));
            return file;
        }

        #region Map-wide decals

        [TestMethod]
        public void TheDecalsPanelListsTheSlotsAboveTheGroups()
        {
            ModeDefinition decals = EditorModes.All.Single(mode => mode.Id == "decals");

            CollectionAssert.AreEqual(new[] { "Map-wide decals", "Decal groups" }, decals.Sections.Select(section => section.Title).ToArray());
            Assert.IsInstanceOfType<MapWideDecalDetailsSection>(decals.Inspector.Single());
            Assert.IsInstanceOfType<ProceduralDetailsSection>(EditorModes.All.Single(mode => mode.Id == "procedural").Inspector.Single());
        }

        [TestMethod]
        public async Task TheSlotsAreListedInTheDecalsLightingAndProceduralPanels()
        {
            MainViewModel model = await Open();

            CollectionAssert.AreEqual(new[] { "Albedo", "Normal decal", "Shadow decal" }, model.WideDecals.Parts.Items.Select(part => part.Title).ToArray());
            Assert.AreSame(model.WideDecals.Normal, model.ProceduralList.PartOf(NormalDecalGenerator.GeneratorId)!.Details);
            Assert.AreSame(model.WideDecals.Shadow, model.ProceduralList.PartOf(ShadowDecalGenerator.GeneratorId)!.Details);
            Assert.AreSame(model.WideDecals.Shadow, model.LightingSettings.Parts.Find(LightingSettingsViewModel.ShadowDecalPart)!.Content);
            Assert.IsTrue(model.WideDecals.Slots.All(slot => slot.Slot.Source == MapWideSource.None), "an imported map has none");
            Assert.AreEqual("none", model.WideDecals.Parts.Find(MapWideDecalsViewModel.ShadowPart)!.Detail);

            await Run(model, "view.inspectPart", new JsonObject { ["part"] = MapWideDecalsViewModel.ShadowPart });
            Assert.AreEqual("decals", model.SelectedMode!.Definition.Id);
            Assert.AreEqual(MapWideDecalsViewModel.ShadowPart, model.WideDecals.Parts.Chosen.Id);
            await Run(model, "view.inspectPart", new JsonObject { ["part"] = MapWideDecalsViewModel.ProceduralShadowPart });
            Assert.AreEqual("procedural", model.SelectedMode!.Definition.Id);
            Assert.AreEqual(MapWideDecalsViewModel.ProceduralShadowPart, model.ProceduralList.Parts.Chosen.Id);
        }

        [TestMethod]
        public async Task SetsASlotsSourceAndFileAsStepsOfUndo()
        {
            MainViewModel model = await Open();

            await Run(model, "decals.setWide", new JsonObject { ["kind"] = "normal", ["source"] = "generated" });
            Assert.AreEqual(MapWideSlot.Generated, model.ShownDocument!.WideDecals.Normal);
            Assert.AreEqual("generated", model.WideDecals.Parts.Find(MapWideDecalsViewModel.NormalPart)!.Detail);
            Assert.IsTrue(model.WideDecals.Normal.CanGenerate);

            string albedo = Picture("albedo.png", MagickColors.Olive);
            string specular = Picture("specular.png", MagickColors.Gray);
            await Run(model, "decals.chooseWideFile", new JsonObject { ["kind"] = "albedo", ["path"] = albedo });
            await Run(model, "decals.setWide", new JsonObject { ["kind"] = "albedo", ["specular"] = specular });
            Assert.AreEqual(MapWideSlot.Of(albedo), model.ShownDocument!.WideDecals.Albedo, "a file outside the project: its full path");
            Assert.AreEqual(specular, model.ShownDocument.WideDecals.Specular);
            Assert.IsInstanceOfType<TexturePicture>(model.WideDecals.Albedo.Preview, "the file's picture");
            StringAssert.StartsWith(model.WideDecals.Albedo.Status, "From ");

            await Run(model, "decals.setShadow", new JsonObject { ["radius"] = 24, ["sunShadows"] = true });
            Assert.AreEqual(24, model.ShownDocument.WideDecals.ShadowSettings.Radius);
            Assert.IsTrue(model.ShownDocument.WideDecals.ShadowSettings.SunShadows);

            Assert.IsFalse((await model.Commands.TryRunAsync("decals.setWide", new JsonObject { ["kind"] = "albedo", ["source"] = "generated" }, interactive: false)).Ran,
                "the albedo cannot be generated");
            Assert.IsFalse((await model.Commands.TryRunAsync("decals.setWide", new JsonObject { ["kind"] = "roof" }, interactive: false)).Ran);

            model.Undo();
            Assert.IsFalse(model.ShownDocument!.WideDecals.ShadowSettings.SunShadows);
            model.Undo();
            Assert.IsNull(model.ShownDocument.WideDecals.Specular);
        }

        [TestMethod]
        public async Task GenerateNowPreviewsTheDecalInTheViews()
        {
            MainViewModel model = await Open();
            await Run(model, "decals.setWide", new JsonObject { ["kind"] = "normal", ["source"] = "generated" });
            await Run(model, "decals.setWide", new JsonObject { ["kind"] = "shadow", ["source"] = "generated" });
            StringAssert.StartsWith(model.WideDecals.Normal.Status, "Not generated yet");
            Assert.AreSame(model.ShownDocument, model.WideDecals.Shown(model.ShownDocument!), "nothing to draw yet");

            await Run(model, "decals.generateNormal");
            await Run(model, "decals.generateShadow");

            StringAssert.StartsWith(model.WideDecals.Normal.Status, "Generated from the final heightmap, up to date");
            DecodedTexture normals = (DecodedTexture)model.Generation.Results.ResultOf(NormalDecalGenerator.GeneratorId)!.Value;
            Assert.AreEqual(2048, Math.Max(normals.Width, normals.Height), "a preview of at most 2048");
            Assert.IsInstanceOfType<TexturePicture>(model.WideDecals.Shadow.Preview);
            MapDocument drawn = model.WideDecals.Shown(model.ShownDocument!);
            Assert.AreEqual(model.ShownDocument!.Decals.Length + 2, drawn.Decals.Length);
            CollectionAssert.AreEqual(new[] { DecalType.Albedo, DecalType.Normals }, drawn.Decals.Take(2).Select(decal => decal.Type).ToArray(), "shadow, then normals, under the map's own");
            Assert.IsTrue(drawn.Decals.Take(2).All(decal => decal.Id < 0), "no selection meets them");
            Assert.AreEqual(drawn.Decals, model.WideDecals.Shown(model.ShownDocument!).Decals, "the same decals each frame, so the 3D view does not rebuild them");
            Assert.IsNotNull(model.WideDecals.TextureOf(drawn.Decals[1].Textures[0]));
            Assert.AreSame(normals, model.WideDecals.TextureOf(drawn.Decals[1].Textures[0])!.Read());

            // the heightmap changes: out of date
            model.EditMap("Raise", document => document with
            {
                Terrain = document.Terrain with
                {
                    Heightmap = document.Terrain.Heightmap with { Samples = document.Terrain.Heightmap.Samples.ToBuilder().Also(b => b[5, 5] = 60000).ToBuffer() },
                },
            });
            StringAssert.StartsWith(model.WideDecals.Normal.Status, "Out of date: heightmap changed");
        }

        [TestMethod]
        public async Task TheExportWritesTheMapWideDecalsAsKaali()
        {
            MainViewModel model = await Open();
            await Run(model, "decals.setWide", new JsonObject { ["kind"] = "albedo", ["file"] = Picture("colour.png", MagickColors.SaddleBrown, 256, 256) });
            await Run(model, "decals.setWide", new JsonObject { ["kind"] = "normal", ["source"] = "generated" });
            await Run(model, "decals.setWide", new JsonObject { ["kind"] = "shadow", ["source"] = "generated" });
            int decals = model.ShownDocument!.Decals.Length;

            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            ExportStepState step = model.Export.Lines.Single(line => line.Id == "wideDecals");
            Assert.AreEqual(ExportStepStatus.Done, step.Status);
            StringAssert.Contains(step.Note, "map_normals.dds generated");
            MapDocument exported = MapImporter.ImportFolder(folder);
            Assert.AreEqual(decals + 3, exported.Decals.Length);
            Decal[] wide = [.. exported.Decals.Take(3)];
            CollectionAssert.AreEqual(new[] { DecalType.Albedo, DecalType.Albedo, DecalType.Normals }, wide.Select(decal => decal.Type).ToArray());
            Assert.IsTrue(wide.All(decal => decal.CutOffLod == 10000 && decal.Position.X == 0 && decal.Position.Z == 0 && decal.Scale.X == exported.Width));
            StringAssert.EndsWith(wide[0].Textures[0], "/env/decals/map_albedo.dds");
            StringAssert.EndsWith(wide[1].Textures[0], "/env/decals/map_lighting.dds");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "env", "decals", "map_normals.dds")));
            Assert.AreEqual(decals, model.ShownDocument!.Decals.Length, "the project keeps only the settings");
        }

        #endregion

        #region Decal sets

        /// <summary>
        /// The test assets with a decal set (rock01: albedo, normals, specular, a main and a moss mask).
        /// </summary>
        private static async Task<MainViewModel> OpenWithSet()
        {
            MainViewModel model = await Open();
            string folder = TestAssets.Folder();
            string decals = Path.Combine(folder, "env", "evergreen", "decals");
            foreach (string name in new[] { "rock01_albedo", "rock01_normals", "rock01_spec", "rock01_mask", "rock01_mask_moss" })
            {
                using MagickImage image = new MagickImage(MagickColors.White, 4, 4);
                File.WriteAllBytes(Path.Combine(decals, name + ".png"), image.ToByteArray(MagickFormat.Png));
            }
            model.Assets.Use(AssetCatalog.Load(folder));
            model.SelectMode("decals");
            return model;
        }

        [TestMethod]
        public async Task TheShelfShowsASetAsOneTileThatPlacesTheWholeSet()
        {
            MainViewModel model = await OpenWithSet();
            AssetsViewModel assets = model.Assets;
            assets.Kind = AssetKind.Decal;
            assets.Scope = AssetScope.Everything;

            AssetTile tile = assets.Tiles.Single(t => t.Title == "rock01");
            Assert.IsTrue(tile.IsSet);
            Assert.AreEqual("set of 5", tile.Detail);
            Assert.AreEqual("env/evergreen/decals/rock01_albedo.dds", tile.Entry.Key, "the albedo shows");
            Assert.AreEqual(5, tile.Members.Count);

            assets.ChooseTileCommand.Execute(tile);
            Assert.IsNotNull(assets.ShownSet);
            Assert.AreEqual("env/evergreen/decals/rock01", model.DecalPlaceTool.Decal, "the place tool places the whole set");
            Assert.AreEqual("rock01 (set)", model.DecalPlaceTool.Title);
            StringAssert.StartsWith(assets.SetText, "The whole set is chosen");

            // one texture chosen in the inspector is placed alone; Choose the whole set brings the set back
            assets.SelectedMember = tile.Members[1];
            Assert.IsNull(assets.ShownSet);
            Assert.AreEqual("env/evergreen/decals/rock01_normals.dds", model.DecalPlaceTool.Decal);
            assets.ChooseWholeSetCommand.Execute(null);
            Assert.AreEqual("env/evergreen/decals/rock01", model.DecalPlaceTool.Decal);

            // without Variants every file is a tile of its own
            assets.Families = false;
            Assert.IsFalse(assets.Tiles.Any(t => t.IsSet));
        }

        [TestMethod]
        public async Task APlacedSetIsOneDecalOneSelectionAndOneStep()
        {
            MainViewModel model = await OpenWithSet();
            int steps = model.History.Count;

            CommandRun run = await Run(model, "decals.place", new JsonObject { ["key"] = "env/evergreen/decals/rock01", ["x"] = 100, ["z"] = 100, ["width"] = 20 });

            ObjectRef added = ObjectRef.Parse((string)run.Result!);
            Decal decal = (Decal)model.ShownDocument!.Find(added)!;
            Assert.AreEqual("/env/evergreen/decals/rock01_normals.dds", decal.Set!.Normals);
            Assert.AreEqual(steps + 1, model.History.Count);
            CollectionAssert.AreEqual(new[] { added }, model.Selection.Items.ToArray());
            StringAssert.Contains(model.Status, "decal set");
            Assert.AreEqual("rock01 (decal set)", SelectionInspector.NameOf(model.ShownDocument, added));

            // moved as one thing, written as two decals
            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 8, ["dz"] = 0 });
            Assert.AreEqual(decal.Set, ((Decal)model.ShownDocument!.Find(added)!).Set);
            int count = model.ShownDocument.Decals.Length;
            Assert.AreEqual(count + 1, MapExporter.Export(model.ShownDocument).Scmap.Decals.Count);
        }

        [TestMethod]
        public async Task AHeightLayerOfDecalMasksFollowsTheDecal()
        {
            MainViewModel model = await Open();
            // a set of the map's own files: its mask full in the left half
            using MagickImage mask = new MagickImage(MagickColors.Black, 8, 8);
            mask.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.White).Rectangle(0, 0, 3, 7));
            byte[] png = mask.ToByteArray(MagickFormat.Png);
            using MagickImage albedo = new MagickImage(MagickColors.Gray, 8, 8);
            model.EditMap("Add a decal set", document => document with
            {
                Files = document.Files.SetItem("env/decals/rock01_albedo.png", [.. albedo.ToByteArray(MagickFormat.Png)]).SetItem("env/decals/rock01_mask.png", [.. png]),
            });
            string set = $"/maps/{model.ShownDocument!.Folder}/env/decals/rock01";
            CommandRun placed = await Run(model, "decals.place", new JsonObject { ["key"] = set, ["x"] = 100, ["z"] = 100, ["width"] = 32, ["length"] = 32 });
            ObjectRef rock = ObjectRef.Parse((string)placed.Result!);
            float flat = model.ShownDocument!.Terrain.Heightmap.HeightAt(90, 100);
            float there = model.ShownDocument.Terrain.Heightmap.HeightAt(130, 100);

            await Run(model, "terrain.addLayer", new JsonObject { ["kind"] = "decals", ["height"] = 6 });

            Assert.AreEqual(flat + 6, model.ShownDocument!.Terrain.Heightmap.HeightAt(90, 100), 0.2f, "the mask's full half raised");
            Assert.AreEqual(model.ShownDocument.Terrain.Heightmap.HeightAt(110, 100), model.ShownDocument.HeightStack.Layers[0].Values![110, 100], 0.2f, "its empty half not");
            Assert.AreEqual("Decal masks", model.HeightStack.KindOf(model.ShownDocument.HeightStack.Layers[^1]));

            model.Selection.Set([rock]);
            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 40, ["dz"] = 0 });
            Assert.AreEqual(flat, model.ShownDocument!.Terrain.Heightmap.HeightAt(90, 100), 0.2f, "gone from the old place");
            Assert.AreEqual(there + 6, model.ShownDocument.Terrain.Heightmap.HeightAt(130, 100), 0.2f, "at the new place");

            await Run(model, "terrain.setDecalMasks", new JsonObject { ["height"] = 2 });
            Assert.AreEqual(there + 2, model.ShownDocument!.Terrain.Heightmap.HeightAt(130, 100), 0.2f);
            await Run(model, "terrain.setDecalMasks", new JsonObject { ["mask"] = "moss" });
            Assert.AreEqual(there, model.ShownDocument!.Terrain.Heightmap.HeightAt(130, 100), 0.2f, "the set has no moss mask");
        }

        [TestMethod]
        public async Task AStratumOfDecalMasksFollowsTheDecal()
        {
            MainViewModel model = await Open();
            using MagickImage mask = new MagickImage(MagickColors.White, 8, 8);
            byte[] png = mask.ToByteArray(MagickFormat.Png);
            model.EditMap("Add a decal set", document => document with
            {
                Files = document.Files.SetItem("env/decals/rock01_albedo.png", [.. png]).SetItem("env/decals/rock01_mask_moss.png", [.. png]),
            });
            CommandRun placed = await Run(model, "decals.place", new JsonObject { ["key"] = $"/maps/{model.ShownDocument!.Folder}/env/decals/rock01", ["x"] = 100, ["z"] = 100, ["width"] = 32, ["length"] = 32 });
            ObjectRef rock = ObjectRef.Parse((string)placed.Result!);
            float At(float x, float z)
            {
                MapDocument d = model.ShownDocument!;
                return LayerStacks.SampleAt(LayerStacks.MaskOf(d, "stratum:2")!, d.Width, d.Height, x, z);
            }
            float before = At(160, 100);

            await Run(model, "layers.add", new JsonObject { ["target"] = "stratum:2", ["source"] = "decals", ["mask"] = "moss" });

            Assert.AreEqual(1, At(100, 100), 0.02f, "the stratum shows where the set's moss mask is");
            model.Selection.Set([rock]);
            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 60, ["dz"] = 0 });
            Assert.AreEqual(1, At(160, 100), 0.02f, "and follows the decal");
            Assert.AreNotEqual(1, At(100, 100), 0.02f);
            Assert.IsTrue(before < 1);

            await Run(model, "layers.setDecalMasks", new JsonObject { ["target"] = "stratum:2", ["layer"] = "Decal masks", ["mask"] = "main" });
            Assert.AreEqual(before, At(160, 100), 0.02f, "the set has no main mask");
        }

        #endregion
    }
}
