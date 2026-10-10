using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using ImageMagick;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The export step that converts the map's own pictures to DDS, in the version the export writes only.
    /// </summary>
    [TestClass]
    public class DdsTexturesExportTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "dds-textures");

        private static byte[] Png(MagickColor colour, uint size)
        {
            using MagickImage image = new MagickImage(colour, size, size);
            return image.ToByteArray(MagickFormat.Png);
        }

        [TestMethod]
        public async Task TheExportConvertsTheMapsOwnPicturesAndTheProjectKeepsThem()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            string own = $"/maps/{model.Map!.Document.Folder}/";
            model.Map.Session.Edit("Own textures", document => document with
            {
                Files = document.Files
                    .SetItem("env/layers/moss_albedo.png", [.. Png(MagickColors.DarkGreen, 64)])
                    .SetItem("env/layers/moss_normal.png", [.. Png(new MagickColor(128, 128, 255, 200), 64)]),
                Terrain = document.Terrain with
                {
                    Layers = document.Terrain.Layers.SetItem(1, document.Terrain.Layers[1] with
                    {
                        AlbedoPath = own + "env/layers/moss_albedo.png",
                        NormalPath = own + "env/layers/moss_normal.png",
                    }),
                },
            });
            Assert.AreEqual(ExportStepStatus.Waiting, model.Export.Lines.Single(line => line.Id == MainViewModel.DdsTexturesExportStep).Status);

            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            ExportStepState step = model.Export.Lines.Single(line => line.Id == MainViewModel.DdsTexturesExportStep);
            Assert.AreEqual(ExportStepStatus.Done, step.Status);
            StringAssert.StartsWith(step.Note, "2 textures converted");
            StringAssert.Contains(step.Note, "moss_albedo.png");
            Assert.IsFalse(Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories).Any(path => Path.GetDirectoryName(path) != folder));
            MapDocument exported = MapImporter.ImportFolder(folder);
            StringAssert.EndsWith(exported.Terrain.Layers[1].AlbedoPath, "/env/layers/moss_albedo.dds");
            StringAssert.EndsWith(exported.Terrain.Layers[1].NormalPath, "/env/layers/moss_normal.dds");
            Assert.IsTrue(model.Map.Document.Files.ContainsKey("env/layers/moss_albedo.png"), "the project keeps its pictures");
            StringAssert.EndsWith(model.Map.Document.Terrain.Layers[1].AlbedoPath, ".png");

            // the next export takes them from the cache
            await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));
            StringAssert.StartsWith(model.Export.Lines.Single(line => line.Id == MainViewModel.DdsTexturesExportStep).Note, "2 textures converted (2 as before)");
        }

        [TestMethod]
        public async Task AMapOfDdsTexturesSkipsTheStep()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);

            await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            ExportStepState step = model.Export.Lines.Single(line => line.Id == MainViewModel.DdsTexturesExportStep);
            Assert.AreEqual(ExportStepStatus.Skipped, step.Status);
            Assert.AreEqual("Not needed: every texture is DDS", step.StatusText);
        }
    }
}
